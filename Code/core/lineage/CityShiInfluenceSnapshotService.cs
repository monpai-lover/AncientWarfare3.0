using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.lineage
{
    internal static class CityShiInfluenceSnapshotService
    {
        private static readonly Dictionary<long, CityShiInfluenceSnapshot> Snapshots =
            new Dictionary<long, CityShiInfluenceSnapshot>();
        private static readonly CitySchoolDirtyQueue Dirty =
            new CitySchoolDirtyQueue();
        private static readonly CitySchoolDirtyQueue DemandedDirty =
            new CitySchoolDirtyQueue();
        private static readonly HashSet<long> Demanded = new HashSet<long>();
        private static int _generation;

        public static bool HasPendingDemand => Demanded.Count > 0;

        public static CityShiInfluenceSnapshot GetSnapshot(City pCity)
        {
            if (!IsUsableCity(pCity)) return null;
            long cityId = pCity.data.id;
            if (!Snapshots.TryGetValue(cityId,
                    out CityShiInfluenceSnapshot snapshot) || Dirty.Contains(cityId))
                Demand(pCity);
            return snapshot;
        }

        public static void Demand(City pCity)
        {
            if (!IsUsableCity(pCity)) return;
            long cityId = pCity.data.id;
            Demanded.Add(cityId);
            Dirty.Mark(cityId);
            DemandedDirty.Mark(cityId);
        }

        public static void MarkDirty(City pCity)
        {
            if (!IsUsableCity(pCity)) return;
            long cityId = pCity.data.id;
            Dirty.Mark(cityId);
            if (Demanded.Contains(cityId)) DemandedDirty.Mark(cityId);
        }

        public static void MarkDirtyById(long pCityId)
        {
            if (pCityId < 0L) return;
            Dirty.Mark(pCityId);
            if (Demanded.Contains(pCityId)) DemandedDirty.Mark(pCityId);
        }

        public static void MarkActorDirty(Actor pActor)
        {
            if (pActor?.data == null) return;
            MarkDirty(pActor.city);
            if (pActor.isKing()) MarkDirty(pActor.kingdom?.capital);
        }

        public static void MarkCityOwnershipChanged(City pCity,
            Kingdom pOldKingdom = null, Kingdom pNewKingdom = null)
        {
            MarkDirty(pCity);
            MarkKingdomDirty(pOldKingdom, pOnlyMissing: false);
            if (pNewKingdom != pOldKingdom)
                MarkKingdomDirty(pNewKingdom, pOnlyMissing: false);
        }

        public static void MarkKingdomDirty(Kingdom pKingdom,
            bool pOnlyMissing = false)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (!IsUsableCity(city)) continue;
                    if (pOnlyMissing && Snapshots.ContainsKey(city.data.id)) continue;
                    MarkDirty(city);
                }
            }
            catch { }
        }

        public static int ProcessDirty(int pBudget, bool pDemandOnly = false)
        {
            if (pBudget <= 0) return 0;
            CitySchoolDirtyQueue source = pDemandOnly ? DemandedDirty : Dirty;
            int rebuilt = 0;
            int budget = Math.Max(1, pBudget);
            while (rebuilt < budget && source.TryDequeue(out long cityId))
            {
                Dirty.Remove(cityId);
                DemandedDirty.Remove(cityId);
                City city = World.world?.cities?.get(cityId);
                if (!IsUsableCity(city))
                {
                    Snapshots.Remove(cityId);
                    Demanded.Remove(cityId);
                    continue;
                }

                try
                {
                    Snapshots[cityId] = Rebuild(city);
                    Demanded.Remove(cityId);
                    rebuilt++;
                }
                catch
                {
                    Dirty.Mark(cityId);
                    if (Demanded.Contains(cityId)) DemandedDirty.Mark(cityId);
                }
            }
            return rebuilt;
        }

        public static void Clear()
        {
            Snapshots.Clear();
            Dirty.Clear();
            DemandedDirty.Clear();
            Demanded.Clear();
            _generation = 0;
        }

        private static CityShiInfluenceSnapshot Rebuild(City pCity)
        {
            var contributions = new List<CityShiInfluenceContribution>();
            var branches = new Dictionary<long, ShiBranchInfo>();
            foreach (Actor actor in pCity.getUnits())
            {
                if (!TryGetContribution(pCity, actor, branches,
                        out CityShiInfluenceContribution contribution)) continue;
                contributions.Add(contribution);
            }

            CityShiInfluenceSnapshot snapshot = CityShiInfluenceRules.BuildSnapshot(
                ++_generation, contributions);
            snapshot.CityId = pCity.data.id;
            return snapshot;
        }

        private static bool TryGetContribution(City pCity, Actor pActor,
            Dictionary<long, ShiBranchInfo> pBranches,
            out CityShiInfluenceContribution pContribution)
        {
            pContribution = default;
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt() ||
                pActor.data.id < 0L || pActor.city != pCity) return false;
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            if (shiId < 0L) return false;
            if (!pBranches.TryGetValue(shiId, out ShiBranchInfo branch))
            {
                try { branch = LineageQuery.GetShiBranchInfo(shiId); }
                catch { branch = null; }
                if (branch == null) return false;
                pBranches[shiId] = branch;
            }
            pContribution = new CityShiInfluenceContribution(pActor.data.id,
                shiId, ResolveRole(pActor), CreatedOrder(branch.created_time));
            return true;
        }

        private static CityShiRole ResolveRole(Actor pActor)
        {
            if (pActor.isKing()) return CityShiRole.King;
            if (pActor.isCityLeader()) return CityShiRole.CityLeader;
            if (HeirService.IsCurrentHeir(pActor.kingdom, pActor))
                return CityShiRole.Heir;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string officeId, "");
            if (!string.IsNullOrEmpty(officeId)) return CityShiRole.Official;
            pActor.data.get(LineageKeys.LINEAGE_STATUS, out string status,
                LineageStatus.NONE);
            pActor.data.get(LineageKeys.NOBLE_DISTANCE, out int nobleDistance, 99);
            return status == LineageStatus.NOBLE || nobleDistance < 99
                ? CityShiRole.Noble
                : CityShiRole.Member;
        }

        private static long CreatedOrder(double pCreatedTime)
        {
            if (double.IsNaN(pCreatedTime) || double.IsInfinity(pCreatedTime))
                return long.MaxValue;
            double scaled = Math.Floor(Math.Max(0d, pCreatedTime) * 1000d);
            return scaled >= long.MaxValue ? long.MaxValue : (long)scaled;
        }

        private static bool IsUsableCity(City pCity)
        {
            return pCity?.data != null && !pCity.isRekt();
        }
    }
}

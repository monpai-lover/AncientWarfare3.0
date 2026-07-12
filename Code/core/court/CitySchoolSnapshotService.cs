using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CitySchoolSnapshotService
    {
        private const int AnnualRefreshBudget = 4;
        private static readonly Dictionary<long, CitySchoolSnapshot> Snapshots =
            new Dictionary<long, CitySchoolSnapshot>();
        private static readonly CitySchoolDirtyQueue Dirty = new CitySchoolDirtyQueue();
        private static int _generation;

        public static CitySchoolSnapshot GetSnapshot(City pCity, bool pEnsureFresh = false)
        {
            if (pCity?.data == null || pCity.isRekt()) return null;
            if (pEnsureFresh) return Rebuild(pCity);
            if (Snapshots.TryGetValue(pCity.data.id, out CitySchoolSnapshot snapshot)) return snapshot;
            Dirty.Mark(pCity.data.id);
            return null;
        }

        public static void MarkDirty(City pCity)
        {
            if (pCity?.data != null && !pCity.isRekt()) Dirty.Mark(pCity.data.id);
        }

        public static void MarkActorDirty(Actor pActor)
        {
            if (pActor?.data == null) return;
            MarkDirty(pActor.city);
            if (pActor.isKing()) MarkDirty(pActor.kingdom?.capital);
        }

        public static void MarkKingdomDirty(Kingdom pKingdom, bool pOnlyMissing = false)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    if (pOnlyMissing && Snapshots.ContainsKey(city.data.id)) continue;
                    Dirty.Mark(city.data.id);
                }
            }
            catch { }
        }

        public static int ProcessDirty(int pBudget)
        {
            int rebuilt = 0;
            foreach (long cityId in Dirty.TakeBatch(pBudget))
            {
                City city = World.world?.cities?.get(cityId);
                if (city?.data == null || city.isRekt())
                {
                    Snapshots.Remove(cityId);
                    continue;
                }
                Rebuild(city);
                rebuilt++;
            }
            return rebuilt;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            MarkKingdomDirty(pKingdom, pOnlyMissing: true);
            ProcessDirty(AnnualRefreshBudget);
        }

        public static Dictionary<string, float> GetKingdomTotals(Kingdom pKingdom)
        {
            var totals = new Dictionary<string, float>(StringComparer.Ordinal);
            if (pKingdom?.data == null) return totals;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    CitySchoolSnapshot snapshot = GetSnapshot(city);
                    if (snapshot == null) continue;
                    foreach (KeyValuePair<string, float> item in snapshot.Scores)
                    {
                        totals.TryGetValue(item.Key, out float previous);
                        totals[item.Key] = previous + item.Value;
                    }
                }
            }
            catch { }
            return totals;
        }

        public static void Clear()
        {
            Snapshots.Clear();
            Dirty.Clear();
            _generation = 0;
        }

        private static CitySchoolSnapshot Rebuild(City pCity)
        {
            var contributions = new List<CitySchoolInfluenceContribution>();
            Kingdom kingdom = pCity.kingdom;
            if (kingdom?.data != null)
            {
                Actor king = kingdom.king;
                if (kingdom.capital == pCity) Add(contributions, king, CitySchoolRole.King, 0);

                Actor heir = HeirService.PeekRegisteredHeir(kingdom);
                if (heir?.city == pCity) Add(contributions, heir, CitySchoolRole.Heir, 5);

                if (pCity.leader?.data != null)
                    Add(contributions, pCity.leader, CitySchoolRole.Leader, 10);

                foreach (CourtOfficerView officer in CourtService.GetActiveOfficers(kingdom, 96))
                {
                    if (officer.layer != CourtOfficeLayer.Central &&
                        officer.layer != CourtOfficeLayer.Censor) continue;
                    Actor actor = World.world?.units?.get(officer.actor_id);
                    if (actor?.city != pCity) continue;
                    Add(contributions, actor, CitySchoolRole.CentralOfficer, 20);
                }

                foreach (GeneralReadModelEntry entry in GeneralService.GetActiveGeneralsForReadModel(
                             kingdom, pAllowUnitFallback: false))
                {
                    if (entry.Actor?.city != pCity) continue;
                    Add(contributions, entry.Actor, CitySchoolRole.General, 40);
                }
            }

            CitySchoolSnapshot snapshot = CitySchoolInfluenceRules.BuildSnapshot(++_generation, contributions);
            snapshot.CityId = pCity.data.id;
            snapshot.KingdomId = kingdom?.data?.id ?? -1L;
            Snapshots[pCity.data.id] = snapshot;
            return snapshot;
        }

        private static void Add(List<CitySchoolInfluenceContribution> pItems, Actor pActor,
            string pRole, int pRoleRank)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return;
            pActor.data.get(LineageKeys.COURT_SCHOOL, out string school, "");
            if (CourtSchoolRegistry.Find(school) == null) return;
            pItems.Add(new CitySchoolInfluenceContribution(pActor.data.id, school, pRole,
                CitySchoolInfluenceRules.RoleBaseWeight(pRole), AbilityScore(pActor), pRoleRank,
                SafeName(pActor)));
        }

        private static float AbilityScore(Actor pActor)
        {
            float sum = SafeStat(pActor, "stewardship") + SafeStat(pActor, "diplomacy") +
                        SafeStat(pActor, "warfare") + SafeStat(pActor, "intelligence");
            return Math.Max(0f, Math.Min(100f, sum * 1.25f));
        }

        private static float SafeStat(Actor pActor, string pStat)
        {
            try { return pActor?.stats?[pStat] ?? 0f; }
            catch { return 0f; }
        }

        private static string SafeName(Actor pActor)
        {
            try { return pActor?.getName() ?? ""; }
            catch { return pActor?.data?.name ?? ""; }
        }
    }

    internal static class SchoolMembershipService
    {
        private static readonly SchoolMembershipIndex Index = new SchoolMembershipIndex();

        public static void Update(Actor pActor, string pSchoolId)
        {
            if (pActor?.data == null) return;
            bool alive = pActor.isAlive() && !pActor.isRekt();
            Index.Update(pActor.data.id, pSchoolId, alive);
            CitySchoolSnapshotService.MarkActorDirty(pActor);
        }

        public static void Remove(Actor pActor)
        {
            if (pActor?.data == null) return;
            Index.Remove(pActor.data.id);
            CitySchoolSnapshotService.MarkActorDirty(pActor);
        }

        public static int Count(string pSchoolId) => Index.Count(pSchoolId);
        public static long[] Members(string pSchoolId) => Index.Members(pSchoolId);

        public static void Clear()
        {
            Index.Clear();
            CitySchoolSnapshotService.Clear();
        }
    }
}

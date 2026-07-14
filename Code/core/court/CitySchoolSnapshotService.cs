using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.court
{
    internal static class CitySchoolSnapshotService
    {
        private const float MembershipInfluencePerLivingActor = 0.03f;
        private const float ActivePresencePerLivingActor = 0.05f;
        private static readonly Dictionary<long, CitySchoolSnapshot> Snapshots =
            new Dictionary<long, CitySchoolSnapshot>();
        private static readonly CitySchoolDirtyQueue Dirty = new CitySchoolDirtyQueue();
        private static readonly CitySchoolDirtyQueue DemandedDirty =
            new CitySchoolDirtyQueue();
        private static readonly HashSet<long> Demanded = new HashSet<long>();
        private static readonly CitySchoolRetryScheduler Retry =
            new CitySchoolRetryScheduler();
        private static readonly CitySchoolRetryGate ContextGate =
            new CitySchoolRetryGate();
        private static int _generation;

        private sealed class CitySchoolSnapshotBatchContext
        {
            private static readonly Dictionary<string, HistoricalSchoolLedgerSnapshot>
                EmptyLedgers = new Dictionary<string, HistoricalSchoolLedgerSnapshot>(
                    StringComparer.Ordinal);
            private readonly Dictionary<long,
                Dictionary<string, HistoricalSchoolLedgerSnapshot>> _ledgers;

            public CitySchoolSnapshotBatchContext(CitySchoolResidentIndex pResidents,
                Dictionary<long, Dictionary<string, HistoricalSchoolLedgerSnapshot>> pLedgers)
            {
                Residents = pResidents;
                _ledgers = pLedgers ??
                    new Dictionary<long,
                        Dictionary<string, HistoricalSchoolLedgerSnapshot>>();
            }

            public CitySchoolResidentIndex Residents { get; }

            public Dictionary<string, HistoricalSchoolLedgerSnapshot> Ledgers(long pCityId)
            {
                return _ledgers.TryGetValue(pCityId,
                    out Dictionary<string, HistoricalSchoolLedgerSnapshot> ledgers)
                    ? ledgers
                    : EmptyLedgers;
            }
        }

        public static int Generation => _generation;
        public static bool HasPendingDemand => Demanded.Count > 0;

        public static CitySchoolSnapshot GetSnapshot(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return null;
            long cityId = pCity.data.id;
            if (!Snapshots.TryGetValue(cityId, out CitySchoolSnapshot snapshot) ||
                Dirty.Contains(cityId)) Demand(cityId);
            return snapshot;
        }

        public static void MarkDirty(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return;
            Retry.Forget(pCity.data.id);
            Dirty.Mark(pCity.data.id);
            if (Demanded.Contains(pCity.data.id)) DemandedDirty.Mark(pCity.data.id);
        }

        public static void MarkDirtyById(long pCityId)
        {
            if (pCityId < 0) return;
            Retry.Forget(pCityId);
            Dirty.Mark(pCityId);
            if (Demanded.Contains(pCityId)) DemandedDirty.Mark(pCityId);
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
                    Retry.Forget(city.data.id);
                    Dirty.Mark(city.data.id);
                }
            }
            catch { }
        }

        public static int ProcessDirty(int pBudget, bool pDemandOnly = false)
        {
            if (pBudget <= 0) return 0;
            if (Dirty.Count == 0 && DemandedDirty.Count == 0 && Retry.Count == 0) return 0;
            foreach (long retryCityId in Retry.AdvanceAndTakeDue())
            {
                Dirty.Mark(retryCityId);
                if (Demanded.Contains(retryCityId)) DemandedDirty.Mark(retryCityId);
            }
            CitySchoolDirtyQueue source = pDemandOnly ? DemandedDirty : Dirty;
            if (source.Count == 0) return 0;
            if (!ContextGate.AdvanceAndCanAttempt()) return 0;
            int budget = Math.Max(1, pBudget);
            if (!source.TryDequeue(out long firstCityId)) return 0;
            var validCities = new List<City>(budget);
            var validCityIds = new List<long>(budget);
            for (int index = 0; index < budget; index++)
            {
                long cityId = index == 0 ? firstCityId : -1L;
                if (index > 0 && !source.TryDequeue(out cityId)) break;
                Dirty.Remove(cityId);
                DemandedDirty.Remove(cityId);
                City city = World.world?.cities?.get(cityId);
                if (city?.data == null || city.isRekt())
                {
                    Retry.Forget(cityId);
                    Snapshots.Remove(cityId);
                    Demanded.Remove(cityId);
                    continue;
                }
                validCities.Add(city);
                validCityIds.Add(cityId);
            }
            if (validCities.Count == 0) return 0;

            if (!TryBuildBatchContext(validCities, out CitySchoolSnapshotBatchContext context,
                    out string contextFailure))
            {
                int requeued = Dirty.RequeueFront(validCityIds);
                RequeueDemandedFront(validCityIds);
                int contextRetryDelay = ContextGate.RecordFailure();
                ModClass.LogWarning("City school snapshot batch context failed for cities [" +
                                    string.Join(",", validCityIds) + "]; requeued " + requeued +
                                    "; retry in " + contextRetryDelay + " ticks: " +
                                    contextFailure);
                return 0;
            }
            ContextGate.RecordSuccess();

            int rebuilt = 0;
            foreach (City city in validCities)
            {
                try
                {
                    Rebuild(city, context);
                    Retry.Forget(city.data.id);
                    Demanded.Remove(city.data.id);
                    DemandedDirty.Remove(city.data.id);
                    rebuilt++;
                }
                catch (Exception error)
                {
                    int retryDelay = Retry.ScheduleFailure(city.data.id);
                    ModClass.LogWarning("City school snapshot rebuild failed for city " +
                                        city.data.id + "; retry in " + retryDelay +
                                        " ticks: " + error.Message);
                }
            }
            if (rebuilt > 0) SchoolMapModeService.DirtyMapIfActive();
            return rebuilt;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            MarkKingdomDirty(pKingdom, pOnlyMissing: true);
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
            DemandedDirty.Clear();
            Demanded.Clear();
            Retry.Clear();
            ContextGate.Clear();
            _generation = 0;
        }

        private static bool TryBuildBatchContext(IReadOnlyList<City> pCities,
            out CitySchoolSnapshotBatchContext pContext, out string pFailure)
        {
            pContext = null;
            pFailure = "";
            try
            {
                var cityIds = new long[pCities.Count];
                for (int i = 0; i < pCities.Count; i++) cityIds[i] = pCities[i].data.id;
                if (!HistoricalSchoolStore.TryLoadLedgersForCities(cityIds,
                        out Dictionary<long,
                            Dictionary<string, HistoricalSchoolLedgerSnapshot>> ledgers,
                        out pFailure)) return false;
                CitySchoolResidentIndex residents = BuildResidentIndex(pCities);
                pContext = new CitySchoolSnapshotBatchContext(residents, ledgers);
                return true;
            }
            catch (Exception error)
            {
                pFailure = error.GetType().Name + ": " + error.Message;
                return false;
            }
        }

        private static CitySchoolResidentIndex BuildResidentIndex(
            IReadOnlyList<City> pCities)
        {
            var candidates = new List<CitySchoolResidentCandidate>();
            var schoolOrders = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < CourtSchoolRegistry.All.Count; i++)
                schoolOrders[CourtSchoolRegistry.All[i].Id] = i;
            HistoricalSchoolRuntimeIndex runtime = HistoricalSchoolRuntimeIndex.Instance;
            for (int cityIndex = 0; cityIndex < (pCities?.Count ?? 0); cityIndex++)
            {
                City city = pCities[cityIndex];
                if (city?.data == null || city.isRekt()) continue;
                foreach (long actorId in
                         HistoricalSchoolRuntimeIndex.Instance.ResidentIds(city.data.id))
                {
                    if (!runtime.TryGet(actorId, out HistoricalSchoolIndexEntry entry) ||
                        !entry.Present || entry.ResidenceCityId != city.data.id ||
                        !schoolOrders.TryGetValue(entry.SchoolId, out int schoolOrder))
                        continue;
                    Actor actor = World.world?.units?.get(actorId);
                    if (actor?.data == null || !actor.isAlive() || actor.isRekt()) continue;
                    bool qualified = entry.Standing == HistoricalSchoolStanding.Teacher ||
                                     entry.Standing == HistoricalSchoolStanding.Leader ||
                                     entry.Standing == HistoricalSchoolStanding.CanonicalMaster;
                    candidates.Add(new CitySchoolResidentCandidate(actorId,
                        entry.SchoolId, city.data.id, pPresent: true, qualified,
                        schoolOrder));
                }
            }
            return CitySchoolResidentIndexRules.Build(candidates);
        }

        private static void Demand(long pCityId)
        {
            if (pCityId < 0) return;
            Demanded.Add(pCityId);
            if (Retry.Contains(pCityId)) return;
            Dirty.Mark(pCityId);
            DemandedDirty.Mark(pCityId);
        }

        private static void RequeueDemandedFront(IEnumerable<long> pCityIds)
        {
            if (pCityIds == null) return;
            var demanded = new List<long>();
            foreach (long cityId in pCityIds)
                if (Demanded.Contains(cityId)) demanded.Add(cityId);
            DemandedDirty.RequeueFront(demanded);
        }

        private static CitySchoolSnapshot Rebuild(City pCity,
            CitySchoolSnapshotBatchContext pContext)
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
                    City officerResidence = HistoricalAffiliationService.ResidenceCity(actor) ??
                                            actor?.city;
                    if (officerResidence != pCity) continue;
                    Add(contributions, actor, CitySchoolRole.CentralOfficer, 20);
                }

                foreach (GeneralReadModelEntry entry in GeneralService.GetActiveGeneralsForReadModel(
                             kingdom, pAllowUnitFallback: false))
                {
                    if (entry.Actor?.city != pCity) continue;
                    Add(contributions, entry.Actor, CitySchoolRole.General, 40);
                }
            }

            AddResidentScholars(contributions, pCity, pContext);

            CitySchoolSnapshot snapshot = CitySchoolInfluenceRules.BuildSnapshot(++_generation, contributions);
            snapshot.CityId = pCity.data.id;
            snapshot.KingdomId = kingdom?.data?.id ?? -1L;
            ApplyLedgerInfluence(snapshot, pCity, pContext);
            Snapshots[pCity.data.id] = snapshot;
            Dirty.Remove(pCity.data.id);
            return snapshot;
        }

        private static void ApplyLedgerInfluence(CitySchoolSnapshot pSnapshot, City pCity,
            CitySchoolSnapshotBatchContext pContext)
        {
            if (pSnapshot == null || pCity?.data == null || pContext?.Residents == null) return;
            Dictionary<string, HistoricalSchoolLedgerSnapshot> ledgers =
                pContext.Ledgers(pCity.data.id);

            var scores = pSnapshot.Scores?.ToDictionary(p => p.Key, p => p.Value,
                StringComparer.Ordinal) ?? new Dictionary<string, float>(StringComparer.Ordinal);
            var ledgerScores = new Dictionary<string, float>(StringComparer.Ordinal);
            var schoolIds = new HashSet<string>(ledgers.Keys, StringComparer.Ordinal);
            foreach (string schoolId in pContext.Residents.SchoolIds(pCity.data.id))
                schoolIds.Add(schoolId);
            // Durable ledger history is blended with the current resident membership
            // so departed or dead scholars cannot retain full local presence forever.
            foreach (string schoolId in schoolIds)
            {
                if (CourtSchoolRegistry.Find(schoolId) == null) continue;
                int livingMembers = pContext.Residents.Count(pCity.data.id, schoolId);
                HistoricalSchoolLedgerSnapshot ledger = ledgers.TryGetValue(schoolId,
                    out HistoricalSchoolLedgerSnapshot persisted)
                    ? persisted
                    : new HistoricalSchoolLedgerSnapshot(schoolId, 0f, 0f, 0f, -1);
                float bonus = LedgerInfluenceScore(ledger, livingMembers);
                if (bonus <= 0f) continue;
                ledgerScores[schoolId] = bonus;
                scores.TryGetValue(schoolId, out float current);
                scores[schoolId] = current + bonus;
            }
            if (scores.Count == 0) return;
            pSnapshot.Scores = scores;
            pSnapshot.LedgerScores = ledgerScores;
            pSnapshot.TotalScore = scores.Values.Sum();
            pSnapshot.DominantSchool = scores
                .OrderByDescending(p => p.Value)
                .ThenBy(p => RegistryOrder(p.Key))
                .First().Key;
        }

        private static float LedgerInfluenceScore(HistoricalSchoolLedgerSnapshot pLedger,
            int pLivingMembers)
        {
            if (pLedger == null) return 0f;
            float membership = Math.Max(0f, Math.Min(1f,
                Math.Max(0, pLivingMembers) * MembershipInfluencePerLivingActor));
            float livePresence = Math.Max(0f, Math.Min(1f,
                Math.Max(0, pLivingMembers) * ActivePresencePerLivingActor));
            float activePresence = pLedger.ActivePresence * livePresence;
            float score = pLedger.Tradition * 4f + activePresence * 8f +
                          pLedger.Momentum * 6f + membership * 4f +
                          Math.Min(4f, pLedger.Institutions * 0.25f);
            return Math.Max(0f, Math.Min(26f, score));
        }

        private static int RegistryOrder(string pSchoolId)
        {
            for (int i = 0; i < CourtSchoolRegistry.All.Count; i++)
                if (CourtSchoolRegistry.All[i].Id == pSchoolId) return i;
            return int.MaxValue;
        }

        private static void Add(List<CitySchoolInfluenceContribution> pItems, Actor pActor,
            string pRole, int pRoleRank)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return;
            if (!HistoricalAffiliationService.IsPresentForInfluence(pActor)) return;
            string school = SchoolMembershipService.GetSchool(pActor.data.id);
            if (CourtSchoolRegistry.Find(school) == null) return;
            pItems.Add(new CitySchoolInfluenceContribution(pActor.data.id, school, pRole,
                CitySchoolInfluenceRules.RoleBaseWeight(pRole), AbilityScore(pActor), pRoleRank,
                SafeName(pActor)));
        }

        private static void AddResidentScholars(List<CitySchoolInfluenceContribution> pItems,
            City pCity, CitySchoolSnapshotBatchContext pContext)
        {
            if (pItems == null || pCity?.data == null || pContext?.Residents == null) return;
            try
            {
                foreach (long actorId in pContext.Residents.ScholarActorIds(pCity.data.id))
                {
                    Actor actor = World.world?.units?.get(actorId);
                    Add(pItems, actor, CitySchoolRole.Scholar, 60);
                }
            }
            catch { }
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

}

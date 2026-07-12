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
        private const int AnnualRefreshBudget = 4;
        private const float MembershipInfluencePerLivingActor = 0.03f;
        private const float ActivePresencePerLivingActor = 0.05f;
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

        public static void MarkDirtyById(long pCityId)
        {
            if (pCityId >= 0) Dirty.Mark(pCityId);
        }

        public static CitySchoolSnapshot GetFreshSnapshotIfDirty(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return null;
            return Dirty.Contains(pCity.data.id) ? Rebuild(pCity) : GetSnapshot(pCity);
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
            if (rebuilt > 0) SchoolMapModeService.DirtyMapIfActive();
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

            AddResidentScholars(contributions, pCity);

            CitySchoolSnapshot snapshot = CitySchoolInfluenceRules.BuildSnapshot(++_generation, contributions);
            snapshot.CityId = pCity.data.id;
            snapshot.KingdomId = kingdom?.data?.id ?? -1L;
            ApplyLedgerInfluence(snapshot, pCity);
            Snapshots[pCity.data.id] = snapshot;
            Dirty.Remove(pCity.data.id);
            return snapshot;
        }

        private static void ApplyLedgerInfluence(CitySchoolSnapshot pSnapshot, City pCity)
        {
            if (pSnapshot == null || pCity?.data == null) return;
            Dictionary<string, HistoricalSchoolLedgerSnapshot> ledgers =
                HistoricalSchoolStore.LoadLedgersForCity(pCity.data.id);
            if (ledgers == null)
                ledgers = new Dictionary<string, HistoricalSchoolLedgerSnapshot>(
                    StringComparer.Ordinal);

            var scores = pSnapshot.Scores?.ToDictionary(p => p.Key, p => p.Value,
                StringComparer.Ordinal) ?? new Dictionary<string, float>(StringComparer.Ordinal);
            var ledgerScores = new Dictionary<string, float>(StringComparer.Ordinal);
            Dictionary<string, int> livingMemberships = LivingMemberships(pCity);
            var schoolIds = new HashSet<string>(ledgers.Keys, StringComparer.Ordinal);
            foreach (string schoolId in livingMemberships.Keys) schoolIds.Add(schoolId);
            // Durable ledger history is blended with the current resident membership
            // so departed or dead scholars cannot retain full local presence forever.
            foreach (string schoolId in schoolIds)
            {
                if (CourtSchoolRegistry.Find(schoolId) == null) continue;
                livingMemberships.TryGetValue(schoolId, out int livingMembers);
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

        private static Dictionary<string, int> LivingMemberships(City pCity)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (pCity?.data == null) return result;
            try
            {
                foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
                {
                    int count = 0;
                    foreach (Actor actor in SchoolMembershipService.LivingMembers(school.Id))
                    {
                        if (actor?.data == null ||
                            !HistoricalAffiliationService.IsPresentForInfluence(actor)) continue;
                        City residence = HistoricalAffiliationService.ResidenceCity(actor) ??
                                         actor.city;
                        if (residence?.data?.id == pCity.data.id) count++;
                    }
                    if (count > 0) result[school.Id] = count;
                }
            }
            catch { }
            return result;
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
            City pCity)
        {
            if (pItems == null || pCity?.data == null) return;
            int added = 0;
            try
            {
                foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
                {
                    foreach (Actor actor in SchoolMembershipService.LivingMembers(school.Id))
                    {
                        if (added >= 24) return;
                        if (actor?.data == null || !HistoricalAffiliationService.IsPresentForInfluence(actor))
                            continue;
                        City residence = HistoricalAffiliationService.ResidenceCity(actor) ?? actor.city;
                        if (residence?.data?.id != pCity.data.id) continue;
                        if (!HistoricalSchoolDescentService.IsCanonicalMaster(actor) &&
                            !SchoolLineageService.IsQualifiedTeacher(actor)) continue;
                        Add(pItems, actor, CitySchoolRole.Scholar, 60);
                        added++;
                    }
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

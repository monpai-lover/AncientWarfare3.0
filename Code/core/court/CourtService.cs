using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.court
{
    internal sealed class CourtSnapshot
    {
        public string mode = "";
        public string dominant_school = "";
        public string secondary_school = "";
        public string faction_cache = "";
        public float efficiency;
        public float concentration;
    }

    internal static class CourtService
    {
        private const int CandidateLimit = 24;

        public static bool HasOfficialCourt(Kingdom pKingdom)
        {
            return KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom) &&
                   KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Tech, "aw_tech_official_court");
        }

        public static bool HasPrimitiveCourt(Kingdom pKingdom)
        {
            return KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom) && !HasOfficialCourt(pKingdom);
        }

        public static CourtSnapshot GetSnapshot(Kingdom pKingdom)
        {
            var snapshot = new CourtSnapshot();
            if (pKingdom?.data == null) return snapshot;

            string fallbackMode = HasOfficialCourt(pKingdom) ? "official" : HasPrimitiveCourt(pKingdom) ? "primitive" : "";
            pKingdom.data.get(LineageKeys.COURT_MODE, out snapshot.mode, fallbackMode);
            pKingdom.data.get(LineageKeys.COURT_DOMINANT_SCHOOL, out snapshot.dominant_school, "");
            pKingdom.data.get(LineageKeys.COURT_SECONDARY_SCHOOL, out snapshot.secondary_school, "");
            pKingdom.data.get(LineageKeys.COURT_FACTION_CACHE, out snapshot.faction_cache, "");
            pKingdom.data.get(LineageKeys.COURT_EFFICIENCY, out snapshot.efficiency, 0f);
            pKingdom.data.get(LineageKeys.COURT_CONCENTRATION, out snapshot.concentration, 0f);
            return snapshot;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            if (!KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom)) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.COURT_LAST_REFRESH_YEAR, out int lastYear, -1);
            if (!CourtRules.ShouldRefreshCourt(year, lastYear, CourtRules.DefaultRefreshIntervalYears)) return;
            pKingdom.data.set(LineageKeys.COURT_LAST_REFRESH_YEAR, year);

            string targetMode = HasOfficialCourt(pKingdom) ? "official" : "primitive";
            pKingdom.data.get(LineageKeys.COURT_MODE, out string previousMode, "");
            if (previousMode != targetMode)
            {
                pKingdom.data.set(LineageKeys.COURT_MODE, targetMode);
                ChronicleEvents.OnCourtFounded(pKingdom, targetMode == "official");
            }

            List<Actor> yearRoster = CourtRules.ShouldUseSingleYearRoster(CourtRules.CentralOfficeCount)
                ? BuildYearRoster(pKingdom)
                : null;

            long benchmark = UpdateAgeBenchmark.Begin();
            try { ValidateOfficers(pKingdom, yearRoster); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtOfficerValidateIndex, benchmark); }

            HashSet<string> occupiedOffices = BuildActiveOfficeSet(pKingdom, yearRoster);
            benchmark = UpdateAgeBenchmark.Begin();
            try { EnsureMinimumCourt(pKingdom, yearRoster, occupiedOffices); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtCandidateRefreshIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { RecalculateFactionCache(pKingdom, yearRoster); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtFactionRecalcIndex, benchmark); }

            UpsertCourtSnapshot(pKingdom);
        }

        private static void ValidateOfficers(Kingdom pKingdom, List<Actor> pRoster)
        {
            foreach (Actor actor in RosterOrSafeUnits(pKingdom, pRoster))
            {
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                if (courtKingdomId != pKingdom.id) continue;

                bool valid = CourtRules.CanHoldOffice(
                    alive: actor.isAlive() && !actor.isRekt(),
                    sameKingdom: actor.kingdom == pKingdom,
                    slave: actor.hasTrait(LineageKeys.TRAIT_SLAVE),
                    madness: actor.hasTrait("madness"));
                if (valid) SyncSchoolTrait(actor, active: true);
                else ClearOfficer(actor, "invalid");
            }
        }

        private static void EnsureMinimumCourt(Kingdom pKingdom, List<Actor> pRoster, HashSet<string> pOccupiedOffices)
        {
            if (!HasOfficialCourt(pKingdom) && !HasPrimitiveCourt(pKingdom)) return;

            AssignKingIfEmpty(pKingdom, pOccupiedOffices);
            if (!HasOfficialCourt(pKingdom)) return;

            FillCentralOffice(pKingdom, pRoster, pOccupiedOffices, CourtOfficeId.Chancellor, CourtSchoolId.Ru);
            FillCentralOffice(pKingdom, pRoster, pOccupiedOffices, CourtOfficeId.Censor, CourtSchoolId.Legalist);
            FillCentralOffice(pKingdom, pRoster, pOccupiedOffices, CourtOfficeId.Marshal, CourtSchoolId.Military);
            FillCentralOffice(pKingdom, pRoster, pOccupiedOffices, CourtOfficeId.Justice, CourtSchoolId.Legalist);
            FillCentralOffice(pKingdom, pRoster, pOccupiedOffices, CourtOfficeId.Steward, CourtSchoolId.Agrarian);
            FillCentralOffice(pKingdom, pRoster, pOccupiedOffices, CourtOfficeId.Erudite, CourtSchoolId.Ru);
        }

        private static void AssignKingIfEmpty(Kingdom pKingdom, HashSet<string> pOccupiedOffices)
        {
            Actor king = pKingdom.king;
            if (king?.data == null || king.isRekt()) return;
            king.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            if (!string.IsNullOrEmpty(office)) return;
            SetOfficer(king, pKingdom, CourtOfficeLayer.Primitive, "king_council", CourtSchoolId.PrimitiveMinister, null);
            pOccupiedOffices?.Add("king_council");
        }

        private static void FillCentralOffice(Kingdom pKingdom, List<Actor> pRoster,
            HashSet<string> pOccupiedOffices, string pOfficeId, string pPreferredSchool)
        {
            if (pOccupiedOffices != null && pOccupiedOffices.Contains(pOfficeId)) return;
            if (pOccupiedOffices == null && HasActiveOffice(pKingdom, pOfficeId)) return;

            Actor candidate = FindBestCandidate(pKingdom, pRoster, pOfficeId, pPreferredSchool);
            if (candidate == null) return;
            SetOfficer(candidate, pKingdom, CourtOfficeLayer.Central, pOfficeId, pPreferredSchool, null);
            pOccupiedOffices?.Add(pOfficeId);
        }

        private static Actor FindBestCandidate(Kingdom pKingdom, List<Actor> pRoster,
            string pOfficeId, string pPreferredSchool)
        {
            Actor best = null;
            float bestScore = -1f;
            int seen = 0;

            foreach (Actor actor in RosterOrSafeUnits(pKingdom, pRoster))
            {
                if (++seen > CandidateLimit * 8) break;
                if (actor?.data == null || actor.isRekt()) continue;
                if (!CourtRules.CanHoldOffice(actor.isAlive(), actor.kingdom == pKingdom,
                        actor.hasTrait(LineageKeys.TRAIT_SLAVE), actor.hasTrait("madness"))) continue;

                actor.data.get(LineageKeys.COURT_OFFICE_ID, out string currentOffice, "");
                if (!string.IsNullOrEmpty(currentOffice)) continue;

                float score = ScoreCandidate(actor, pOfficeId, pPreferredSchool);
                if (score <= bestScore) continue;
                best = actor;
                bestScore = score;
            }

            return best;
        }

        private static float ScoreCandidate(Actor pActor, string pOfficeId, string pPreferredSchool)
        {
            float stewardship = SafeStat(pActor, "stewardship");
            float diplomacy = SafeStat(pActor, "diplomacy");
            float warfare = SafeStat(pActor, "warfare");
            float intelligence = SafeStat(pActor, "intelligence");
            float score = intelligence + stewardship;

            if (pOfficeId == CourtOfficeId.Marshal) score += warfare * 2f;
            if (pOfficeId == CourtOfficeId.Chancellor || pOfficeId == CourtOfficeId.Censor) score += diplomacy;
            if (ChronicleGate.IsNobleActor(pActor)) score += 4f;

            pActor.data.get(LineageKeys.COURT_SCHOOL, out string naturalSchool, "");
            if (naturalSchool == pPreferredSchool) score += 6f;
            return score;
        }

        private static void SetOfficer(Actor pActor, Kingdom pKingdom, string pLayer, string pOfficeId, string pSchoolId, City pCity)
        {
            if (pActor?.data == null || pKingdom?.data == null) return;

            pActor.data.set(LineageKeys.COURT_KINGDOM_ID, pKingdom.id);
            pActor.data.set(LineageKeys.COURT_LAYER, pLayer ?? "");
            pActor.data.set(LineageKeys.COURT_OFFICE_ID, pOfficeId ?? "");
            pActor.data.set(LineageKeys.COURT_SCHOOL, pSchoolId ?? "");
            pActor.data.set(LineageKeys.COURT_CITY_ID, pCity?.data?.id ?? -1L);
            SyncSchoolTrait(pActor, active: true);
            ChronicleEvents.OnCourtOfficerAppointed(pActor, pKingdom, pOfficeId ?? "", pSchoolId ?? "");
        }

        private static void ClearOfficer(Actor pActor, string pReason)
        {
            if (pActor?.data == null) return;

            SyncSchoolTrait(pActor, active: false);
            pActor.data.set(LineageKeys.COURT_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.COURT_LAYER, "");
            pActor.data.set(LineageKeys.COURT_OFFICE_ID, "");
            pActor.data.set(LineageKeys.COURT_SCHOOL, "");
            pActor.data.set(LineageKeys.COURT_CITY_ID, -1L);
        }

        private static void SyncSchoolTrait(Actor pActor, bool active)
        {
            if (pActor?.data == null) return;

            pActor.data.get(LineageKeys.COURT_SCHOOL, out string school, "");
            foreach (string traitId in AllSchoolTraits())
            {
                if (string.IsNullOrEmpty(traitId)) continue;
                if (active && traitId == CourtTraitRules.TraitForSchool(school))
                {
                    if (!pActor.hasTrait(traitId)) pActor.addTrait(traitId);
                }
                else if (pActor.hasTrait(traitId))
                {
                    pActor.removeTrait(traitId);
                }
            }
        }

        private static void RecalculateFactionCache(Kingdom pKingdom, List<Actor> pRoster)
        {
            var values = new Dictionary<string, float>();
            foreach (Actor actor in RosterOrSafeUnits(pKingdom, pRoster))
            {
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                if (courtKingdomId != pKingdom.id) continue;

                actor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
                actor.data.get(LineageKeys.COURT_SCHOOL, out string school, "");
                if (string.IsNullOrEmpty(school)) continue;

                float influence = CourtInfluenceRules.InfluenceWeight(layer,
                    ChronicleGate.IsImportant(actor), GeneralService.GetMerit(actor));
                values.TryGetValue(school, out float old);
                values[school] = old + influence;
            }

            string[] schools = values.Keys.ToArray();
            float[] influenceValues = schools.Select(s => values[s]).ToArray();
            string encoded = CourtStateCodec.EncodeFactionCache(schools, influenceValues);
            string dominant = CourtInfluenceRules.DominantSchool(encoded, "");
            pKingdom.data.get(LineageKeys.COURT_DOMINANT_SCHOOL, out string previousDominant, "");
            float total = influenceValues.Sum();
            float dominantValue = string.IsNullOrEmpty(dominant) || !values.ContainsKey(dominant) ? 0f : values[dominant];

            pKingdom.data.set(LineageKeys.COURT_FACTION_CACHE, encoded);
            pKingdom.data.set(LineageKeys.COURT_DOMINANT_SCHOOL, dominant);
            pKingdom.data.set(LineageKeys.COURT_CONCENTRATION, CourtInfluenceRules.Concentration(dominantValue, total));
            pKingdom.data.set(LineageKeys.COURT_EFFICIENCY, total <= 0f ? 0f : Math.Min(100f, 35f + total * 3f));
            pKingdom.data.set(LineageKeys.COURT_MODE, HasOfficialCourt(pKingdom) ? "official" : "primitive");
            if (!string.IsNullOrEmpty(dominant) && previousDominant != dominant)
                ChronicleEvents.OnCourtFactionDominant(pKingdom, dominant);
        }

        private static void UpsertCourtSnapshot(Kingdom pKingdom)
        {
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null || pKingdom?.data == null) return;

            CourtSnapshot s = GetSnapshot(pKingdom);
            pKingdom.data.get(LineageKeys.COURT_LAST_REFRESH_YEAR, out int lastRefresh, -1);
            pKingdom.data.get(LineageKeys.COURT_LAST_CANDIDATE_YEAR, out int lastCandidate, -1);
            pKingdom.data.get(LineageKeys.COURT_LAST_STRONG_EVENT_YEAR, out int lastStrong, -1);
            var values = new[]
            {
                ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                ColumnVal.Create("COURT_MODE", s.mode ?? ""),
                ColumnVal.Create("DOMINANT_SCHOOL", s.dominant_school ?? ""),
                ColumnVal.Create("SECONDARY_SCHOOL", s.secondary_school ?? ""),
                ColumnVal.Create("COURT_EFFICIENCY", (double)s.efficiency),
                ColumnVal.Create("FACTION_CONCENTRATION", (double)s.concentration),
                ColumnVal.Create("FACTION_CACHE", s.faction_cache ?? ""),
                ColumnVal.Create("LAST_REFRESH_YEAR", lastRefresh),
                ColumnVal.Create("LAST_CANDIDATE_REFRESH_YEAR", lastCandidate),
                ColumnVal.Create("LAST_STRONG_EVENT_YEAR", lastStrong),
                ColumnVal.Create("UPDATED_TIME", LineageService.CurTime())
            };

            try
            {
                string table = KingdomCourtStateTableItem.GetTableName();
                if (db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdom.id)))
                {
                    db.UpdateValue(table,
                        new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdom.id) },
                        values);
                }
                else
                {
                    var insert = new List<ColumnVal> { ColumnVal.Create("KINGDOM_ID", pKingdom.id) };
                    insert.AddRange(values);
                    db.Insert(table, insert.ToArray());
                }
            }
            catch (Exception e)
            {
                AncientWarfare3.ModClass.LogWarning("KingdomCourtState upsert failed: " + e.Message);
            }
        }

        private static List<Actor> BuildYearRoster(Kingdom pKingdom)
        {
            return SafeUnits(pKingdom).ToList();
        }

        private static HashSet<string> BuildActiveOfficeSet(Kingdom pKingdom, List<Actor> pRoster)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (Actor actor in RosterOrSafeUnits(pKingdom, pRoster))
            {
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                if (courtKingdomId != pKingdom.id) continue;
                actor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
                if (!string.IsNullOrEmpty(office)) result.Add(office);
            }
            return result;
        }

        private static IEnumerable<Actor> RosterOrSafeUnits(Kingdom pKingdom, List<Actor> pRoster)
        {
            return pRoster ?? SafeUnits(pKingdom);
        }

        private static bool HasActiveOffice(Kingdom pKingdom, string pOfficeId)
        {
            foreach (Actor actor in SafeUnits(pKingdom))
            {
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                actor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
                if (courtKingdomId == pKingdom.id && office == pOfficeId) return true;
            }
            return false;
        }

        private static IEnumerable<Actor> SafeUnits(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) yield break;

            IEnumerable<Actor> units;
            try { units = pKingdom.getUnits(); }
            catch { yield break; }

            foreach (Actor unit in units)
                if (unit?.data != null) yield return unit;
        }

        private static IEnumerable<string> AllSchoolTraits()
        {
            yield return CourtTraitId.Ru;
            yield return CourtTraitId.Legalist;
            yield return CourtTraitId.Dao;
            yield return CourtTraitId.Mohist;
            yield return CourtTraitId.Military;
            yield return CourtTraitId.Diplomat;
            yield return CourtTraitId.Agrarian;
            yield return CourtTraitId.YinYang;
            yield return CourtTraitId.Logician;
        }

        private static float SafeStat(Actor pActor, string pStat)
        {
            try { return pActor?.stats?[pStat] ?? 0f; }
            catch { return 0f; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data.SQLite;
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
        public float livelihood;
        public float aggression;
        public float peace;
    }

    internal sealed class CourtOfficerView
    {
        public long actor_id = -1L;
        public string actor_name = "";
        public string office_id = "";
        public string school_id = "";
        public string layer = "";
        public long city_id = -1L;
        public float influence;
        public int appointed_year = -1;
    }

    internal sealed class CityBureauView
    {
        public string city_name = "";
        public int office_slots;
        public string local_school = "";
        public float efficiency;
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

        public static bool HasThreeDepartments(Kingdom pKingdom)
        {
            return HasOfficialCourt(pKingdom) &&
                   KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Tech, "aw_tech_three_departments");
        }

        public static string ResolveTier(Kingdom pKingdom)
        {
            return CourtTierRules.ResolveTier(HasOfficialCourt(pKingdom), HasThreeDepartments(pKingdom));
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
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_LIVELIHOOD, out snapshot.livelihood, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_AGGRESSION, out snapshot.aggression, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_PEACE, out snapshot.peace, 0.5f);
            return snapshot;
        }

        // 从缓存表读取在任官员，UI 打开时只做一次索引查询，不扫描全国人物。
        public static List<CourtOfficerView> GetActiveOfficers(Kingdom pKingdom, int pLimit)
        {
            var result = new List<CourtOfficerView>();
            var db = CourtDB;
            if (db == null || pKingdom?.data == null) return result;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText = "SELECT ACTOR_NAME, OFFICE_ID, SCHOOL_ID, LAYER, CITY_ID, INFLUENCE, ACTOR_ID, APPOINTED_YEAR FROM " +
                    CourtOfficerTableItem.GetTableName() +
                    " WHERE KINGDOM_ID = @kid AND ACTIVE = 1 ORDER BY INFLUENCE DESC LIMIT @lim";
                cmd.Parameters.AddWithValue("@kid", pKingdom.id);
                cmd.Parameters.AddWithValue("@lim", pLimit <= 0 ? 32 : pLimit);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new CourtOfficerView
                    {
                        actor_name = reader.IsDBNull(0) ? "" : reader.GetValue(0)?.ToString() ?? "",
                        office_id = reader.IsDBNull(1) ? "" : reader.GetValue(1)?.ToString() ?? "",
                        school_id = reader.IsDBNull(2) ? "" : reader.GetValue(2)?.ToString() ?? "",
                        layer = reader.IsDBNull(3) ? "" : reader.GetValue(3)?.ToString() ?? "",
                        city_id = reader.IsDBNull(4) ? -1L : Convert.ToInt64(reader.GetValue(4)),
                        influence = reader.IsDBNull(5) ? 0f : (float)Convert.ToDouble(reader.GetValue(5)),
                        actor_id = reader.IsDBNull(6) ? -1L : Convert.ToInt64(reader.GetValue(6)),
                        appointed_year = reader.IsDBNull(7) ? -1 : Convert.ToInt32(reader.GetValue(7))
                    });
                }
            }
            catch (Exception e) { AncientWarfare3.ModClass.LogWarning("CourtOfficer read failed: " + e.Message); }
            return result;
        }

        // 从缓存表读取地方官署快照，同样只做一次索引查询。
        public static List<CityBureauView> GetCityBureaus(Kingdom pKingdom, int pLimit)
        {
            var result = new List<CityBureauView>();
            var db = CourtDB;
            if (db == null || pKingdom?.data == null) return result;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText = "SELECT CITY_NAME, OFFICE_SLOTS, LOCAL_SCHOOL, BUREAU_EFFICIENCY FROM " +
                    CityBureauStateTableItem.GetTableName() +
                    " WHERE KINGDOM_ID = @kid ORDER BY OFFICE_SLOTS DESC, BUREAU_EFFICIENCY DESC LIMIT @lim";
                cmd.Parameters.AddWithValue("@kid", pKingdom.id);
                cmd.Parameters.AddWithValue("@lim", pLimit <= 0 ? 16 : pLimit);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new CityBureauView
                    {
                        city_name = reader.IsDBNull(0) ? "" : reader.GetValue(0)?.ToString() ?? "",
                        office_slots = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                        local_school = reader.IsDBNull(2) ? "" : reader.GetValue(2)?.ToString() ?? "",
                        efficiency = reader.IsDBNull(3) ? 0f : (float)Convert.ToDouble(reader.GetValue(3))
                    });
                }
            }
            catch (Exception e) { AncientWarfare3.ModClass.LogWarning("CityBureauState read failed: " + e.Message); }
            return result;
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

            // 官场历史层级(原始 → 三公九卿 → 三省六部):升级时记一次朝廷改制史。
            string tier = ResolveTier(pKingdom);
            pKingdom.data.get(LineageKeys.COURT_TIER, out string previousTier, "");
            if (previousTier != tier)
            {
                pKingdom.data.set(LineageKeys.COURT_TIER, tier);
                if (CourtTierRules.IsUpgrade(previousTier, tier))
                    ChronicleEvents.OnCourtTierUpgraded(pKingdom, tier);
            }

            List<Actor> yearRoster = CourtRules.ShouldUseSingleYearRoster(CourtRules.CentralOfficeCount)
                ? BuildYearRoster(pKingdom)
                : null;

            long benchmark = UpdateAgeBenchmark.Begin();
            try { ValidateOfficers(pKingdom, yearRoster, tier); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtOfficerValidateIndex, benchmark); }

            HashSet<string> occupiedOffices = BuildActiveOfficeSet(pKingdom, yearRoster);
            benchmark = UpdateAgeBenchmark.Begin();
            try { EnsureMinimumCourt(pKingdom, yearRoster, occupiedOffices, tier); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtCandidateRefreshIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { RecalculateFactionCache(pKingdom, yearRoster); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtFactionRecalcIndex, benchmark); }

            CourtSnapshot snapshot = GetSnapshot(pKingdom);
            benchmark = UpdateAgeBenchmark.Begin();
            try { RefreshCityBureaus(pKingdom, snapshot); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCityBureauRefreshIndex, benchmark); }

            EvaluateStrongEvent(pKingdom, snapshot);
            UpsertCourtSnapshot(pKingdom);
        }

        private static void ValidateOfficers(Kingdom pKingdom, List<Actor> pRoster, string pTier)
        {
            CloseStaleOfficerRows(pKingdom);
            var tierOffices = new HashSet<string>(CourtTierRules.CentralOfficesForTier(pTier), StringComparer.Ordinal);
            foreach (Actor actor in RosterOrSafeUnits(pKingdom, pRoster))
            {
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                if (courtKingdomId != pKingdom.id) continue;

                actor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
                bool baseValid = CourtRules.CanHoldOffice(
                    alive: actor.isAlive() && !actor.isRekt(),
                    sameKingdom: actor.kingdom == pKingdom,
                    slave: actor.hasTrait(LineageKeys.TRAIT_SLAVE),
                    madness: actor.hasTrait("madness"));
                bool valid = CourtRules.CanHoldLayerOffice(layer, actor.isSexMale(), baseValid);
                if (!valid) { ClearOfficer(actor, "invalid"); continue; }

                // 改制后清退不属于当前层级的中央官(旧三公九卿官在升三省六部后退场)。
                actor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
                if (layer == CourtOfficeLayer.Central && tierOffices.Count > 0 &&
                    !string.IsNullOrEmpty(office) && !tierOffices.Contains(office))
                {
                    ClearOfficer(actor, "reform");
                    continue;
                }
                SyncSchoolTrait(actor, active: true);
            }
        }

        private static void EnsureMinimumCourt(Kingdom pKingdom, List<Actor> pRoster,
            HashSet<string> pOccupiedOffices, string pTier)
        {
            if (!HasOfficialCourt(pKingdom) && !HasPrimitiveCourt(pKingdom)) return;

            AssignKingIfEmpty(pKingdom, pOccupiedOffices);
            if (!HasOfficialCourt(pKingdom)) return;

            foreach (string office in CourtTierRules.CentralOfficesForTier(pTier))
                FillCentralOffice(pKingdom, pRoster, pOccupiedOffices, office,
                    CourtTierRules.PreferredSchoolForOffice(office));
        }

        private static void AssignKingIfEmpty(Kingdom pKingdom, HashSet<string> pOccupiedOffices)
        {
            Actor king = pKingdom.king;
            if (king?.data == null || king.isRekt()) return;
            king.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            if (!string.IsNullOrEmpty(office))
            {
                if (office == "king_council" && HasOfficialCourt(pKingdom))
                {
                    EnsurePersonalSchool(king);
                    SyncSchoolTrait(king, active: true);
                }
                return;
            }
            string initialSchool = EnsurePersonalSchool(king);
            SetOfficer(king, pKingdom, CourtOfficeLayer.Primitive, "king_council", initialSchool, null);
            pOccupiedOffices?.Add("king_council");
        }

        private static void FillCentralOffice(Kingdom pKingdom, List<Actor> pRoster,
            HashSet<string> pOccupiedOffices, string pOfficeId, string pPreferredSchool)
        {
            if (pOccupiedOffices != null && pOccupiedOffices.Contains(pOfficeId)) return;
            if (pOccupiedOffices == null && HasActiveOffice(pKingdom, pOfficeId)) return;

            Actor candidate = FindBestCandidate(pKingdom, pRoster, pOfficeId, pPreferredSchool);
            if (candidate == null) return;
            string school = EnsurePersonalSchool(candidate);
            SetOfficer(candidate, pKingdom, CourtOfficeLayer.Central, pOfficeId, school, null);
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
                bool baseEligible = CourtRules.CanHoldOffice(actor.isAlive(), actor.kingdom == pKingdom,
                    actor.hasTrait(LineageKeys.TRAIT_SLAVE), actor.hasTrait("madness"));
                if (!CourtRules.CanHoldLayerOffice(
                        CourtOfficeLayer.Central, actor.isSexMale(), baseEligible)) continue;

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
            if (pOfficeId == CourtOfficeId.ImperialPhysician)
                score += intelligence * 2f + stewardship * 1.5f;
            if (pOfficeId == CourtOfficeId.ImperialAstrologer)
                score += intelligence * 2f + diplomacy * 1.5f;
            if (ChronicleGate.IsNobleActor(pActor)) score += 4f;

            pActor.data.get(LineageKeys.COURT_SCHOOL, out string naturalSchool, "");
            score += CourtSchoolAssignmentRules.CompatibilityBonus(pOfficeId, naturalSchool);
            return score;
        }

        internal static string EnsurePersonalSchool(Actor pActor)
        {
            if (pActor?.data == null) return CourtSchoolId.None;
            pActor.data.get(LineageKeys.COURT_SCHOOL, out string existingSchool, "");
            string parentSchool = ResolveParentSchool(pActor);
            string citySchool = CitySchoolSnapshotService.GetSnapshot(pActor.city)?.DominantSchool ??
                                CourtSchoolId.None;
            string school = CourtSchoolIdentityRules.Resolve(new CourtSchoolIdentityProfile(
                pActor.data.id, SafeStat(pActor, "stewardship"), SafeStat(pActor, "diplomacy"),
                SafeStat(pActor, "warfare"), SafeStat(pActor, "intelligence"),
                existingSchool, parentSchool, citySchool));
            pActor.data.set(LineageKeys.COURT_SCHOOL, school ?? "");
            SchoolMembershipService.Update(pActor, school);
            return school ?? CourtSchoolId.None;
        }

        private static string ResolveParentSchool(Actor pActor)
        {
            string fallback = "";
            foreach (long parentId in new[] { pActor.data.parent_id_1, pActor.data.parent_id_2 })
            {
                if (parentId < 0) continue;
                Actor parent = World.world?.units?.get(parentId);
                if (parent?.data == null) continue;
                parent.data.get(LineageKeys.COURT_SCHOOL, out string school, "");
                if (string.IsNullOrEmpty(school)) continue;
                if (parent.isSexMale()) return school;
                fallback = school;
            }
            return fallback;
        }

        private static CourtCandidateProfile CandidateProfile(Actor pActor)
        {
            pActor.data.get(LineageKeys.COURT_SCHOOL, out string existingSchool, "");
            return new CourtCandidateProfile(pActor.data.id,
                SafeStat(pActor, "stewardship"), SafeStat(pActor, "diplomacy"),
                SafeStat(pActor, "warfare"), SafeStat(pActor, "intelligence"), existingSchool);
        }

        private static void SetOfficer(Actor pActor, Kingdom pKingdom, string pLayer, string pOfficeId, string pSchoolId, City pCity)
        {
            if (pActor?.data == null || pKingdom?.data == null) return;

            pActor.data.get(LineageKeys.COURT_KINGDOM_ID, out long previousKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string previousOffice, "");
            Kingdom previousKingdom = previousKingdomId >= 0
                ? World.world?.kingdoms?.get(previousKingdomId)
                : null;
            bool clearedPreviousPhysician = false;
            if (!string.IsNullOrEmpty(previousOffice) && previousOffice != pOfficeId)
            {
                previousKingdom ??= pKingdom;
                if (previousKingdom?.data != null)
                {
                    previousKingdom.data.get(LineageKeys.COURT_IMPERIAL_PHYSICIAN_ID,
                        out long cachedPhysicianId, -1L);
                    if (RoyalMedicalCareRules.ShouldClearCachedPhysician(
                            cachedPhysicianId, pActor.data.id, previousOffice))
                    {
                        previousKingdom.data.set(LineageKeys.COURT_IMPERIAL_PHYSICIAN_ID, -1L);
                        clearedPreviousPhysician = true;
                    }
                }
                if (previousKingdom?.data != null)
                    ChronicleEvents.OnCourtOfficerDismissed(pActor, previousKingdom,
                        previousOffice, "reassigned");
            }

            pActor.data.set(LineageKeys.COURT_KINGDOM_ID, pKingdom.id);
            pActor.data.set(LineageKeys.COURT_LAYER, pLayer ?? "");
            pActor.data.set(LineageKeys.COURT_OFFICE_ID, pOfficeId ?? "");
            string personalSchool = EnsurePersonalSchool(pActor);
            pActor.data.set(LineageKeys.COURT_CITY_ID, pCity?.data?.id ?? -1L);
            if (pOfficeId == CourtOfficeId.ImperialPhysician)
                pKingdom.data.set(LineageKeys.COURT_IMPERIAL_PHYSICIAN_ID, pActor.data.id);
            LineageService.EnsureOfficialShiAndClan(pActor);
            SyncSchoolTrait(pActor, active: true);
            bool careerStarted = OfficialCareerService.Appoint(pActor, pKingdom, pLayer ?? "",
                pOfficeId ?? "", personalSchool, pCity);
            if (careerStarted)
                ChronicleEvents.OnCourtOfficerAppointed(pActor, pKingdom, pOfficeId ?? "", personalSchool);
            LineageService.ArchiveActor(pActor, pAlive: true);
            CourtDirectionService.MarkDirty(pKingdom);
            CitySchoolSnapshotService.MarkActorDirty(pActor);
            if (clearedPreviousPhysician && previousKingdom?.data != null)
                RoyalMedicalCareService.ReconcileTargets(previousKingdom);
            if (pOfficeId == CourtOfficeId.ImperialPhysician)
                RoyalMedicalCareService.ReconcileTargets(pKingdom);
        }

        private static void ClearOfficer(Actor pActor, string pReason)
        {
            if (pActor?.data == null) return;

            pActor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            Kingdom courtKingdom = courtKingdomId >= 0 ? World.world?.kingdoms?.get(courtKingdomId) : null;
            bool alive = pActor.isAlive() && !pActor.isRekt();
            if (!alive) LineageService.ArchiveActor(pActor, pAlive: false);

            if (courtKingdom?.data != null)
            {
                courtKingdom.data.get(LineageKeys.COURT_IMPERIAL_PHYSICIAN_ID,
                    out long cachedPhysicianId, -1L);
                if (RoyalMedicalCareRules.ShouldClearCachedPhysician(
                        cachedPhysicianId, pActor.data.id, office))
                    courtKingdom.data.set(LineageKeys.COURT_IMPERIAL_PHYSICIAN_ID, -1L);
            }

            SyncSchoolTrait(pActor, active: false);
            pActor.data.set(LineageKeys.COURT_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.COURT_LAYER, "");
            pActor.data.set(LineageKeys.COURT_OFFICE_ID, "");
            pActor.data.set(LineageKeys.COURT_CITY_ID, -1L);

            OfficialCareerService.End(pActor, layer, office, pReason ?? "");
            if (courtKingdom != null && !string.IsNullOrEmpty(office))
                ChronicleEvents.OnCourtOfficerDismissed(pActor, courtKingdom, office, pReason ?? "");
            if (alive) LineageService.ArchiveActor(pActor, pAlive: true);
            if (courtKingdom?.data != null) CourtDirectionService.MarkDirty(courtKingdom);
            CitySchoolSnapshotService.MarkActorDirty(pActor);
            if (courtKingdom?.data != null && office == CourtOfficeId.ImperialPhysician)
                RoyalMedicalCareService.ReconcileTargets(courtKingdom);
        }

        private static void CloseStaleOfficerRows(Kingdom pKingdom)
        {
            var db = CourtDB;
            if (db == null || pKingdom?.data == null) return;
            var actorIds = new List<long>();
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText = "SELECT ACTOR_ID FROM " + CourtOfficerTableItem.GetTableName() +
                                  " WHERE KINGDOM_ID = @kid AND ACTIVE = 1";
                cmd.Parameters.AddWithValue("@kid", pKingdom.id);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    if (!reader.IsDBNull(0)) actorIds.Add(Convert.ToInt64(reader.GetValue(0)));
            }
            catch (Exception e)
            {
                AncientWarfare3.ModClass.LogWarning("CourtOfficer stale-row read failed: " + e.Message);
                return;
            }

            foreach (long actorId in actorIds)
            {
                Actor actor = World.world?.units?.get(actorId);
                if (actor?.data == null)
                {
                    OfficialCareerService.EndForKingdom(actorId, pKingdom.id, "missing");
                    continue;
                }

                bool alive = actor.isAlive() && !actor.isRekt();
                if (alive && actor.kingdom == pKingdom) continue;
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                string reason = alive ? "defected" : "dead";
                if (courtKingdomId == pKingdom.id) ClearOfficer(actor, reason);
                else OfficialCareerService.EndForKingdom(actorId, pKingdom.id, reason);
            }
        }

        private static void SyncSchoolTrait(Actor pActor, bool active)
        {
            if (pActor?.data == null) return;

            pActor.data.get(LineageKeys.COURT_SCHOOL, out string school, "");
            foreach (string traitId in AllSchoolTraits())
            {
                if (string.IsNullOrEmpty(traitId)) continue;
                if (traitId == CourtTraitRules.TraitForSchool(school))
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
            int factionYear = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.COURT_DOMINANT_SINCE_YEAR, out int prevSinceYear, -1);
            pKingdom.data.set(LineageKeys.COURT_DOMINANT_SINCE_YEAR,
                CourtEventRules.NextDominantSinceYear(factionYear, previousDominant, dominant, prevSinceYear));
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
                ColumnVal.Create("DIRECTION_LIVELIHOOD", (double)s.livelihood),
                ColumnVal.Create("DIRECTION_AGGRESSION", (double)s.aggression),
                ColumnVal.Create("DIRECTION_PEACE", (double)s.peace),
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

        private static SQLiteConnection CourtDB => LineageArchiveManager.Instance?.OperatingDB;

        private static void RecordOfficerAppointment(Actor pActor, Kingdom pKingdom, string pLayer,
            string pOfficeId, string pSchoolId, City pCity)
        {
            var db = CourtDB;
            if (db == null || pActor?.data == null || pKingdom?.data == null) return;
            try
            {
                string table = CourtOfficerTableItem.GetTableName();
                // 每人至多保留一条 active 履历：新任命前关闭旧的。
                if (CourtOfficerRecordRules.ShouldCloseActiveRecord(true))
                    CloseActiveOfficerRows(db, table, pActor.data.id, "reassigned");

                long id = TableIdAllocator.Next(db, table, "OFFICER_ID");
                float influence = CourtInfluenceRules.InfluenceWeight(pLayer,
                    ChronicleGate.IsImportant(pActor), GeneralService.GetMerit(pActor));
                db.Insert(table,
                    ColumnVal.Create("OFFICER_ID", id),
                    ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                    ColumnVal.Create("ACTOR_ID", pActor.data.id),
                    ColumnVal.Create("ACTOR_NAME", pActor.getName() ?? ""),
                    ColumnVal.Create("CITY_ID", pCity?.data?.id ?? -1L),
                    ColumnVal.Create("LAYER", pLayer ?? ""),
                    ColumnVal.Create("OFFICE_ID", pOfficeId ?? ""),
                    ColumnVal.Create("SCHOOL_ID", pSchoolId ?? ""),
                    ColumnVal.Create("INFLUENCE", (double)influence),
                    ColumnVal.Create("APPOINTED_YEAR", Date.getCurrentYear()),
                    ColumnVal.Create("ACTIVE", CourtOfficerRecordRules.ActiveFlag(true)),
                    ColumnVal.Create("END_REASON", ""),
                    ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()));
            }
            catch (Exception e) { AncientWarfare3.ModClass.LogWarning("CourtOfficer insert failed: " + e.Message); }
        }

        private static void CloseOfficerRecord(long pActorId, string pReason)
        {
            var db = CourtDB;
            if (db == null) return;
            try { CloseActiveOfficerRows(db, CourtOfficerTableItem.GetTableName(), pActorId, pReason ?? ""); }
            catch (Exception e) { AncientWarfare3.ModClass.LogWarning("CourtOfficer close failed: " + e.Message); }
        }

        private static void CloseActiveOfficerRows(SQLiteConnection pDb, string pTable, long pActorId, string pReason)
        {
            pDb.UpdateValue(pTable,
                new List<SimpleColumnConstraint>
                {
                    SimpleColumnConstraint.CreateEq("ACTOR_ID", pActorId),
                    SimpleColumnConstraint.CreateEq("ACTIVE", CourtOfficerRecordRules.ActiveFlag(true))
                },
                ColumnVal.Create("ACTIVE", CourtOfficerRecordRules.ActiveFlag(false)),
                ColumnVal.Create("END_REASON", pReason ?? ""),
                ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()));
        }

        private static void RefreshCityBureaus(Kingdom pKingdom, CourtSnapshot pSnapshot)
        {
            var db = CourtDB;
            if (db == null || pKingdom?.data == null) return;
            if (!HasOfficialCourt(pKingdom)) return;

            int year = Date.getCurrentYear();
            float courtEfficiency = pSnapshot?.efficiency ?? 0f;

            IEnumerable<City> cities;
            try { cities = pKingdom.getCities(); }
            catch { return; }
            if (cities == null) return;

            string table = CityBureauStateTableItem.GetTableName();
            foreach (City city in cities)
            {
                if (city?.data == null || city.isRekt()) continue;

                int population = SafeCityPopulation(city);
                int zoneCount = SafeZoneCount(city);
                bool isCapital = pKingdom.capital == city;
                int slots = CourtRules.CityOfficeSlots(population, zoneCount, isCapital);
                int filled = CourtBureauRules.FilledSlots(slots, courtEfficiency);
                CitySchoolSnapshot schoolSnapshot = CitySchoolSnapshotService.GetSnapshot(city);
                string localSchool = schoolSnapshot?.DominantSchool ?? CourtSchoolId.None;
                if (schoolSnapshot == null) CitySchoolSnapshotService.MarkDirty(city);
                float efficiency = CourtBureauRules.BureauEfficiency(slots, filled);

                try { UpsertCityBureau(db, table, pKingdom, city, slots, localSchool, efficiency, year); }
                catch (Exception e) { AncientWarfare3.ModClass.LogWarning("CityBureauState upsert failed: " + e.Message); }
            }
        }

        private static void UpsertCityBureau(SQLiteConnection pDb, string pTable, Kingdom pKingdom, City pCity,
            int pSlots, string pSchool, float pEfficiency, int pYear)
        {
            bool exists = pDb.CheckKeyExist(pTable, SimpleColumnConstraint.CreateEq("CITY_ID", pCity.data.id));
            int prevSlots = -1;
            string prevSchool = "";
            if (exists) ReadBureauPrevious(pDb, pTable, pCity.data.id, out prevSlots, out prevSchool);

            var values = new List<ColumnVal>
            {
                ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                ColumnVal.Create("CITY_NAME", pCity.data.name ?? ""),
                ColumnVal.Create("OFFICE_SLOTS", pSlots),
                ColumnVal.Create("LOCAL_SCHOOL", pSchool ?? ""),
                ColumnVal.Create("BUREAU_EFFICIENCY", (double)pEfficiency),
                ColumnVal.Create("OFFICER_ACTOR_IDS", ""),
                ColumnVal.Create("LAST_REFRESH_YEAR", pYear),
                ColumnVal.Create("UPDATED_TIME", LineageService.CurTime())
            };

            if (exists)
            {
                pDb.UpdateValue(pTable,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("CITY_ID", pCity.data.id) },
                    values.ToArray());
            }
            else
            {
                var insert = new List<ColumnVal> { ColumnVal.Create("CITY_ID", pCity.data.id) };
                insert.AddRange(values);
                pDb.Insert(pTable, insert.ToArray());
            }

            if (CourtBureauRules.ShouldRecordCityBureauChange(prevSlots, pSlots, prevSchool, pSchool))
                ChronicleEvents.OnCourtCityBureau(pKingdom, pCity.data.name ?? "", pSchool ?? "");
        }

        private static void ReadBureauPrevious(SQLiteConnection pDb, string pTable, long pCityId,
            out int pSlots, out string pSchool)
        {
            pSlots = -1;
            pSchool = "";
            try
            {
                using var cmd = new SQLiteCommand(pDb);
                cmd.CommandText = $"SELECT OFFICE_SLOTS, LOCAL_SCHOOL FROM {pTable} WHERE CITY_ID = @cid LIMIT 1";
                cmd.Parameters.AddWithValue("@cid", pCityId);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    pSlots = reader.IsDBNull(0) ? -1 : Convert.ToInt32(reader.GetValue(0));
                    pSchool = reader.IsDBNull(1) ? "" : reader.GetValue(1)?.ToString() ?? "";
                }
            }
            catch { pSlots = -1; pSchool = ""; }
        }

        private static void EvaluateStrongEvent(Kingdom pKingdom, CourtSnapshot pSnapshot)
        {
            if (pKingdom?.data == null || !HasOfficialCourt(pKingdom)) return;

            string dominant = pSnapshot?.dominant_school ?? "";
            if (string.IsNullOrEmpty(dominant)) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.COURT_DOMINANT_SINCE_YEAR, out int sinceYear, -1);
            pKingdom.data.get(LineageKeys.COURT_LAST_STRONG_EVENT_YEAR, out int lastStrong, -1);
            int yearsDominant = sinceYear < 0 ? 0 : Math.Max(0, year - sinceYear);
            float share = pSnapshot?.concentration ?? 0f;
            bool crisis = SafeHasEnemies(pKingdom);
            bool weakKing = IsWeakKing(pKingdom.king);

            if (!CourtEventRules.ShouldFireStrongEvent(year, lastStrong, yearsDominant, share, crisis, weakKing))
                return;

            pKingdom.data.set(LineageKeys.COURT_LAST_STRONG_EVENT_YEAR, year);
            ChronicleEvents.OnCourtReformEvent(pKingdom, dominant);
        }

        private static bool SafeHasEnemies(Kingdom pKingdom)
        {
            try { return pKingdom != null && pKingdom.hasEnemies(); }
            catch { return false; }
        }

        private static bool IsWeakKing(Actor pKing)
        {
            if (pKing?.data == null || !pKing.isAlive() || pKing.isRekt()) return true;
            try { if (pKing.isBaby()) return true; } catch { }
            float ability = SafeStat(pKing, "stewardship") + SafeStat(pKing, "warfare") + SafeStat(pKing, "diplomacy");
            return ability < 18f;
        }

        private static int SafeCityPopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

        private static int SafeZoneCount(City pCity)
        {
            try { return pCity?.countZones() ?? 0; }
            catch { return 0; }
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
            yield return CourtTraitId.Medical;
            yield return CourtTraitId.Syncretist;
            yield return CourtTraitId.Merchant;
            yield return CourtTraitId.Craftsman;
            yield return CourtTraitId.Historian;
        }

        private static float SafeStat(Actor pActor, string pStat)
        {
            try { return pActor?.stats?[pStat] ?? 0f; }
            catch { return 0f; }
        }
    }
}

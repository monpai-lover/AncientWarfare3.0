using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;
using AncientWarfare3.patch;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.court
{
    internal sealed class CourtSnapshot
    {
        public string mode = "";
        public string dominant_school = "";
        public string secondary_school = "";
        public string faction_cache = "";
        public string aristocratic_group_cache = "";
        public float efficiency;
        public float concentration;
        public float livelihood;
        public float war;
        public float aggression;
        public float peace;
        public float order;
        public float commerce;
        public float technology;
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
        public long city_id = -1L;
        public string city_name = "";
        public int office_slots;
        public string local_school = "";
        public float efficiency;
        public string officer_actor_ids = "";
        public string local_template_id = "";
        public bool local_template_manual;
    }

    internal sealed class CourtAppointmentCandidateView
    {
        public long actor_id = -1L;
        public string actor_name = "";
        public string school_id = "";
        public int age;
        public float stewardship;
        public float diplomacy;
        public float warfare;
        public float intelligence;
        public int official_rank;
        public int local_grade = -1;
        public float score;
        public bool is_heir;
        public bool is_city_leader;
        public bool is_general;
    }

    internal sealed class CourtAppointmentCandidateScan
    {
        public readonly long kingdom_id;
        public readonly string office_id;
        public readonly long incumbent_actor_id;
        public readonly long heir_actor_id;
        public readonly string preferred_school_id;
        public readonly bool nine_rank_system;
        public readonly List<long> actor_ids;
        public readonly Dictionary<long, CivilServiceQualificationRecord>
            qualification_by_actor_id;
        public readonly bool qualifications_captured;
        public readonly string layer;
        public readonly long city_id;

        public CourtAppointmentCandidateScan(long pKingdomId, string pOfficeId,
            long pIncumbentActorId, long pHeirActorId, string pPreferredSchoolId,
            List<long> pActorIds, bool pNineRankSystem,
            Dictionary<long, CivilServiceQualificationRecord>
                pQualificationByActorId,
            bool pQualificationsCaptured, string pLayer = CourtOfficeLayer.Central,
            long pCityId = -1L)
        {
            kingdom_id = pKingdomId;
            office_id = pOfficeId ?? "";
            incumbent_actor_id = pIncumbentActorId;
            heir_actor_id = pHeirActorId;
            preferred_school_id = pPreferredSchoolId ?? "";
            nine_rank_system = pNineRankSystem;
            actor_ids = pActorIds ?? new List<long>();
            qualification_by_actor_id = pQualificationByActorId ??
                                      new Dictionary<long,
                                          CivilServiceQualificationRecord>();
            qualifications_captured = pQualificationsCaptured;
            layer = pLayer ?? CourtOfficeLayer.Central;
            city_id = pCityId;
        }
    }

    internal static class CourtService
    {
        private const int CandidateLimit = 24;

        public static bool HasOfficialCourt(Kingdom pKingdom)
        {
            if (!KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom))
                return false;
            if (KingdomPolicyService.GetPolicyProfile(pKingdom) ==
                KingdomPolicyProfileId.WesternGeneral)
                return KingdomPolicyEffectService.Read(pKingdom)
                    .WesternCourtUnlocked;
            return KingdomPolicyService.IsCompleted(pKingdom,
                PolicyNodeKind.Tech, "aw_tech_official_court");
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
            return CourtInstitutionRules.TierForInstitution(
                CourtInstitutionService.GetInstitution(pKingdom));
        }

        public static string[] CentralOfficeIdsForCurrentProfile(
            Kingdom pKingdom)
        {
            return CourtProfileRegistry.CentralOfficeIdsFor(pKingdom);
        }

        public static string[] MilitaryOfficeIdsForCurrentProfile(
            Kingdom pKingdom)
        {
            return CourtProfileRegistry.OfficeIdsForLayer(pKingdom,
                CourtOfficeLayer.Military);
        }

        internal static string ResolveCityOffice(Kingdom pKingdom,
            City pCity)
        {
            return LocalChiefOfficeResolver.ResolveChiefOffice(pKingdom,
                pCity);
        }

        internal static string ResolveBuiltInCityOffice(Kingdom pKingdom,
            City pCity)
        {
            CourtProfileId profile = CourtProfileRegistry.For(pKingdom)?.Id ??
                                      CourtProfileId.None;
            bool feudatorySeat = false;
            if (pCity?.data != null &&
                FeudatoryService.TryGetByCity(pCity.data.id,
                    out FeudatorySnapshot feudatory))
                feudatorySeat = feudatory.SeatCityId == pCity.data.id;
            return CourtCityOfficeRules.Resolve(profile,
                CourtInstitutionService.GetInstitution(pKingdom),
                feudatorySeat);
        }

        internal static bool IsCityLeaderOffice(string pOfficeId)
        {
            return CourtCityOfficeRules.IsCityLeaderOffice(pOfficeId);
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
            pKingdom.data.get(LineageKeys.COURT_ARISTOCRATIC_GROUP_CACHE,
                out snapshot.aristocratic_group_cache, "");
            pKingdom.data.get(LineageKeys.COURT_EFFICIENCY, out snapshot.efficiency, 0f);
            pKingdom.data.get(LineageKeys.COURT_CONCENTRATION, out snapshot.concentration, 0f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_LIVELIHOOD, out snapshot.livelihood, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_WAR, out snapshot.war, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_AGGRESSION, out snapshot.aggression, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_PEACE, out snapshot.peace, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_ORDER, out snapshot.order, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_COMMERCE, out snapshot.commerce, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_TECHNOLOGY, out snapshot.technology, 0.5f);
            return snapshot;
        }

        // 从缓存表读取在任官员，UI 打开时只做一次索引查询，不扫描全国人物。
        public static bool RestoreIdentityContinuity(Kingdom pKingdom)
        {
            var db = CourtDB;
            if (db == null || pKingdom?.data == null) return false;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT COURT_MODE, DOMINANT_SCHOOL, SECONDARY_SCHOOL, COURT_EFFICIENCY, " +
                    $"FACTION_CONCENTRATION, FACTION_CACHE, ARISTOCRATIC_GROUP_CACHE, " +
                    $"DIRECTION_LIVELIHOOD, DIRECTION_WAR, " +
                    $"DIRECTION_AGGRESSION, DIRECTION_PEACE, DIRECTION_ORDER, DIRECTION_COMMERCE, " +
                    $"DIRECTION_TECHNOLOGY, LAST_REFRESH_YEAR, LAST_CANDIDATE_REFRESH_YEAR, " +
                    $"LAST_STRONG_EVENT_YEAR, COURT_PROFILE_ID, INSTITUTION_ID " +
                    $"FROM {KingdomCourtStateTableItem.GetTableName()} " +
                    "WHERE KINGDOM_ID=@k LIMIT 1";
                cmd.Parameters.AddWithValue("@k", pKingdom.id);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                if (!reader.Read()) return false;
                pKingdom.data.set(LineageKeys.COURT_MODE, CourtDbString(reader, 0));
                pKingdom.data.set(LineageKeys.COURT_DOMINANT_SCHOOL, CourtDbString(reader, 1));
                pKingdom.data.set(LineageKeys.COURT_SECONDARY_SCHOOL, CourtDbString(reader, 2));
                pKingdom.data.set(LineageKeys.COURT_EFFICIENCY, CourtDbFloat(reader, 3));
                pKingdom.data.set(LineageKeys.COURT_CONCENTRATION, CourtDbFloat(reader, 4));
                pKingdom.data.set(LineageKeys.COURT_FACTION_CACHE, CourtDbString(reader, 5));
                pKingdom.data.set(LineageKeys.COURT_ARISTOCRATIC_GROUP_CACHE,
                    CourtDbString(reader, 6));
                pKingdom.data.set(LineageKeys.COURT_DIRECTION_LIVELIHOOD, CourtDbFloat(reader, 7));
                pKingdom.data.set(LineageKeys.COURT_DIRECTION_WAR, CourtDbFloat(reader, 8));
                pKingdom.data.set(LineageKeys.COURT_DIRECTION_AGGRESSION, CourtDbFloat(reader, 9));
                pKingdom.data.set(LineageKeys.COURT_DIRECTION_PEACE, CourtDbFloat(reader, 10));
                pKingdom.data.set(LineageKeys.COURT_DIRECTION_ORDER, CourtDbFloat(reader, 11));
                pKingdom.data.set(LineageKeys.COURT_DIRECTION_COMMERCE, CourtDbFloat(reader, 12));
                pKingdom.data.set(LineageKeys.COURT_DIRECTION_TECHNOLOGY, CourtDbFloat(reader, 13));
                pKingdom.data.set(LineageKeys.COURT_LAST_REFRESH_YEAR, CourtDbInt(reader, 14));
                pKingdom.data.set(LineageKeys.COURT_LAST_CANDIDATE_YEAR, CourtDbInt(reader, 15));
                pKingdom.data.set(LineageKeys.COURT_LAST_STRONG_EVENT_YEAR, CourtDbInt(reader, 16));
                string courtProfileId = CourtDbString(reader, 17);
                if (string.IsNullOrEmpty(courtProfileId))
                    courtProfileId = KingdomPolicyProfileRules.ToPersistedId(
                        KingdomPolicyService.GetPolicyProfile(pKingdom));
                pKingdom.data.set(LineageKeys.COURT_PROFILE_ID,
                    courtProfileId);
                string institutionId = CourtDbString(reader, 18);
                if (string.IsNullOrEmpty(institutionId))
                    institutionId = CourtInstitutionService.GetInstitution(
                        pKingdom);
                pKingdom.data.set(LineageKeys.COURT_INSTITUTION,
                    institutionId);
                pKingdom.data.set(LineageKeys.COURT_TIER, ResolveTier(pKingdom));
                pKingdom.data.set(LineageKeys.COURT_DIRECTION_DIRTY, true);
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Kingdom court continuity read failed: " + e.Message);
                return false;
            }
        }

        public static bool HasNineRankSystem(Kingdom pKingdom)
        {
            return HasOfficialCourt(pKingdom) &&
                   KingdomPolicyService.IsCompleted(pKingdom,
                       PolicyNodeKind.Tech, "aw_tech_nine_rank_system");
        }

        private static string CourtDbString(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : Convert.ToString(pReader.GetValue(pIndex)) ?? "";
        }

        private static float CourtDbFloat(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? 0f : Convert.ToSingle(pReader.GetValue(pIndex));
        }

        private static int CourtDbInt(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? -1 : Convert.ToInt32(pReader.GetValue(pIndex));
        }

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

        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            List<CourtOfficerView> officers = GetActiveOfficers(pKingdom,
                int.MaxValue);
            var clearedActors = new HashSet<long>();
            for (int i = 0; i < officers.Count; i++)
            {
                CourtOfficerView row = officers[i];
                if (row == null || row.actor_id < 0) continue;
                Actor actor = null;
                try { actor = World.world?.units?.get(row.actor_id); }
                catch { }

                bool runtimeMatches = false;
                if (actor?.data != null)
                {
                    actor.data.get(LineageKeys.COURT_KINGDOM_ID,
                        out long runtimeKingdomId, -1L);
                    runtimeMatches = runtimeKingdomId == pKingdom.id;
                }

                if (runtimeMatches && clearedActors.Add(row.actor_id))
                {
                    ClearOfficer(actor, "kingdom_fell",
                        pRecordHistory: false);
                    continue;
                }

                OfficialCareerService.EndForOffice(row.actor_id,
                    pKingdom.id, row.layer, row.office_id,
                    "kingdom_fell");
            }
            pKingdom.data.set(LineageKeys.COURT_IMPERIAL_PHYSICIAN_ID, -1L);
            CourtDirectionService.MarkDirty(pKingdom);
        }

        internal static List<CourtOfficerView> GetActiveFeudatoryOfficersAtSeat(
            Kingdom pKingdom, long pSeatCityId, int pLimit = 4)
        {
            var result = new List<CourtOfficerView>();
            SQLiteConnection db = CourtDB;
            if (db == null || pKingdom?.data == null || pSeatCityId < 0)
                return result;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT ACTOR_NAME,OFFICE_ID,SCHOOL_ID," +
                    "LAYER,CITY_ID,INFLUENCE,ACTOR_ID,APPOINTED_YEAR FROM " +
                    CourtOfficerTableItem.GetTableName() +
                    " WHERE KINGDOM_ID=@kingdom AND ACTIVE=1 AND LAYER=@layer " +
                    "AND OFFICE_ID=@office AND CITY_ID=@city " +
                    "ORDER BY ACTOR_ID LIMIT @limit";
                command.Parameters.AddWithValue("@kingdom", pKingdom.id);
                command.Parameters.AddWithValue("@layer", CourtOfficeLayer.Feudatory);
                command.Parameters.AddWithValue("@office", CourtOfficeId.FeudatoryChiefClerk);
                command.Parameters.AddWithValue("@city", pSeatCityId);
                command.Parameters.AddWithValue("@limit", Math.Max(1, pLimit));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    result.Add(new CourtOfficerView
                    {
                        actor_name = CourtDbString(reader, 0),
                        office_id = CourtDbString(reader, 1),
                        school_id = CourtDbString(reader, 2),
                        layer = CourtDbString(reader, 3),
                        city_id = reader.IsDBNull(4) ? -1L :
                            Convert.ToInt64(reader.GetValue(4)),
                        influence = CourtDbFloat(reader, 5),
                        actor_id = reader.IsDBNull(6) ? -1L :
                            Convert.ToInt64(reader.GetValue(6)),
                        appointed_year = CourtDbInt(reader, 7)
                    });
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory seat officer read failed: " +
                                    exception.Message);
            }
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
                cmd.CommandText = "SELECT CITY_ID,CITY_NAME,OFFICE_SLOTS," +
                    "LOCAL_SCHOOL,BUREAU_EFFICIENCY,OFFICER_ACTOR_IDS," +
                    "LOCAL_TEMPLATE_ID,LOCAL_TEMPLATE_MANUAL FROM " +
                    CityBureauStateTableItem.GetTableName() +
                    " WHERE KINGDOM_ID = @kid ORDER BY OFFICE_SLOTS DESC, BUREAU_EFFICIENCY DESC LIMIT @lim";
                cmd.Parameters.AddWithValue("@kid", pKingdom.id);
                cmd.Parameters.AddWithValue("@lim", pLimit <= 0 ? 16 : pLimit);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new CityBureauView
                    {
                        city_id = reader.IsDBNull(0) ? -1L :
                            Convert.ToInt64(reader.GetValue(0)),
                        city_name = reader.IsDBNull(1) ? "" :
                            reader.GetValue(1)?.ToString() ?? "",
                        office_slots = reader.IsDBNull(2) ? 0 :
                            Convert.ToInt32(reader.GetValue(2)),
                        local_school = reader.IsDBNull(3) ? "" :
                            reader.GetValue(3)?.ToString() ?? "",
                        efficiency = reader.IsDBNull(4) ? 0f :
                            (float)Convert.ToDouble(reader.GetValue(4)),
                        officer_actor_ids = reader.IsDBNull(5) ? "" :
                            reader.GetValue(5)?.ToString() ?? "",
                        local_template_id = reader.IsDBNull(6) ? "" :
                            reader.GetValue(6)?.ToString() ?? "",
                        local_template_manual = !reader.IsDBNull(7) &&
                            Convert.ToInt32(reader.GetValue(7)) != 0
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
            if (!LineageArchiveManager.Instance.IsOperational) return;
            ChronicleEvents.EnsureCurrentRulerRecorded(pKingdom);
            CourtInstitutionService.Refresh(pKingdom, pRecordHistory: true);

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

            // 官场历史层级(东周六卿 → 三公九卿 → 三省六部):升级时记一次朝廷改制史。
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

            benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                if (IsWesternElective(pKingdom))
                {
                    WesternCourtElectionService.QueueKingdomVacancies(pKingdom);
                }
                else
                {
                    HashSet<string> occupiedOffices = BuildActiveOfficeSet(
                        pKingdom, yearRoster);
                    HashSet<long> occupiedActors =
                        BuildActiveOfficerActorSet();
                    EnsureMinimumCourt(pKingdom, yearRoster, occupiedOffices,
                        tier, occupiedActors);
                }
            }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtCandidateRefreshIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            List<CourtOfficerView> activeOfficers = GetActiveOfficers(pKingdom, 96);
            try
            {
                RecalculateFactionCache(pKingdom, yearRoster,
                    activeOfficers);
                CourtAristocraticGroupService.Refresh(pKingdom, activeOfficers);
            }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtFactionRecalcIndex, benchmark); }

            CourtSnapshot snapshot = GetSnapshot(pKingdom);
            benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                CityBureauAnnualWorkService.Schedule(pKingdom,
                    snapshot?.efficiency ?? 0f);
            }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCityBureauRefreshIndex, benchmark); }

            EvaluateStrongEvent(pKingdom, snapshot);
            UpsertCourtSnapshot(pKingdom);
        }

        public static void FillVacanciesAfterCivilServiceExam(
            Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                !KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom) ||
                !LineageArchiveManager.Instance.IsOperational) return;

            string tier = ResolveTier(pKingdom);
            List<Actor> roster = BuildYearRoster(pKingdom);
            ValidateOfficers(pKingdom, roster, tier);
            if (IsWesternElective(pKingdom))
            {
                WesternCourtElectionService.QueueKingdomVacancies(pKingdom);
                return;
            }
            HashSet<string> occupied = BuildActiveOfficeSet(pKingdom, roster);
            HashSet<long> occupiedActors = BuildActiveOfficerActorSet();
            EnsureMinimumCourt(pKingdom, roster, occupied, tier,
                occupiedActors, pAllowActing: false);
            SchoolGuestOfficeService.FillVacanciesAfterCivilServiceExam(
                pKingdom, pAllowActing: false);
            SchoolGuestOfficeService.FillVacanciesAfterCivilServiceExam(
                pKingdom, pAllowActing: true);
            occupied = BuildActiveOfficeSet(pKingdom, roster);
            occupiedActors = BuildActiveOfficerActorSet();
            EnsureMinimumCourt(pKingdom, roster, occupied, tier,
                occupiedActors, pAllowActing: true);
            AW_CityLeaderPatch.FillVacanciesAfterCivilServiceExam(pKingdom);
            CourtAristocraticGroupService.Refresh(pKingdom,
                GetActiveOfficers(pKingdom, 96));
        }

        internal static CourtImmediateVacancyOutcome
            FillCentralVacanciesImmediately(Kingdom pKingdom,
                out int pChangedCount)
        {
            pChangedCount = 0;
            if (pKingdom?.data == null || pKingdom.isRekt())
                return CourtImmediateVacancyOutcome.InvalidKingdom;
            if (!KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom) ||
                !LineageArchiveManager.Instance.IsOperational ||
                (!HasOfficialCourt(pKingdom) && !HasPrimitiveCourt(pKingdom)))
                return CourtImmediateVacancyOutcome.Unavailable;

            string tier = ResolveTier(pKingdom);
            List<Actor> roster = BuildYearRoster(pKingdom);
            ValidateOfficers(pKingdom, roster, tier);

            if (CourtImmediateVacancyModeRules.Resolve(
                    IsWesternElective(pKingdom)) ==
                CourtImmediateVacancyMode.QueueWesternElection)
            {
                pChangedCount = WesternCourtElectionService
                    .QueueKingdomVacancies(pKingdom);
                return CourtImmediateVacancyModeRules.ShouldReportQueued(
                        pChangedCount)
                    ? CourtImmediateVacancyOutcome.Queued
                    : CourtImmediateVacancyOutcome.NoChange;
            }

            HashSet<string> occupied = BuildActiveOfficeSet(pKingdom, roster);
            HashSet<long> occupiedActors = BuildActiveOfficerActorSet();
            pChangedCount = EnsureMinimumCourt(pKingdom, roster, occupied,
                tier, occupiedActors);
            if (pChangedCount <= 0)
                return CourtImmediateVacancyOutcome.NoChange;

            CourtAristocraticGroupService.Refresh(pKingdom,
                GetActiveOfficers(pKingdom, 96));
            UpsertCourtSnapshot(pKingdom);
            return CourtImmediateVacancyOutcome.Filled;
        }

        private static void ValidateOfficers(Kingdom pKingdom, List<Actor> pRoster, string pTier)
        {
            CloseStaleOfficerRows(pKingdom);
            var tierOffices = new HashSet<string>(
                CentralOfficeIdsForCurrentProfile(pKingdom),
                StringComparer.Ordinal);
            foreach (Actor actor in RosterOrSafeUnits(pKingdom, pRoster))
            {
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                if (courtKingdomId != pKingdom.id) continue;

                actor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
                actor.data.get(LineageKeys.OFFICER_WAITING_SINCE_YEAR,
                    out int actingSinceYear, -1);
                if (CivilServiceExamRules.ShouldExpireActingCentralOfficial(
                        layer, actingSinceYear, Date.getCurrentYear()))
                {
                    ClearOfficer(actor, "acting_term_ended",
                        pRecordHistory: false);
                    continue;
                }
                bool baseValid = RoyalAsylumRules.CanPerformProtectedRole(
                                     RoyalAsylumService.IsActive(actor)) &&
                                 CourtRules.CanHoldOffice(
                    alive: actor.isAlive() && !actor.isRekt(),
                    sameKingdom: CourtAffiliationResolver.CanServe(actor, pKingdom, layer),
                    slave: actor.hasTrait(LineageKeys.TRAIT_SLAVE),
                    madness: actor.hasTrait("madness"));
                bool valid = CourtRules.CanHoldLayerOffice(layer, actor.isSexMale(), baseValid);
                if (!valid) { ClearOfficer(actor, "invalid"); continue; }

                // 改制后清退不属于当前层级的中央官(旧三公九卿官在升三省六部后退场)。
                actor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
                if (layer == CourtOfficeLayer.Central && tierOffices.Count > 0 &&
                    !string.IsNullOrEmpty(office) && !tierOffices.Contains(office))
                {
                    HistoricalSchoolAffiliationSnapshot affiliation =
                        HistoricalAffiliationService.Get(actor.data.id);
                    if (affiliation?.LifecycleState ==
                            HistoricalSchoolLifecycleState.Serving &&
                        affiliation.ServiceKingdomId == pKingdom.id)
                    {
                        SchoolGuestOfficeService.EndGuestOfficer(actor, pKingdom,
                            "reform", Date.getCurrentYear());
                        continue;
                    }
                    ClearOfficer(actor, "reform");
                    continue;
                }
                SyncSchoolTrait(actor, active: true);
            }
        }

        private static int EnsureMinimumCourt(Kingdom pKingdom, List<Actor> pRoster,
            HashSet<string> pOccupiedOffices, string pTier,
            HashSet<long> pUnavailableActorIds,
            bool pAllowActing = true)
        {
            if (!HasOfficialCourt(pKingdom) && !HasPrimitiveCourt(pKingdom)) return 0;

            EnsureKingProjection(pKingdom);
            int filledCount = 0;

            List<Actor> indexedFormalCandidates =
                CivilServiceQualificationService.HasExaminationSystem(pKingdom)
                    ? BuildIndexedFormalCandidateRoster(pKingdom)
                    : null;

            foreach (string office in
                     CentralOfficeIdsForCurrentProfile(pKingdom))
                if (FillCentralOffice(pKingdom, pRoster, pOccupiedOffices, office,
                    CourtProfileRegistry.PreferredSchoolFor(
                        pKingdom, office),
                    indexedFormalCandidates, pUnavailableActorIds,
                    pAllowActing)) filledCount++;

            foreach (string office in
                     MilitaryOfficeIdsForCurrentProfile(pKingdom))
                if (FillCentralOffice(pKingdom, pRoster, pOccupiedOffices, office,
                    CourtProfileRegistry.PreferredSchoolFor(
                        pKingdom, office),
                    indexedFormalCandidates, pUnavailableActorIds,
                    pAllowActing, CourtOfficeLayer.Military)) filledCount++;
            return filledCount;
        }

        private static void EnsureKingProjection(Kingdom pKingdom)
        {
            Actor king = pKingdom.king;
            if (king?.data == null || king.isRekt()) return;
            ClearOfficeForReignTransition(king, "became_king");
            EnsurePersonalSchool(king);
            SyncSchoolTrait(king, active: true);
        }

        internal static bool IsWesternElective(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   KingdomPolicyService.GetPolicyProfile(pKingdom) ==
                       KingdomPolicyProfileId.WesternGeneral &&
                   KingdomPolicyEffectService.Read(pKingdom)
                       .ElectiveTermsUnlocked;
        }

        internal static bool IsWesternElectiveCentralOffice(Kingdom pKingdom,
            string pOfficeId)
        {
            return IsWesternElectiveOffice(pKingdom, pOfficeId,
                CourtOfficeLayer.Central);
        }

        internal static bool IsWesternElectiveOffice(Kingdom pKingdom,
            string pOfficeId, string pLayer)
        {
            return WesternCourtElectionRules.CanQueueVacancy(
                CourtProfileRegistry.IsOfficeAvailableFor(pKingdom,
                    pOfficeId, pLayer), IsWesternElective(pKingdom));
        }

        internal static bool TryOpenWesternElectiveVacancy(Kingdom pKingdom,
            string pOfficeId, out long pFormerIncumbentActorId,
            string pLayer = CourtOfficeLayer.Central)
        {
            pFormerIncumbentActorId = -1L;
            if (!IsWesternElectiveOffice(pKingdom, pOfficeId, pLayer))
                return false;
            CourtOfficerView incumbent = GetActiveOfficers(pKingdom, 96)
                .FirstOrDefault(p => p.layer == pLayer &&
                    p.office_id == pOfficeId);
            if (incumbent == null) return true;
            Actor actor = World.world?.units?.get(incumbent.actor_id);
            if (!IsValidActiveOfficeActor(actor, pKingdom,
                    incumbent.layer, incumbent.office_id))
            {
                CloseDurableOfficerRow(pKingdom, incumbent, actor);
                return ReadActiveCentralOffice(pKingdom, pOfficeId) == null;
            }

            int year = Date.getCurrentYear();
            int termEndYear = ResolveWesternElectiveTermEndYear(pKingdom,
                pOfficeId, incumbent.actor_id, year);
            if (!WesternCourtElectionRules.ShouldQueueVacancy(
                    hasIncumbent: true, termEndYear: termEndYear,
                    currentYear: year)) return false;
            pFormerIncumbentActorId = incumbent.actor_id;
            return TryExpireWesternElectiveOfficial(actor, pKingdom,
                pOfficeId, pLayer);
        }

        internal static bool IsWesternElectiveCentralOfficeVacant(
            Kingdom pKingdom, string pOfficeId)
        {
            return IsWesternElectiveOfficeVacant(pKingdom, pOfficeId,
                CourtOfficeLayer.Central);
        }

        internal static bool IsWesternElectiveOfficeVacant(Kingdom pKingdom,
            string pOfficeId, string pLayer)
        {
            if (!IsWesternElectiveOffice(pKingdom, pOfficeId, pLayer))
                return false;
            return !GetActiveOfficers(pKingdom, 96).Any(p =>
                p.layer == pLayer && p.office_id == pOfficeId);
        }

        internal static int ResolveWesternElectiveTermEndYear(
            Kingdom pKingdom, string pOfficeId, long pActorId,
            int pCurrentYear)
        {
            CourtOfficerView incumbent = ReadActiveCentralOffice(pKingdom,
                pOfficeId);
            if (incumbent != null && incumbent.actor_id == pActorId &&
                incumbent.appointed_year >= 0)
                return WesternCourtElectionRules.TermEndYear(
                    incumbent.appointed_year);
            Actor actor = World.world?.units?.get(pActorId);
            if (actor?.data != null)
            {
                actor.data.get(LineageKeys.OFFICER_TERM_END_YEAR,
                    out int projectedTermEndYear, -1);
                if (projectedTermEndYear >= 0 &&
                    projectedTermEndYear < int.MaxValue)
                    return projectedTermEndYear;
            }
            return WesternCourtElectionRules.TermEndYear(pCurrentYear);
        }

        internal static bool TryExpireWesternElectiveCentralOfficial(
            Actor pActor, Kingdom pKingdom, string pOfficeId)
        {
            return TryExpireWesternElectiveOfficial(pActor, pKingdom,
                pOfficeId, CourtOfficeLayer.Central);
        }

        internal static bool TryExpireWesternElectiveOfficial(Actor pActor,
            Kingdom pKingdom, string pOfficeId, string pLayer)
        {
            if (!IsWesternElectiveOffice(pKingdom, pOfficeId, pLayer) ||
                pActor?.data == null || string.IsNullOrEmpty(pOfficeId))
                return false;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long courtKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            if (courtKingdomId != pKingdom.id || layer != pLayer ||
                office != pOfficeId)
                return false;
            if (CourtAffiliationResolver.IsValidGuestService(
                    pActor, pKingdom))
                return EndGuestOfficer(pActor, pKingdom,
                    "elective_term_ended", Date.getCurrentYear());
            return TryDismissOfficer(pActor, pKingdom, "elective_term_ended");
        }

        internal static List<WesternCourtElectionCandidate>
            BuildWesternElectionCandidates(Kingdom pKingdom,
                string pOfficeId, long pFormerIncumbentActorId, int pLimit)
        {
            return BuildWesternElectionCandidates(pKingdom, pOfficeId,
                pFormerIncumbentActorId, pLimit, CourtOfficeLayer.Central);
        }

        internal static List<WesternCourtElectionCandidate>
            BuildWesternElectionCandidates(Kingdom pKingdom,
                string pOfficeId, long pFormerIncumbentActorId, int pLimit,
                string pLayer)
        {
            var result = new List<WesternCourtElectionCandidate>();
            if (!IsWesternElectiveOffice(pKingdom, pOfficeId, pLayer))
                return result;
            int limit = Math.Max(0, Math.Min(
                WesternCourtElectionRules.MaxCandidatesPerVacancy, pLimit));
            int inspected = 0;
            foreach (Actor actor in SafeUnits(pKingdom).
                         OrderBy(p => p.data.id))
            {
                if (result.Count >= limit || inspected++ >= limit * 8) break;
                bool eligible = IsManualCentralCandidateEligible(actor,
                    pKingdom, pOfficeId, pAllowVacancyPromotion: true,
                    pLayer: pLayer);
                if (!eligible) continue;
                string school = SchoolMembershipService.GetSchool(
                    actor.data.id);
                float familyInfluence = ChronicleGate.IsNobleActor(actor)
                    ? 4f
                    : 0f;
                if (FiefService.GetFiefCityId(actor) >= 0L)
                    familyInfluence += 4f;
                result.Add(new WesternCourtElectionCandidate(actor.data.id,
                    eligible, actor.isKing(),
                    actor.data.id == pFormerIncumbentActorId,
                    ElectionAbility(actor),
                    OfficialCareerStateService.ReadMeritFast(actor),
                    familyInfluence,
                    CourtSchoolAssignmentRules.CompatibilityBonus(pOfficeId,
                        school),
                    CourtAristocraticGroupService.AppointmentPatronageBonus(
                        actor, pKingdom)));
            }
            return result;
        }

        internal static bool TryElectCentralOfficer(Kingdom pKingdom,
            string pOfficeId, Actor pCandidate)
        {
            return TryElectOfficer(pKingdom, pOfficeId, pCandidate,
                CourtOfficeLayer.Central);
        }

        internal static bool TryElectOfficer(Kingdom pKingdom,
            string pOfficeId, Actor pCandidate, string pLayer)
        {
            if (!IsWesternElectiveOfficeVacant(pKingdom, pOfficeId, pLayer) ||
                !IsManualCentralCandidateEligible(pCandidate, pKingdom,
                    pOfficeId, pAllowVacancyPromotion: true,
                    pLayer: pLayer)) return false;
            string school = SchoolMembershipService.GetSchool(
                pCandidate.data.id);
            bool committed = SetOfficer(pCandidate, pKingdom,
                pLayer, pOfficeId, school, null,
                pVacancyPromotion: true);
            if (committed)
                CourtAristocraticGroupService.Refresh(pKingdom,
                    GetActiveOfficers(pKingdom, 96));
            return committed;
        }

        private static float ElectionAbility(Actor pActor)
        {
            return SafeStat(pActor, "stewardship") +
                   SafeStat(pActor, "diplomacy") +
                   SafeStat(pActor, "warfare") +
                   SafeStat(pActor, "intelligence");
        }

        internal static void ClearOfficeForReignTransition(Actor pActor,
            string pReason, bool pPersistCareer = true)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            if (courtKingdomId < 0 && string.IsNullOrEmpty(office)) return;
            ClearOfficer(pActor, pReason ?? "reign_transition",
                pPersistCareer: pPersistCareer);
        }

        private static bool FillCentralOffice(Kingdom pKingdom, List<Actor> pRoster,
            HashSet<string> pOccupiedOffices, string pOfficeId, string pPreferredSchool,
            List<Actor> indexedFormalCandidates,
            HashSet<long> pUnavailableActorIds, bool pAllowActing,
            string pLayer = CourtOfficeLayer.Central)
        {
            if (pOccupiedOffices != null && pOccupiedOffices.Contains(pOfficeId)) return false;
            if (pOccupiedOffices == null && HasActiveOffice(pKingdom, pOfficeId,
                    pLayer)) return false;
            if (SchoolGuestOfficeService.IsOfficeReserved(pKingdom.id, pOfficeId))
                return false;

            bool examinationSystem = CivilServiceQualificationService.
                HasExaminationSystem(pKingdom);
            Actor indexedStrict = examinationSystem
                ? FindBestIndexedFormalCandidate(pKingdom,
                    indexedFormalCandidates, pOfficeId, pPreferredSchool,
                    pAllowVacancyPromotion: false, pUnavailableActorIds, pLayer)
                : null;
            Actor rosterStrict = FindBestCandidate(pKingdom, pRoster,
                pOfficeId, pPreferredSchool,
                pAllowVacancyPromotion: false, pUnavailableActorIds, pLayer);
            Actor candidate = BetterCandidate(pKingdom, indexedStrict,
                rosterStrict, pOfficeId, pPreferredSchool);
            bool vacancyPromotion = false;
            if (candidate == null)
            {
                Actor indexedFallback = examinationSystem
                    ? FindBestIndexedFormalCandidate(pKingdom,
                        indexedFormalCandidates, pOfficeId, pPreferredSchool,
                        pAllowVacancyPromotion: true, pUnavailableActorIds,
                        pLayer)
                    : null;
                Actor rosterFallback = FindBestCandidate(pKingdom, pRoster,
                    pOfficeId, pPreferredSchool,
                    pAllowVacancyPromotion: true, pUnavailableActorIds,
                    pLayer);
                candidate = BetterCandidate(pKingdom, indexedFallback,
                    rosterFallback, pOfficeId, pPreferredSchool);
                vacancyPromotion = candidate != null;
            }
            bool acting = false;
            if (candidate == null)
            {
                Actor educatedCandidate = FindBestActingCentralCandidate(
                    pKingdom, pRoster, pOfficeId, pPreferredSchool,
                    pUnavailableActorIds, pLayer);
                if (!CivilServiceExamRules.ShouldUseActingCentralFallback(
                        allowActing: pAllowActing,
                        hasExaminationSystem:
                        CivilServiceQualificationService.HasExaminationSystem(
                            pKingdom), formalCandidateFound: false,
                        educatedCandidateFound: educatedCandidate != null)) return false;
                candidate = educatedCandidate;
                acting = true;
            }
            if (candidate == null) return false;
            string school = SchoolMembershipService.GetSchool(candidate.data.id);
            if (SetOfficer(candidate, pKingdom, pLayer,
                    pOfficeId, school, null, pActing: acting,
                    pVacancyPromotion: vacancyPromotion))
            {
                pOccupiedOffices?.Add(pOfficeId);
                pUnavailableActorIds?.Add(candidate.data.id);
                return true;
            }
            return false;
        }

        private static List<Actor> BuildIndexedFormalCandidateRoster(
            Kingdom pKingdom)
        {
            var result = new List<Actor>();
            SQLiteConnection db = CourtDB;
            if (db == null || pKingdom?.data == null) return result;
            try
            {
                foreach (long actorId in CivilServiceFormalCandidateQuery.Load(
                             db,
                             CivilServiceExamCandidateTableItem.GetTableName(),
                             CivilServiceExamSessionTableItem.GetTableName(),
                             ActorArchiveTableItem.GetTableName(),
                             CourtOfficerTableItem.GetTableName(), pKingdom.id,
                             CivilServiceExamRules.CandidateSourceLimit))
                {
                    Actor actor = World.world?.units?.get(actorId);
                    if (actor?.data != null) result.Add(actor);
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Civil-service candidate index read failed: " +
                                    exception.Message);
                result.Clear();
            }
            CivilServiceLegacyTransitionService.AppendEligibleCandidates(
                pKingdom, result);
            return result;
        }

        private static Actor FindBestIndexedFormalCandidate(Kingdom pKingdom,
            List<Actor> pCandidates, string pOfficeId, string pPreferredSchool,
            bool pAllowVacancyPromotion,
            HashSet<long> pUnavailableActorIds,
            string pLayer = CourtOfficeLayer.Central)
        {
            if (pCandidates == null || pCandidates.Count == 0) return null;
            Actor best = null;
            float bestScore = -1f;
            bool nineRankSystem = HasNineRankSystem(pKingdom);
            for (int index = 0; index < pCandidates.Count; index++)
            {
                Actor actor = pCandidates[index];
                if (!IsManualCentralCandidateEligible(actor, pKingdom,
                        pOfficeId, pAllowVacancyPromotion,
                        pUnavailableActorIds, pLayer)) continue;
                float score = ScoreCandidate(pKingdom, actor, pOfficeId,
                    pPreferredSchool, nineRankSystem);
                if (score <= bestScore) continue;
                best = actor;
                bestScore = score;
            }
            return best;
        }

        private static Actor FindBestCandidate(Kingdom pKingdom, List<Actor> pRoster,
            string pOfficeId, string pPreferredSchool,
            bool pAllowVacancyPromotion,
            HashSet<long> pUnavailableActorIds,
            string pLayer = CourtOfficeLayer.Central)
        {
            Actor best = null;
            float bestScore = -1f;
            int seen = 0;
            bool nineRankSystem = HasNineRankSystem(pKingdom);

            foreach (Actor actor in RosterOrSafeUnits(pKingdom, pRoster))
            {
                if (++seen > CandidateLimit * 8) break;
                if (!IsManualCentralCandidateEligible(actor, pKingdom,
                        pOfficeId, pAllowVacancyPromotion,
                        pUnavailableActorIds, pLayer)) continue;
                float score = ScoreCandidate(pKingdom, actor, pOfficeId, pPreferredSchool,
                    nineRankSystem);
                if (score <= bestScore) continue;
                best = actor;
                bestScore = score;
            }

            return best;
        }

        private static Actor FindBestActingCentralCandidate(Kingdom pKingdom,
            List<Actor> pRoster, string pOfficeId, string pPreferredSchool,
            HashSet<long> pUnavailableActorIds,
            string pLayer = CourtOfficeLayer.Central)
        {
            Actor best = null;
            float bestScore = -1f;
            int seen = 0;
            bool nineRankSystem = HasNineRankSystem(pKingdom);

            foreach (Actor actor in RosterOrSafeUnits(pKingdom, pRoster))
            {
                if (++seen > CandidateLimit * 8) break;
                if (!IsActingCentralCandidateEligible(actor, pKingdom,
                        pOfficeId, pUnavailableActorIds, pLayer)) continue;
                float score = ScoreCandidate(pKingdom, actor, pOfficeId,
                    pPreferredSchool, nineRankSystem);
                if (score <= bestScore) continue;
                best = actor;
                bestScore = score;
            }

            return best;
        }

        private static Actor BetterCandidate(Kingdom pKingdom, Actor pFirst,
            Actor pSecond, string pOfficeId, string pPreferredSchool)
        {
            if (pFirst?.data == null) return pSecond;
            if (pSecond?.data == null) return pFirst;
            bool nineRankSystem = HasNineRankSystem(pKingdom);
            float firstScore = ScoreCandidate(pKingdom, pFirst, pOfficeId,
                pPreferredSchool, nineRankSystem);
            float secondScore = ScoreCandidate(pKingdom, pSecond, pOfficeId,
                pPreferredSchool, nineRankSystem);
            if (secondScore > firstScore) return pSecond;
            if (firstScore > secondScore) return pFirst;
            return pSecond.data.id < pFirst.data.id ? pSecond : pFirst;
        }

        private static float ScoreCandidate(Kingdom pKingdom, Actor pActor,
            string pOfficeId, string pPreferredSchool, bool pNineRankSystem)
        {
            float stewardship = SafeStat(pActor, "stewardship");
            float diplomacy = SafeStat(pActor, "diplomacy");
            float warfare = SafeStat(pActor, "warfare");
            float intelligence = SafeStat(pActor, "intelligence");
            float score = intelligence + stewardship;

            if (pOfficeId == CourtOfficeId.Marshal || pOfficeId == CourtOfficeId.SiMa)
                score += warfare * 2f;
            if (pOfficeId == CourtOfficeId.Chancellor || pOfficeId == CourtOfficeId.Censor) score += diplomacy;
            if (pOfficeId == CourtOfficeId.ImperialPhysician)
                score += intelligence * 2f + stewardship * 1.5f;
            if (pOfficeId == CourtOfficeId.ImperialAstrologer)
                score += intelligence * 2f + diplomacy * 1.5f;
            if (ChronicleGate.IsNobleActor(pActor)) score += 4f;
            KingdomPolicyEffects effects =
                KingdomPolicyEffectService.Read(pKingdom);
            bool feudalInstitution = string.Equals(
                CourtInstitutionService.GetInstitution(pKingdom),
                CourtInstitutionId.WesternFeudal,
                StringComparison.Ordinal);
            if (effects.FeudalRetainersUnlocked)
            {
                if (ChronicleGate.IsNobleActor(pActor) &&
                    FiefService.GetFiefCityId(pActor) >= 0L) score += 6f;
                if (HistoricalSchoolEducationService.IsEducated(pActor,
                        Date.getCurrentYear())) score += 3f;
            }
            if (feudalInstitution)
            {
                if (ChronicleGate.IsNobleActor(pActor) &&
                    FiefService.GetFiefCityId(pActor) >= 0L) score += 6f;
                if (HistoricalSchoolEducationService.IsEducated(pActor,
                        Date.getCurrentYear())) score += 3f;
            }
            if (pNineRankSystem)
                score += OfficialCareerRankRules.OfficeRankMatchScore(
                    OfficialCareerStateService.ReadRankFast(pActor),
                    OfficialCareerStateService.OfficeGradeForOffice(pOfficeId));
            score += CourtPetitionService.AppointmentFavor(pActor,
                Date.getCurrentYear());
            score += CourtAristocraticGroupService.AppointmentPatronageBonus(
                pActor, pKingdom);
            score += CourtAuxiliaryLawService.AppointmentCultureScore(pKingdom, pActor);
            if (pNineRankSystem)
                score += NineRankRules.AppointmentScore(
                    OfficialCareerStateService.EstimateLocalGradeFast(
                        pActor, pKingdom, pNineRankSystem));

            string naturalSchool = SchoolMembershipService.GetSchool(pActor.data.id);
            return CourtManualAppointmentRules.CandidateScore(score,
                CourtSchoolAssignmentRules.CompatibilityBonus(pOfficeId, naturalSchool));
        }

        internal static CourtManualAppointmentResult ValidateManualAppointmentTarget(
            Kingdom pKingdom, string pOfficeId, long pExpectedIncumbentActorId = -1L)
        {
            return ValidateManualAppointmentTarget(pKingdom, pOfficeId,
                pExpectedIncumbentActorId, CourtOfficeLayer.Central, -1L);
        }

        internal static CourtManualAppointmentResult ValidateManualAppointmentTarget(
            Kingdom pKingdom, string pOfficeId, long pExpectedIncumbentActorId,
            string pLayer, long pCityId)
        {
            if (pKingdom?.data == null || pKingdom.isRekt())
                return CourtManualAppointmentResult.InvalidKingdom;
            if (string.IsNullOrEmpty(pLayer)) pLayer = CourtOfficeLayer.Central;
            bool officeAvailable = IsManualOfficeAvailable(
                pKingdom, pOfficeId, pLayer, pCityId);
            if (!officeAvailable)
                return CourtManualAppointmentResult.InvalidOffice;
            if (!CanUseManualAppointment(pKingdom))
                return CourtManualAppointmentResult.AppointmentNotAllowed;
            Actor incumbent = FindActiveOfficeActor(pKingdom, pOfficeId,
                pLayer, pCityId);
            return CourtManualAppointmentRules.ValidateTarget(
                officeAvailable,
                pExpectedIncumbentActorId, incumbent?.data?.id ?? -1L);
        }

        internal static CourtManualAppointmentResult BeginManualAppointmentScan(
            Kingdom pKingdom, string pOfficeId, long pExpectedIncumbentActorId,
            out CourtAppointmentCandidateScan pScan)
        {
            return BeginManualAppointmentScan(pKingdom, pOfficeId,
                pExpectedIncumbentActorId, CourtOfficeLayer.Central, -1L,
                out pScan);
        }

        internal static CourtManualAppointmentResult BeginManualAppointmentScan(
            Kingdom pKingdom, string pOfficeId, long pExpectedIncumbentActorId,
            string pLayer, long pCityId,
            out CourtAppointmentCandidateScan pScan)
        {
            pScan = null;
            if (pKingdom?.data == null || pKingdom.isRekt())
                return CourtManualAppointmentResult.InvalidKingdom;
            CourtManualAppointmentResult validation =
                ValidateManualAppointmentTarget(pKingdom, pOfficeId,
                    pExpectedIncumbentActorId, pLayer, pCityId);
            if (validation != CourtManualAppointmentResult.Success) return validation;

            Actor incumbent = FindActiveOfficeActor(pKingdom, pOfficeId,
                pLayer, pCityId);
            long incumbentActorId = incumbent?.data?.id ?? -1L;

            var actorIds = new List<long>();
            foreach (Actor actor in SafeUnits(pKingdom)) actorIds.Add(actor.data.id);
            bool examinationSystem = CivilServiceQualificationService.
                HasExaminationSystem(pKingdom);
            Dictionary<long, CivilServiceQualificationRecord> qualifications =
                examinationSystem
                    ? CivilServiceQualificationService.
                        CaptureManualAppointmentQualifications(pKingdom,
                            actorIds)
                    : null;
            Actor heir = HeirService.PeekRegisteredHeir(pKingdom);
            pScan = new CourtAppointmentCandidateScan(pKingdom.id, pOfficeId,
                incumbentActorId, heir?.data?.id ?? -1L,
                CourtProfileRegistry.PreferredSchoolFor(pKingdom, pOfficeId),
                actorIds, HasNineRankSystem(pKingdom), qualifications,
                examinationSystem, pLayer, pCityId);
            return CourtManualAppointmentResult.Success;
        }

        internal static bool TryProjectManualAppointmentCandidate(
            CourtAppointmentCandidateScan pScan, int pActorIndex,
            out CourtAppointmentCandidateView pCandidate)
        {
            pCandidate = null;
            if (pScan == null || pActorIndex < 0 ||
                pActorIndex >= pScan.actor_ids.Count) return false;

            Kingdom kingdom = World.world?.kingdoms?.get(pScan.kingdom_id);
            if (kingdom?.data == null || kingdom.isRekt()) return false;
            long actorId = pScan.actor_ids[pActorIndex];
            if (!CourtManualAppointmentRules.CanChooseCandidate(
                    actorId, pScan.incumbent_actor_id)) return false;
            Actor actor = World.world?.units?.get(actorId);
            pScan.qualification_by_actor_id.TryGetValue(actorId,
                out CivilServiceQualificationRecord qualification);
            if (!IsManualCentralCandidateEligible(actor, kingdom,
                    pScan.office_id,
                    pAllowVacancyPromotion: pScan.incumbent_actor_id < 0,
                    pQualification: qualification,
                    pQualificationsCaptured: pScan.qualifications_captured,
                    pLayer: pScan.layer))
                return false;

            string school = SchoolMembershipService.GetSchool(actorId);
            actor.data.get(LineageKeys.OFFICER_RANK,
                out int persistedOfficialRank, -1);
            pCandidate = new CourtAppointmentCandidateView
            {
                actor_id = actorId,
                actor_name = SafeActorName(actor),
                school_id = school,
                age = SafeActorAge(actor),
                stewardship = SafeStat(actor, "stewardship"),
                diplomacy = SafeStat(actor, "diplomacy"),
                warfare = SafeStat(actor, "warfare"),
                intelligence = SafeStat(actor, "intelligence"),
                official_rank = pScan.nine_rank_system &&
                                persistedOfficialRank > 0
                    ? OfficialCareerRankRules.ClampRank(persistedOfficialRank)
                    : -1,
                local_grade = pScan.nine_rank_system
                    ? OfficialCareerStateService.EstimateLocalGradeFast(actor,
                        kingdom, pScan.nine_rank_system)
                    : -1,
                score = ScoreCandidate(kingdom, actor, pScan.office_id,
                    pScan.preferred_school_id, pScan.nine_rank_system),
                is_heir = actorId == pScan.heir_actor_id,
                is_city_leader = actor.isCityLeader(),
                is_general = GeneralService.IsActiveGeneralFast(actor)
            };
            return true;
        }

        internal static CourtManualAppointmentResult TryManualAppointment(
            long pKingdomId, string pOfficeId, long pActorId,
            long pExpectedIncumbentActorId = -1L)
        {
            return TryManualAppointment(pKingdomId, pOfficeId, pActorId,
                pExpectedIncumbentActorId, CourtOfficeLayer.Central, -1L);
        }

        internal static CourtManualAppointmentResult TryManualAppointment(
            long pKingdomId, string pOfficeId, long pActorId,
            long pExpectedIncumbentActorId, string pLayer, long pCityId)
        {
            Kingdom kingdom = World.world?.kingdoms?.get(pKingdomId);
            if (string.IsNullOrEmpty(pLayer)) pLayer = CourtOfficeLayer.Central;
            if (pLayer == CourtOfficeLayer.Central)
                CloseStaleCentralOfficeRow(kingdom, pOfficeId);
            CourtManualAppointmentResult targetResult =
                ValidateManualAppointmentTarget(kingdom, pOfficeId,
                    pExpectedIncumbentActorId, pLayer, pCityId);
            if (targetResult != CourtManualAppointmentResult.Success)
                return targetResult;

            Actor incumbent = FindActiveOfficeActor(kingdom, pOfficeId,
                pLayer, pCityId);
            Actor actor = World.world?.units?.get(pActorId);
            if (actor?.data == null || actor.isRekt())
                return CourtManualAppointmentResult.InvalidActor;
            if (!CourtManualAppointmentRules.CanChooseCandidate(pActorId,
                    incumbent?.data?.id ?? -1L))
                return CourtManualAppointmentResult.CandidateIneligible;
            bool vacancyPromotion = incumbent?.data == null;
            bool candidateEligible = IsManualCentralCandidateEligible(actor,
                kingdom, pOfficeId, vacancyPromotion, null, pLayer);
            if (!candidateEligible)
                return CourtManualAppointmentResult.CandidateIneligible;
            if (!HasCustomAppointmentPrerequisites(kingdom, pOfficeId))
                return CourtManualAppointmentResult.CandidateIneligible;

            string school = SchoolMembershipService.GetSchool(actor.data.id);
            City city = pCityId >= 0 ? World.world?.cities?.get(pCityId) : null;
            Func<bool> persistAppointment = () => incumbent?.data != null
                ? ReplaceOfficer(incumbent, actor, kingdom, pLayer, pOfficeId,
                    school, city)
                : SetOfficer(actor, kingdom, pLayer, pOfficeId, school, city,
                    pVacancyPromotion: true,
                    pAllowLocalLowerQualification:
                        pLayer == CourtOfficeLayer.City);
            bool authoritativeLocalChief =
                CourtManualAppointmentRules.IsAuthoritativeLocalChiefScope(
                    pLayer, city?.data != null && !city.isRekt(),
                    city?.kingdom == kingdom, pOfficeId,
                    city?.data == null
                        ? null
                        : ResolveCityOffice(kingdom, city));
            bool committed = authoritativeLocalChief
                ? ManualLocalChiefAppointmentService.TryAppoint(kingdom, city,
                    actor, persistAppointment)
                : persistAppointment();
            if (!committed)
                return CourtManualAppointmentResult.PersistenceFailed;
            CourtAristocraticGroupService.Refresh(kingdom, GetActiveOfficers(kingdom, 96));
            return CourtManualAppointmentResult.Success;
        }

        private static bool HasCustomAppointmentPrerequisites(
            Kingdom kingdom, string officeId)
        {
            CustomCourtInstance instance;
            if (!CustomCourtRuntime.TryGetInstance(kingdom, out instance))
                return true;
            var filled = new HashSet<string>(GetActiveOfficers(kingdom, 96)
                .Where(item => item != null && item.actor_id >= 0)
                .Select(item => item.office_id), StringComparer.Ordinal);
            return CustomCourtPrerequisiteRules.HasPrerequisiteOffice(
                instance.ResolvedSnapshot?.Edges, officeId, filled);
        }

        private static bool IsManualCentralCandidateEligible(Actor pActor,
            Kingdom pKingdom, string pOfficeId,
            bool pAllowVacancyPromotion = false,
            HashSet<long> pUnavailableActorIds = null,
            string pLayer = CourtOfficeLayer.Central,
            CivilServiceQualificationRecord pQualification = null,
            bool pQualificationsCaptured = false)
        {
            return IsCentralCandidateEligibleWithoutQualification(pActor,
                       pKingdom, pOfficeId, pUnavailableActorIds, pLayer) &&
                   CivilServiceQualificationService.
                       CanReceiveFormalCivilAppointment(pActor, pKingdom,
                           pLayer, pOfficeId,
                           pAllowVacancyPromotion, pQualification,
                           pQualificationsCaptured);
        }

        private static bool IsActingCentralCandidateEligible(Actor pActor,
            Kingdom pKingdom, string pOfficeId,
            HashSet<long> pUnavailableActorIds,
            string pLayer = CourtOfficeLayer.Central)
        {
            return IsCentralCandidateEligibleWithoutQualification(pActor,
                       pKingdom, pOfficeId, pUnavailableActorIds, pLayer) &&
                   HistoricalSchoolEducationService.IsEducated(pActor,
                       Date.getCurrentYear());
        }

        private static bool IsCentralCandidateEligibleWithoutQualification(
            Actor pActor, Kingdom pKingdom, string pOfficeId,
            HashSet<long> pUnavailableActorIds = null,
            string pLayer = CourtOfficeLayer.Central)
        {
            if (pActor?.data == null || pKingdom?.data == null || pActor.isRekt())
                return false;
            if (!RoyalGuardOfficeRules.CanAppearInOfficeCandidateList(
                    RoyalGuardService.IsRoyalGuard(pActor))) return false;
            if (pUnavailableActorIds != null &&
                pUnavailableActorIds.Contains(pActor.data.id)) return false;
            bool alive = pActor.isAlive();
            bool adult = pActor.isAdult();
            bool male = pActor.isSexMale();
            bool slave = pActor.hasTrait(LineageKeys.TRAIT_SLAVE);
            bool madness = pActor.hasTrait("madness");
            bool king = pActor.isKing();
            pActor.data.get(LineageKeys.COURT_LAYER, out string currentLayer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string currentOffice, "");
            bool hasCentralOffice = currentLayer == pLayer &&
                                    !string.IsNullOrEmpty(currentOffice);
            if (!CourtManualAppointmentRules.CanListCandidate(
                    new CourtManualCandidateFacts(alive, adult, domestic: true,
                        slave, madness, male, royalAsylum: false, king,
                        hasCentralOffice, affiliationAvailable: true))) return false;

            bool otherwiseEligible = CourtManualAppointmentRules.CanListCandidate(
                new CourtManualCandidateFacts(
                    alive,
                    adult: pActor.isAdult(),
                    domestic: CourtAffiliationResolver.IsDomestic(pActor, pKingdom),
                    slave,
                    madness,
                    male,
                    royalAsylum: RoyalAsylumService.IsActive(pActor),
                    king,
                    hasCentralOffice,
                    affiliationAvailable:
                         HistoricalAffiliationService.IsAvailableForOffice(pActor)));
            if (!otherwiseEligible) return false;

            bool westernProfile = CourtProfileRegistry.For(pKingdom)?.Id ==
                                  CourtProfileId.Western;
            bool historicalSchoolEligible = westernProfile ||
                HistoricalSchoolEducationService.CanAppoint(pActor,
                    pKingdom, pLayer, pOfficeId);
            return WesternCourtElectionRules.CanUseLocalCandidate(
                otherwiseEligible, westernProfile, historicalSchoolEligible);
        }

        internal static bool IsManualOfficeInCurrentTier(Kingdom pKingdom,
            string pOfficeId)
        {
            if (string.IsNullOrEmpty(pOfficeId) ||
                !KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom))
                return false;
            return CourtProfileRegistry.IsOfficeAvailableFor(
                pKingdom, pOfficeId, CourtOfficeLayer.Central);
        }

        internal static bool IsManualOfficeAvailable(Kingdom pKingdom,
            string pOfficeId, string pLayer, long pCityId)
        {
            if (!CourtManualAppointmentRules.IsSupportedAppointmentScope(
                    pLayer, pCityId) || string.IsNullOrEmpty(pOfficeId) ||
                !KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom))
                return false;
            if (pLayer == CourtOfficeLayer.Central)
                return IsManualOfficeInCurrentTier(pKingdom, pOfficeId);
            City city = World.world?.cities?.get(pCityId);
            if (city?.data == null || city.isRekt() || city.kingdom != pKingdom)
                return false;
            LocalCourtReadModel local = CourtReadModelService.BuildLocal(
                pKingdom, city);
            return local?.Nodes?.Any(node => node != null &&
                node.OfficeLayer == CourtOfficeLayer.City &&
                node.OfficeId == pOfficeId) == true;
        }

        internal static bool CanUseManualAppointment(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            string institution = CourtInstitutionService.GetInstitution(
                pKingdom);
            return CourtManualAppointmentRules.CanUseManualAppointment(
                institution, KingdomPolicyEffectService.Read(pKingdom)
                    .RoyalAppointmentsUnlocked);
        }

        internal static bool CanPromiseAmnestyOffice(Kingdom pKingdom,
            Actor pLeader, string pOfficeId)
        {
            if (pKingdom?.data == null || pLeader?.data == null ||
                pKingdom.isRekt() || pLeader.isRekt() ||
                !pLeader.isAlive() || !pLeader.isAdult() ||
                !IsManualOfficeInCurrentTier(pKingdom, pOfficeId) ||
                !CanUseManualAppointment(pKingdom) ||
                FindActiveOfficeActor(pKingdom, pOfficeId) != null)
                return false;
            bool slave = pLeader.hasTrait(LineageKeys.TRAIT_SLAVE);
            bool madness = pLeader.hasTrait("madness");
            bool male = pLeader.isSexMale();
            bool candidate = CourtManualAppointmentRules.CanListCandidate(
                new CourtManualCandidateFacts(
                    alive: true, adult: true, domestic: true, slave,
                    madness, male, royalAsylum: false, king: false,
                    hasCentralOffice: false,
                    affiliationAvailable: true));
            if (!candidate || RoyalGuardService.IsRoyalGuard(pLeader))
                return false;
            bool westernProfile = CourtProfileRegistry.For(pKingdom)?.Id ==
                                  CourtProfileId.Western;
            bool educated = westernProfile ||
                HistoricalSchoolEducationService.CanAppoint(pLeader,
                    pKingdom, CourtOfficeLayer.Central, pOfficeId);
            return WesternCourtElectionRules.CanUseLocalCandidate(
                candidate, westernProfile, educated);
        }

        internal static IReadOnlyList<string>
            GetPromiseableAmnestyOffices(Kingdom pKingdom, Actor pLeader)
        {
            var result = new List<string>();
            ICourtProfile profile = CourtProfileRegistry.For(pKingdom);
            string institution = CourtInstitutionService.GetInstitution(
                pKingdom);
            foreach (CourtOfficeDefinition office in profile?.Offices ??
                     Array.Empty<CourtOfficeDefinition>())
                if (office.Layer == CourtOfficeLayer.Central &&
                    office.AvailableIn(institution) &&
                    CanPromiseAmnestyOffice(pKingdom, pLeader, office.Id))
                    result.Add(office.Id);
            return result;
        }

        private static bool ReplaceOfficer(Actor pIncumbent, Actor pCandidate,
            Kingdom pKingdom, string pLayer, string pOfficeId, string pSchoolId,
            City pCity = null)
        {
            SQLiteConnection db = CourtDB;
            if (db == null || pIncumbent?.data == null || pCandidate?.data == null ||
                pKingdom?.data == null) return false;

            int year = Date.getCurrentYear();
            double now = LineageService.CurTime();
            OfficialCareerAppointment appointment =
                OfficialCareerService.PrepareAppointment(pCandidate, pKingdom,
                    pLayer, pOfficeId, pSchoolId, pCity, year, now,
                    pVacancyPromotion: true,
                    pAllowLocalLowerQualification:
                        pLayer == CourtOfficeLayer.City);
            if (appointment == null) return false;

            bool guestIncumbent = CourtAffiliationResolver.IsValidGuestService(
                pIncumbent, pKingdom);
            GuestOfficeEndRequest guestEnd = null;
            OfficialCareerCloseRequest localClose = null;
            if (guestIncumbent)
            {
                HistoricalSchoolAffiliationSnapshot affiliation =
                    HistoricalAffiliationService.Get(pIncumbent.data.id);
                guestEnd = GuestOfficeEndPersistence.PrepareEnd(db, affiliation,
                    "replaced", year, now);
                if (guestEnd == null) return false;
            }
            else
            {
                localClose = new OfficialCareerCloseRequest(pIncumbent.data.id,
                    pKingdom.id, pLayer, pOfficeId, year, now,
                    "replaced");
            }

            OfficialCareerPrior candidateRuntimePrior =
                CaptureRuntimeOfficerProjection(pCandidate);
            OfficialCareerAppointmentProjection candidateStateProjection = null;
            CourtOfficerReplacementResult committed =
                CourtOfficerReplacementPersistence.Replace(db, appointment,
                    localClose, guestEnd, (connection, transaction) =>
                        candidateStateProjection = OfficialCareerStateService.
                            StageAppointment(connection, transaction, pCandidate,
                                pKingdom, pLayer, pOfficeId,
                                pCity, pActing: false));
            if (!committed.IsCommitted) return false;
            OfficialCareerStateService.PublishAppointment(candidateStateProjection);

            if (guestIncumbent)
            {
                if (!HistoricalAffiliationService.AdoptCommittedServiceEnd(
                        committed.GuestAffiliation)) return false;
                if (!ApplyCommittedGuestOfficerEnd(pIncumbent, pKingdom,
                        pKingdom.id, pOfficeId, "replaced")) return false;
            }
            else
            {
                ClearOfficer(pIncumbent, "replaced", pPersistCareer: false);
            }

            return ApplyCommittedOfficerProjection(pCandidate, pKingdom,
                pLayer, pOfficeId, pSchoolId, pCity,
                committed.Appointment, candidateRuntimePrior,
                pRecordCareerHistory: true,
                pStateProjectionCommitted: true);
        }

        internal static string EnsurePersonalSchool(Actor pActor)
        {
            if (pActor?.data == null) return CourtSchoolId.None;
            string school = SchoolMembershipService.GetSchool(pActor.data.id);
            pActor.data.set(LineageKeys.COURT_SCHOOL, school);
            SyncSchoolTrait(pActor, active: true);
            return school;
        }

        private static CourtCandidateProfile CandidateProfile(Actor pActor)
        {
            string existingSchool = SchoolMembershipService.GetSchool(pActor.data.id);
            return new CourtCandidateProfile(pActor.data.id,
                SafeStat(pActor, "stewardship"), SafeStat(pActor, "diplomacy"),
                SafeStat(pActor, "warfare"), SafeStat(pActor, "intelligence"), existingSchool,
                !string.IsNullOrEmpty(existingSchool));
        }

        internal static bool CanAppointGuestOfficer(Actor pActor, Kingdom pKingdom,
            string pOfficeId, City pCity, bool pActing = false)
        {
            if (IsWesternElective(pKingdom)) return false;
            if (!RoyalAsylumRules.CanPerformProtectedRole(
                    RoyalAsylumService.IsActive(pActor))) return false;
            HistoricalSchoolAffiliationSnapshot affiliation =
                HistoricalAffiliationService.Get(pActor?.data?.id ?? -1L);
            if (pActor?.data == null || pKingdom?.data == null || pCity?.data == null ||
                pCity.kingdom != pKingdom || affiliation == null ||
                !pActor.isAlive() || pActor.isRekt() || pActor.isKing() ||
                pActor.isCityLeader() || GeneralService.IsGeneral(pActor) ||
                pActor.hasTrait(LineageKeys.TRAIT_SLAVE) || pActor.hasTrait("madness") ||
                !HistoricalAffiliationService.IsPresentForInfluence(pActor) ||
                HistoricalAffiliationService.ResidenceCity(pActor)?.data?.id != pCity.data.id ||
                HasActiveOffice(pKingdom, pOfficeId)) return false;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string currentOffice, "");
            if (!string.IsNullOrEmpty(currentOffice)) return false;
            if (!CourtRules.CanHoldLayerOffice(CourtOfficeLayer.Central,
                    pActor.isSexMale(), otherwiseEligible: true)) return false;
            string school = SchoolMembershipService.GetSchool(pActor.data.id);
            if (string.IsNullOrEmpty(school) || CourtSchoolRegistry.Find(school) == null)
                return false;
            if (!HistoricalSchoolEducationService.CanAppoint(pActor,
                    pKingdom, CourtOfficeLayer.Central, pOfficeId)) return false;
            return pActing
                ? CivilServiceExamCandidateQuery.HasHostIssuedQualification(
                    pActor, pKingdom)
                : CivilServiceQualificationService.
                    CanReceiveFormalCivilAppointment(pActor, pKingdom,
                        CourtOfficeLayer.Central, pOfficeId,
                        pAllowVacancyPromotion: true);
        }

        internal static bool EndGuestOfficer(Actor pActor, Kingdom pHost, string pReason,
            int pYear)
        {
            return SchoolGuestOfficeService.EndGuestOfficer(pActor, pHost, pReason, pYear);
        }

        internal static bool ApplyCommittedGuestOfficerEnd(Actor pActor, Kingdom pHost,
            long pHostKingdomId, string pOfficeId, string pReason)
        {
            if (pActor?.data == null)
            {
                if (pHost?.data != null) CourtDirectionService.MarkDirty(pHost);
                return true;
            }

            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long runtimeKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER, out string runtimeLayer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string runtimeOffice, "");
            bool frozenProjectionStillLive = runtimeKingdomId == pHostKingdomId &&
                runtimeLayer == CourtOfficeLayer.Central &&
                runtimeOffice == (pOfficeId ?? "");
            bool runtimeAlreadyCleared = runtimeKingdomId < 0 &&
                                         string.IsNullOrEmpty(runtimeOffice);
            if (frozenProjectionStillLive)
                ClearOfficer(pActor, pReason ?? "guest_term", pPersistCareer: false);
            else if (!runtimeAlreadyCleared &&
                     !OfficialCareerStateService.ClearCurrentOffice(pActor,
                         pHostKingdomId, pOfficeId))
                return false;
            try { pActor.finishStatusEffect(HistoricalSchoolContent.GuestStatusId); }
            catch { }
            if (!frozenProjectionStillLive && pHost?.data != null)
                CourtDirectionService.MarkDirty(pHost);
            CitySchoolSnapshotService.MarkActorDirty(pActor);
            return true;
        }

        internal static void ClearGuestOfficerAfterDeath(Actor pActor,
            HistoricalSchoolAffiliationSnapshot pCommittedFromState)
        {
            if (pActor?.data == null || pCommittedFromState == null ||
                pCommittedFromState.ActorId != pActor.data.id ||
                pCommittedFromState.LifecycleState !=
                HistoricalSchoolLifecycleState.Serving) return;
            ClearOfficer(pActor, "death", pRecordHistory: false, pArchive: false);
            try { pActor.finishStatusEffect(HistoricalSchoolContent.GuestStatusId); }
            catch { }
        }

        internal static void RequestLocalOfficerDeathReconcile(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long kingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_CITY_ID,
                out long cityId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER,
                out string layer, "");
            if (kingdomId < 0L || cityId < 0L ||
                layer != CourtOfficeLayer.City) return;
            Kingdom kingdom = World.world?.kingdoms?.get(kingdomId);
            if (kingdom?.data != null)
                CityBureauAnnualWorkService.RequestImmediateReconcile(
                    kingdom, cityId);
        }

        private static bool SetOfficer(Actor pActor, Kingdom pKingdom, string pLayer, string pOfficeId, string pSchoolId, City pCity,
            bool pActing = false, bool pVacancyPromotion = false,
            bool pAllowLocalLowerQualification = false)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (!RoyalGuardOfficeRules.CanAcceptOfficeAppointment(
                    RoyalGuardService.IsRoyalGuard(pActor))) return false;
            if (!RoyalAsylumRules.CanPerformProtectedRole(
                    RoyalAsylumService.IsActive(pActor))) return false;

            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long runtimePreviousKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_CITY_ID,
                out long runtimePreviousCityId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER,
                out string runtimePreviousLayer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string runtimePreviousOffice, "");
            OfficialCareerPrior runtimePrior = runtimePreviousKingdomId >= 0 ||
                                               !string.IsNullOrEmpty(runtimePreviousOffice)
                ? new OfficialCareerPrior(runtimePreviousKingdomId, runtimePreviousCityId,
                    runtimePreviousLayer, runtimePreviousOffice)
                : null;
            string personalSchool = SchoolMembershipService.GetSchool(pActor.data.id);
            OfficialCareerAppointmentResult careerResult = OfficialCareerService.Appoint(
                pActor, pKingdom, pLayer ?? "", pOfficeId ?? "", personalSchool,
                pCity, pActing, pVacancyPromotion,
                pAllowLocalLowerQualification);
            if (!careerResult.IsCommitted)
            {
                ModClass.LogWarning("Court appointment persistence failed: kingdom=" +
                    pKingdom.id + " actor=" + pActor.data.id + " office=" +
                    (pOfficeId ?? "") + " outcome=" + careerResult.Persistence +
                    " mutation=" + careerResult.Mutation);
                return false;
            }

            return ApplyCommittedOfficerProjection(pActor, pKingdom, pLayer, pOfficeId,
                personalSchool, pCity, careerResult, runtimePrior,
                pRecordCareerHistory: !pActing, pActing,
                pStateProjectionCommitted: true);
        }

        internal static bool TryAssignFeudatoryChiefClerk(Actor pActor,
            Kingdom pKingdom, City pSeat)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                pSeat?.data == null || pSeat.kingdom != pKingdom) return false;
            return SetOfficer(pActor, pKingdom, CourtOfficeLayer.Feudatory,
                CourtOfficeId.FeudatoryChiefClerk,
                SchoolMembershipService.GetSchool(pActor.data.id), pSeat);
        }

        internal static bool TryAssignCityGovernor(Actor pActor,
            Kingdom pKingdom, City pCity,
            bool pVacancyPromotion = false)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                pCity?.data == null || pCity.kingdom != pKingdom ||
                pCity.leader != pActor) return false;
            string officeId = ResolveCityOffice(pKingdom, pCity);
            return !string.IsNullOrEmpty(officeId) &&
                SetOfficer(pActor, pKingdom, CourtOfficeLayer.City, officeId,
                SchoolMembershipService.GetSchool(pActor.data.id), pCity,
                pVacancyPromotion: pVacancyPromotion);
        }

        internal static bool TryAssignLocalOfficer(Actor pActor,
            Kingdom pKingdom, City pCity, string pOfficeId,
            bool pVacancyPromotion = true)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                pCity?.data == null || pCity.kingdom != pKingdom ||
                string.IsNullOrEmpty(pOfficeId)) return false;
            return SetOfficer(pActor, pKingdom, CourtOfficeLayer.City,
                pOfficeId, SchoolMembershipService.GetSchool(pActor.data.id),
                pCity, pActing: false, pVacancyPromotion,
                pAllowLocalLowerQualification: true);
        }

        internal static bool EnsureLocalOfficerHistory(Kingdom pKingdom,
            City pCity, string pOfficeId)
        {
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.kingdom != pKingdom || string.IsNullOrEmpty(pOfficeId))
                return false;
            bool hasRow = GetActiveOfficers(pKingdom, 512).Any(row =>
                row != null && row.layer == CourtOfficeLayer.City &&
                row.city_id == pCity.data.id && row.office_id == pOfficeId);
            if (hasRow) return true;
            return TryAssignLocalOfficer(pCity.leader, pKingdom, pCity,
                pOfficeId, pVacancyPromotion: true);
        }

        internal static bool TryAssignActingCityGovernor(Actor pActor,
            Kingdom pKingdom, City pCity)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                pCity?.data == null || pCity.kingdom != pKingdom ||
                pCity.leader != pActor ||
                !HistoricalSchoolEducationService.CanAppoint(pActor,
                    pKingdom, CourtOfficeLayer.City,
                    ResolveCityOffice(pKingdom, pCity))) return false;
            string officeId = ResolveCityOffice(pKingdom, pCity);
            return !string.IsNullOrEmpty(officeId) &&
                SetOfficer(pActor, pKingdom, CourtOfficeLayer.City, officeId,
                SchoolMembershipService.GetSchool(pActor.data.id), pCity,
                pActing: true);
        }

        internal static void RebuildOfficialCareerRuntimeProjections()
        {
            SQLiteConnection db = CourtDB;
            if (db == null) return;
            IReadOnlyList<OfficialCareerRecord> appointments;
            try
            {
                appointments = OfficialCareerPersistence.
                    ReadAuthoritativeActiveAppointments(db);
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Official career runtime rebuild read failed: " + e.Message);
                return;
            }

            foreach (OfficialCareerRecord appointment in appointments)
            {
                Actor actor = World.world?.units?.get(appointment.ActorId);
                if (actor?.data == null || actor.isRekt() || !actor.isAlive())
                    continue;
                RestoreOfficerProjection(actor, appointment);
            }
        }

        internal static bool TryRestoreActiveOfficerProjection(Actor pActor)
        {
            SQLiteConnection db = CourtDB;
            if (db == null || pActor?.data == null) return false;
            OfficialCareerRecord appointment;
            try
            {
                appointment = OfficialCareerPersistence.
                    ReadAuthoritativeActiveAppointment(db, pActor.data.id);
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Official career runtime restore read failed: " + e.Message);
                return false;
            }
            return appointment != null &&
                   RestoreOfficerProjection(pActor, appointment);
        }

        private static bool RestoreOfficerProjection(Actor pActor,
            OfficialCareerRecord pAppointment)
        {
            if (pActor?.data == null || pAppointment == null) return false;
            if (!RoyalGuardOfficeRules.CanAcceptOfficeAppointment(
                    RoyalGuardService.IsRoyalGuard(pActor)))
            {
                OfficialCareerService.End(pActor, pAppointment.Layer,
                    pAppointment.OfficeId, "royal_guard_lifetime");
                OfficialCareerStateService.ClearCurrentOffice(pActor,
                    pAppointment.KingdomId, pAppointment.OfficeId);
                ClearOfficer(pActor, "royal_guard_lifetime");
                return false;
            }
            Kingdom kingdom = World.world?.kingdoms?.get(
                pAppointment.KingdomId);
            if (kingdom?.data == null || kingdom.isRekt() ||
                !CourtAffiliationResolver.CanServe(pActor, kingdom,
                    pAppointment.Layer)) return false;
            City city = pAppointment.CityId >= 0L
                ? World.world?.cities?.get(pAppointment.CityId)
                : null;
            if (pAppointment.CityId >= 0L &&
                (city?.data == null || city.isRekt() || city.kingdom != kingdom))
                return false;
            bool cityOffice = pAppointment.Layer == CourtOfficeLayer.City &&
                              IsCityLeaderOffice(pAppointment.OfficeId);
            if (cityOffice && city?.leader != null && city.leader != pActor)
                return false;
            if (!OfficialCareerStateService.RestoreAppointmentProjection(
                    pActor, kingdom, pAppointment.Layer,
                    pAppointment.OfficeId, city, pAppointment.IsActing))
                return false;

            City staleLeaderCity = pActor.city;
            if (staleLeaderCity?.leader == pActor &&
                (!cityOffice || staleLeaderCity != city))
            {
                try { staleLeaderCity.removeLeader(); }
                catch { }
            }
            if (cityOffice && city != null)
            {
                using (GovernorRotationRuntimeScope.Enter())
                {
                    if (pActor.city != city) pActor.joinCity(city);
                    if (city.leader != pActor) city.setLeader(pActor, pNew: false);
                }
            }

            pActor.data.set(LineageKeys.COURT_KINGDOM_ID, kingdom.id);
            pActor.data.set(LineageKeys.COURT_LAYER,
                pAppointment.Layer ?? "");
            pActor.data.set(LineageKeys.COURT_OFFICE_ID,
                pAppointment.OfficeId ?? "");
            pActor.data.set(LineageKeys.COURT_CITY_ID,
                city?.data?.id ?? -1L);
            pActor.data.set(LineageKeys.COURT_SCHOOL,
                pAppointment.SchoolId ?? "");
            if (!pAppointment.IsActing)
                LineageService.EnsureOfficialShiAndClan(pActor,
                    pAppointment.OfficeId);
            SyncSchoolTrait(pActor, active: true);
            CitySchoolSnapshotService.MarkActorDirty(pActor);
            CourtDirectionService.MarkDirty(kingdom);
            return true;
        }

        internal static bool TryExpireActingCityGovernor(Actor pActor,
            Kingdom pKingdom, long pCityId)
        {
            if (pActor?.data == null || pKingdom?.data == null || pCityId < 0L)
                return false;
            pActor.data.get(LineageKeys.OFFICER_WAITING_SINCE_YEAR,
                out int actingSinceYear, -1);
            if (actingSinceYear < 0) return false;
            City city = World.world?.cities?.get(pCityId);
            if (city?.data == null || city.kingdom != pKingdom ||
                city.leader != pActor) return false;
            try
            {
                city.removeLeader();
                if (city.leader == pActor) return false;
            }
            catch { return false; }
            ClearOfficer(pActor, "acting_term_ended",
                pRecordHistory: false, pArchive: true,
                pPersistCareer: true);
            TryRestoreActiveOfficerProjection(pActor);
            return true;
        }

        internal static bool TryExpireActingCentralOfficial(Actor pActor,
            Kingdom pKingdom, string pOfficeId)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                string.IsNullOrEmpty(pOfficeId)) return false;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long courtKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            pActor.data.get(LineageKeys.OFFICER_WAITING_SINCE_YEAR,
                out int actingSinceYear, -1);
            if (courtKingdomId != pKingdom.id ||
                layer != CourtOfficeLayer.Central || office != pOfficeId ||
                actingSinceYear < 0) return false;
            ClearOfficer(pActor, "acting_term_ended",
                pRecordHistory: false, pArchive: true,
                pPersistCareer: true);
            TryRestoreActiveOfficerProjection(pActor);
            return true;
        }

        internal static bool ClearCityGovernor(Actor pActor,
            string pReason)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            if (layer != CourtOfficeLayer.City ||
                !IsCityLeaderOffice(office)) return false;
            ClearOfficer(pActor, pReason ?? "city_governor_ended");
            return true;
        }

        internal static bool ClearFeudatoryChiefClerk(Actor pActor,
            string pReason)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            if (layer != CourtOfficeLayer.Feudatory ||
                office != CourtOfficeId.FeudatoryChiefClerk) return false;
            ClearOfficer(pActor, pReason ?? "feudatory_office_ended");
            return true;
        }

        internal static bool TryDismissOfficer(Actor pActor,
            Kingdom pKingdom, string pReason)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                pActor.isRekt() || !pActor.isAlive()) return false;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long courtKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_CITY_ID,
                out long courtCityId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            if (courtKingdomId != pKingdom.id ||
                string.IsNullOrEmpty(layer) || string.IsNullOrEmpty(office) ||
                CourtDB == null) return false;

            string reason = pReason ?? "court_disposition";
            var request = new OfficialCareerCloseRequest(pActor.data.id,
                pKingdom.id, layer, office, Date.getCurrentYear(),
                LineageService.CurTime(), reason);
            OfficialCareerCloseResult closed =
                OfficialCareerPersistence.Close(CourtDB, request);
            if (!closed.IsCommitted) return false;

            if (layer == CourtOfficeLayer.City &&
                pActor.city?.leader == pActor)
            {
                try { pActor.city.removeLeader(); }
                catch { return false; }
            }
            else
            {
                ClearOfficer(pActor, reason, pPersistCareer: false);
            }

            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long remainingKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string remainingOffice, "");
            bool result = remainingKingdomId < 0 &&
                   string.IsNullOrEmpty(remainingOffice) &&
                   (layer != CourtOfficeLayer.City || !pActor.isCityLeader());
            if (result && layer == CourtOfficeLayer.City && courtCityId >= 0L)
                CityBureauAnnualWorkService.RequestImmediateReconcile(
                    pKingdom, courtCityId);
            return result;
        }

        internal static bool ApplyCommittedOfficerProjection(Actor pActor, Kingdom pKingdom,
            string pLayer, string pOfficeId, string pSchoolId, City pCity,
            OfficialCareerAppointmentResult careerResult, OfficialCareerPrior pRuntimePrior,
            bool pRecordCareerHistory, bool pActing = false,
            bool pStateProjectionCommitted = false)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                !careerResult.IsCommitted) return false;
            CivilServiceLegacyTransitionService.ConsumeAfterCommittedAppointment(
                pActor, pKingdom, pLayer, pOfficeId, pActing);
            OfficialCareerPrior cleanupPrior =
                OfficialCareerProjectionRecoveryRules.SelectCleanupPrior(careerResult,
                    pRuntimePrior, pKingdom.id, pOfficeId);
            Kingdom cleanupKingdom = cleanupPrior == null
                ? null
                : cleanupPrior.KingdomId == pKingdom.id
                    ? pKingdom
                    : World.world?.kingdoms?.get(cleanupPrior.KingdomId);
            bool targetIsPhysician = pOfficeId == CourtOfficeId.ImperialPhysician;
            bool cleanupWasPhysician = cleanupPrior != null &&
                cleanupPrior.OfficeId == CourtOfficeId.ImperialPhysician;
            bool continuousPhysician = cleanupWasPhysician && targetIsPhysician &&
                cleanupKingdom?.data != null && cleanupKingdom.id == pKingdom.id;
            bool cleanupNeedsReconcile = cleanupWasPhysician &&
                cleanupKingdom?.data != null && !continuousPhysician;
            bool targetNeedsReconcile = targetIsPhysician;
            if (cleanupNeedsReconcile)
            {
                cleanupKingdom.data.get(LineageKeys.COURT_IMPERIAL_PHYSICIAN_ID,
                    out long cachedPhysicianId, -1L);
                if (RoyalMedicalCareRules.ShouldClearCachedPhysician(
                        cachedPhysicianId, pActor.data.id, cleanupPrior.OfficeId))
                    cleanupKingdom.data.set(LineageKeys.COURT_IMPERIAL_PHYSICIAN_ID, -1L);
                RoyalMedicalCareService.ReconcileTargets(cleanupKingdom);
            }
            if (pRecordCareerHistory &&
                careerResult.Mutation == OfficialCareerMutation.Reassigned &&
                careerResult.Prior != null && cleanupKingdom?.data != null)
                ChronicleEvents.OnCourtOfficerDismissed(pActor, cleanupKingdom,
                    careerResult.Prior.OfficeId, "reassigned");
            if (OfficialCareerProjectionRecoveryRules.
                    ShouldReleasePriorCityLeader(cleanupPrior, pLayer))
            {
                City cleanupCity = null;
                try
                {
                    cleanupCity = World.world?.cities?.get(cleanupPrior.CityId);
                }
                catch { }
                if (cleanupCity?.leader == pActor)
                    using (GovernorRotationRuntimeScope.Enter())
                        cleanupCity.removeLeader();
            }
            pActor.data.set(LineageKeys.COURT_KINGDOM_ID, pKingdom.id);
            pActor.data.set(LineageKeys.COURT_LAYER, pLayer ?? "");
            pActor.data.set(LineageKeys.COURT_OFFICE_ID, pOfficeId ?? "");
            pActor.data.set(LineageKeys.COURT_SCHOOL, pSchoolId ?? "");
            pActor.data.set(LineageKeys.COURT_CITY_ID, pCity?.data?.id ?? -1L);
            if (!pActing && !pStateProjectionCommitted)
                OfficialCareerStateService.ProjectAppointment(pActor, pKingdom,
                    pLayer, pOfficeId, pCity);
            if (targetIsPhysician)
                pKingdom.data.set(LineageKeys.COURT_IMPERIAL_PHYSICIAN_ID, pActor.data.id);
            CourtOfficerMilitaryTransitionService.ReleaseAfterCommittedAppointment(
                pActor, pLayer, pOfficeId);
            if (CourtOfficerRecordRules.ShouldGrantNobleIdentity(
                    careerResult.IsCommitted, pActing))
                LineageService.EnsureOfficialShiAndClan(pActor, pOfficeId);
            SyncSchoolTrait(pActor, active: true);
            if (pRecordCareerHistory && careerResult.CreatedAppointmentEvent)
            {
                ChronicleEvents.OnCourtOfficerAppointed(pActor, pKingdom, pOfficeId ?? "",
                    pSchoolId ?? "");
                pActor.data.get(
                    LineageKeys.CIVIL_SERVICE_FIRST_APPOINTMENT_RECORDED,
                    out bool firstAppointmentRecorded, false);
                pActor.data.get(LineageKeys.CIVIL_SERVICE_QUALIFICATION,
                    out string civilQualification, "none");
                bool hasQualification = CivilServiceExamRules.
                    IsFormalAppointmentQualification(civilQualification);
                if (OfficialCareerBiographyRules.
                    ShouldRecordFirstFormalAppointment(
                        CivilServiceQualificationService.
                            HasExaminationSystem(pKingdom),
                        careerResult.IsCommitted,
                        careerResult.CreatedAppointmentEvent, pActing,
                        hasQualification, firstAppointmentRecorded))
                {
                    ChronicleEvents.OnCivilServiceFirstAppointment(pActor,
                        pKingdom, pOfficeId ?? "", civilQualification);
                    pActor.data.set(LineageKeys.
                        CIVIL_SERVICE_FIRST_APPOINTMENT_RECORDED, true);
                }
            }
            LineageService.ArchiveActor(pActor, pAlive: true);
            CourtDirectionService.MarkDirty(pKingdom);
            CitySchoolSnapshotService.MarkActorDirty(pActor);
            HistoricalSchoolEliteEnrollmentService.MarkPriority(pActor,
                pKingdom, pLayer == CourtOfficeLayer.City
                    ? HistoricalSchoolElitePriority.LocalOfficial
                    : HistoricalSchoolElitePriority.CentralOfficial);
            if (targetNeedsReconcile)
                RoyalMedicalCareService.ReconcileTargets(pKingdom);
            CityShiInfluenceSnapshotService.MarkActorDirty(pActor);
            return true;
        }

        internal static OfficialCareerPrior CaptureRuntimeOfficerProjection(Actor pActor)
        {
            if (pActor?.data == null) return null;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID, out long kingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_CITY_ID, out long cityId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            return kingdomId >= 0 || !string.IsNullOrEmpty(office)
                ? new OfficialCareerPrior(kingdomId, cityId, layer, office)
                : null;
        }

        private static void ClearOfficer(Actor pActor, string pReason,
            bool pRecordHistory = true, bool pArchive = true,
            bool pPersistCareer = true)
        {
            if (pActor?.data == null) return;

            pActor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_CITY_ID, out long courtCityId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            Kingdom courtKingdom = courtKingdomId >= 0 ? World.world?.kingdoms?.get(courtKingdomId) : null;
            bool alive = pActor.isAlive() && !pActor.isRekt();
            if (!alive && pArchive) LineageService.ArchiveActor(pActor, pAlive: false);

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

            if (pPersistCareer)
            {
                OfficialCareerService.End(pActor, layer, office, pReason ?? "");
                OfficialCareerStateService.ClearCurrentOffice(pActor,
                    courtKingdomId, office);
            }
            if (pRecordHistory && courtKingdom != null && !string.IsNullOrEmpty(office))
                ChronicleEvents.OnCourtOfficerDismissed(pActor, courtKingdom, office, pReason ?? "");
            if (alive && pArchive) LineageService.ArchiveActor(pActor, pAlive: true);
            if (courtKingdom?.data != null) CourtDirectionService.MarkDirty(courtKingdom);
            CitySchoolSnapshotService.MarkActorDirty(pActor);
            CityShiInfluenceSnapshotService.MarkActorDirty(pActor);
            if (courtKingdom?.data != null && layer == CourtOfficeLayer.City &&
                courtCityId >= 0L && pReason != "kingdom_fell")
                CityBureauAnnualWorkService.RequestImmediateReconcile(
                    courtKingdom, courtCityId);
            if (courtKingdom?.data != null && office == CourtOfficeId.ImperialPhysician)
                RoyalMedicalCareService.ReconcileTargets(courtKingdom);
        }

        private static void CloseStaleOfficerRows(Kingdom pKingdom)
        {
            var db = CourtDB;
            if (db == null || pKingdom?.data == null) return;
            var rows = new List<CourtOfficerView>();
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText = "SELECT ACTOR_ID,LAYER,OFFICE_ID FROM " +
                                  CourtOfficerTableItem.GetTableName() +
                                  " WHERE KINGDOM_ID = @kid AND ACTIVE = 1";
                cmd.Parameters.AddWithValue("@kid", pKingdom.id);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    if (!reader.IsDBNull(0))
                        rows.Add(new CourtOfficerView
                        {
                            actor_id = Convert.ToInt64(reader.GetValue(0)),
                            layer = reader.IsDBNull(1) ? "" :
                                reader.GetValue(1)?.ToString() ?? "",
                            office_id = reader.IsDBNull(2) ? "" :
                                reader.GetValue(2)?.ToString() ?? ""
                        });
            }
            catch (Exception e)
            {
                AncientWarfare3.ModClass.LogWarning("CourtOfficer stale-row read failed: " + e.Message);
                return;
            }

            foreach (CourtOfficerView row in rows)
            {
                Actor actor = World.world?.units?.get(row.actor_id);
                if (IsValidActiveOfficeActor(actor, pKingdom, row.layer,
                        row.office_id)) continue;
                if (TryRestoreActiveOfficerProjection(actor) &&
                    IsValidActiveOfficeActor(actor, pKingdom, row.layer,
                        row.office_id)) continue;
                CloseDurableOfficerRow(pKingdom, row, actor);
            }
        }

        private static void CloseStaleCentralOfficeRow(Kingdom pKingdom,
            string pOfficeId)
        {
            CourtOfficerView row = ReadActiveCentralOffice(pKingdom, pOfficeId);
            if (row == null) return;
            Actor actor = World.world?.units?.get(row.actor_id);
            if (IsValidActiveOfficeActor(actor, pKingdom, row.layer,
                    row.office_id)) return;
            if (TryRestoreActiveOfficerProjection(actor) &&
                IsValidActiveOfficeActor(actor, pKingdom, row.layer,
                    row.office_id)) return;
            CloseDurableOfficerRow(pKingdom, row, actor);
        }

        private static void CloseDurableOfficerRow(Kingdom pKingdom,
            CourtOfficerView pRow, Actor pActor)
        {
            if (pKingdom?.data == null || pRow == null) return;
            string reason = pActor?.data == null
                ? "missing"
                : pActor.isAlive() && !pActor.isRekt() ? "defected" : "dead";
            bool runtimeMatches = false;
            if (pActor?.data != null)
            {
                pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                    out long runtimeKingdomId, -1L);
                pActor.data.get(LineageKeys.COURT_LAYER,
                    out string runtimeLayer, "");
                pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                    out string runtimeOffice, "");
                runtimeMatches = runtimeKingdomId == pKingdom.id &&
                    runtimeLayer == pRow.layer && runtimeOffice == pRow.office_id;
            }
            if (runtimeMatches)
                ClearOfficer(pActor, reason);
            else
                OfficialCareerService.EndForOffice(pRow.actor_id, pKingdom.id,
                    pRow.layer, pRow.office_id, reason);
        }

        private static void SyncSchoolTrait(Actor pActor, bool active)
        {
            if (pActor?.data == null) return;

            string school = SchoolMembershipService.GetSchool(pActor.data.id);
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

        private static void RecalculateFactionCache(Kingdom pKingdom,
            List<Actor> pRoster, IReadOnlyList<CourtOfficerView> pActiveOfficers)
        {
            var values = new Dictionary<string, float>();
            var seenActors = new HashSet<long>();
            foreach (Actor actor in RosterOrSafeUnits(pKingdom, pRoster))
            {
                if (actor?.data == null || !seenActors.Add(actor.data.id)) continue;
                AccumulateFactionValue(values, actor, pKingdom);
            }
            // A foreign guest is intentionally absent from pKingdom.getUnits().  Read the
            // durable officer index as well so guest appointments influence court direction
            // and faction concentration just like local ministers.
            foreach (CourtOfficerView officer in
                     pActiveOfficers ?? Array.Empty<CourtOfficerView>())
            {
                Actor actor = World.world?.units?.get(officer.actor_id);
                if (actor?.data == null || !seenActors.Add(actor.data.id)) continue;
                AccumulateFactionValue(values, actor, pKingdom);
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

        private static void AccumulateFactionValue(Dictionary<string, float> pValues,
            Actor pActor, Kingdom pKingdom)
        {
            if (pValues == null || pActor?.data == null || pKingdom?.data == null) return;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
            if (courtKingdomId != pKingdom.id) return;
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            if (!CourtAffiliationResolver.CanServe(pActor, pKingdom, layer)) return;
            string school = SchoolMembershipService.GetSchool(pActor.data.id);
            if (string.IsNullOrEmpty(school)) return;
            float influence = CourtInfluenceRules.InfluenceWeight(layer,
                ChronicleGate.IsImportant(pActor),
                OfficialCareerStateService.ReadMeritFast(pActor),
                OfficialCareerStateService.ReadRankFast(pActor));
            pValues.TryGetValue(school, out float old);
            pValues[school] = old + influence;
        }

        private static void UpsertCourtSnapshot(Kingdom pKingdom)
        {
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null || pKingdom?.data == null) return;

            CourtSnapshot s = GetSnapshot(pKingdom);
            pKingdom.data.get(LineageKeys.COURT_LAST_REFRESH_YEAR, out int lastRefresh, -1);
            pKingdom.data.get(LineageKeys.COURT_LAST_CANDIDATE_YEAR, out int lastCandidate, -1);
            pKingdom.data.get(LineageKeys.COURT_LAST_STRONG_EVENT_YEAR, out int lastStrong, -1);
            string courtProfileId = KingdomPolicyProfileRules.ToPersistedId(
                KingdomPolicyService.GetPolicyProfile(pKingdom));
            pKingdom.data.set(LineageKeys.COURT_PROFILE_ID, courtProfileId);
            string institutionId = CourtInstitutionService.GetInstitution(
                pKingdom);
            var values = new[]
            {
                ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                ColumnVal.Create("COURT_PROFILE_ID", courtProfileId),
                ColumnVal.Create("INSTITUTION_ID", institutionId),
                ColumnVal.Create("COURT_MODE", s.mode ?? ""),
                ColumnVal.Create("DOMINANT_SCHOOL", s.dominant_school ?? ""),
                ColumnVal.Create("SECONDARY_SCHOOL", s.secondary_school ?? ""),
                ColumnVal.Create("COURT_EFFICIENCY", (double)s.efficiency),
                ColumnVal.Create("FACTION_CONCENTRATION", (double)s.concentration),
                ColumnVal.Create("FACTION_CACHE", s.faction_cache ?? ""),
                ColumnVal.Create("ARISTOCRATIC_GROUP_CACHE",
                    s.aristocratic_group_cache ?? ""),
                ColumnVal.Create("DIRECTION_LIVELIHOOD", (double)s.livelihood),
                ColumnVal.Create("DIRECTION_WAR", (double)s.war),
                ColumnVal.Create("DIRECTION_AGGRESSION", (double)s.aggression),
                ColumnVal.Create("DIRECTION_PEACE", (double)s.peace),
                ColumnVal.Create("DIRECTION_ORDER", (double)s.order),
                ColumnVal.Create("DIRECTION_COMMERCE", (double)s.commerce),
                ColumnVal.Create("DIRECTION_TECHNOLOGY", (double)s.technology),
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
            // Guest officers are not part of the host kingdom's unit roster.  Include
            // their durable rows before filling vacancies so a local candidate cannot
            // overwrite a still-valid foreign appointment in the same year.
            foreach (CourtOfficerView officer in GetActiveOfficers(pKingdom, 96))
            {
                if (string.IsNullOrEmpty(officer?.office_id)) continue;
                Actor actor = World.world?.units?.get(officer.actor_id);
                if (IsValidActiveOfficeActor(actor, pKingdom, officer.layer,
                        officer.office_id)) result.Add(officer.office_id);
            }
            foreach (Actor actor in RosterOrSafeUnits(pKingdom, pRoster))
            {
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                if (courtKingdomId != pKingdom.id) continue;
                actor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
                if (!string.IsNullOrEmpty(office)) result.Add(office);
            }
            return result;
        }

        private static HashSet<long> BuildActiveOfficerActorSet()
        {
            var result = new HashSet<long>();
            SQLiteConnection db = CourtDB;
            if (db == null) return result;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT ACTOR_ID,KINGDOM_ID,OFFICE_ID,LAYER FROM " +
                                      CourtOfficerTableItem.GetTableName() +
                                      " WHERE ACTIVE = 1";
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.IsDBNull(0) || reader.IsDBNull(1) ||
                        reader.IsDBNull(2) || reader.IsDBNull(3)) continue;
                    long actorId = Convert.ToInt64(reader.GetValue(0));
                    long kingdomId = Convert.ToInt64(reader.GetValue(1));
                    string officeId = Convert.ToString(reader.GetValue(2)) ?? "";
                    string layer = Convert.ToString(reader.GetValue(3)) ?? "";
                    Actor actor = World.world?.units?.get(actorId);
                    Kingdom kingdom = World.world?.kingdoms?.get(kingdomId);
                    if (IsValidActiveOfficeActor(actor, kingdom, layer,
                            officeId))
                        result.Add(actorId);
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Active officer actor read failed: " +
                                    exception.Message);
            }
            return result;
        }

        private static IEnumerable<Actor> RosterOrSafeUnits(Kingdom pKingdom, List<Actor> pRoster)
        {
            return pRoster ?? SafeUnits(pKingdom);
        }

        private static bool HasActiveOffice(Kingdom pKingdom, string pOfficeId,
            string pLayer = CourtOfficeLayer.Central)
        {
            if (pLayer == CourtOfficeLayer.Central)
                return FindActiveOfficeActor(pKingdom, pOfficeId) != null;
            return GetActiveOfficers(pKingdom, 96).Any(p =>
                p.layer == pLayer && p.office_id == pOfficeId &&
                IsValidActiveOfficeActor(World.world?.units?.get(p.actor_id),
                    pKingdom, p.layer, p.office_id));
        }

        private static Actor FindActiveOfficeActor(Kingdom pKingdom,
            string pOfficeId)
        {
            return FindActiveOfficeActor(pKingdom, pOfficeId,
                CourtOfficeLayer.Central, -1L);
        }

        private static Actor FindActiveOfficeActor(Kingdom pKingdom,
            string pOfficeId, string pLayer, long pCityId)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pOfficeId))
                return null;
            CourtOfficerView officer = pLayer == CourtOfficeLayer.Central
                ? ReadActiveCentralOffice(pKingdom, pOfficeId)
                : GetActiveOfficers(pKingdom, 512).FirstOrDefault(row =>
                    row != null && row.layer == pLayer &&
                    row.city_id == pCityId && row.office_id == pOfficeId);
            if (officer == null) return null;
            Actor persistedActor = World.world?.units?.get(officer.actor_id);
            return IsValidActiveOfficeActor(persistedActor, pKingdom,
                officer.layer, officer.office_id)
                ? persistedActor
                : null;
        }

        private static CourtOfficerView ReadActiveCentralOffice(Kingdom pKingdom,
            string pOfficeId)
        {
            SQLiteConnection db = CourtDB;
            if (db == null || pKingdom?.data == null ||
                string.IsNullOrEmpty(pOfficeId)) return null;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText = "SELECT ACTOR_NAME,OFFICE_ID,SCHOOL_ID,LAYER," +
                                  "CITY_ID,INFLUENCE,ACTOR_ID,APPOINTED_YEAR FROM " +
                                  CourtOfficerTableItem.GetTableName() +
                                  " WHERE KINGDOM_ID = @kid AND ACTIVE = 1 " +
                                  "AND LAYER = @layer AND OFFICE_ID = @office LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdom.id);
                cmd.Parameters.AddWithValue("@layer", CourtOfficeLayer.Central);
                cmd.Parameters.AddWithValue("@office", pOfficeId);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;
                return new CourtOfficerView
                {
                    actor_name = reader.IsDBNull(0) ? "" :
                        reader.GetValue(0)?.ToString() ?? "",
                    office_id = reader.IsDBNull(1) ? "" :
                        reader.GetValue(1)?.ToString() ?? "",
                    school_id = reader.IsDBNull(2) ? "" :
                        reader.GetValue(2)?.ToString() ?? "",
                    layer = reader.IsDBNull(3) ? "" :
                        reader.GetValue(3)?.ToString() ?? "",
                    city_id = reader.IsDBNull(4) ? -1L :
                        Convert.ToInt64(reader.GetValue(4)),
                    influence = reader.IsDBNull(5) ? 0f :
                        Convert.ToSingle(reader.GetValue(5)),
                    actor_id = reader.IsDBNull(6) ? -1L :
                        Convert.ToInt64(reader.GetValue(6)),
                    appointed_year = reader.IsDBNull(7) ? -1 :
                        Convert.ToInt32(reader.GetValue(7))
                };
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Central court office read failed: kingdom=" +
                    pKingdom.id + " office=" + pOfficeId + " error=" + error);
                return null;
            }
        }

        private static bool IsValidActiveOfficeActor(Actor pActor,
            Kingdom pKingdom, string pLayer, string pOfficeId = null)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt() ||
                pActor.isKing()) return false;
            bool baseValid = RoyalAsylumRules.CanPerformProtectedRole(
                                 RoyalAsylumService.IsActive(pActor)) &&
                             CourtRules.CanHoldOffice(
                                 alive: true,
                                 sameKingdom: CourtAffiliationResolver.CanServe(
                                     pActor, pKingdom, pLayer),
                                 slave: pActor.hasTrait(LineageKeys.TRAIT_SLAVE),
                                 madness: pActor.hasTrait("madness"));
            if (!CourtRules.CanHoldLayerOffice(pLayer, pActor.isSexMale(),
                    baseValid)) return false;
            if (string.IsNullOrEmpty(pOfficeId)) return true;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long runtimeKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER, out string runtimeLayer,
                "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string runtimeOffice,
                "");
            return runtimeKingdomId == pKingdom.id && runtimeLayer == pLayer &&
                   runtimeOffice == pOfficeId;
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

        private static string SafeActorName(Actor pActor)
        {
            try { return pActor?.getName() ?? ""; }
            catch { return pActor?.data?.name ?? ""; }
        }

        private static int SafeActorAge(Actor pActor)
        {
            try { return pActor?.getAge() ?? 0; }
            catch { return 0; }
        }
    }
}

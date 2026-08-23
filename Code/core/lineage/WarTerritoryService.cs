using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class WarTerritoryService
    {
        public const string CLAIM_WEAK = "weak_claim";
        public const string CLAIM_STRONG = "strong_claim";
        public const string CLAIM_GENERIC = "claim";

        public const string PROJECT_CORE = "fabricate_core";
        public const string PROJECT_WEAK_CLAIM = "fabricate_weak_claim";
        public const string PROJECT_STRONG_CLAIM = "fabricate_strong_claim";

        public const string GOAL_TAKE_MANDATE = WarGoalTypeIds.TakeMandate;
        public const string GOAL_MANDATE_CONQUEST =
            WarGoalTypeIds.MandateConquest;
        public const string GOAL_TAKE_CORE_CITY = WarGoalTypeIds.TakeCoreCity;
        public const string GOAL_PRESS_CLAIM_CITY =
            WarGoalTypeIds.PressClaimCity;
        public const string GOAL_TAKE_DE_JURE_REGION =
            WarGoalTypeIds.TakeDeJureRegion;
        public const string GOAL_FORCE_VASSAL = WarGoalTypeIds.ForceVassal;
        public const string GOAL_FORCE_TRIBUTARY =
            WarGoalTypeIds.ForceTributary;
        public const string GOAL_INDEPENDENCE = WarGoalTypeIds.Independence;
        public const string GOAL_RESTORE_KINGDOM =
            WarGoalTypeIds.RestoreKingdom;
        public const string GOAL_REUNIFY_SUCCESSION =
            WarGoalTypeIds.ReunifySuccession;
        public const string GOAL_NO_CB = WarGoalTypeIds.NoCb;
        public const string GOAL_BANDIT_SUPPRESSION = WarGoalTypeIds.BanditSuppression;
        public const string GOAL_ZHULU_ANNEXATION =
            ZhuluWarRules.GoalTypeId;

        private const double DEFAULT_PROJECT_COST = 100.0;

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private static readonly Dictionary<string, bool> OwnedNonCoreCache = new Dictionary<string, bool>();
        private static HistoryText H(string pKey) => HistoryLocalizationRules.H(pKey);
        private static string T(string pKey) => HistoryLocalizationRules.Text(pKey);

        public static void ClearRuntime()
        {
            OwnedNonCoreCache.Clear();
        }

        internal sealed class WarGoalRequest
        {
            public string goal_type = "";
            public City target_city;
            public Kingdom target_kingdom;
            public long source_claim_id = -1;
            public long source_core_id = -1;
            public long source_project_id = -1;
            public long source_de_jure_region_id = -1;
            public Actor claimant = null;
        }

        internal sealed class TerritoryStatus
        {
            public long city_id = -1;
            public string city_name = "";
            public string status = "";
            public string label = "";
            public double progress;
            public double cost;
            public double expires_time = -1;
        }

        internal sealed class TargetReport
        {
            public Kingdom target;
            public int core_count;
            public int weak_claim_count;
            public int strong_claim_count;
            public int pending_count;
            public bool can_reclaim;
            public bool can_press_claim;
            public bool can_force_vassal;
            public bool can_force_tributary;
            public bool can_independence;
            public bool can_restore;
            public bool can_reunify_succession;
            public bool can_take_mandate;
            public bool can_mandate_conquest;
            public bool can_no_cb;
            public bool can_fabricate;
            public bool vassal_blocked;
            public City fabrication_city;
            public string fabrication_reason = "";
            public int restoration_claim_count;
            public float power_ratio;
        }

        public sealed class WarTargetOption
        {
            public Kingdom target_kingdom;
            public City target_city;
            public string goal_type = "";
            public string label = "";
            public long source_core_id = -1;
            public long source_claim_id = -1;
            public long restoration_claim_id = -1;
            public long source_de_jure_region_id = -1;
            public long claimant_actor_id = -1;
            public string claimant_name = "";
            public int score;
        }

        internal static bool TryGetDeJureRegion(long pRegionId,
            out DeJureRegion pRegion)
        {
            pRegion = null;
            if (pRegionId < 0L) return false;
            foreach (DeJureRegion region in DeJureRegionStore.ActiveRegions())
                if (region?.RegionId == pRegionId)
                {
                    pRegion = region;
                    return true;
                }
            return false;
        }

        internal static List<City> GetDeJureRegionCities(
            long pRegionId, Kingdom pDefender)
        {
            var result = new List<City>();
            if (!TryGetDeJureRegion(pRegionId, out DeJureRegion region) ||
                pDefender?.data == null) return result;
            foreach (long cityId in region.MemberCityIds ??
                     new List<long>())
            {
                City city = World.world?.cities?.get(cityId);
                if (city?.data != null && !city.isRekt() &&
                    city.kingdom == pDefender &&
                    !PeasantRebelBanditStrongholdService.IsStrongholdCity(
                        city)) result.Add(city);
            }
            result.Sort((a, b) => a.data.id.CompareTo(b.data.id));
            return result;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!IsCivil(pKingdom) || !Ready) return;
            AdvanceProjects(pKingdom);
        }

        public static void ResetTransientStateForIdentityRestoration(long pKingdomId)
        {
            if (!Ready || pKingdomId < 0) return;
            using var transaction = DB.BeginTransaction();
            try
            {
                using (var cmd = new SQLiteCommand(DB))
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = $"UPDATE {WarProjectTableItem.GetTableName()} " +
                                      "SET ACTIVE=0, COMPLETED=0, FINISHED_TIME=@now " +
                                      "WHERE SOURCE_KINGDOM_ID=@k AND ACTIVE=1";
                    cmd.Parameters.AddWithValue("@now", LineageService.CurTime());
                    cmd.Parameters.AddWithValue("@k", pKingdomId);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new SQLiteCommand(DB))
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = $"UPDATE {WarClaimTableItem.GetTableName()} SET ACTIVE=0 " +
                                      "WHERE SOURCE_KINGDOM_ID=@k AND ACTIVE=1";
                    cmd.Parameters.AddWithValue("@k", pKingdomId);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new SQLiteCommand(DB))
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = $"UPDATE {WarGoalTableItem.GetTableName()} " +
                                      "SET RESOLVED=1, RESOLVED_TIME=@now, RESULT='kingdom_fell_reset' " +
                                      "WHERE RESOLVED=0 AND (ATTACKER_KINGDOM_ID=@k OR DEFENDER_KINGDOM_ID=@k)";
                    cmd.Parameters.AddWithValue("@now", LineageService.CurTime());
                    cmd.Parameters.AddWithValue("@k", pKingdomId);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                try { transaction.Rollback(); } catch { }
                ModClass.LogWarning("Restoration transient war reset failed: " + e.Message);
            }
            DirtyWarMaps();
        }

        public static void OnCityTransferred(City pCity, Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (pCity?.data == null || pNewKingdom?.data == null || !Ready) return;
            try
            {
                EraChangeTriggerService.MarkTerritoryRecovery(
                    pCity, pOldKingdom, pNewKingdom);
                foreach (War war in GetCandidateWarsForTransferredCity(pCity, pNewKingdom))
                    if (TryResolveTransferredCityGoal(pCity, pNewKingdom, war))
                        return;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("WarTerritoryService.OnCityTransferred failed: " + e.Message);
            }
        }

        internal static bool TryGetPrimaryOpenGoalCityId(long pWarId,
            out long pCityId)
        {
            pCityId = -1L;
            if (!Ready || pWarId < 0L) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT TARGET_CITY_ID FROM " +
                    WarGoalTableItem.GetTableName() +
                    " WHERE WAR_ID=@war AND RESOLVED=0 AND " +
                    "TARGET_CITY_ID>=0 ORDER BY WAR_GOAL_ID ASC LIMIT 1";
                command.Parameters.AddWithValue("@war", pWarId);
                object value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value) return false;
                pCityId = Convert.ToInt64(value);
                return pCityId >= 0L;
            }
            catch
            {
                pCityId = -1L;
                return false;
            }
        }

        internal static bool HasOpenCityControlGoalForAttacker(City pCity, Kingdom pCapturingKingdom)
        {
            if (pCity?.data == null || pCapturingKingdom?.data == null || !Ready) return false;
            try
            {
                foreach (War war in pCapturingKingdom.getWars())
                {
                    if (war?.data == null || war.hasEnded()) continue;
                    foreach (GoalRow goal in ReadOpenGoals(war.data.id))
                    {
                        bool targetCityMatches = goal.target_city_id == pCity.data.id || goal.target_city_id == pCity.id;
                        if (!targetCityMatches) continue;
                        Kingdom attacker = FindKingdom(goal.attacker_kingdom_id) ?? war.getMainAttacker();
                        if (!IsOnAttackerSideOrSystem(war, pCapturingKingdom, attacker)) continue;
                        if (WarGoalControlRules.ShouldResolveControlledCityGoal(goal.goal_type, true))
                            return true;
                    }
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("WarTerritoryService.HasOpenCityControlGoalForAttacker failed: " + e.Message);
            }

            return false;
        }

        internal static bool HasOpenNonTerritorialSettlementGoal(City pCity,
            Kingdom pCapturingKingdom)
        {
            return FindControlledSettlementWar(pCity, pCapturingKingdom) != null;
        }

        internal static bool TryResolveControlledSettlementGoal(City pCity,
            Kingdom pCapturingKingdom)
        {
            War war = FindControlledSettlementWar(pCity, pCapturingKingdom);
            if (war?.data == null || war.hasEnded()) return false;
            bool completed = WarGoalSettlementRuntimeService.
                OnCityControlChanged(war, pCity, pCapturingKingdom);
            return completed ||
                   WarGoalSettlementRuntimeService.QueueIfReady(war);
        }

        private static War FindControlledSettlementWar(City pCity,
            Kingdom pCapturingKingdom)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null ||
                pCapturingKingdom?.data == null || !Ready) return null;
            try
            {
                foreach (War war in pCapturingKingdom.getWars())
                {
                    if (war?.data == null || war.hasEnded()) continue;
                    foreach (GoalRow goal in ReadOpenGoals(war.data.id))
                    {
                        bool targetCityMatches = goal.target_city_id == pCity.data.id ||
                                                 goal.target_city_id == pCity.id;
                        Kingdom attacker = FindKingdom(goal.attacker_kingdom_id) ??
                                           war.getMainAttacker();
                        Kingdom defender = FindKingdom(goal.defender_kingdom_id) ??
                                           war.getMainDefender();
                        bool capturerIsOnAttackerSide = IsOnAttackerSideOrSystem(
                            war, pCapturingKingdom, attacker);
                        bool cityStillOwnedByDefender = defender?.data != null &&
                                                        pCity.kingdom == defender;
                        if (WarGoalControlRules.ShouldResolveControlledSettlementGoal(
                                goal.goal_type, targetCityMatches,
                                capturerIsOnAttackerSide,
                                cityStillOwnedByDefender)) return war;
                    }
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Controlled settlement lookup failed city=" +
                                    (pCity?.id ?? -1L) + ": " + e.Message);
            }
            return null;
        }

        private static List<War> GetCandidateWarsForTransferredCity(City pCity, Kingdom pNewKingdom)
        {
            var result = new List<War>();
            var seen = new HashSet<long>();

            try
            {
                foreach (War war in pNewKingdom.getWars())
                    AddCandidateWar(result, seen, war);
            }
            catch { }

            foreach (long warId in ReadOpenGoalWarIdsForCity(pCity?.data?.id ?? -1L, pCity?.id ?? -1L))
            {
                War war = null;
                try { war = World.world?.wars?.get(warId); }
                catch { }
                AddCandidateWar(result, seen, war);
            }

            return result;
        }

        private static void AddCandidateWar(List<War> pResult, HashSet<long> pSeen, War pWar)
        {
            if (pWar?.data == null || pWar.hasEnded()) return;
            if (!pSeen.Add(pWar.data.id)) return;
            pResult.Add(pWar);
        }

        private static bool TryResolveTransferredCityGoal(City pCity, Kingdom pNewKingdom, War pWar)
        {
            if (pCity?.data == null || pNewKingdom?.data == null || pWar?.data == null || pWar.hasEnded()) return false;
            if (WarPeaceSettlementService.Instance.HasActionableSettlement(
                    pWar.data.id))
                return false;

            foreach (GoalRow goal in ReadOpenGoals(pWar.data.id))
            {
                bool targetCityMatches = goal.target_city_id == pCity.data.id || goal.target_city_id == pCity.id;
                Kingdom attacker = FindKingdom(goal.attacker_kingdom_id) ?? pWar.getMainAttacker();
                bool newOwnerIsWarAttacker = IsOnAttackerSideOrSystem(pWar, pNewKingdom, attacker);
                if (!WarGoalControlRules.ShouldResolveTransferredCityGoal(goal.goal_type, targetCityMatches,
                        newOwnerIsWarAttacker))
                    continue;

                return WarGoalSettlementRuntimeService.QueueIfReady(pWar);
            }

            return false;
        }

        public static long EnsureCore(Kingdom pKingdom, City pCity, string pSourceType, string pSourceLabel)
        {
            if (!IsCivil(pKingdom) || pCity?.data == null || !Ready) return -1L;
            long existing = FindCoreId(pKingdom.id, pCity.data.id);
            if (existing >= 0) return existing;

            long coreId = TableIdAllocator.Next(DB, KingdomCoreTableItem.GetTableName(), "CORE_ID");
            try
            {
                DB.Insert(KingdomCoreTableItem.GetTableName(),
                    ColumnVal.Create("CORE_ID", coreId),
                    ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                    ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                    ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
                    ColumnVal.Create("CITY_ID", pCity.data.id),
                    ColumnVal.Create("CITY_NAME", pCity.data.name ?? ""),
                    ColumnVal.Create("OWNER_KINGDOM_ID", pCity.kingdom?.id ?? -1L),
                    ColumnVal.Create("OWNER_KINGDOM_NAME", pCity.kingdom?.name ?? ""),
                    ColumnVal.Create("SOURCE_TYPE", pSourceType ?? ""),
                    ColumnVal.Create("SOURCE_LABEL", pSourceLabel ?? ""),
                    ColumnVal.Create("CREATED_TIME", LineageService.CurTime()),
                    ColumnVal.Create("ACTIVE", 1));

                HistoryWriter.RecordKingdom(pKingdom, "war_core_created",
                    HistoryText.Kingdom(pKingdom) + H("aw_hist_core_mark_mid") +
                    HistoryText.City(pCity, pKingdom) + H("aw_hist_core_mark_suffix"), HistoryTarget.City(pCity));
                HistoryWriter.RecordCity(pCity, pKingdom, "war_core_created",
                    HistoryText.City(pCity, pKingdom) + H("aw_hist_core_city_became_mid") +
                    HistoryText.Kingdom(pKingdom) + H("aw_hist_core_city_became_suffix"), HistoryTarget.Kingdom(pKingdom));
                MandateService.OnKingdomCoreCreated(pKingdom, pCity, pSourceType);
                DirtyWarMaps();
                return coreId;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("WarTerritoryService.EnsureCore failed: " + e.Message);
                return -1L;
            }
        }

        public static long CreateProject(Kingdom pSource, Kingdom pTarget, City pTargetCity, string pProjectType,
            string pWarType, string pReasonKey, double pCost = DEFAULT_PROJECT_COST)
        {
            if (!IsCivil(pSource) || !Ready) return -1L;
            if (pProjectType == PROJECT_CORE)
            {
                if (pTargetCity?.data == null) pTargetCity = FindFirstCoreProjectTargetCity(pSource);
                pTarget = pSource;
                if (!CanFabricateCoreProject(pSource, pTargetCity, out _)) return -1L;
            }
            else if (IsClaimProject(pProjectType))
            {
                if (pTargetCity?.data == null) pTargetCity = FindFirstFabricationTargetCity(pSource, pTarget);
                if (!CanFabricateAgainst(pSource, pTarget, pTargetCity, out _)) return -1L;
            }
            else if (!IsCivil(pTarget))
            {
                return -1L;
            }

            long cityId = pTargetCity?.data?.id ?? -1L;
            long existing = FindActiveProjectId(pSource.id, pTarget?.id ?? -1L, cityId, pProjectType);
            if (existing >= 0) return existing;

            long projectId = TableIdAllocator.Next(DB, WarProjectTableItem.GetTableName(), "PROJECT_ID");
            Actor king = pSource.king;
            try
            {
                DB.Insert(WarProjectTableItem.GetTableName(),
                    ColumnVal.Create("PROJECT_ID", projectId),
                    ColumnVal.Create("SOURCE_KINGDOM_ID", pSource.id),
                    ColumnVal.Create("SOURCE_KINGDOM_NAME", pSource.name ?? ""),
                    ColumnVal.Create("SOURCE_KINGDOM_COLOR", HistoryColors.FromKingdom(pSource)),
                    ColumnVal.Create("TARGET_KINGDOM_ID", pTarget?.id ?? -1L),
                    ColumnVal.Create("TARGET_KINGDOM_NAME", pTarget?.name ?? ""),
                    ColumnVal.Create("TARGET_KINGDOM_COLOR", HistoryColors.FromKingdom(pTarget)),
                    ColumnVal.Create("TARGET_CITY_ID", cityId),
                    ColumnVal.Create("TARGET_CITY_NAME", pTargetCity?.data?.name ?? ""),
                    ColumnVal.Create("PROJECT_TYPE", pProjectType ?? ""),
                    ColumnVal.Create("WAR_TYPE", pWarType ?? WarDecisionService.WAR_NORMAL),
                    ColumnVal.Create("REASON_KEY", pReasonKey ?? ""),
                    ColumnVal.Create("PROGRESS", 0.0),
                    ColumnVal.Create("COST", Math.Max(1.0, pCost)),
                    ColumnVal.Create("CREATED_TIME", LineageService.CurTime()),
                    ColumnVal.Create("FINISHED_TIME", -1.0),
                    ColumnVal.Create("ACTIVE", 1),
                    ColumnVal.Create("COMPLETED", 0),
                    ColumnVal.Create("CREATED_BY_ACTOR_ID", king?.data?.id ?? -1L),
                    ColumnVal.Create("CREATED_BY_NAME", king?.getName() ?? ""));

                HistoryWriter.RecordKingdom(pSource, "war_project_started",
                    HistoryText.Kingdom(pSource) + H("aw_hist_project_started_prefix") +
                    HistoryText.PlainText(ProjectLabel(pProjectType)) +
                    H("aw_hist_project_target_mid") + TargetText(pTarget, pTargetCity),
                    pTargetCity?.data != null ? HistoryTarget.City(pTargetCity) : HistoryTarget.Kingdom(pTarget));
                DirtyWarMaps();
                return projectId;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("WarTerritoryService.CreateProject failed: " + e.Message);
                return -1L;
            }
        }

        public static bool TryDeclareReclaimWar(Kingdom pAttacker, Kingdom pDefender)
        {
            City city = FindBestCoreTargetCity(pAttacker, pDefender, out long coreId);
            return TryDeclareReclaimWar(pAttacker, pDefender, city, coreId);
        }

        public static bool TryDeclareReclaimWar(Kingdom pAttacker, Kingdom pDefender, City pSelectedCity, long pCoreId)
        {
            if (IsVassalDecisionOnlyTarget(pAttacker, pDefender)) return false;
            City city = pSelectedCity;
            long coreId = pCoreId;
            if (city?.data == null) return false;
            if (city.kingdom != pDefender) return false;
            if (coreId < 0) coreId = FindCoreId(pAttacker.id, city.data.id);
            if (coreId < 0) return false;
            var goal = new WarGoalRequest
            {
                goal_type = GOAL_TAKE_CORE_CITY,
                target_city = city,
                target_kingdom = pDefender,
                source_core_id = coreId
            };
            War war = WarDecisionService.TryStartWarWithResult(pAttacker, pDefender, "reclaim", "core_reclaim");
            if (war?.data == null) return false;
            return TryPersistGoalOrEndWar(war, goal).Success;
        }

        public static bool TryDeclareClaimWar(Kingdom pAttacker, Kingdom pDefender)
        {
            City city = FindBestClaimTargetCity(pAttacker, pDefender, out long claimId);
            return TryDeclareClaimWar(pAttacker, pDefender, city, claimId);
        }

        public static bool TryDeclareClaimWar(Kingdom pAttacker, Kingdom pDefender, City pSelectedCity, long pClaimId)
        {
            if (IsVassalDecisionOnlyTarget(pAttacker, pDefender)) return false;
            City city = pSelectedCity;
            long claimId = pClaimId;
            if (city?.data == null && claimId < 0) return false;
            if (city?.data != null && city.kingdom != pDefender) return false;
            if (claimId < 0 && city?.data != null)
                claimId = FindBestClaim(pAttacker.id, pDefender.id, city.data.id).claim_id;
            if (claimId < 0) return false;
            var goal = new WarGoalRequest
            {
                goal_type = GOAL_PRESS_CLAIM_CITY,
                target_city = city,
                target_kingdom = pDefender,
                source_claim_id = claimId
            };
            War war = WarDecisionService.TryStartWarWithResult(pAttacker, pDefender, WarDecisionService.WAR_NORMAL,
                "claim_war");
            if (war?.data == null) return false;
            return TryPersistGoalOrEndWar(war, goal).Success;
        }

        public static bool TryDeclareVassalWar(Kingdom pAttacker, Kingdom pDefender)
        {
            if (IsVassalDecisionOnlyTarget(pAttacker, pDefender)) return false;
            City targetCapital = pDefender?.capital ?? FindFirstTargetCity(pDefender);
            if (targetCapital?.data == null) return false;
            var goal = new WarGoalRequest
            {
                goal_type = GOAL_FORCE_VASSAL,
                target_kingdom = pDefender,
                target_city = targetCapital
            };
            War war = WarDecisionService.TryStartWarWithResult(pAttacker, pDefender, "vassal_war", "force_vassal");
            if (war?.data == null) return false;
            return TryPersistGoalOrEndWar(war, goal).Success;
        }

        public static bool TryDeclareTributaryWar(Kingdom pAttacker, Kingdom pDefender)
        {
            if (!WarDecisionService.CanForceTributary(pAttacker, pDefender)) return false;
            City targetCapital = pDefender?.capital ?? FindFirstTargetCity(pDefender);
            if (targetCapital?.data == null) return false;
            var goal = new WarGoalRequest
            {
                goal_type = GOAL_FORCE_TRIBUTARY,
                target_kingdom = pDefender,
                target_city = targetCapital
            };
            War war = WarDecisionService.TryStartWarWithResult(pAttacker, pDefender,
                WarDecisionService.WAR_TRIBUTARY, "tributary_war");
            if (war?.data == null) return false;
            return TryPersistGoalOrEndWar(war, goal).Success;
        }

        public static bool TryDeclareIndependenceWar(Kingdom pAttacker, Kingdom pSuzerain)
        {
            if (pAttacker?.data == null || pSuzerain?.data == null) return false;
            if (VassalService.GetDiplomaticSuzerain(pAttacker) != pSuzerain)
                return false;
            City targetCapital = pSuzerain.capital ??
                                 FindFirstTargetCity(pSuzerain);
            if (targetCapital?.data == null) return false;
            var goal = new WarGoalRequest
            {
                goal_type = GOAL_INDEPENDENCE,
                target_kingdom = pSuzerain,
                target_city = targetCapital
            };
            War war = WarDecisionService.TryStartWarWithResult(pAttacker, pSuzerain,
                "independence_war", "independence_war");
            if (war?.data == null) return false;
            return TryPersistGoalOrEndWar(war, goal).Success;
        }

        public static bool TryDeclareRestorationWar(Kingdom pAttacker, Kingdom pDefender)
        {
            RoyalClaimService.RoyalClaimInfo claim = FindBestRestorationClaim(pAttacker, pDefender, out City targetCity);
            if (claim == null || claim.claim_id < 0) return false;

            Actor claimant = FindActor(claim.claimant_actor_id);
            if (!RoyalClaimService.IsEligibleRestorationClaimant(claimant)) return false;
            return TryDeclareRestorationWar(pAttacker, pDefender, targetCity, claim.claim_id, claimant);
        }

        public static bool TryDeclareRestorationWar(Kingdom pAttacker, Kingdom pDefender, City pTargetCity,
            long pClaimId, Actor pClaimant)
        {
            if (IsVassalDecisionOnlyTarget(pAttacker, pDefender)) return false;
            if (IsAlreadyAtWar(pAttacker, pDefender)) return false;
            City targetCity = pTargetCity;
            long claimId = pClaimId;
            Actor claimant = pClaimant;
            if (claimId < 0 || targetCity?.data == null)
            {
                RoyalClaimService.RoyalClaimInfo claim = FindBestRestorationClaim(pAttacker, pDefender, out targetCity);
                if (claim == null || claim.claim_id < 0) return false;
                claimId = claim.claim_id;
                claimant = FindActor(claim.claimant_actor_id);
            }
            if (!RoyalClaimService.IsEligibleRestorationClaimant(claimant)) return false;
            if (targetCity?.data == null || targetCity.kingdom != pDefender) return false;
            var goal = new WarGoalRequest
            {
                goal_type = GOAL_RESTORE_KINGDOM,
                target_city = targetCity,
                target_kingdom = pDefender,
                source_claim_id = claimId,
                claimant = claimant
            };
            War war = WarDecisionService.TryStartWarWithResult(pAttacker, pDefender,
                WarDecisionService.WAR_RESTORATION, "restoration");
            if (war?.data == null) return false;
            return TryPersistGoalOrEndWar(war, goal).Success;
        }

        public static War TryDeclareAutonomousRestorationCoreWar(Kingdom pAttacker,
            City pTargetCity, long pClaimId, Actor pClaimant)
        {
            Kingdom defender = pTargetCity?.kingdom;
            if (!IsCivil(pAttacker) || !IsCivil(defender) || pAttacker == defender) return null;
            if (pTargetCity?.data == null || pTargetCity.isRekt()) return null;
            if (IsAlreadyAtWar(pAttacker, defender)) return null;
            var goal = new WarGoalRequest
            {
                goal_type = GOAL_TAKE_CORE_CITY,
                target_city = pTargetCity,
                target_kingdom = defender,
                source_claim_id = pClaimId,
                source_core_id = FindCoreId(pAttacker.id, pTargetCity.data.id),
                claimant = pClaimant
            };
            War war = WarDecisionService.TryStartSystemWar(pAttacker,
                defender,
                WarDecisionService.WAR_RESTORATION, "self_restoration_core");
            if (war?.data == null) return null;
            return TryPersistGoalOrEndWar(war, goal).Success ? war : null;
        }

        public static bool TryDeclareNoCbWar(Kingdom pAttacker, Kingdom pDefender)
        {
            if (IsVassalDecisionOnlyTarget(pAttacker, pDefender)) return false;
            City targetCapital = pDefender?.capital ??
                                 FindFirstTargetCity(pDefender);
            if (targetCapital?.data == null) return false;
            var goal = new WarGoalRequest
            {
                goal_type = GOAL_NO_CB,
                target_kingdom = pDefender,
                target_city = targetCapital
            };
            War war = WarDecisionService.TryStartWarWithResult(pAttacker, pDefender, WarDecisionService.WAR_NORMAL,
                "no_cb", pNoCb: true);
            if (war?.data == null) return false;
            return TryPersistGoalOrEndWar(war, goal).Success;
        }

        public static bool TryDeclareMandateWar(Kingdom pAttacker, Kingdom pDefender)
        {
            if (IsVassalDecisionOnlyTarget(pAttacker, pDefender)) return false;
            if (!MandatePhaseService.CanContestMandate) return false;
            if (MandateService.GetCurrentMandateKingdom() != pDefender) return false;
            var goal = new WarGoalRequest
            {
                goal_type = GOAL_TAKE_MANDATE,
                target_kingdom = pDefender,
                target_city = pDefender?.capital ?? FindFirstTargetCity(pDefender)
            };
            War war = WarDecisionService.TryStartSystemWar(pAttacker,
                pDefender, MandateService.WAR_TIANMING, "tianming");
            if (war?.data == null) return false;
            return TryPersistGoalOrEndWar(war, goal).Success;
        }

        public static bool TryDeclareMandateConquestWar(Kingdom pAttacker, Kingdom pDefender, City pSelectedCity)
        {
            if (!CanUseMandateConquest(pAttacker, pDefender)) return false;
            City city = pSelectedCity?.data != null ? pSelectedCity : pDefender?.capital ?? FindFirstTargetCity(pDefender);
            var goal = new WarGoalRequest
            {
                goal_type = GOAL_MANDATE_CONQUEST,
                target_kingdom = pDefender,
                target_city = city
            };
            War war = WarDecisionService.TryStartSystemWar(pAttacker,
                pDefender, WarDecisionService.WAR_NORMAL,
                "mandate_conquest");
            if (war?.data == null) return false;
            return TryPersistGoalOrEndWar(war, goal).Success;
        }

        internal static WarGoalCreateResult TryPersistGoalOrEndWar(
            War pWar, WarGoalRequest pGoal)
        {
            WarGoalCreateResult result = CreateGoalForWar(pWar, pGoal);
            if (result.Success) return result;
            try { World.world?.wars?.endWar(pWar, WarWinner.Nobody); }
            catch { }
            return result;
        }

        public static WarGoalCreateResult CreateGoalForWar(War pWar,
            WarGoalRequest pGoal)
        {
            if (pWar?.data == null || pGoal == null)
                return new WarGoalCreateResult(false, -1L,
                    "invalid_war_goal_snapshot");
            if (!Ready)
                return new WarGoalCreateResult(false, -1L,
                    "war_goal_database_unavailable");
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (attacker?.data == null || defender?.data == null)
                return new WarGoalCreateResult(false, -1L,
                    "invalid_war_goal_participants");
            if (pGoal.goal_type == GOAL_TAKE_DE_JURE_REGION &&
                (pGoal.source_de_jure_region_id < 0L ||
                 !TryGetDeJureRegion(pGoal.source_de_jure_region_id,
                     out DeJureRegion region) ||
                 GetDeJureRegionCities(region.RegionId, defender).Count == 0))
                return new WarGoalCreateResult(false, -1L,
                    "invalid_de_jure_region_target");
            if (!TryGetGoalSettlementSnapshot(pGoal, attacker, defender,
                    out string completionKind, out int requiredWarScore,
                    out string snapshotFailure))
                return new WarGoalCreateResult(false, -1L, snapshotFailure);

            var snapshot = new WarGoalSnapshot
            {
                WarId = pWar.data.id,
                AttackerKingdomId = attacker.id,
                AttackerName = attacker.name ?? "",
                AttackerColor = HistoryColors.FromKingdom(attacker),
                DefenderKingdomId = defender.id,
                DefenderName = defender.name ?? "",
                DefenderColor = HistoryColors.FromKingdom(defender),
                WarType = pWar.getAsset()?.id ?? "",
                GoalType = pGoal.goal_type ?? "",
                RequiredWarScore = requiredWarScore,
                CompletionKind = completionKind,
                TargetCityId = pGoal.target_city?.data?.id ?? -1L,
                TargetCityName = pGoal.target_city?.data?.name ?? "",
                TargetKingdomId = pGoal.target_kingdom?.id ?? -1L,
                TargetKingdomName = pGoal.target_kingdom?.name ?? "",
                SourceClaimId = pGoal.source_claim_id,
                SourceCoreId = pGoal.source_core_id,
                SourceProjectId = pGoal.source_project_id,
                SourceDeJureRegionId = pGoal.source_de_jure_region_id,
                ClaimantActorId = pGoal.claimant?.data?.id ?? -1L,
                ClaimantName = pGoal.claimant?.getName() ?? "",
                CreatedTime = LineageService.CurTime()
            };
            WarGoalCreateResult result = WarGoalPersistence.TryCreate(
                DB, snapshot);
            if (!result.Success) return result;
            try
            {
                HistoryWriter.RecordKingdom(attacker, "war_goal_set",
                    HistoryText.Kingdom(attacker) + H("aw_hist_war_goal_set_mid") +
                    HistoryText.PlainText(GoalLabel(pGoal.goal_type)) +
                    GoalTargetText(pGoal), GoalHistoryTarget(pGoal, defender));
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "WarTerritoryService war-goal history failed: " +
                    e.Message);
            }
            return result;
        }

        private static bool TryGetGoalSettlementSnapshot(
            WarGoalRequest pGoal, Kingdom pAttacker, Kingdom pDefender,
            out string pCompletionKind, out int pRequiredWarScore,
            out string pFailureReason)
        {
            pCompletionKind = "";
            pRequiredWarScore = -1;
            pFailureReason = "";
            if (!WarGoalSettlementRules.TryGetAutomaticSettlementProfile(
                    pGoal?.goal_type, out var profile))
            {
                pFailureReason = "unknown_war_goal_type";
                return false;
            }
            if (pGoal.target_city?.data == null &&
                pGoal.goal_type != GOAL_TAKE_DE_JURE_REGION)
            {
                pFailureReason = "war_goal_target_city_unavailable";
                return false;
            }

            pCompletionKind = profile.CompletionKind;
            int actualCost = pGoal.goal_type == GOAL_TAKE_DE_JURE_REGION
                ? WarGoalSettlementRules.MinimumRequiredScore
                : profile.UsesDynamicCityCost
                ? WarPeaceTermsRules.CityCessionCost(
                    WarPeaceSettlementWorld.CityFacts(pGoal.target_city,
                        pAttacker.id, pDefender.id))
                : profile.RequiredWarScore;
            pRequiredWarScore =
                WarGoalSettlementRules.SnapshotRequiredScore(actualCost);
            return true;
        }

        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null || !Ready) return;
            if (!WarPeaceSettlementService.Instance
                    .TryHasExecutedCoalitionSettlement(pWar.data.id,
                        out bool hasExecutedSettlement)) return;
            if (hasExecutedSettlement)
            {
                if (!WarPeaceSettlementService.Instance
                        .TryReadExecutedCoalitionTerms(pWar.data.id,
                            out IReadOnlyList<WarPeaceSettlementTerm>
                                executedTerms)) return;
                foreach (GoalRow row in ReadOpenGoals(pWar.data.id))
                    ResolveNegotiatedGoalRecord(pWar, pWinner, row,
                        executedTerms);
                return;
            }
            foreach (GoalRow row in ReadOpenGoals(pWar.data.id))
                ResolveGoal(pWar, pWinner, row);
        }

        private static void ResolveNegotiatedGoalRecord(War pWar,
            WarWinner pWinner, GoalRow pGoal,
            IReadOnlyList<WarPeaceSettlementTerm> pExecutedTerms)
        {
            Kingdom attacker = FindKingdom(pGoal.attacker_kingdom_id) ??
                               pWar.getMainAttacker();
            Kingdom defender = FindKingdom(pGoal.defender_kingdom_id) ??
                               pWar.getMainDefender();
            City targetCity = FindCity(pGoal.target_city_id);
            string result;
            if (NegotiatedGoalMatchesExecutedTerm(pGoal, pExecutedTerms))
            {
                RecordGoalVictory(attacker, defender, targetCity, pGoal);
                result = "negotiated_goal_enforced";
            }
            else
            {
                result = "negotiated_goal_not_enforced";
                RecordGoalFailure(attacker, defender, targetCity, pGoal,
                    result);
            }
            MarkGoalResolved(pGoal.war_goal_id, result);
        }

        private static bool NegotiatedGoalMatchesExecutedTerm(
            GoalRow pGoal,
            IReadOnlyList<WarPeaceSettlementTerm> pExecutedTerms)
        {
            if (pExecutedTerms == null) return false;
            for (int i = 0; i < pExecutedTerms.Count; i++)
            {
                WarPeaceSettlementTerm term = pExecutedTerms[i];
                if (term == null ||
                    term.ToKingdomId != pGoal.attacker_kingdom_id ||
                    term.FromKingdomId != pGoal.defender_kingdom_id)
                    continue;
                if (pGoal.goal_type == GOAL_FORCE_VASSAL &&
                    term.Kind == WarPeaceTermKind.ForceVassal) return true;
                if (pGoal.goal_type == GOAL_FORCE_TRIBUTARY &&
                    term.Kind == WarPeaceTermKind.ForceTributary) return true;
                if (pGoal.target_city_id >= 0 &&
                    term.Kind == WarPeaceTermKind.CedeCity &&
                    term.CityId == pGoal.target_city_id) return true;
                if (pGoal.goal_type == GOAL_TAKE_DE_JURE_REGION &&
                    term.Kind == WarPeaceTermKind.CedeCity &&
                    term.WarGoalId == pGoal.war_goal_id &&
                    TryGetDeJureRegion(pGoal.source_de_jure_region_id,
                        out DeJureRegion region) &&
                    DeJureRegionStore.IsEligibleCityId(term.CityId) &&
                    region.MemberCityIds.Contains(term.CityId)) return true;
                if (term.WarGoalId != pGoal.war_goal_id ||
                    !WarGoalSettlementRules.TryGetAutomaticSettlementProfile(
                        pGoal.goal_type, out var profile))
                    continue;
                if (profile.Effect ==
                        WarGoalAutomaticSettlementEffect.TakeMandate &&
                    term.Kind == WarPeaceTermKind.TakeMandate) return true;
                if (profile.Effect ==
                        WarGoalAutomaticSettlementEffect.RestoreKingdom &&
                    term.Kind == WarPeaceTermKind.RestoreKingdom) return true;
                if (profile.Effect ==
                        WarGoalAutomaticSettlementEffect.Independence &&
                    term.Kind == WarPeaceTermKind.Independence) return true;
                if (profile.Effect ==
                        WarGoalAutomaticSettlementEffect.ReunifySuccession &&
                    term.Kind == WarPeaceTermKind.ReunifySuccession) return true;
                if (profile.Effect ==
                        WarGoalAutomaticSettlementEffect.NoCbOutcome &&
                    term.Kind == WarPeaceTermKind.NoCbOutcome) return true;
            }
            return false;
        }

        public static bool HasWarGoal(long pWarId)
        {
            if (!Ready || pWarId < 0) return false;
            return CountSql(WarGoalTableItem.GetTableName(),
                "WAR_ID=@w AND RESOLVED=0", ("@w", pWarId)) > 0;
        }

        public static int ResolveLegacyZhuluGoals(long pWarId,
            string pResult)
        {
            if (!Ready || pWarId < 0L) return 0;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText =
                    $"UPDATE {WarGoalTableItem.GetTableName()} " +
                    "SET RESOLVED=1, RESOLVED_TIME=@time, RESULT=@result " +
                    "WHERE WAR_ID=@war AND GOAL_TYPE=@goal AND RESOLVED=0";
                command.Parameters.AddWithValue("@time",
                    LineageService.CurTime());
                command.Parameters.AddWithValue("@result", pResult ?? "");
                command.Parameters.AddWithValue("@war", pWarId);
                command.Parameters.AddWithValue("@goal",
                    GOAL_ZHULU_ANNEXATION);
                return command.ExecuteNonQuery();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("ResolveLegacyZhuluGoals failed war=" +
                                    pWarId + ": " + exception.Message);
                return 0;
            }
        }

        public static bool HasOpenGoalType(long pWarId, params string[] pGoalTypes)
        {
            if (pWarId < 0 || pGoalTypes == null || pGoalTypes.Length == 0) return false;
            foreach (GoalRow row in ReadOpenGoals(pWarId))
            {
                foreach (string goalType in pGoalTypes)
                    if (row.goal_type == goalType) return true;
            }
            return false;
        }

        public static TerritoryStatus GetCoreStatus(Kingdom pFocus, City pCity)
        {
            var result = BaseStatus(pCity);
            if (!IsCivil(pFocus) || pCity?.data == null || !Ready) return result;

            if (FindCoreId(pFocus.id, pCity.data.id) >= 0)
            {
                result.status = "core";
                result.label = T("aw_map_status_core");
                return result;
            }

            ProjectRow pending = FindPendingProject(pFocus.id, pCity.data.id, PROJECT_CORE);
            if (pending.project_id < 0)
                pending = FindCurrentDecisionProject(pFocus, pCity.data.id, PROJECT_CORE);
            if (pending.project_id >= 0)
            {
                result.status = "pending_core";
                result.label = T("aw_map_status_fabricate_core");
                result.progress = pending.progress;
                result.cost = pending.cost;
                return result;
            }

            if (pCity.kingdom == pFocus)
            {
                result.status = "owned_non_core";
                result.label = T("aw_map_status_non_core");
            }
            return result;
        }

        public static bool IsOwnedNonCore(Kingdom pFocus, City pCity)
        {
            if (!IsCivil(pFocus) || pCity?.data == null || pCity.kingdom != pFocus || !Ready) return false;
            string key = WarTerritoryCacheRules.BuildOwnedNonCoreKey(pFocus.id, pCity.data.id, pCity.kingdom?.id ?? -1L);
            if (string.IsNullOrEmpty(key)) return false;
            if (OwnedNonCoreCache.TryGetValue(key, out bool cached)) return cached;
            bool result = FindCoreId(pFocus.id, pCity.data.id) < 0;
            OwnedNonCoreCache[key] = result;
            return result;
        }

        public static TerritoryStatus GetClaimStatus(Kingdom pFocus, City pCity)
        {
            var result = BaseStatus(pCity);
            if (!IsCivil(pFocus) || pCity?.data == null || !Ready) return result;

            if (FindCoreId(pFocus.id, pCity.data.id) >= 0)
            {
                result.status = "strong_claim";
                result.label = T("aw_map_status_core_claim");
                return result;
            }

            ClaimRow claim = FindBestClaim(pFocus.id, pCity.kingdom?.id ?? -1L, pCity.data.id);
            if (claim.claim_id >= 0)
            {
                result.status = claim.claim_type == CLAIM_STRONG ? "strong_claim" : "weak_claim";
                result.label = claim.claim_type == CLAIM_STRONG
                    ? T("aw_map_status_strong_claim")
                    : T("aw_map_status_weak_claim");
                result.expires_time = claim.expires_time;
                return result;
            }

            ProjectRow pending = FindPendingProject(pFocus.id, pCity.data.id, PROJECT_WEAK_CLAIM, PROJECT_STRONG_CLAIM);
            if (pending.project_id < 0)
                pending = FindPendingProjectByTargetKingdom(pFocus.id, pCity.kingdom?.id ?? -1L,
                    PROJECT_WEAK_CLAIM, PROJECT_STRONG_CLAIM);
            if (pending.project_id < 0)
                pending = FindCurrentDecisionProject(pFocus, pCity.data.id, PROJECT_WEAK_CLAIM, PROJECT_STRONG_CLAIM);
            if (pending.project_id >= 0)
            {
                result.status = "pending_claim";
                result.label = pending.project_type == PROJECT_STRONG_CLAIM
                    ? T("aw_map_status_fabricate_strong_claim")
                    : T("aw_map_status_fabricate_weak_claim");
                result.progress = pending.progress;
                result.cost = pending.cost;
            }
            return result;
        }

        public static string BuildCoreTooltip(Kingdom pFocus, Kingdom pHover)
        {
            if (pFocus?.data == null) return "";
            var lines = new List<string> { T("aw_map_focus_realm") + pFocus.name };
            if (pHover?.data != null) lines.Add(T("aw_map_hover_realm") + pHover.name);
            int cores = CountCores(pFocus.id);
            int pending = CountProjects(pFocus.id, PROJECT_CORE);
            int nonCoreOwned = CountOwnedNonCore(pFocus);
            lines.Add(T("aw_map_core_cities") + cores);
            lines.Add(T("aw_map_non_core") + nonCoreOwned);
            lines.Add(T("aw_map_pending_core") + pending);
            return string.Join("\n", lines.ToArray());
        }

        public static string BuildCoreTooltip(Kingdom pFocus, Kingdom pHover, City pHoverCity)
        {
            string text = BuildCoreTooltip(pFocus, pHover);
            if (pFocus?.data == null || pHoverCity?.data == null) return text;

            TerritoryStatus status = GetCoreStatus(pFocus, pHoverCity);
            string cityBlock = MapModeTooltipTextRules.BuildPointedCityStatusBlock(
                T("aw_map_hover_city"),
                T("aw_map_city_status"),
                T("aw_map_progress"),
                pHoverCity.data.name ?? "",
                string.IsNullOrEmpty(status.label) ? T("aw_map_status_none") : status.label,
                status.progress,
                status.cost);
            if (string.IsNullOrEmpty(cityBlock)) return text;
            return string.IsNullOrEmpty(text) ? cityBlock : text + "\n" + cityBlock;
        }

        public static string BuildClaimTooltip(Kingdom pFocus, Kingdom pHover)
        {
            if (pFocus?.data == null) return "";
            var lines = new List<string> { T("aw_map_focus_realm") + pFocus.name };
            if (pHover?.data != null) lines.Add(T("aw_map_hover_realm") + pHover.name);
            lines.Add(T("aw_map_strong_claim") + WarTargetSelectionRules.CountStrongClaimsForDisplay(
                CountClaims(pFocus.id, CLAIM_STRONG), CountCores(pFocus.id)));
            lines.Add(T("aw_map_weak_claim") + CountClaims(pFocus.id, CLAIM_WEAK));
            lines.Add(T("aw_map_pending_claim") + CountProjects(pFocus.id, PROJECT_WEAK_CLAIM, PROJECT_STRONG_CLAIM));
            return string.Join("\n", lines.ToArray());
        }

        public static string BuildClaimTooltip(Kingdom pFocus, Kingdom pHover, City pHoverCity)
        {
            string text = BuildClaimTooltip(pFocus, pHover);
            if (pFocus?.data == null || pHoverCity?.data == null) return text;

            TerritoryStatus status = GetClaimStatus(pFocus, pHoverCity);
            string cityBlock = MapModeTooltipTextRules.BuildPointedCityStatusBlock(
                T("aw_map_hover_city"),
                T("aw_map_city_status"),
                T("aw_map_progress"),
                pHoverCity.data.name ?? "",
                string.IsNullOrEmpty(status.label) ? T("aw_map_status_none") : status.label,
                status.progress,
                status.cost);
            if (string.IsNullOrEmpty(cityBlock)) return text;
            return string.IsNullOrEmpty(text) ? cityBlock : text + "\n" + cityBlock;
        }

        public static bool CanUseMandateConquest(Kingdom pSource, Kingdom pTarget)
        {
            if (!IsCivil(pSource) || !IsCivil(pTarget) || pSource == pTarget) return false;
            return MandateConquestRules.CanUseMandateConquest(
                pAttackerIsCurrentMandate: MandateService.GetCurrentMandateKingdom() == pSource,
                pVassalBlocked: IsVassalDecisionOnlyTarget(pSource, pTarget),
                pSameAlliance: IsSameAlliance(pSource, pTarget),
                pAttackerSystemPower: GetAllianceSystemPower(pSource),
                pDefenderAlliancePower: GetAllianceSystemPower(pTarget));
        }

        public static bool CanUseMandateConquestReadOnly(Kingdom pSource,
            Kingdom pTarget, MandateReport pMandateReport)
        {
            if (!IsCivil(pSource) || !IsCivil(pTarget) || pSource == pTarget)
                return false;
            return MandateConquestRules.CanUseMandateConquest(
                pAttackerIsCurrentMandate:
                MandateService.IsMandateKingdomReadOnly(pSource,
                    pMandateReport),
                pVassalBlocked: IsVassalDecisionOnlyTarget(pSource, pTarget),
                pSameAlliance: IsSameAlliance(pSource, pTarget),
                pAttackerSystemPower: GetAllianceSystemPower(pSource),
                pDefenderAlliancePower: GetAllianceSystemPower(pTarget));
        }

        public static float GetAllianceSystemPower(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0f;
            float power = 0f;
            var seen = new HashSet<long>();
            try
            {
                Alliance alliance = pKingdom.getAlliance();
                if (alliance != null)
                {
                    foreach (Kingdom member in alliance.kingdoms_hashset)
                    {
                        if (member?.data == null || member.isRekt() || !seen.Add(member.id)) continue;
                        power += VassalService.GetWarPowerScore(member, pIncludeVassals: true);
                    }
                }
            }
            catch { }

            if (seen.Add(pKingdom.id))
                power += VassalService.GetWarPowerScore(pKingdom, pIncludeVassals: true);
            return power;
        }

        public static bool AreInSameAlliance(Kingdom pSource,
            Kingdom pTarget)
        {
            return IsSameAlliance(pSource, pTarget);
        }

        public static List<TargetReport> BuildTargetReports(Kingdom pSource)
        {
            var result = new List<TargetReport>();
            if (!IsCivil(pSource)) return result;
            List<RoyalClaimService.RoyalClaimInfo> hostedRoyalClaims = RoyalClaimService.GetHostedClaims(pSource);
            foreach (Kingdom target in CandidateKingdoms(pSource))
            {
                City fabricationCity = FindFirstFabricationTargetCity(pSource, target);
                City reasonCity = fabricationCity ?? FindFirstTargetCity(target);
                bool canFabricate = CanFabricateAgainst(pSource, target, reasonCity, out string fabricationReason);
                bool vassalBlocked = IsVassalDecisionOnlyTarget(pSource, target);
                int coreTargets = CountCoreTargets(pSource.id, target.id);
                int weakClaims = CountClaimTargets(pSource.id, target.id, CLAIM_WEAK);
                int explicitStrongClaims = CountClaimTargets(pSource.id, target.id, CLAIM_STRONG);
                var report = new TargetReport
                {
                    target = target,
                    core_count = coreTargets,
                    weak_claim_count = weakClaims,
                    strong_claim_count = WarTargetSelectionRules.CountStrongClaimsForDisplay(
                        explicitStrongClaims, coreTargets),
                    pending_count = CountProjectTargets(pSource.id, target.id) +
                                    CountCurrentDecisionProjectsAgainst(pSource, target,
                                        PROJECT_WEAK_CLAIM, PROJECT_STRONG_CLAIM),
                    can_no_cb = !vassalBlocked && CanNoCb(pSource),
                    can_fabricate = canFabricate,
                    vassal_blocked = vassalBlocked,
                    fabrication_city = fabricationCity,
                    fabrication_reason = fabricationReason,
                    power_ratio = PowerRatio(pSource, target)
                };
                report.can_reclaim = !vassalBlocked && report.core_count > 0;
                report.can_press_claim = !vassalBlocked && WarTargetSelectionRules.HasClaimLikeCasusBelli(
                    weakClaims, explicitStrongClaims, coreTargets);
                report.can_take_mandate = !vassalBlocked &&
                    MandateService.GetCurrentMandateKingdom() == target &&
                    WarDecisionService.HasValidCasusBelli(pSource, target, MandateService.WAR_TIANMING);
                report.can_mandate_conquest = CanUseMandateConquest(pSource, target);
                report.can_force_vassal = !vassalBlocked &&
                    WarDecisionService.HasValidCasusBelli(pSource, target, "vassal_war");
                report.can_force_tributary = !vassalBlocked &&
                    WarDecisionService.HasValidCasusBelli(pSource, target,
                        WarDecisionService.WAR_TRIBUTARY);
                report.can_independence = VassalService.GetDiplomaticSuzerain(pSource) == target &&
                                          !IsAlreadyAtWar(pSource, target);
                report.restoration_claim_count = CountRestorationClaimsAgainst(hostedRoyalClaims, target);
                report.can_restore = WarRestorationRules.CanExposeRestorationAction(
                    report.restoration_claim_count > 0,
                    vassalBlocked,
                    IsAlreadyAtWar(pSource, target),
                    out _);
                report.can_reunify_succession =
                    SuccessionDisputeService.CanDeclareReunification(
                        pSource, target);
                result.Add(report);
            }
            result.Sort((a, b) =>
            {
                int scoreA = (a.can_take_mandate ? 500 : 0) + (a.can_reunify_succession ? 420 : 0) + (a.can_mandate_conquest ? 260 : 0) + (a.can_force_tributary ? 45 : 0) + a.core_count * 100 + (a.can_restore ? 80 : 0) + (a.can_independence ? 90 : 0) + a.strong_claim_count * 50 +
                              a.weak_claim_count * 20 + a.pending_count;
                int scoreB = (b.can_take_mandate ? 500 : 0) + (b.can_reunify_succession ? 420 : 0) + (b.can_mandate_conquest ? 260 : 0) + (b.can_force_tributary ? 45 : 0) + b.core_count * 100 + (b.can_restore ? 80 : 0) + (b.can_independence ? 90 : 0) + b.strong_claim_count * 50 +
                             b.weak_claim_count * 20 + b.pending_count;
                int cmp = scoreB.CompareTo(scoreA);
                return cmp != 0 ? cmp : string.Compare(a.target?.name, b.target?.name, StringComparison.Ordinal);
            });
            return result;
        }

        public static List<WarTargetOption> BuildTargetOptions(Kingdom pSource, Kingdom pTarget)
        {
            return BuildTargetOptions(pSource, pTarget,
                pIncludeUnavailable: false);
        }

        internal static List<WarTargetOption> BuildTargetOptions(
            Kingdom pSource, Kingdom pTarget, bool pIncludeUnavailable)
        {
            var result = new List<WarTargetOption>();
            if (!IsCivil(pSource) || pTarget?.data == null) return result;

            if (PeasantRebelRouteService.IsOriginSuppressionPair(
                    pSource, pTarget))
            {
                City stronghold = PeasantRebelBanditStrongholdService.
                    ResolveStronghold(pTarget);
                if (stronghold?.data != null)
                    result.Add(MakeOption(pTarget, stronghold,
                        GOAL_BANDIT_SUPPRESSION,
                        GoalLabel(GOAL_BANDIT_SUPPRESSION), -1, -1, -1,
                        null, hasCore: false, hasStrongClaim: false,
                        hasWeakClaim: false, restorationStrength: 0));
            }

            bool vassalBlocked = IsVassalDecisionOnlyTarget(pSource, pTarget);
            if (pIncludeUnavailable ||
                (!vassalBlocked && WarDecisionService.HasValidCasusBelli(
                    pSource, pTarget, WarDecisionService.WAR_ZHULU)))
                result.Add(MakeOption(pTarget,
                    pTarget.capital ?? FindFirstTargetCity(pTarget),
                    GOAL_ZHULU_ANNEXATION,
                    GoalLabel(GOAL_ZHULU_ANNEXATION), -1, -1, -1, null,
                    hasCore: false, hasStrongClaim: false,
                    hasWeakClaim: false, restorationStrength: 0));
            if (pIncludeUnavailable ||
                (!vassalBlocked &&
                 SuccessionDisputeService.CanDeclareReunification(
                     pSource, pTarget)))
                result.Add(MakeOption(pTarget,
                    pTarget.capital ?? FindFirstTargetCity(pTarget),
                    GOAL_REUNIFY_SUCCESSION,
                    GoalLabel(GOAL_REUNIFY_SUCCESSION), -1, -1, -1,
                    pSource.king, hasCore: false, hasStrongClaim: true,
                    hasWeakClaim: false, restorationStrength: 0));
            if (pIncludeUnavailable ||
                (!vassalBlocked &&
                 MandateService.GetCurrentMandateKingdom() == pTarget &&
                 WarDecisionService.HasValidCasusBelli(pSource, pTarget,
                     MandateService.WAR_TIANMING)))
                result.Add(MakeOption(pTarget, pTarget.capital ?? FindFirstTargetCity(pTarget),
                    GOAL_TAKE_MANDATE, GoalLabel(GOAL_TAKE_MANDATE),
                    -1, -1, -1, null, hasCore: false, hasStrongClaim: false, hasWeakClaim: false,
                    restorationStrength: 0));

            if (pIncludeUnavailable || CanUseMandateConquest(pSource, pTarget))
                result.Add(MakeOption(pTarget, pTarget.capital ?? FindFirstTargetCity(pTarget),
                    GOAL_MANDATE_CONQUEST, GoalLabel(GOAL_MANDATE_CONQUEST),
                    -1, -1, -1, null, hasCore: false, hasStrongClaim: true, hasWeakClaim: false,
                    restorationStrength: 0));

            City city = FindBestCoreTargetCity(pSource, pTarget, out long coreId);
            if (pIncludeUnavailable || (!vassalBlocked && city?.data != null))
                result.Add(MakeOption(pTarget, city, GOAL_TAKE_CORE_CITY, GoalLabel(GOAL_TAKE_CORE_CITY),
                    coreId, -1, -1, null, hasCore: true, hasStrongClaim: false, hasWeakClaim: false,
                    restorationStrength: 0));

            city = FindBestClaimTargetCity(pSource, pTarget, out long claimId);
            bool hasClaim = claimId >= 0;
            ClaimRow claim = hasClaim
                ? FindBestClaim(pSource.id, pTarget.id,
                    city?.data?.id ?? -1L)
                : default(ClaimRow);
            bool strong = hasClaim && claim.claim_type == CLAIM_STRONG;
            if (pIncludeUnavailable || (!vassalBlocked && hasClaim))
            {
                result.Add(MakeOption(pTarget, city, GOAL_PRESS_CLAIM_CITY,
                    !hasClaim ? GoalLabel(GOAL_PRESS_CLAIM_CITY) :
                    strong ? T("aw_hist_goal_press_strong_claim_city") :
                        T("aw_hist_goal_press_weak_claim_city"),
                    -1, claimId, -1, null, hasCore: false,
                    hasStrongClaim: strong,
                    hasWeakClaim: hasClaim && !strong,
                    restorationStrength: 0));
            }

            foreach (DeJureRegion region in DeJureRegionStore.ActiveRegions())
            {
                List<City> sourceRegionCities = GetDeJureRegionCities(
                    region?.RegionId ?? -1L, pSource);
                List<City> regionCities = GetDeJureRegionCities(
                    region?.RegionId ?? -1L, pTarget);
                if (!DeJureWarEligibilityRules.HasCommonRegionMembers(
                        sourceRegionCities.Count, regionCities.Count))
                    continue;
                City representative = regionCities[0];
                result.Add(MakeOption(pTarget, representative,
                    GOAL_TAKE_DE_JURE_REGION,
                    GoalLabel(GOAL_TAKE_DE_JURE_REGION) + ": " +
                    (region.RegionName ?? representative.data.name),
                    -1, -1, -1, null, hasCore: false,
                    hasStrongClaim: true, hasWeakClaim: false,
                    restorationStrength: 0, pRegionId: region.RegionId));
            }

            RoyalClaimService.RoyalClaimInfo restoration = FindBestRestorationClaim(pSource, pTarget, out City restorationCity);
            bool hasRestoration = restoration != null &&
                                  restoration.claim_id >= 0 &&
                                  restorationCity?.data != null;
            Actor claimant = hasRestoration
                ? FindActor(restoration.claimant_actor_id)
                : null;
            hasRestoration = hasRestoration &&
                             RoyalClaimService.IsEligibleRestorationClaimant(
                                 claimant);
            if (pIncludeUnavailable || (!vassalBlocked && hasRestoration))
            {
                result.Add(MakeOption(pTarget, restorationCity,
                    GOAL_RESTORE_KINGDOM, GoalLabel(GOAL_RESTORE_KINGDOM),
                    -1, -1, restoration?.claim_id ?? -1L, claimant,
                    hasCore: false, hasStrongClaim: false,
                    hasWeakClaim: false,
                    restorationStrength: restoration?.claim_strength ?? 0));
            }

            if (pIncludeUnavailable ||
                (!vassalBlocked && WarDecisionService.HasValidCasusBelli(
                    pSource, pTarget, "vassal_war")))
                result.Add(MakeOption(pTarget, pTarget.capital ?? FindFirstTargetCity(pTarget), GOAL_FORCE_VASSAL, GoalLabel(GOAL_FORCE_VASSAL),
                    -1, -1, -1, null, hasCore: false, hasStrongClaim: false, hasWeakClaim: false,
                    restorationStrength: 0));

            if (pIncludeUnavailable ||
                (!vassalBlocked && WarDecisionService.HasValidCasusBelli(
                    pSource, pTarget, WarDecisionService.WAR_TRIBUTARY)))
                result.Add(MakeOption(pTarget, pTarget.capital ?? FindFirstTargetCity(pTarget),
                    GOAL_FORCE_TRIBUTARY, GoalLabel(GOAL_FORCE_TRIBUTARY),
                    -1, -1, -1, null, hasCore: false, hasStrongClaim: false, hasWeakClaim: false,
                    restorationStrength: 0));

            if (pIncludeUnavailable ||
                (VassalService.GetDiplomaticSuzerain(pSource) == pTarget &&
                 !IsAlreadyAtWar(pSource, pTarget)))
                result.Add(MakeOption(pTarget, FindFirstTargetCity(pTarget), GOAL_INDEPENDENCE, GoalLabel(GOAL_INDEPENDENCE),
                    -1, -1, -1, null, hasCore: false, hasStrongClaim: false, hasWeakClaim: false,
                    restorationStrength: 0));

            if (pIncludeUnavailable || (!vassalBlocked && CanNoCb(pSource)))
                result.Add(MakeOption(pTarget, FindFirstTargetCity(pTarget), GOAL_NO_CB, GoalLabel(GOAL_NO_CB),
                    -1, -1, -1, null, hasCore: false, hasStrongClaim: false, hasWeakClaim: false,
                    restorationStrength: 0));

            result.Sort((a, b) => b.score.CompareTo(a.score));
            return result;
        }

        public static WarTargetOption FindBestTargetOption(Kingdom pSource, Kingdom pTarget, string pGoalType)
        {
            foreach (WarTargetOption option in BuildTargetOptions(pSource, pTarget))
                if (option.goal_type == pGoalType) return option;
            return null;
        }

        private static WarTargetOption MakeOption(Kingdom pTarget, City pCity, string pGoalType, string pLabel,
            long pCoreId, long pClaimId, long pRestorationClaimId, Actor pClaimant, bool hasCore,
            bool hasStrongClaim, bool hasWeakClaim, int restorationStrength,
            long pRegionId = -1L)
        {
            int population = 0;
            try { population = pCity?.getPopulationPeople() ?? 0; } catch { }
            return new WarTargetOption
            {
                target_kingdom = pTarget,
                target_city = pCity,
                goal_type = pGoalType ?? "",
                label = pLabel ?? "",
                source_core_id = pCoreId,
                source_claim_id = pClaimId,
                restoration_claim_id = pRestorationClaimId,
                source_de_jure_region_id = pRegionId,
                claimant_actor_id = pClaimant?.data?.id ?? -1L,
                claimant_name = pClaimant?.getName() ?? "",
                score = WarTargetSelectionRules.ScoreTarget(pGoalType, hasCore, hasStrongClaim, hasWeakClaim,
                    restorationStrength, population)
            };
        }

        internal static bool HasDeJureRegionTarget(Kingdom pSource,
            Kingdom pTarget)
        {
            if (IsVassalDecisionOnlyTarget(pSource, pTarget)) return false;
            foreach (DeJureRegion region in DeJureRegionStore.ActiveRegions())
            {
                int sourceCount = GetDeJureRegionCities(
                    region?.RegionId ?? -1L, pSource).Count;
                int targetCount = GetDeJureRegionCities(
                    region?.RegionId ?? -1L, pTarget).Count;
                if (DeJureWarEligibilityRules.HasCommonRegionMembers(
                        sourceCount, targetCount)) return true;
            }
            return false;
        }

        internal static City FindBestDeJureRegionTargetCity(
            Kingdom pSource, Kingdom pTarget)
        {
            City selected = null;
            foreach (DeJureRegion region in DeJureRegionStore.ActiveRegions())
            {
                if (!DeJureWarEligibilityRules.HasCommonRegionMembers(
                        GetDeJureRegionCities(region?.RegionId ?? -1L,
                            pSource).Count,
                        GetDeJureRegionCities(region?.RegionId ?? -1L,
                            pTarget).Count))
                    continue;
                List<City> cities = GetDeJureRegionCities(
                    region?.RegionId ?? -1L, pTarget);
                if (cities.Count == 0) continue;
                City candidate = cities[0];
                if (selected == null || candidate.data.id < selected.data.id)
                    selected = candidate;
            }
            return selected;
        }

        public static Kingdom FindBestClaimWarTarget(Kingdom pSource)
        {
            if (!IsCivil(pSource) || !Ready) return null;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT TARGET_KINGDOM_ID FROM {WarClaimTableItem.GetTableName()} " +
                                  "WHERE SOURCE_KINGDOM_ID=@s AND ACTIVE=1 AND CONSUMED=0 " +
                                  "ORDER BY CASE CLAIM_TYPE WHEN @strong THEN 2 ELSE 1 END DESC, CREATED_TIME ASC";
                cmd.Parameters.AddWithValue("@s", pSource.id);
                cmd.Parameters.AddWithValue("@strong", CLAIM_STRONG);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    long targetId = reader.IsDBNull(0) ? -1L : reader.GetInt64(0);
                    Kingdom target = FindKingdom(targetId);
                    if (target?.data == null || IsVassalDecisionOnlyTarget(pSource, target)) continue;
                    return target;
                }
            }
            catch { }
            return FindBestCoreWarTarget(pSource);
        }

        public static bool HasClaimLikeCasusBelli(Kingdom pSource, Kingdom pTarget)
        {
            if (!IsCivil(pSource) || pTarget?.data == null || !Ready) return false;
            int coreTargets = CountCoreTargets(pSource.id, pTarget.id);
            int weakClaims = CountClaimTargets(pSource.id, pTarget.id, CLAIM_WEAK);
            int explicitStrongClaims = CountClaimTargets(pSource.id, pTarget.id, CLAIM_STRONG);
            return WarTargetSelectionRules.HasClaimLikeCasusBelli(weakClaims, explicitStrongClaims, coreTargets);
        }

        private static Kingdom FindBestCoreWarTarget(Kingdom pSource)
        {
            if (!IsCivil(pSource) || !Ready) return null;
            Kingdom best = null;
            int bestCount = 0;
            foreach (Kingdom target in CandidateKingdoms(pSource))
            {
                if (target?.data == null || IsVassalDecisionOnlyTarget(pSource, target)) continue;
                int count = CountCoreTargets(pSource.id, target.id);
                if (count <= bestCount) continue;
                best = target;
                bestCount = count;
            }
            return best;
        }

        public static bool HasActiveProjectAgainst(Kingdom pSource, Kingdom pTarget)
        {
            if (pSource?.data == null || pTarget?.data == null) return false;
            return CountProjectTargets(pSource.id, pTarget.id) > 0 ||
                   CountCurrentDecisionProjectsAgainst(pSource, pTarget,
                       PROJECT_CORE, PROJECT_WEAK_CLAIM, PROJECT_STRONG_CLAIM) > 0;
        }

        public static string BuildTargetTooltip(Kingdom pSource, TargetReport pReport)
        {
            if (pReport?.target == null) return "";
            var lines = new List<string>
            {
                T("aw_map_power_ratio") + pReport.power_ratio.ToString("0.00"),
                T("aw_map_reclaimable_core") + pReport.core_count,
                T("aw_map_strong_claim") + pReport.strong_claim_count,
                T("aw_map_weak_claim") + pReport.weak_claim_count,
                T("aw_map_pending") + pReport.pending_count
            };
            if (pReport.can_mandate_conquest) lines.Add(T("aw_map_can_mandate_conquest"));
            if (pReport.can_force_vassal) lines.Add(T("aw_map_can_force_vassal"));
            if (pReport.restoration_claim_count > 0)
                lines.Add(pReport.can_restore
                    ? T("aw_map_can_restore") + pReport.restoration_claim_count + T("aw_map_count_suffix")
                    : T("aw_map_restore_blocked"));
            if (pReport.can_no_cb) lines.Add(T("aw_map_can_no_cb"));
            return string.Join("\n", lines.ToArray());
        }

        public static bool CanFabricateAgainst(Kingdom pSource, Kingdom pTarget, City pTargetCity,
            out string pReason)
        {
            return CanFabricateAgainst(pSource, pTarget, pTargetCity, pCheckExistingProject: true, out pReason);
        }

        private static bool CanFabricateAgainst(Kingdom pSource, Kingdom pTarget, City pTargetCity,
            bool pCheckExistingProject, out string pReason)
        {
            bool foreignCivil = IsCivil(pSource) && IsCivil(pTarget) && pSource != pTarget;
            bool targetCityOwned = pTargetCity?.data != null && !pTargetCity.isRekt() && pTargetCity.kingdom == pTarget;
            bool neighbor = targetCityOwned && IsNeighboringTargetCity(pSource, pTargetCity);
            bool blockedByVassal = IsVassalDecisionOnlyTarget(pSource, pTarget);
            bool existing = pCheckExistingProject && targetCityOwned &&
                            (FindPendingProject(pSource.id, pTargetCity.data.id,
                                 PROJECT_WEAK_CLAIM, PROJECT_STRONG_CLAIM).project_id >= 0 ||
                             HasCurrentDecisionProjectForCity(pSource, pTargetCity.data.id,
                                 PROJECT_WEAK_CLAIM, PROJECT_STRONG_CLAIM));
            return WarFabricationRules.CanFabricateClaim(foreignCivil, targetCityOwned, neighbor,
                blockedByVassal, existing, out pReason);
        }

        public static bool CanFabricateCoreProject(Kingdom pSource, City pTargetCity, out string pReason)
        {
            return CanFabricateCoreProject(pSource, pTargetCity, pCheckExistingProject: true, out pReason);
        }

        public static bool HasCore(Kingdom pKingdom, City pCity)
        {
            return pKingdom?.data != null && pCity?.data != null && FindCoreId(pKingdom.id, pCity.data.id) >= 0;
        }

        private static bool CanFabricateCoreProject(Kingdom pSource, City pTargetCity, bool pCheckExistingProject,
            out string pReason)
        {
            bool sourceValid = IsCivil(pSource);
            bool ownCity = pTargetCity?.data != null && !pTargetCity.isRekt() && pTargetCity.kingdom == pSource;
            bool alreadyCore = ownCity && FindCoreId(pSource.id, pTargetCity.data.id) >= 0;
            bool existing = pCheckExistingProject && ownCity &&
                            (FindPendingProject(pSource.id, pTargetCity.data.id, PROJECT_CORE).project_id >= 0 ||
                             HasCurrentDecisionProjectForCity(pSource, pTargetCity.data.id, PROJECT_CORE) ||
                             KingdomPolicyService.HasCoreFabricationProjectForCity(pSource, pTargetCity.data.id));
            return WarFabricationRules.CanFabricateCore(sourceValid, ownCity, alreadyCore, existing, out pReason);
        }

        public static City FindFirstFabricationTargetCity(Kingdom pSource, Kingdom pTarget)
        {
            if (!IsCivil(pSource) || !IsCivil(pTarget) || pSource == pTarget) return null;
            foreach (City city in pTarget.getCities())
            {
                if (CanFabricateAgainst(pSource, pTarget, city, out _)) return city;
            }
            return null;
        }

        public static Kingdom FindFirstFabricationTargetKingdom(Kingdom pSource)
        {
            if (!IsCivil(pSource)) return null;
            foreach (Kingdom target in CandidateKingdoms(pSource))
                if (FindFirstFabricationTargetCity(pSource, target)?.data != null)
                    return target;
            return null;
        }

        public static City FindFirstCoreProjectTargetCity(Kingdom pSource)
        {
            if (!IsCivil(pSource)) return null;
            foreach (City city in pSource.getCities())
                if (CanFabricateCoreProject(pSource, city, out _)) return city;
            return null;
        }

        public static City FindBestCoreTargetCityForDecision(Kingdom pSource, Kingdom pDefender)
        {
            return FindBestCoreTargetCity(pSource, pDefender, out _);
        }

        public static City FindBestClaimTargetCityForDecision(Kingdom pSource, Kingdom pDefender)
        {
            return FindBestClaimTargetCity(pSource, pDefender, out _);
        }

        public static City FindBestRestorationTargetCityForDecision(Kingdom pSource, Kingdom pDefender)
        {
            FindBestRestorationClaim(pSource, pDefender, out City city);
            return city;
        }

        public static bool IsVassalDecisionOnlyTarget(Kingdom pSource, Kingdom pTarget)
        {
            if (pSource?.data == null || pTarget?.data == null || pSource == pTarget) return false;
            if (TributaryProtectionService.IsProtectedPair(pSource,
                    pTarget)) return true;
            Kingdom sourceSuzerain = VassalService.GetSuzerain(pSource);
            Kingdom targetSuzerain = VassalService.GetSuzerain(pTarget);
            bool sourceSubject = sourceSuzerain?.data != null &&
                                 !sourceSuzerain.isRekt();
            bool targetSubject = targetSuzerain?.data != null &&
                                 !targetSuzerain.isRekt();
            return !VassalWarPermissionRules.CanUseOrdinaryWarDecision(
                sourceSubject, targetSubject);
        }

        private static bool IsSameAlliance(Kingdom pSource, Kingdom pTarget)
        {
            try
            {
                Alliance sourceAlliance = pSource?.getAlliance();
                Alliance targetAlliance = pTarget?.getAlliance();
                if (sourceAlliance == null || targetAlliance == null) return false;
                return Alliance.isSame(sourceAlliance, targetAlliance);
            }
            catch { return false; }
        }

        public static string FabricationReasonText(string pReason)
        {
            switch (pReason)
            {
                case "same_kingdom_or_invalid":
                    return AW_L10n.Text("aw_war_fabrication_reason_same_kingdom_or_invalid",
                        "Not an eligible foreign target");
                case "target_city_invalid":
                    return AW_L10n.Text("aw_war_fabrication_reason_target_city_invalid",
                        "No eligible target city");
                case "vassal_annex_by_decision":
                    return AW_L10n.Text("aw_war_fabrication_reason_vassal_annex_by_decision",
                        "Vassals can be annexed only through a vassal decision");
                case "not_neighbor":
                    return AW_L10n.Text("aw_war_fabrication_reason_not_neighbor",
                        "Can be fabricated only in a bordering foreign city");
                case "source_invalid":
                    return AW_L10n.Text("aw_war_fabrication_reason_source_invalid",
                        "The source realm is unavailable");
                case "not_own_city":
                    return AW_L10n.Text("aw_war_fabrication_reason_not_own_city",
                        "Cores can be fabricated only in a controlled city");
                case "already_core":
                    return AW_L10n.Text("aw_war_fabrication_reason_already_core",
                        "This city is already a core");
                case "project_exists":
                    return AW_L10n.Text("aw_war_fabrication_reason_project_exists",
                        "A similar decision or project is already active");
                default:
                    return AW_L10n.Text("aw_war_fabrication_reason_available", "Fabrication available");
            }
        }

        public static City FindFirstTargetCity(Kingdom pTarget)
        {
            if (pTarget?.data == null) return null;
            foreach (City city in pTarget.getCities())
                if (city?.data != null && !city.isRekt()) return city;
            return null;
        }

        private static bool IsFabricationProject(string pProjectType)
        {
            return pProjectType == PROJECT_CORE || IsClaimProject(pProjectType);
        }

        private static bool IsClaimProject(string pProjectType)
        {
            return pProjectType == PROJECT_WEAK_CLAIM || pProjectType == PROJECT_STRONG_CLAIM;
        }

        private static bool IsNeighboringTargetCity(Kingdom pSource, City pTargetCity)
        {
            if (pSource?.data == null || pTargetCity?.data == null) return false;
            try
            {
                foreach (Kingdom neighbor in pTargetCity.neighbours_kingdoms)
                    if (neighbor == pSource) return true;
            }
            catch { }
            return false;
        }

        private static void AdvanceProjects(Kingdom pKingdom)
        {
            List<ProjectRow> rows = ReadActiveProjects(pKingdom.id);
            if (rows.Count == 0) return;
            double gain = ProjectYearlyGain(pKingdom);
            foreach (ProjectRow row in rows)
            {
                double next = row.progress + gain;
                if (next >= row.cost)
                    CompleteProject(row);
                else
                    UpdateProjectProgress(row.project_id, next);
            }
            DirtyWarMaps();
        }

        private static double ProjectYearlyGain(Kingdom pKingdom)
        {
            double gain = 12.0;
            try
            {
                Actor king = pKingdom.king;
                if (king?.data != null)
                    gain += king.stats["diplomacy"] * 0.18f + king.stats["stewardship"] * 0.12f;
            }
            catch { }
            gain += Math.Min(10.0, Math.Max(0, pKingdom.countCities()) * 0.8);
            return Math.Max(4.0, gain);
        }

        private static void CompleteProject(ProjectRow pRow)
        {
            Kingdom source = FindKingdom(pRow.source_kingdom_id);
            Kingdom target = FindKingdom(pRow.target_kingdom_id);
            City city = FindCity(pRow.target_city_id);
            if (source?.data == null || target?.data == null)
            {
                CloseProject(pRow.project_id, completed: false);
                return;
            }
            if (pRow.project_type == PROJECT_CORE)
            {
                if (city?.data == null) city = FindFirstCoreProjectTargetCity(source);
                if (!CanFabricateCoreProject(source, city, pCheckExistingProject: false, out _))
                {
                    CloseProject(pRow.project_id, completed: false);
                    DirtyWarMaps();
                    return;
                }
            }
            else if (IsClaimProject(pRow.project_type))
            {
                if (city?.data == null) city = FindFirstFabricationTargetCity(source, target);
                if (!CanFabricateAgainst(source, target, city, pCheckExistingProject: false, out _))
                {
                    CloseProject(pRow.project_id, completed: false);
                    DirtyWarMaps();
                    return;
                }
            }

            if (pRow.project_type == PROJECT_CORE && city?.data != null)
            {
                EnsureCore(source, city, "fabricated", T("aw_hist_project_core"));
            }
            else if (pRow.project_type == PROJECT_STRONG_CLAIM)
            {
                WarDecisionService.CreateClaim(source, target, city, CLAIM_STRONG,
                    pRow.war_type, string.IsNullOrEmpty(pRow.reason_key) ? "strong_claim" : pRow.reason_key, 45);
            }
            else
            {
                WarDecisionService.CreateClaim(source, target, city, CLAIM_WEAK,
                    pRow.war_type, string.IsNullOrEmpty(pRow.reason_key) ? "weak_claim" : pRow.reason_key, 20);
            }

            CloseProject(pRow.project_id, completed: true);
            HistoryWriter.RecordKingdom(source, "war_project_completed",
                HistoryText.Kingdom(source) + H("aw_hist_project_completed_prefix") +
                HistoryText.PlainText(ProjectLabel(pRow.project_type)) +
                H("aw_hist_project_target_mid") + TargetText(target, city),
                city?.data != null ? HistoryTarget.City(city) : HistoryTarget.Kingdom(target));
        }

        private static void ResolveGoal(War pWar, WarWinner pWinner, GoalRow pGoal)
        {
            Kingdom attacker = FindKingdom(pGoal.attacker_kingdom_id) ?? pWar.getMainAttacker();
            Kingdom defender = FindKingdom(pGoal.defender_kingdom_id) ?? pWar.getMainDefender();
            City targetCity = FindCity(pGoal.target_city_id);

            string result = WinnerResultKey(pWinner);
            if (UsePeaceSettlementResolver())
            {
                Actor claimant = FindActor(pGoal.claimant_actor_id);
                PeaceSettlementAction action = PeaceSettlementRules.ResolveAction(pGoal.goal_type, result);
                Kingdom winner = action == PeaceSettlementAction.DefenderVictory ? defender : attacker;
                Kingdom loser = action == PeaceSettlementAction.DefenderVictory ? attacker : defender;

                switch (action)
                {
                    case PeaceSettlementAction.TransferCity:
                        TryTransferTargetCity(attacker, targetCity);
                        RecordGoalVictory(attacker, defender, targetCity, pGoal);
                        result = "attacker_goal_enforced";
                        break;
                    case PeaceSettlementAction.ForceVassal:
                        bool vassalSettled = false;
                        try
                        {
                            vassalSettled = VassalService.SetVassal(defender,
                                attacker, "peace_force_vassal", pWar.data.id,
                                pEnforceWarVictory: true,
                                pContractTier: VassalContractTierRules.Inner) ||
                                VassalService.GetSuzerain(defender) == attacker;
                        }
                        catch (Exception e)
                        {
                            ModClass.LogWarning("War goal force vassal failed: " +
                                                e.Message);
                        }
                        if (vassalSettled)
                        {
                            RecordGoalVictory(attacker, defender, targetCity, pGoal);
                            result = "attacker_goal_enforced";
                        }
                        else
                        {
                            RecordGoalFailure(attacker, defender, targetCity,
                                pGoal, "settlement_failed");
                            result = "settlement_failed";
                        }
                        break;
                    case PeaceSettlementAction.ForceTributary:
                        bool tributarySettled = false;
                        try
                        {
                            tributarySettled = VassalService.SetTributary(defender,
                                attacker, "peace_force_tributary", pWar.data.id,
                                pEnforceWarVictory: true) ||
                                VassalService.GetTributarySuzerain(defender) ==
                                attacker;
                        }
                        catch (Exception e)
                        {
                            ModClass.LogWarning("War goal force tributary failed: " +
                                                e.Message);
                        }
                        if (tributarySettled)
                        {
                            RecordGoalVictory(attacker, defender, targetCity, pGoal);
                            result = "attacker_goal_enforced";
                        }
                        else
                        {
                            RecordGoalFailure(attacker, defender, targetCity,
                                pGoal, "settlement_failed");
                            result = "settlement_failed";
                        }
                        break;
                    case PeaceSettlementAction.ReleaseVassal:
                        try { VassalService.ResolveIndependenceWarWon(attacker, defender); }
                        catch (Exception e) { ModClass.LogWarning("War goal independence failed: " + e.Message); }
                        RecordGoalVictory(attacker, defender, targetCity, pGoal);
                        result = "attacker_goal_enforced";
                        break;
                    case PeaceSettlementAction.RestoreKingdom:
                        try { RoyalClaimService.OnRestorationWarWon(attacker, defender, pWar.data.id, pGoal.source_claim_id, targetCity); }
                        catch (Exception e) { ModClass.LogWarning("War goal restoration failed: " + e.Message); }
                        RecordGoalVictory(attacker, defender, targetCity, pGoal);
                        result = "attacker_goal_enforced";
                        break;
                    case PeaceSettlementAction.ApplyNoCbOutcome:
                        RecordGoalVictory(attacker, defender, targetCity, pGoal);
                        result = "attacker_goal_enforced";
                        break;
                    case PeaceSettlementAction.DefenderVictory:
                        RecordGoalFailure(attacker, defender, targetCity, pGoal, "defender_victory");
                        break;
                    case PeaceSettlementAction.WhitePeace:
                        RecordGoalFailure(attacker, defender, targetCity, pGoal, "white_peace");
                        break;
                    default:
                        RecordGoalFailure(attacker, defender, targetCity, pGoal, result);
                        break;
                }

                InsertPeaceSettlement(pWar, pGoal, action, winner, loser, targetCity, claimant, result);
                MarkGoalResolved(pGoal.war_goal_id, result);
                DirtyWarMaps();
                return;
            }

            if (pWinner == WarWinner.Attackers)
            {
                if ((pGoal.goal_type == GOAL_TAKE_CORE_CITY || pGoal.goal_type == GOAL_PRESS_CLAIM_CITY) &&
                    attacker?.data != null && targetCity?.data != null && targetCity.kingdom != attacker)
                {
                    try { targetCity.joinAnotherKingdom(attacker); }
                    catch (Exception e) { ModClass.LogWarning("War goal city transfer failed: " + e.Message); }
                }

                RecordGoalVictory(attacker, defender, targetCity, pGoal);
                if (pGoal.goal_type == GOAL_RESTORE_KINGDOM)
                    RoyalClaimService.OnRestorationWarWon(attacker, defender, pWar.data.id,
                        pGoal.source_claim_id, targetCity);
                result = "attacker_goal_enforced";
            }
            else if (pWinner == WarWinner.Defenders)
            {
                RecordGoalFailure(attacker, defender, targetCity, pGoal, T("aw_hist_goal_defender_win"));
            }
            else
            {
                RecordGoalFailure(attacker, defender, targetCity, pGoal, T("aw_hist_goal_peace_unresolved"));
            }

            MarkGoalResolved(pGoal.war_goal_id, result);
            DirtyWarMaps();
        }

        private static bool UsePeaceSettlementResolver() => true;

        private static bool IsOnAttackerSideOrSystem(War pWar, Kingdom pOwner, Kingdom pAttacker)
        {
            if (pOwner?.data == null) return false;
            try
            {
                if (pWar != null && pWar.isAttacker(pOwner)) return true;
            }
            catch { }
            return IsControlledByAttackerSystem(pOwner, pAttacker);
        }

        private static bool IsControlledByAttackerSystem(Kingdom pOwner, Kingdom pAttacker)
        {
            if (pOwner?.data == null || pAttacker?.data == null) return false;
            if (pOwner == pAttacker) return true;
            return VassalService.GetRootSuzerain(pOwner) == pAttacker;
        }

        private static void TryTransferTargetCity(Kingdom pAttacker, City pTargetCity)
        {
            if (pAttacker?.data == null || pTargetCity?.data == null || pTargetCity.kingdom == pAttacker) return;
            try { pTargetCity.joinAnotherKingdom(pAttacker); }
            catch (Exception e) { ModClass.LogWarning("War goal city transfer failed: " + e.Message); }
        }

        private static void InsertPeaceSettlement(War pWar, GoalRow pGoal, PeaceSettlementAction pAction,
            Kingdom pWinner, Kingdom pLoser, City pCity, Actor pClaimant, string pResult)
        {
            if (!Ready || pWar?.data == null) return;
            try
            {
                long id = TableIdAllocator.Next(DB, PeaceSettlementTableItem.GetTableName(), "SETTLEMENT_ID");
                DB.Insert(PeaceSettlementTableItem.GetTableName(),
                    ColumnVal.Create("SETTLEMENT_ID", id),
                    ColumnVal.Create("WAR_ID", pWar.data.id),
                    ColumnVal.Create("WAR_GOAL_ID", pGoal.war_goal_id),
                    ColumnVal.Create("ACTION", pAction.ToString()),
                    ColumnVal.Create("WINNER_KINGDOM_ID", pWinner?.id ?? -1L),
                    ColumnVal.Create("WINNER_NAME", pWinner?.name ?? ""),
                    ColumnVal.Create("LOSER_KINGDOM_ID", pLoser?.id ?? -1L),
                    ColumnVal.Create("LOSER_NAME", pLoser?.name ?? ""),
                    ColumnVal.Create("TARGET_CITY_ID", pCity?.data?.id ?? pGoal.target_city_id),
                    ColumnVal.Create("TARGET_CITY_NAME", pCity?.data?.name ?? pGoal.target_city_name ?? ""),
                    ColumnVal.Create("CLAIMANT_ACTOR_ID", pClaimant?.data?.id ?? pGoal.claimant_actor_id),
                    ColumnVal.Create("CLAIMANT_NAME", pClaimant?.getName() ?? ""),
                    ColumnVal.Create("TERMS_TEXT", GoalLabel(pGoal.goal_type) + T("aw_hist_colon") + (pResult ?? "")),
                    ColumnVal.Create("WORLD_TIME", LineageService.CurTime()));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("WarTerritoryService.InsertPeaceSettlement failed: " + e.Message);
            }
        }

        private static void RecordGoalVictory(Kingdom pAttacker, Kingdom pDefender, City pCity, GoalRow pGoal)
        {
            if (pAttacker?.data == null) return;
            HistoryText target = pCity?.data != null
                ? HistoryText.City(pCity, pAttacker)
                : HistoryText.Kingdom(pDefender, pGoal.target_kingdom_name);
            HistoryWriter.RecordKingdom(pAttacker, "war_goal_enforced",
                HistoryText.Kingdom(pAttacker) + H("aw_hist_war_goal_achieved_mid") +
                HistoryText.PlainText(GoalLabel(pGoal.goal_type)) + " " + target,
                pCity?.data != null ? HistoryTarget.City(pCity) : HistoryTarget.Kingdom(pDefender));
            if (pDefender?.data != null)
                HistoryWriter.RecordKingdom(pDefender, "war_goal_lost",
                    HistoryText.Kingdom(pDefender) + H("aw_hist_war_goal_defender_failed_mid") +
                    HistoryText.PlainText(GoalLabel(pGoal.goal_type)) + " " + target,
                    pCity?.data != null ? HistoryTarget.City(pCity) : HistoryTarget.Kingdom(pAttacker));
            if (pCity?.data != null)
                HistoryWriter.RecordCity(pCity, pAttacker, "war_goal_city",
                    HistoryText.City(pCity, pAttacker) + H("aw_hist_city_taken_by_goal_mid") + HistoryText.Kingdom(pAttacker),
                    HistoryTarget.Kingdom(pAttacker));
        }

        private static void RecordGoalFailure(Kingdom pAttacker, Kingdom pDefender, City pCity, GoalRow pGoal,
            string pReason)
        {
            if (pAttacker?.data == null) return;
            string reason = LocalizeGoalFailureReason(pReason);
            HistoryText target = pCity?.data != null
                ? HistoryText.City(pCity, pAttacker)
                : HistoryText.Kingdom(pDefender, pGoal.target_kingdom_name);
            HistoryWriter.RecordKingdom(pAttacker, "war_goal_failed",
                HistoryText.Kingdom(pAttacker) + H("aw_hist_war_goal_failed_mid") +
                HistoryText.PlainText(GoalLabel(pGoal.goal_type)) + " " + target +
                H("aw_hist_paren_open") + HistoryText.PlainText(reason) + H("aw_hist_paren_close"),
                pCity?.data != null ? HistoryTarget.City(pCity) : HistoryTarget.Kingdom(pDefender));
        }

        private static string LocalizeGoalFailureReason(string pReason)
        {
            switch (pReason ?? "")
            {
                case "negotiated_goal_not_enforced":
                    return T("aw_hist_goal_negotiated_goal_not_enforced");
                case "defender_victory":
                    return T("aw_hist_goal_defender_win");
                case "white_peace":
                    return T("aw_hist_goal_peace_unresolved");
                default:
                    return pReason ?? "";
            }
        }

        private static void MarkGoalResolved(long pGoalId, string pResult)
        {
            DB.UpdateValue(WarGoalTableItem.GetTableName(),
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("WAR_GOAL_ID", pGoalId) },
                ColumnVal.Create("RESOLVED", 1),
                ColumnVal.Create("RESOLVED_TIME", LineageService.CurTime()),
                ColumnVal.Create("RESULT", pResult ?? ""));
        }

        private static TerritoryStatus BaseStatus(City pCity)
        {
            return new TerritoryStatus
            {
                city_id = pCity?.data?.id ?? -1L,
                city_name = pCity?.data?.name ?? "",
                status = "",
                label = ""
            };
        }

        private static HistoryText TargetText(Kingdom pTarget, City pCity)
        {
            return pCity?.data != null ? HistoryText.City(pCity, pTarget) : HistoryText.Kingdom(pTarget);
        }

        private static string GoalTargetText(WarGoalRequest pGoal)
        {
            if (pGoal?.target_city?.data != null) return T("aw_hist_colon") + pGoal.target_city.data.name;
            if (pGoal?.target_kingdom?.data != null) return T("aw_hist_colon") + pGoal.target_kingdom.name;
            if (pGoal?.claimant?.data != null) return T("aw_hist_colon") + pGoal.claimant.getName();
            return "";
        }

        private static HistoryTarget GoalHistoryTarget(WarGoalRequest pGoal, Kingdom pFallback)
        {
            if (pGoal?.target_city?.data != null) return HistoryTarget.City(pGoal.target_city);
            if (pGoal?.claimant?.data != null) return HistoryTarget.Actor(pGoal.claimant);
            return HistoryTarget.Kingdom(pGoal?.target_kingdom ?? pFallback);
        }

        public static string ProjectLabel(string pProjectType)
        {
            switch (pProjectType)
            {
                case PROJECT_CORE: return T("aw_hist_project_core");
                case PROJECT_STRONG_CLAIM: return T("aw_hist_project_strong_claim");
                case PROJECT_WEAK_CLAIM: return T("aw_hist_project_weak_claim");
                default: return T("aw_hist_project_prepare");
            }
        }

        public static string GoalLabel(string pGoalType)
        {
            switch (pGoalType)
            {
                case GOAL_TAKE_CORE_CITY: return T("aw_hist_goal_take_core_city");
                case GOAL_PRESS_CLAIM_CITY: return T("aw_hist_goal_press_claim_city");
                case GOAL_TAKE_DE_JURE_REGION: return T("aw_hist_goal_take_de_jure_region");
                case GOAL_TAKE_MANDATE: return T("aw_hist_goal_take_mandate");
                case GOAL_MANDATE_CONQUEST: return T("aw_hist_goal_mandate_conquest");
                case GOAL_FORCE_VASSAL: return T("aw_hist_goal_force_vassal");
                case GOAL_FORCE_TRIBUTARY: return T("aw_hist_goal_force_tributary");
                case GOAL_INDEPENDENCE: return T("aw_hist_goal_independence");
                case GOAL_RESTORE_KINGDOM: return T("aw_hist_goal_restore_kingdom");
                case GOAL_REUNIFY_SUCCESSION:
                    return T("aw_hist_goal_reunify_succession");
                case GOAL_NO_CB: return T("aw_hist_goal_no_cb");
                case GOAL_ZHULU_ANNEXATION:
                    return T("aw_hist_goal_zhulu_annexation");
                case GOAL_BANDIT_SUPPRESSION:
                    return T("aw_hist_goal_bandit_suppression");
                default: return T("aw_hist_goal_generic");
            }
        }

        private static string WinnerResultKey(WarWinner pWinner)
        {
            switch (pWinner)
            {
                case WarWinner.Attackers: return "attackers";
                case WarWinner.Defenders: return "defenders";
                case WarWinner.Peace: return "peace";
                case WarWinner.Merged: return "merged";
                default: return "nobody";
            }
        }

        private static bool IsCivil(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() && pKingdom.isCiv() && !pKingdom.isNeutral();
        }

        private static IEnumerable<Kingdom> CandidateKingdoms(Kingdom pSource)
        {
            if (World.world?.kingdoms == null) yield break;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsCivil(kingdom) || kingdom == pSource) continue;
                yield return kingdom;
            }
        }

        private static Kingdom FindKingdom(long pId)
        {
            if (pId < 0 || World.world?.kingdoms == null) return null;
            try
            {
                Kingdom kingdom = World.world.kingdoms.get(pId);
                if (kingdom?.data != null) return kingdom;
            }
            catch { }
            foreach (Kingdom kingdom in World.world.kingdoms)
                if (kingdom?.data != null && kingdom.id == pId) return kingdom;
            return null;
        }

        private static Actor FindActor(long pId)
        {
            if (pId < 0 || World.world?.units == null) return null;
            try { return World.world.units.get(pId); }
            catch { return null; }
        }

        private static City FindCity(long pId)
        {
            if (pId < 0 || World.world?.cities == null) return null;
            try
            {
                City city = World.world.cities.get(pId);
                if (city?.data != null) return city;
            }
            catch { }
            foreach (City city in World.world.cities)
                if (city?.data != null && city.data.id == pId) return city;
            return null;
        }

        private static float PowerRatio(Kingdom pSource, Kingdom pTarget)
        {
            float own = Mathf.Max(1f, VassalService.GetWarPowerScore(pSource, pIncludeVassals: true));
            float target = Mathf.Max(1f, VassalService.GetWarPowerScore(pTarget, pIncludeVassals: true));
            return own / target;
        }

        private static bool IsAlreadyAtWar(Kingdom pSource, Kingdom pTarget)
        {
            if (pSource?.data == null || pTarget?.data == null) return false;
            try { return World.world?.wars?.getWar(pSource, pTarget, pOnlyMain: false) != null; }
            catch { return false; }
        }

        private static bool CanNoCb(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            int year = Date.getCurrentYear();
            pKingdom.data.get("aw_no_cb_penalty_until_year", out int until, -99999);
            return year >= until;
        }

        private static long FindCoreId(long pKingdomId, long pCityId)
        {
            if (!Ready || pKingdomId < 0 || pCityId < 0) return -1L;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT CORE_ID FROM {KingdomCoreTableItem.GetTableName()} " +
                                  "WHERE KINGDOM_ID=@k AND CITY_ID=@c AND ACTIVE=1 LIMIT 1";
                cmd.Parameters.AddWithValue("@k", pKingdomId);
                cmd.Parameters.AddWithValue("@c", pCityId);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? -1L : Convert.ToInt64(value);
            }
            catch { return -1L; }
        }

        private static long FindActiveProjectId(long pSourceId, long pTargetId, long pCityId, string pProjectType)
        {
            if (!Ready) return -1L;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT PROJECT_ID FROM {WarProjectTableItem.GetTableName()} " +
                                  "WHERE SOURCE_KINGDOM_ID=@s AND TARGET_KINGDOM_ID=@t AND TARGET_CITY_ID=@c " +
                                  "AND PROJECT_TYPE=@p AND ACTIVE=1 AND COMPLETED=0 LIMIT 1";
                cmd.Parameters.AddWithValue("@s", pSourceId);
                cmd.Parameters.AddWithValue("@t", pTargetId);
                cmd.Parameters.AddWithValue("@c", pCityId);
                cmd.Parameters.AddWithValue("@p", pProjectType ?? "");
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? -1L : Convert.ToInt64(value);
            }
            catch { return -1L; }
        }

        private static ClaimRow FindBestClaim(long pSourceId, long pTargetKingdomId, long pCityId)
        {
            var result = new ClaimRow { claim_id = -1 };
            if (!Ready || pSourceId < 0) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT CLAIM_ID, CLAIM_TYPE, EXPIRES_TIME FROM {WarClaimTableItem.GetTableName()} " +
                                  "WHERE SOURCE_KINGDOM_ID=@s AND ACTIVE=1 AND CONSUMED=0 " +
                                  "AND (TARGET_CITY_ID=@c OR (TARGET_CITY_ID<0 AND TARGET_KINGDOM_ID=@t)) " +
                                  "ORDER BY CASE CLAIM_TYPE WHEN @strong THEN 2 ELSE 1 END DESC, CREATED_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@s", pSourceId);
                cmd.Parameters.AddWithValue("@c", pCityId);
                cmd.Parameters.AddWithValue("@t", pTargetKingdomId);
                cmd.Parameters.AddWithValue("@strong", CLAIM_STRONG);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                if (!reader.Read()) return result;
                result.claim_id = reader.GetInt64(0);
                result.claim_type = reader.IsDBNull(1) ? "" : reader.GetString(1);
                result.expires_time = reader.IsDBNull(2) ? -1.0 : reader.GetDouble(2);
                if (result.expires_time > 0 && Date.getYear(result.expires_time) < Date.getCurrentYear())
                    return new ClaimRow { claim_id = -1 };
                return result;
            }
            catch { return result; }
        }

        private static City FindBestCoreTargetCity(Kingdom pSource, Kingdom pDefender, out long pCoreId)
        {
            pCoreId = -1L;
            if (pSource?.data == null) return null;
            return FindBestCoreTargetCity(pSource.id, pDefender, out pCoreId);
        }

        private static City FindBestCoreTargetCity(long pCoreKingdomId, Kingdom pDefender, out long pCoreId)
        {
            pCoreId = -1L;
            if (!Ready || pCoreKingdomId < 0 || pDefender?.data == null) return null;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT CORE_ID, CITY_ID FROM {KingdomCoreTableItem.GetTableName()} " +
                                  "WHERE KINGDOM_ID=@s AND ACTIVE=1 ORDER BY CREATED_TIME ASC";
                cmd.Parameters.AddWithValue("@s", pCoreKingdomId);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    long coreId = reader.GetInt64(0);
                    long cityId = reader.GetInt64(1);
                    City city = FindCity(cityId);
                    if (city?.data == null || city.isRekt() || city.kingdom != pDefender) continue;
                    pCoreId = coreId;
                    return city;
                }
            }
            catch { }
            return null;
        }

        private static RoyalClaimService.RoyalClaimInfo FindBestRestorationClaim(Kingdom pSource, Kingdom pDefender,
            out City pTargetCity)
        {
            pTargetCity = null;
            if (!Ready || pSource?.data == null || pDefender?.data == null) return null;
            foreach (RoyalClaimService.RoyalClaimInfo claim in RoyalClaimService.GetHostedClaims(pSource))
            {
                if (!RoyalClaimService.IsAvailableRestorationLeader(FindActor(claim.claimant_actor_id))) continue;
                City city = FindBestCoreTargetCity(claim.original_kingdom_id, pDefender, out _);
                if (city?.data == null) continue;
                pTargetCity = city;
                return claim;
            }
            return null;
        }

        private static City FindBestClaimTargetCity(Kingdom pSource, Kingdom pDefender, out long pClaimId)
        {
            pClaimId = -1L;
            if (!Ready || pSource?.data == null || pDefender?.data == null) return null;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT CLAIM_ID, TARGET_CITY_ID FROM {WarClaimTableItem.GetTableName()} " +
                                  "WHERE SOURCE_KINGDOM_ID=@s AND TARGET_KINGDOM_ID=@t AND ACTIVE=1 AND CONSUMED=0 " +
                                  "ORDER BY CASE CLAIM_TYPE WHEN @strong THEN 2 ELSE 1 END DESC, CREATED_TIME DESC";
                cmd.Parameters.AddWithValue("@s", pSource.id);
                cmd.Parameters.AddWithValue("@t", pDefender.id);
                cmd.Parameters.AddWithValue("@strong", CLAIM_STRONG);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    pClaimId = reader.GetInt64(0);
                    long cityId = reader.IsDBNull(1) ? -1L : reader.GetInt64(1);
                    City city = FindCity(cityId);
                    if (city?.data != null && !city.isRekt()) return city;
                }
            }
            catch { }
            return null;
        }

        private static int CountCores(long pKingdomId)
        {
            return CountSql(KingdomCoreTableItem.GetTableName(), "KINGDOM_ID=@k AND ACTIVE=1", ("@k", pKingdomId));
        }

        private static int CountClaims(long pKingdomId, string pClaimType)
        {
            return CountSql(WarClaimTableItem.GetTableName(),
                "SOURCE_KINGDOM_ID=@k AND CLAIM_TYPE=@c AND ACTIVE=1 AND CONSUMED=0",
                ("@k", pKingdomId), ("@c", pClaimType));
        }

        private static int CountProjects(long pKingdomId, params string[] pTypes)
        {
            if (pTypes == null || pTypes.Length == 0)
                return CountSql(WarProjectTableItem.GetTableName(),
                    "SOURCE_KINGDOM_ID=@k AND ACTIVE=1 AND COMPLETED=0", ("@k", pKingdomId)) +
                       KingdomPolicyService.CountCoreFabricationProjects(FindKingdom(pKingdomId));
            int total = 0;
            foreach (string type in pTypes)
            {
                total += CountSql(WarProjectTableItem.GetTableName(),
                    "SOURCE_KINGDOM_ID=@k AND PROJECT_TYPE=@p AND ACTIVE=1 AND COMPLETED=0",
                    ("@k", pKingdomId), ("@p", type));
                if (type == PROJECT_CORE)
                    total += KingdomPolicyService.CountCoreFabricationProjects(FindKingdom(pKingdomId));
            }
            return total;
        }

        private static int CountCoreTargets(long pSourceId, long pTargetId)
        {
            int count = 0;
            foreach (City city in FindKingdom(pTargetId)?.getCities() ?? new List<City>())
                if (city?.data != null && FindCoreId(pSourceId, city.data.id) >= 0) count++;
            return count;
        }

        private static int CountClaimTargets(long pSourceId, long pTargetId, string pClaimType)
        {
            return CountSql(WarClaimTableItem.GetTableName(),
                "SOURCE_KINGDOM_ID=@s AND TARGET_KINGDOM_ID=@t AND CLAIM_TYPE=@c AND ACTIVE=1 AND CONSUMED=0",
                ("@s", pSourceId), ("@t", pTargetId), ("@c", pClaimType));
        }

        private static int CountProjectTargets(long pSourceId, long pTargetId)
        {
            return CountSql(WarProjectTableItem.GetTableName(),
                "SOURCE_KINGDOM_ID=@s AND TARGET_KINGDOM_ID=@t AND ACTIVE=1 AND COMPLETED=0",
                ("@s", pSourceId), ("@t", pTargetId));
        }

        private static int CountRestorationClaimsAgainst(List<RoyalClaimService.RoyalClaimInfo> pClaims,
            Kingdom pTarget)
        {
            if (pClaims == null || pTarget?.data == null) return 0;
            int count = 0;
            foreach (RoyalClaimService.RoyalClaimInfo claim in pClaims)
            {
                if (claim == null || claim.claim_id < 0) continue;
                if (!RoyalClaimService.IsAvailableRestorationLeader(FindActor(claim.claimant_actor_id))) continue;
                if (CountCoreTargets(claim.original_kingdom_id, pTarget.id) > 0) count++;
            }
            return count;
        }

        private static int CountOwnedNonCore(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0;
            int count = 0;
            foreach (City city in pKingdom.getCities())
                if (city?.data != null && FindCoreId(pKingdom.id, city.data.id) < 0) count++;
            return count;
        }

        private static int CountSql(string pTable, string pWhere, params (string name, object value)[] pParams)
        {
            if (!Ready) return 0;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT COUNT(*) FROM {pTable} WHERE {pWhere}";
                foreach (var pair in pParams) cmd.Parameters.AddWithValue(pair.name, pair.value);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
            }
            catch { return 0; }
        }

        private static ProjectRow FindPendingProject(long pSourceId, long pCityId, params string[] pTypes)
        {
            var result = new ProjectRow { project_id = -1 };
            if (!Ready || pCityId < 0) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT PROJECT_ID, PROJECT_TYPE, PROGRESS, COST FROM {WarProjectTableItem.GetTableName()} " +
                                  "WHERE SOURCE_KINGDOM_ID=@s AND TARGET_CITY_ID=@c AND ACTIVE=1 AND COMPLETED=0 " +
                                  BuildTypeFilter(pTypes) + " ORDER BY CREATED_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@s", pSourceId);
                cmd.Parameters.AddWithValue("@c", pCityId);
                AddTypeParams(cmd, pTypes);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                if (!reader.Read()) return result;
                result.project_id = reader.GetInt64(0);
                result.project_type = reader.IsDBNull(1) ? "" : reader.GetString(1);
                result.progress = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2);
                result.cost = reader.IsDBNull(3) ? 1.0 : reader.GetDouble(3);
                return result;
            }
            catch { return result; }
        }

        private static ProjectRow FindPendingProjectByTargetKingdom(long pSourceId, long pTargetId, params string[] pTypes)
        {
            var result = new ProjectRow { project_id = -1 };
            if (!Ready || pTargetId < 0) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT PROJECT_ID, PROJECT_TYPE, PROGRESS, COST FROM {WarProjectTableItem.GetTableName()} " +
                                  "WHERE SOURCE_KINGDOM_ID=@s AND TARGET_KINGDOM_ID=@t AND ACTIVE=1 AND COMPLETED=0 " +
                                  BuildTypeFilter(pTypes) + " ORDER BY CREATED_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@s", pSourceId);
                cmd.Parameters.AddWithValue("@t", pTargetId);
                AddTypeParams(cmd, pTypes);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                if (!reader.Read()) return result;
                result.project_id = reader.GetInt64(0);
                result.project_type = reader.IsDBNull(1) ? "" : reader.GetString(1);
                result.progress = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2);
                result.cost = reader.IsDBNull(3) ? 1.0 : reader.GetDouble(3);
                return result;
            }
            catch { return result; }
        }

        private static int CountCurrentDecisionProjectsAgainst(Kingdom pKingdom, Kingdom pTarget,
            params string[] pTypes)
        {
            if (pKingdom?.data == null || pTarget?.data == null || pTypes == null || pTypes.Length == 0) return 0;
            if (!CurrentDecisionProjectMatches(pKingdom, pTypes)) return 0;
            pKingdom.data.get(LineageKeys.DECISION_TARGET_KINGDOM_ID, out long targetId, -1L);
            return targetId == pTarget.id ? 1 : 0;
        }

        private static bool HasCurrentDecisionProjectForCity(Kingdom pKingdom, long pCityId, params string[] pTypes)
        {
            if (pKingdom?.data == null || pCityId < 0 || pTypes == null || pTypes.Length == 0) return false;
            if (!CurrentDecisionProjectMatches(pKingdom, pTypes)) return false;
            pKingdom.data.get(LineageKeys.DECISION_WAR_TARGET_CITY_ID, out long targetCityId, -1L);
            return targetCityId == pCityId;
        }

        private static ProjectRow FindCurrentDecisionProject(Kingdom pKingdom, long pCityId, params string[] pTypes)
        {
            var result = new ProjectRow { project_id = -1 };
            if (pKingdom?.data == null || pCityId < 0 || pTypes == null || pTypes.Length == 0) return result;

            if (Array.IndexOf(pTypes, PROJECT_CORE) >= 0 &&
                KingdomPolicyService.TryGetCoreFabricationProject(pKingdom, pCityId, out float coreProgress,
                    out float coreCost))
            {
                result.project_id = -3L;
                result.project_type = PROJECT_CORE;
                result.progress = coreProgress;
                result.cost = coreCost;
                return result;
            }

            if (!CurrentDecisionProjectMatches(pKingdom, pTypes)) return result;
            pKingdom.data.get(LineageKeys.DECISION_PROJECT_TYPE, out string projectType, "");
            pKingdom.data.get(LineageKeys.DECISION_WAR_TARGET_CITY_ID, out long targetCityId, -1L);
            if (targetCityId != pCityId) return result;

            KingdomPolicyDef def = KingdomPolicyService.GetDefinition(
                pKingdom, KingdomPolicyService.GetCurrent(pKingdom,
                    PolicyNodeKind.Decision));
            if (def == null) return result;

            result.project_id = -2L;
            result.project_type = projectType;
            result.progress = KingdomPolicyService.GetProgress(pKingdom, PolicyNodeKind.Decision);
            result.cost = Math.Max(1f, def.Cost);
            return result;
        }

        private static bool CurrentDecisionProjectMatches(Kingdom pKingdom, params string[] pTypes)
        {
            if (pKingdom?.data == null || pTypes == null || pTypes.Length == 0) return false;
            pKingdom.data.get(LineageKeys.DECISION_PROJECT_TYPE, out string projectType, "");
            if (string.IsNullOrEmpty(projectType)) return false;
            string current = KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Decision);
            if (current != DecisionIdForProjectType(projectType)) return false;

            bool matches = false;
            for (int i = 0; i < pTypes.Length; i++)
            {
                if (projectType != pTypes[i]) continue;
                matches = true;
                break;
            }
            return matches;
        }

        private static string DecisionIdForProjectType(string pProjectType)
        {
            switch (pProjectType ?? "")
            {
                case PROJECT_CORE:
                    return "aw_decision_fabricate_core";
                case PROJECT_WEAK_CLAIM:
                    return "aw_decision_fabricate_weak_claim";
                case PROJECT_STRONG_CLAIM:
                    return "aw_decision_fabricate_strong_claim";
                default:
                    return "";
            }
        }

        private static List<ProjectRow> ReadActiveProjects(long pKingdomId)
        {
            var result = new List<ProjectRow>();
            if (!Ready) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT PROJECT_ID, SOURCE_KINGDOM_ID, TARGET_KINGDOM_ID, TARGET_CITY_ID, " +
                                  $"PROJECT_TYPE, WAR_TYPE, REASON_KEY, PROGRESS, COST FROM {WarProjectTableItem.GetTableName()} " +
                                  "WHERE SOURCE_KINGDOM_ID=@k AND ACTIVE=1 AND COMPLETED=0";
                cmd.Parameters.AddWithValue("@k", pKingdomId);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new ProjectRow
                    {
                        project_id = reader.GetInt64(0),
                        source_kingdom_id = reader.GetInt64(1),
                        target_kingdom_id = reader.GetInt64(2),
                        target_city_id = reader.GetInt64(3),
                        project_type = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        war_type = reader.IsDBNull(5) ? "" : reader.GetString(5),
                        reason_key = reader.IsDBNull(6) ? "" : reader.GetString(6),
                        progress = reader.IsDBNull(7) ? 0.0 : reader.GetDouble(7),
                        cost = reader.IsDBNull(8) ? 1.0 : reader.GetDouble(8)
                    });
                }
            }
            catch { }
            return result;
        }

        private static List<GoalRow> ReadOpenGoals(long pWarId)
        {
            var result = new List<GoalRow>();
            if (!Ready) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT WAR_GOAL_ID, ATTACKER_KINGDOM_ID, DEFENDER_KINGDOM_ID, GOAL_TYPE, " +
                                  $"TARGET_CITY_ID, TARGET_CITY_NAME, TARGET_KINGDOM_ID, TARGET_KINGDOM_NAME, " +
                                  $"SOURCE_CLAIM_ID, SOURCE_CORE_ID, CLAIMANT_ACTOR_ID, " +
                                  $"SOURCE_DE_JURE_REGION_ID " +
                                  $"FROM {WarGoalTableItem.GetTableName()} WHERE WAR_ID=@w AND RESOLVED=0";
                cmd.Parameters.AddWithValue("@w", pWarId);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new GoalRow
                    {
                        war_goal_id = reader.GetInt64(0),
                        attacker_kingdom_id = reader.GetInt64(1),
                        defender_kingdom_id = reader.GetInt64(2),
                        goal_type = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        target_city_id = reader.GetInt64(4),
                        target_city_name = reader.IsDBNull(5) ? "" : reader.GetString(5),
                        target_kingdom_id = reader.GetInt64(6),
                        target_kingdom_name = reader.IsDBNull(7) ? "" : reader.GetString(7),
                        source_claim_id = reader.GetInt64(8),
                        source_core_id = reader.GetInt64(9),
                        claimant_actor_id = reader.GetInt64(10),
                        source_de_jure_region_id = reader.IsDBNull(11)
                            ? -1L : reader.GetInt64(11)
                    });
                }
            }
            catch { }
            return result;
        }

        private static List<long> ReadOpenGoalWarIdsForCity(long pCityDataId, long pCityObjectId)
        {
            var result = new List<long>();
            if (!Ready || pCityDataId < 0) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT DISTINCT WAR_ID FROM {WarGoalTableItem.GetTableName()} " +
                                  "WHERE RESOLVED=0 AND (TARGET_CITY_ID=@data_id OR TARGET_CITY_ID=@object_id) " +
                                  "AND (GOAL_TYPE=@core_goal OR GOAL_TYPE=@claim_goal)";
                cmd.Parameters.AddWithValue("@data_id", pCityDataId);
                cmd.Parameters.AddWithValue("@object_id", pCityObjectId);
                cmd.Parameters.AddWithValue("@core_goal", GOAL_TAKE_CORE_CITY);
                cmd.Parameters.AddWithValue("@claim_goal", GOAL_PRESS_CLAIM_CITY);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    long warId = reader.GetInt64(0);
                    if (!result.Contains(warId)) result.Add(warId);
                }
            }
            catch { }
            return result;
        }

        private static void UpdateProjectProgress(long pProjectId, double pProgress)
        {
            DB.UpdateValue(WarProjectTableItem.GetTableName(),
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("PROJECT_ID", pProjectId) },
                ColumnVal.Create("PROGRESS", pProgress));
        }

        private static void CloseProject(long pProjectId, bool completed)
        {
            DB.UpdateValue(WarProjectTableItem.GetTableName(),
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("PROJECT_ID", pProjectId) },
                ColumnVal.Create("ACTIVE", 0),
                ColumnVal.Create("COMPLETED", completed ? 1 : 0),
                ColumnVal.Create("FINISHED_TIME", LineageService.CurTime()));
        }

        private static string BuildTypeFilter(string[] pTypes)
        {
            if (pTypes == null || pTypes.Length == 0) return "";
            var parts = new List<string>();
            for (int i = 0; i < pTypes.Length; i++) parts.Add("@p" + i);
            return "AND PROJECT_TYPE IN (" + string.Join(",", parts.ToArray()) + ") ";
        }

        private static void AddTypeParams(SQLiteCommand pCmd, string[] pTypes)
        {
            if (pTypes == null) return;
            for (int i = 0; i < pTypes.Length; i++) pCmd.Parameters.AddWithValue("@p" + i, pTypes[i] ?? "");
        }

        private static void DirtyWarMaps()
        {
            OwnedNonCoreCache.Clear();
            try { core.policy.WarCoreMapModeService.DirtyMapIfActive(); } catch { }
            try { core.policy.WarClaimMapModeService.DirtyMapIfActive(); } catch { }
        }

        private struct ClaimRow
        {
            public long claim_id;
            public string claim_type;
            public double expires_time;
        }

        private struct ProjectRow
        {
            public long project_id;
            public long source_kingdom_id;
            public long target_kingdom_id;
            public long target_city_id;
            public string project_type;
            public string war_type;
            public string reason_key;
            public double progress;
            public double cost;
        }

        private struct GoalRow
        {
            public long war_goal_id;
            public long attacker_kingdom_id;
            public long defender_kingdom_id;
            public string goal_type;
            public long target_city_id;
            public string target_city_name;
            public long target_kingdom_id;
            public string target_kingdom_name;
            public long source_claim_id;
            public long source_core_id;
            public long claimant_actor_id;
            public long source_de_jure_region_id;
        }
    }
}

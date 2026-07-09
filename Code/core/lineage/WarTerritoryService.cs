using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
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

        public const string GOAL_TAKE_MANDATE = "take_mandate";
        public const string GOAL_MANDATE_CONQUEST = "mandate_conquest";
        public const string GOAL_TAKE_CORE_CITY = "take_core_city";
        public const string GOAL_PRESS_CLAIM_CITY = "press_claim_city";
        public const string GOAL_FORCE_VASSAL = "force_vassal";
        public const string GOAL_INDEPENDENCE = "independence";
        public const string GOAL_RESTORE_KINGDOM = "restore_kingdom";
        public const string GOAL_NO_CB = "no_cb_punitive";

        private const double DEFAULT_PROJECT_COST = 100.0;

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private static readonly Dictionary<string, bool> OwnedNonCoreCache = new Dictionary<string, bool>();
        private static HistoryText H(string pKey) => HistoryLocalizationRules.H(pKey);
        private static string T(string pKey) => HistoryLocalizationRules.Text(pKey);

        internal sealed class WarGoalRequest
        {
            public string goal_type = "";
            public City target_city;
            public Kingdom target_kingdom;
            public long source_claim_id = -1;
            public long source_core_id = -1;
            public long source_project_id = -1;
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
            public bool can_independence;
            public bool can_restore;
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
            public long claimant_actor_id = -1;
            public string claimant_name = "";
            public int score;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!IsCivil(pKingdom) || !Ready) return;
            AdvanceProjects(pKingdom);
        }

        public static void OnCityTransferred(City pCity, Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (pCity?.data == null || pNewKingdom?.data == null || !Ready) return;
            try
            {
                foreach (War war in GetCandidateWarsForTransferredCity(pCity, pNewKingdom))
                    if (TryResolveTransferredCityGoal(pCity, pNewKingdom, war))
                        return;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("WarTerritoryService.OnCityTransferred failed: " + e.Message);
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

            foreach (GoalRow goal in ReadOpenGoals(pWar.data.id))
            {
                bool targetCityMatches = goal.target_city_id == pCity.data.id || goal.target_city_id == pCity.id;
                Kingdom attacker = FindKingdom(goal.attacker_kingdom_id) ?? pWar.getMainAttacker();
                bool newOwnerIsWarAttacker = IsOnAttackerSideOrSystem(pWar, pNewKingdom, attacker);
                if (!WarGoalControlRules.ShouldResolveTransferredCityGoal(goal.goal_type, targetCityMatches,
                        newOwnerIsWarAttacker))
                    continue;

                World.world?.wars?.endWar(pWar, WarWinner.Attackers);
                return true;
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
            CreateGoalForWar(war, goal);
            return true;
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
            CreateGoalForWar(war, goal);
            return true;
        }

        public static bool TryDeclareVassalWar(Kingdom pAttacker, Kingdom pDefender)
        {
            if (IsVassalDecisionOnlyTarget(pAttacker, pDefender)) return false;
            var goal = new WarGoalRequest { goal_type = GOAL_FORCE_VASSAL, target_kingdom = pDefender };
            War war = WarDecisionService.TryStartWarWithResult(pAttacker, pDefender, "vassal_war", "force_vassal");
            if (war?.data == null) return false;
            CreateGoalForWar(war, goal);
            return true;
        }

        public static bool TryDeclareIndependenceWar(Kingdom pAttacker, Kingdom pSuzerain)
        {
            if (pAttacker?.data == null || pSuzerain?.data == null) return false;
            if (VassalService.GetSuzerain(pAttacker) != pSuzerain) return false;
            var goal = new WarGoalRequest { goal_type = GOAL_INDEPENDENCE, target_kingdom = pSuzerain };
            War war = WarDecisionService.TryStartWarWithResult(pAttacker, pSuzerain,
                "independence_war", "independence_war");
            if (war?.data == null) return false;
            CreateGoalForWar(war, goal);
            return true;
        }

        public static bool TryDeclareRestorationWar(Kingdom pAttacker, Kingdom pDefender)
        {
            RoyalClaimService.RoyalClaimInfo claim = FindBestRestorationClaim(pAttacker, pDefender, out City targetCity);
            if (claim == null || claim.claim_id < 0) return false;

            Actor claimant = FindActor(claim.claimant_actor_id);
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
            CreateGoalForWar(war, goal);
            return true;
        }

        public static bool TryDeclareNoCbWar(Kingdom pAttacker, Kingdom pDefender)
        {
            if (IsVassalDecisionOnlyTarget(pAttacker, pDefender)) return false;
            var goal = new WarGoalRequest { goal_type = GOAL_NO_CB, target_kingdom = pDefender };
            War war = WarDecisionService.TryStartWarWithResult(pAttacker, pDefender, WarDecisionService.WAR_NORMAL,
                "no_cb", pNoCb: true);
            if (war?.data == null) return false;
            CreateGoalForWar(war, goal);
            return true;
        }

        public static bool TryDeclareMandateWar(Kingdom pAttacker, Kingdom pDefender)
        {
            if (IsVassalDecisionOnlyTarget(pAttacker, pDefender)) return false;
            if (MandateService.GetCurrentMandateKingdom() != pDefender) return false;
            var goal = new WarGoalRequest
            {
                goal_type = GOAL_TAKE_MANDATE,
                target_kingdom = pDefender,
                target_city = pDefender?.capital ?? FindFirstTargetCity(pDefender)
            };
            War war = WarDecisionService.TryStartWarWithResult(pAttacker, pDefender,
                MandateService.WAR_TIANMING, "tianming");
            if (war?.data == null) return false;
            CreateGoalForWar(war, goal);
            return true;
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
            War war = WarDecisionService.TryStartWarWithResult(pAttacker, pDefender,
                WarDecisionService.WAR_NORMAL, "mandate_conquest");
            if (war?.data == null) return false;
            CreateGoalForWar(war, goal);
            return true;
        }

        public static void CreateGoalForWar(War pWar, WarGoalRequest pGoal)
        {
            if (pWar?.data == null || pGoal == null || !Ready) return;
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (attacker?.data == null || defender?.data == null) return;

            long goalId = TableIdAllocator.Next(DB, WarGoalTableItem.GetTableName(), "WAR_GOAL_ID");
            try
            {
                DB.Insert(WarGoalTableItem.GetTableName(),
                    ColumnVal.Create("WAR_GOAL_ID", goalId),
                    ColumnVal.Create("WAR_ID", pWar.data.id),
                    ColumnVal.Create("ATTACKER_KINGDOM_ID", attacker.id),
                    ColumnVal.Create("ATTACKER_NAME", attacker.name ?? ""),
                    ColumnVal.Create("ATTACKER_COLOR", HistoryColors.FromKingdom(attacker)),
                    ColumnVal.Create("DEFENDER_KINGDOM_ID", defender.id),
                    ColumnVal.Create("DEFENDER_NAME", defender.name ?? ""),
                    ColumnVal.Create("DEFENDER_COLOR", HistoryColors.FromKingdom(defender)),
                    ColumnVal.Create("WAR_TYPE", pWar.getAsset()?.id ?? ""),
                    ColumnVal.Create("GOAL_TYPE", pGoal.goal_type ?? ""),
                    ColumnVal.Create("TARGET_CITY_ID", pGoal.target_city?.data?.id ?? -1L),
                    ColumnVal.Create("TARGET_CITY_NAME", pGoal.target_city?.data?.name ?? ""),
                    ColumnVal.Create("TARGET_KINGDOM_ID", pGoal.target_kingdom?.id ?? -1L),
                    ColumnVal.Create("TARGET_KINGDOM_NAME", pGoal.target_kingdom?.name ?? ""),
                    ColumnVal.Create("SOURCE_CLAIM_ID", pGoal.source_claim_id),
                    ColumnVal.Create("SOURCE_CORE_ID", pGoal.source_core_id),
                    ColumnVal.Create("SOURCE_PROJECT_ID", pGoal.source_project_id),
                    ColumnVal.Create("CLAIMANT_ACTOR_ID", pGoal.claimant?.data?.id ?? -1L),
                    ColumnVal.Create("CLAIMANT_NAME", pGoal.claimant?.getName() ?? ""),
                    ColumnVal.Create("CREATED_TIME", LineageService.CurTime()),
                    ColumnVal.Create("RESOLVED_TIME", -1.0),
                    ColumnVal.Create("RESOLVED", 0),
                    ColumnVal.Create("RESULT", ""));

                HistoryWriter.RecordKingdom(attacker, "war_goal_set",
                    HistoryText.Kingdom(attacker) + H("aw_hist_war_goal_set_mid") +
                    HistoryText.PlainText(GoalLabel(pGoal.goal_type)) +
                    GoalTargetText(pGoal), GoalHistoryTarget(pGoal, defender));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("WarTerritoryService.CreateGoalForWar failed: " + e.Message);
            }
        }

        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null || !Ready) return;
            foreach (GoalRow row in ReadOpenGoals(pWar.data.id))
                ResolveGoal(pWar, pWinner, row);
        }

        public static bool HasWarGoal(long pWarId)
        {
            if (!Ready || pWarId < 0) return false;
            return CountSql(WarGoalTableItem.GetTableName(), "WAR_ID=@w", ("@w", pWarId)) > 0;
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
                        power += VassalService.GetPowerScore(member, pIncludeVassals: true);
                    }
                }
            }
            catch { }

            if (seen.Add(pKingdom.id))
                power += VassalService.GetPowerScore(pKingdom, pIncludeVassals: true);
            return power;
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
                report.can_independence = VassalService.GetSuzerain(pSource) == target &&
                                          !IsAlreadyAtWar(pSource, target);
                report.restoration_claim_count = CountRestorationClaimsAgainst(hostedRoyalClaims, target);
                report.can_restore = WarRestorationRules.CanExposeRestorationAction(
                    report.restoration_claim_count > 0,
                    vassalBlocked,
                    IsAlreadyAtWar(pSource, target),
                    out _);
                result.Add(report);
            }
            result.Sort((a, b) =>
            {
                int scoreA = (a.can_take_mandate ? 500 : 0) + (a.can_mandate_conquest ? 260 : 0) + a.core_count * 100 + (a.can_restore ? 80 : 0) + (a.can_independence ? 90 : 0) + a.strong_claim_count * 50 +
                             a.weak_claim_count * 20 + a.pending_count;
                int scoreB = (b.can_take_mandate ? 500 : 0) + (b.can_mandate_conquest ? 260 : 0) + b.core_count * 100 + (b.can_restore ? 80 : 0) + (b.can_independence ? 90 : 0) + b.strong_claim_count * 50 +
                             b.weak_claim_count * 20 + b.pending_count;
                int cmp = scoreB.CompareTo(scoreA);
                return cmp != 0 ? cmp : string.Compare(a.target?.name, b.target?.name, StringComparison.Ordinal);
            });
            return result;
        }

        public static List<WarTargetOption> BuildTargetOptions(Kingdom pSource, Kingdom pTarget)
        {
            var result = new List<WarTargetOption>();
            if (!IsCivil(pSource) || pTarget?.data == null) return result;

            bool vassalBlocked = IsVassalDecisionOnlyTarget(pSource, pTarget);
            if (!vassalBlocked && MandateService.GetCurrentMandateKingdom() == pTarget &&
                WarDecisionService.HasValidCasusBelli(pSource, pTarget, MandateService.WAR_TIANMING))
                result.Add(MakeOption(pTarget, pTarget.capital ?? FindFirstTargetCity(pTarget),
                    GOAL_TAKE_MANDATE, GoalLabel(GOAL_TAKE_MANDATE),
                    -1, -1, -1, null, hasCore: false, hasStrongClaim: false, hasWeakClaim: false,
                    restorationStrength: 0));

            if (CanUseMandateConquest(pSource, pTarget))
                result.Add(MakeOption(pTarget, pTarget.capital ?? FindFirstTargetCity(pTarget),
                    GOAL_MANDATE_CONQUEST, GoalLabel(GOAL_MANDATE_CONQUEST),
                    -1, -1, -1, null, hasCore: false, hasStrongClaim: true, hasWeakClaim: false,
                    restorationStrength: 0));

            City city = FindBestCoreTargetCity(pSource, pTarget, out long coreId);
            if (!vassalBlocked && city?.data != null)
                result.Add(MakeOption(pTarget, city, GOAL_TAKE_CORE_CITY, GoalLabel(GOAL_TAKE_CORE_CITY),
                    coreId, -1, -1, null, hasCore: true, hasStrongClaim: false, hasWeakClaim: false,
                    restorationStrength: 0));

            city = FindBestClaimTargetCity(pSource, pTarget, out long claimId);
            if (!vassalBlocked && claimId >= 0)
            {
                ClaimRow claim = FindBestClaim(pSource.id, pTarget.id, city?.data?.id ?? -1L);
                bool strong = claim.claim_type == CLAIM_STRONG;
                result.Add(MakeOption(pTarget, city, GOAL_PRESS_CLAIM_CITY,
                    strong ? T("aw_hist_goal_press_strong_claim_city") : T("aw_hist_goal_press_weak_claim_city"),
                    -1, claimId, -1, null, hasCore: false, hasStrongClaim: strong, hasWeakClaim: !strong,
                    restorationStrength: 0));
            }

            RoyalClaimService.RoyalClaimInfo restoration = FindBestRestorationClaim(pSource, pTarget, out City restorationCity);
            if (!vassalBlocked && restoration != null && restoration.claim_id >= 0 && restorationCity?.data != null)
            {
                Actor claimant = FindActor(restoration.claimant_actor_id);
                result.Add(MakeOption(pTarget, restorationCity, GOAL_RESTORE_KINGDOM, GoalLabel(GOAL_RESTORE_KINGDOM),
                    -1, -1, restoration.claim_id, claimant, hasCore: false, hasStrongClaim: false, hasWeakClaim: false,
                    restorationStrength: restoration.claim_strength));
            }

            if (!vassalBlocked && WarDecisionService.HasValidCasusBelli(pSource, pTarget, "vassal_war"))
                result.Add(MakeOption(pTarget, FindFirstTargetCity(pTarget), GOAL_FORCE_VASSAL, GoalLabel(GOAL_FORCE_VASSAL),
                    -1, -1, -1, null, hasCore: false, hasStrongClaim: false, hasWeakClaim: false,
                    restorationStrength: 0));

            if (VassalService.GetSuzerain(pSource) == pTarget && !IsAlreadyAtWar(pSource, pTarget))
                result.Add(MakeOption(pTarget, FindFirstTargetCity(pTarget), GOAL_INDEPENDENCE, GoalLabel(GOAL_INDEPENDENCE),
                    -1, -1, -1, null, hasCore: false, hasStrongClaim: false, hasWeakClaim: false,
                    restorationStrength: 0));

            if (!vassalBlocked && CanNoCb(pSource))
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
            bool hasStrongClaim, bool hasWeakClaim, int restorationStrength)
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
                claimant_actor_id = pClaimant?.data?.id ?? -1L,
                claimant_name = pClaimant?.getName() ?? "",
                score = WarTargetSelectionRules.ScoreTarget(pGoalType, hasCore, hasStrongClaim, hasWeakClaim,
                    restorationStrength, population)
            };
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
            Kingdom sourceSuzerain = VassalService.GetSuzerain(pSource);
            Kingdom targetSuzerain = VassalService.GetSuzerain(pTarget);
            bool sourceVassal = sourceSuzerain?.data != null && !sourceSuzerain.isRekt();
            bool targetVassal = targetSuzerain?.data != null && !targetSuzerain.isRekt();

            if (sourceVassal)
            {
                bool sameSuzerain = targetVassal && sourceSuzerain == targetSuzerain;
                return !sameSuzerain;
            }

            if (targetVassal) return true;

            Kingdom sourceRoot = VassalService.GetRootSuzerain(pSource);
            Kingdom targetRoot = VassalService.GetRootSuzerain(pTarget);
            return sourceRoot?.data != null && targetRoot?.data != null && sourceRoot == targetRoot;
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
                case "same_kingdom_or_invalid": return "\u4e0d\u662f\u53ef\u7528\u7684\u4ed6\u56fd\u76ee\u6807";
                case "target_city_invalid": return "\u6ca1\u6709\u53ef\u7528\u7684\u76ee\u6807\u57ce\u5e02";
                case "vassal_annex_by_decision": return "\u9644\u5eb8\u4f53\u7cfb\u53ea\u80fd\u901a\u8fc7\u9644\u5eb8\u51b3\u8bae\u541e\u5e76";
                case "not_neighbor": return "\u53ea\u80fd\u5728\u63a5\u58e4\u7684\u4ed6\u56fd\u57ce\u5e02\u5236\u9020";
                case "source_invalid": return "\u672c\u56fd\u4e0d\u53ef\u7528";
                case "not_own_city": return "\u53ea\u80fd\u5728\u672c\u56fd\u63a7\u5236\u7684\u57ce\u5e02\u5236\u9020\u6838\u5fc3";
                case "already_core": return "\u8be5\u57ce\u5e02\u5df2\u662f\u6838\u5fc3";
                case "project_exists": return "\u5df2\u6709\u540c\u7c7b\u51b3\u7b56\u6216\u9879\u76ee\u5728\u8fdb\u884c";
                default: return "\u5df2\u53ef\u5236\u9020";
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
                        try { VassalService.SetVassal(defender, attacker, "peace_force_vassal", pWar.data.id); }
                        catch (Exception e) { ModClass.LogWarning("War goal force vassal failed: " + e.Message); }
                        RecordGoalVictory(attacker, defender, targetCity, pGoal);
                        result = "attacker_goal_enforced";
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
                    try { targetCity.setKingdom(attacker, false); }
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
            try { pTargetCity.setKingdom(pAttacker, false); }
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
            HistoryText target = pCity?.data != null
                ? HistoryText.City(pCity, pAttacker)
                : HistoryText.Kingdom(pDefender, pGoal.target_kingdom_name);
            HistoryWriter.RecordKingdom(pAttacker, "war_goal_failed",
                HistoryText.Kingdom(pAttacker) + H("aw_hist_war_goal_failed_mid") +
                HistoryText.PlainText(GoalLabel(pGoal.goal_type)) + " " + target +
                H("aw_hist_paren_open") + HistoryText.PlainText(pReason) + H("aw_hist_paren_close"),
                pCity?.data != null ? HistoryTarget.City(pCity) : HistoryTarget.Kingdom(pDefender));
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
                case GOAL_TAKE_MANDATE: return T("aw_hist_goal_take_mandate");
                case GOAL_MANDATE_CONQUEST: return T("aw_hist_goal_mandate_conquest");
                case GOAL_FORCE_VASSAL: return T("aw_hist_goal_force_vassal");
                case GOAL_INDEPENDENCE: return T("aw_hist_goal_independence");
                case GOAL_RESTORE_KINGDOM: return T("aw_hist_goal_restore_kingdom");
                case GOAL_NO_CB: return T("aw_hist_goal_no_cb");
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
            float own = Mathf.Max(1f, VassalService.GetPowerScore(pSource, pIncludeVassals: true));
            float target = Mathf.Max(1f, VassalService.GetPowerScore(pTarget, pIncludeVassals: true));
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

            KingdomPolicyDef def = KingdomPolicyDefs.Get(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Decision));
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
                                  $"SOURCE_CLAIM_ID, SOURCE_CORE_ID, CLAIMANT_ACTOR_ID " +
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
                        claimant_actor_id = reader.GetInt64(10)
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
        }
    }
}

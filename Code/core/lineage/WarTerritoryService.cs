using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
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

        public const string GOAL_TAKE_CORE_CITY = "take_core_city";
        public const string GOAL_PRESS_CLAIM_CITY = "press_claim_city";
        public const string GOAL_FORCE_VASSAL = "force_vassal";
        public const string GOAL_INDEPENDENCE = "independence";
        public const string GOAL_RESTORE_KINGDOM = "restore_kingdom";
        public const string GOAL_NO_CB = "no_cb_punitive";

        private const double DEFAULT_PROJECT_COST = 100.0;

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

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
            public bool can_no_cb;
            public bool can_fabricate;
            public bool vassal_blocked;
            public City fabrication_city;
            public string fabrication_reason = "";
            public float power_ratio;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!IsCivil(pKingdom) || !Ready) return;
            AdvanceProjects(pKingdom);
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
                    HistoryText.Kingdom(pKingdom) + " 将 " + HistoryText.City(pCity, pKingdom) +
                    " 列为核心领土", HistoryTarget.City(pCity));
                HistoryWriter.RecordCity(pCity, pKingdom, "war_core_created",
                    HistoryText.City(pCity, pKingdom) + " 成为 " + HistoryText.Kingdom(pKingdom) +
                    " 的核心领土", HistoryTarget.Kingdom(pKingdom));
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
            if (!IsCivil(pSource) || !IsCivil(pTarget) || pSource == pTarget || !Ready) return -1L;
            if (IsFabricationProject(pProjectType))
            {
                if (pTargetCity?.data == null) pTargetCity = FindFirstFabricationTargetCity(pSource, pTarget);
                if (!CanFabricateAgainst(pSource, pTarget, pTargetCity, out _)) return -1L;
            }

            long cityId = pTargetCity?.data?.id ?? -1L;
            long existing = FindActiveProjectId(pSource.id, pTarget.id, cityId, pProjectType);
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
                    ColumnVal.Create("TARGET_KINGDOM_ID", pTarget.id),
                    ColumnVal.Create("TARGET_KINGDOM_NAME", pTarget.name ?? ""),
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
                    HistoryText.Kingdom(pSource) + " 开始" + HistoryText.PlainText(ProjectLabel(pProjectType)) +
                    "，目标为 " + TargetText(pTarget, pTargetCity),
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
            if (IsVassalDecisionOnlyTarget(pAttacker, pDefender)) return false;
            City city = FindBestCoreTargetCity(pAttacker, pDefender, out long coreId);
            if (city?.data == null) return false;
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
            if (IsVassalDecisionOnlyTarget(pAttacker, pDefender)) return false;
            City city = FindBestClaimTargetCity(pAttacker, pDefender, out long claimId);
            if (city?.data == null && claimId < 0) return false;
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
                    HistoryText.Kingdom(attacker) + " 设定战争目标：" + HistoryText.PlainText(GoalLabel(pGoal.goal_type)) +
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
                result.label = "核心";
                return result;
            }

            ProjectRow pending = FindPendingProject(pFocus.id, pCity.data.id, PROJECT_CORE);
            if (pending.project_id >= 0)
            {
                result.status = "pending_core";
                result.label = "制造核心";
                result.progress = pending.progress;
                result.cost = pending.cost;
                return result;
            }

            if (pCity.kingdom == pFocus)
            {
                result.status = "owned_non_core";
                result.label = "非核心领土";
            }
            return result;
        }

        public static TerritoryStatus GetClaimStatus(Kingdom pFocus, City pCity)
        {
            var result = BaseStatus(pCity);
            if (!IsCivil(pFocus) || pCity?.data == null || !Ready) return result;

            ClaimRow claim = FindBestClaim(pFocus.id, pCity.kingdom?.id ?? -1L, pCity.data.id);
            if (claim.claim_id >= 0)
            {
                result.status = claim.claim_type == CLAIM_STRONG ? "strong_claim" : "weak_claim";
                result.label = claim.claim_type == CLAIM_STRONG ? "强宣称" : "弱宣称";
                result.expires_time = claim.expires_time;
                return result;
            }

            ProjectRow pending = FindPendingProject(pFocus.id, pCity.data.id, PROJECT_WEAK_CLAIM, PROJECT_STRONG_CLAIM);
            if (pending.project_id < 0)
                pending = FindPendingProjectByTargetKingdom(pFocus.id, pCity.kingdom?.id ?? -1L,
                    PROJECT_WEAK_CLAIM, PROJECT_STRONG_CLAIM);
            if (pending.project_id >= 0)
            {
                result.status = "pending_claim";
                result.label = pending.project_type == PROJECT_STRONG_CLAIM ? "制造强宣称" : "制造弱宣称";
                result.progress = pending.progress;
                result.cost = pending.cost;
            }
            return result;
        }

        public static string BuildCoreTooltip(Kingdom pFocus, Kingdom pHover)
        {
            if (pFocus?.data == null) return "";
            var lines = new List<string> { "查看国：" + pFocus.name };
            if (pHover?.data != null) lines.Add("当前国：" + pHover.name);
            int cores = CountCores(pFocus.id);
            int pending = CountProjects(pFocus.id, PROJECT_CORE);
            int nonCoreOwned = CountOwnedNonCore(pFocus);
            lines.Add("核心城市：" + cores);
            lines.Add("非核心领土：" + nonCoreOwned);
            lines.Add("制造核心中：" + pending);
            return string.Join("\n", lines.ToArray());
        }

        public static string BuildClaimTooltip(Kingdom pFocus, Kingdom pHover)
        {
            if (pFocus?.data == null) return "";
            var lines = new List<string> { "查看国：" + pFocus.name };
            if (pHover?.data != null) lines.Add("当前国：" + pHover.name);
            lines.Add("强宣称：" + CountClaims(pFocus.id, CLAIM_STRONG));
            lines.Add("弱宣称：" + CountClaims(pFocus.id, CLAIM_WEAK));
            lines.Add("制造宣称中：" + CountProjects(pFocus.id, PROJECT_WEAK_CLAIM, PROJECT_STRONG_CLAIM));
            return string.Join("\n", lines.ToArray());
        }

        public static List<TargetReport> BuildTargetReports(Kingdom pSource)
        {
            var result = new List<TargetReport>();
            if (!IsCivil(pSource)) return result;
            foreach (Kingdom target in CandidateKingdoms(pSource))
            {
                City fabricationCity = FindFirstFabricationTargetCity(pSource, target);
                City reasonCity = fabricationCity ?? FindFirstTargetCity(target);
                bool canFabricate = CanFabricateAgainst(pSource, target, reasonCity, out string fabricationReason);
                bool vassalBlocked = IsVassalDecisionOnlyTarget(pSource, target);
                var report = new TargetReport
                {
                    target = target,
                    core_count = CountCoreTargets(pSource.id, target.id),
                    weak_claim_count = CountClaimTargets(pSource.id, target.id, CLAIM_WEAK),
                    strong_claim_count = CountClaimTargets(pSource.id, target.id, CLAIM_STRONG),
                    pending_count = CountProjectTargets(pSource.id, target.id),
                    can_no_cb = !vassalBlocked && CanNoCb(pSource),
                    can_fabricate = canFabricate,
                    vassal_blocked = vassalBlocked,
                    fabrication_city = fabricationCity,
                    fabrication_reason = fabricationReason,
                    power_ratio = PowerRatio(pSource, target)
                };
                report.can_reclaim = !vassalBlocked && report.core_count > 0;
                report.can_press_claim = !vassalBlocked && report.weak_claim_count + report.strong_claim_count > 0;
                report.can_force_vassal = !vassalBlocked &&
                    WarDecisionService.HasValidCasusBelli(pSource, target, "vassal_war");
                result.Add(report);
            }
            result.Sort((a, b) =>
            {
                int scoreA = a.core_count * 100 + a.strong_claim_count * 50 + a.weak_claim_count * 20 + a.pending_count;
                int scoreB = b.core_count * 100 + b.strong_claim_count * 50 + b.weak_claim_count * 20 + b.pending_count;
                int cmp = scoreB.CompareTo(scoreA);
                return cmp != 0 ? cmp : string.Compare(a.target?.name, b.target?.name, StringComparison.Ordinal);
            });
            return result;
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
            catch { return null; }
            return null;
        }

        public static bool HasActiveProjectAgainst(Kingdom pSource, Kingdom pTarget)
        {
            if (pSource?.data == null || pTarget?.data == null) return false;
            return CountProjectTargets(pSource.id, pTarget.id) > 0;
        }

        public static string BuildTargetTooltip(Kingdom pSource, TargetReport pReport)
        {
            if (pReport?.target == null) return "";
            var lines = new List<string>
            {
                "国力比：" + pReport.power_ratio.ToString("0.00"),
                "可收复核心：" + pReport.core_count,
                "强宣称：" + pReport.strong_claim_count,
                "弱宣称：" + pReport.weak_claim_count,
                "制造中：" + pReport.pending_count
            };
            if (pReport.can_force_vassal) lines.Add("可发动附庸战争");
            if (pReport.can_no_cb) lines.Add("可强宣，但会产生惩罚");
            return string.Join("\n", lines.ToArray());
        }

        public static bool CanFabricateAgainst(Kingdom pSource, Kingdom pTarget, City pTargetCity,
            out string pReason)
        {
            bool foreignCivil = IsCivil(pSource) && IsCivil(pTarget) && pSource != pTarget;
            bool targetCityOwned = pTargetCity?.data != null && !pTargetCity.isRekt() && pTargetCity.kingdom == pTarget;
            bool neighbor = targetCityOwned && IsNeighboringTargetCity(pSource, pTargetCity);
            bool blockedByVassal = IsVassalDecisionOnlyTarget(pSource, pTarget);
            return WarFabricationRules.CanFabricate(foreignCivil, targetCityOwned, neighbor, blockedByVassal,
                out pReason);
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

        public static bool IsVassalDecisionOnlyTarget(Kingdom pSource, Kingdom pTarget)
        {
            if (pSource?.data == null || pTarget?.data == null || pSource == pTarget) return false;
            if (VassalService.IsVassalKingdom(pSource)) return true;
            if (VassalService.IsVassalKingdom(pTarget)) return true;

            Kingdom sourceRoot = VassalService.GetRootSuzerain(pSource);
            Kingdom targetRoot = VassalService.GetRootSuzerain(pTarget);
            return sourceRoot?.data != null && targetRoot?.data != null && sourceRoot == targetRoot;
        }

        public static string FabricationReasonText(string pReason)
        {
            switch (pReason)
            {
                case "same_kingdom_or_invalid": return "\u4e0d\u662f\u53ef\u7528\u7684\u4ed6\u56fd\u76ee\u6807";
                case "target_city_invalid": return "\u6ca1\u6709\u53ef\u7528\u7684\u76ee\u6807\u57ce\u5e02";
                case "vassal_annex_by_decision": return "\u9644\u5eb8\u4f53\u7cfb\u53ea\u80fd\u901a\u8fc7\u9644\u5eb8\u51b3\u8bae\u541e\u5e76";
                case "not_neighbor": return "\u53ea\u80fd\u5728\u63a5\u58e4\u7684\u4ed6\u56fd\u57ce\u5e02\u5236\u9020";
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
            return pProjectType == PROJECT_CORE || pProjectType == PROJECT_WEAK_CLAIM ||
                   pProjectType == PROJECT_STRONG_CLAIM;
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
            if (IsFabricationProject(pRow.project_type))
            {
                if (city?.data == null) city = FindFirstFabricationTargetCity(source, target);
                if (!CanFabricateAgainst(source, target, city, out _))
                {
                    CloseProject(pRow.project_id, completed: false);
                    DirtyWarMaps();
                    return;
                }
            }

            if (pRow.project_type == PROJECT_CORE && city?.data != null)
            {
                EnsureCore(source, city, "fabricated", "制造核心");
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
                HistoryText.Kingdom(source) + " 完成" + HistoryText.PlainText(ProjectLabel(pRow.project_type)) +
                "，目标为 " + TargetText(target, city),
                city?.data != null ? HistoryTarget.City(city) : HistoryTarget.Kingdom(target));
        }

        private static void ResolveGoal(War pWar, WarWinner pWinner, GoalRow pGoal)
        {
            Kingdom attacker = FindKingdom(pGoal.attacker_kingdom_id) ?? pWar.getMainAttacker();
            Kingdom defender = FindKingdom(pGoal.defender_kingdom_id) ?? pWar.getMainDefender();
            City targetCity = FindCity(pGoal.target_city_id);

            string result = WinnerResultKey(pWinner);
            if (pWinner == WarWinner.Attackers)
            {
                if ((pGoal.goal_type == GOAL_TAKE_CORE_CITY || pGoal.goal_type == GOAL_PRESS_CLAIM_CITY) &&
                    attacker?.data != null && targetCity?.data != null && targetCity.kingdom != attacker)
                {
                    try { targetCity.setKingdom(attacker, false); }
                    catch (Exception e) { ModClass.LogWarning("War goal city transfer failed: " + e.Message); }
                }

                RecordGoalVictory(attacker, defender, targetCity, pGoal);
                result = "attacker_goal_enforced";
            }
            else if (pWinner == WarWinner.Defenders)
            {
                RecordGoalFailure(attacker, defender, targetCity, pGoal, "守方胜利");
            }
            else
            {
                RecordGoalFailure(attacker, defender, targetCity, pGoal, "议和未决");
            }

            MarkGoalResolved(pGoal.war_goal_id, result);
            DirtyWarMaps();
        }

        private static void RecordGoalVictory(Kingdom pAttacker, Kingdom pDefender, City pCity, GoalRow pGoal)
        {
            if (pAttacker?.data == null) return;
            HistoryText target = pCity?.data != null
                ? HistoryText.City(pCity, pAttacker)
                : HistoryText.Kingdom(pDefender, pGoal.target_kingdom_name);
            HistoryWriter.RecordKingdom(pAttacker, "war_goal_enforced",
                HistoryText.Kingdom(pAttacker) + " 达成战争目标：" +
                HistoryText.PlainText(GoalLabel(pGoal.goal_type)) + " " + target,
                pCity?.data != null ? HistoryTarget.City(pCity) : HistoryTarget.Kingdom(pDefender));
            if (pDefender?.data != null)
                HistoryWriter.RecordKingdom(pDefender, "war_goal_lost",
                    HistoryText.Kingdom(pDefender) + " 战败，被迫接受战争目标：" +
                    HistoryText.PlainText(GoalLabel(pGoal.goal_type)) + " " + target,
                    pCity?.data != null ? HistoryTarget.City(pCity) : HistoryTarget.Kingdom(pAttacker));
            if (pCity?.data != null)
                HistoryWriter.RecordCity(pCity, pAttacker, "war_goal_city",
                    HistoryText.City(pCity, pAttacker) + " 因战争目标归于 " + HistoryText.Kingdom(pAttacker),
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
                HistoryText.Kingdom(pAttacker) + " 未能达成战争目标：" +
                HistoryText.PlainText(GoalLabel(pGoal.goal_type)) + " " + target +
                "（" + HistoryText.PlainText(pReason) + "）",
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
            if (pGoal?.target_city?.data != null) return "：" + pGoal.target_city.data.name;
            if (pGoal?.target_kingdom?.data != null) return "：" + pGoal.target_kingdom.name;
            if (pGoal?.claimant?.data != null) return "：" + pGoal.claimant.getName();
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
                case PROJECT_CORE: return "制造核心";
                case PROJECT_STRONG_CLAIM: return "制造强宣称";
                case PROJECT_WEAK_CLAIM: return "制造弱宣称";
                default: return "战争准备";
            }
        }

        public static string GoalLabel(string pGoalType)
        {
            switch (pGoalType)
            {
                case GOAL_TAKE_CORE_CITY: return "收复核心城市";
                case GOAL_PRESS_CLAIM_CITY: return "夺取宣称城市";
                case GOAL_FORCE_VASSAL: return "强制臣服";
                case GOAL_INDEPENDENCE: return "脱离宗主";
                case GOAL_RESTORE_KINGDOM: return "复国";
                case GOAL_NO_CB: return "强宣";
                default: return "战争目标";
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
            if (!Ready || pSource?.data == null || pDefender?.data == null) return null;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT CORE_ID, CITY_ID FROM {KingdomCoreTableItem.GetTableName()} " +
                                  "WHERE KINGDOM_ID=@s AND ACTIVE=1 ORDER BY CREATED_TIME ASC";
                cmd.Parameters.AddWithValue("@s", pSource.id);
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
            return FindFirstTargetCity(pDefender);
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
                    "SOURCE_KINGDOM_ID=@k AND ACTIVE=1 AND COMPLETED=0", ("@k", pKingdomId));
            int total = 0;
            foreach (string type in pTypes)
                total += CountSql(WarProjectTableItem.GetTableName(),
                    "SOURCE_KINGDOM_ID=@k AND PROJECT_TYPE=@p AND ACTIVE=1 AND COMPLETED=0",
                    ("@k", pKingdomId), ("@p", type));
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
                                  $"TARGET_CITY_ID, TARGET_CITY_NAME, TARGET_KINGDOM_ID, TARGET_KINGDOM_NAME " +
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
                        target_kingdom_name = reader.IsDBNull(7) ? "" : reader.GetString(7)
                    });
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
        }
    }
}

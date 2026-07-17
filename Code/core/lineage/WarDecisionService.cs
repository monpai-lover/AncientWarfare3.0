using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class WarDecisionService
    {
        public const string WAR_NORMAL = "aw_normal_war";
        public const string WAR_RESTORATION = "restoration_war";

        private const int DEFAULT_CLAIM_YEARS = 30;
        private const int NO_CB_COOLDOWN_YEARS = 20;
        private const float NO_CB_POLITICAL_COST = 35f;

        [ThreadStatic] private static int _allowWarStartDepth;

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private static HistoryText H(string pKey) => HistoryLocalizationRules.H(pKey);
        private static string T(string pKey) => HistoryLocalizationRules.Text(pKey);

        public static bool IsAw3AllowedWarStart => _allowWarStartDepth > 0;

        public static bool CanStartCivilWar(Kingdom pAttacker, Kingdom pDefender, string pWarType,
            out string pReason)
        {
            pReason = "";
            if (!IsCivilKingdom(pAttacker) || !IsCivilKingdom(pDefender))
            {
                pReason = "not_civil";
                return false;
            }

            if (pAttacker == pDefender)
            {
                pReason = "same_kingdom";
                return false;
            }

            try
            {
                if (World.world?.wars?.getWar(pAttacker, pDefender, pOnlyMain: false) != null)
                {
                    pReason = "already_at_war";
                    return false;
                }
            }
            catch { }

            string type = string.IsNullOrEmpty(pWarType) ? WAR_NORMAL : pWarType;
            if (!CanPassAllianceWarRules(pAttacker, pDefender, type, pSystemWar: false, out pReason)) return false;
            if (!CanPassVassalWarRules(pAttacker, pDefender, type, out pReason)) return false;
            if (HasValidCasusBelli(pAttacker, pDefender, type)) return true;

            pReason = "missing_cb";
            return false;
        }

        public static bool CanQueueWarDecision(Kingdom pAttacker, Kingdom pDefender, string pWarType,
            bool pNoCb, out string pReason)
        {
            string type = string.IsNullOrEmpty(pWarType) ? WAR_NORMAL : pWarType;
            if (!CanQueueWarPair(pAttacker, pDefender, type, out pReason)) return false;

            if (pNoCb)
            {
                if (CanForceNoCb(pAttacker)) return true;
                pReason = "cannot_force_no_cb";
                return false;
            }
            if (HasValidCasusBelli(pAttacker, pDefender, type)) return true;
            pReason = "missing_cb";
            return false;
        }

        public static bool CanQueueWarPair(Kingdom pAttacker, Kingdom pDefender, string pWarType,
            out string pReason)
        {
            pReason = "";
            if (!IsCivilKingdom(pAttacker) || !IsCivilKingdom(pDefender) || pAttacker == pDefender)
            {
                pReason = "invalid";
                return false;
            }

            string type = string.IsNullOrEmpty(pWarType) ? WAR_NORMAL : pWarType;
            if (!CanPassAllianceWarRules(pAttacker, pDefender, type, pSystemWar: false, out pReason)) return false;
            if (!CanPassVassalWarRules(pAttacker, pDefender, type, out pReason)) return false;
            try
            {
                if (World.world?.wars?.getWar(pAttacker, pDefender, pOnlyMain: false) != null)
                {
                    pReason = "already_at_war";
                    return false;
                }
            }
            catch { }

            return true;
        }

        public static bool TryStartWar(Kingdom pAttacker, Kingdom pDefender, string pWarType,
            string pReasonKey, bool pNoCb = false)
        {
            War war = StartWar(pAttacker, pDefender, pWarType, pReasonKey, pNoCb, pSystemWar: false);
            return war?.data != null;
        }

        public static War TryStartWarWithResult(Kingdom pAttacker, Kingdom pDefender, string pWarType,
            string pReasonKey, bool pNoCb = false)
        {
            return StartWar(pAttacker, pDefender, pWarType, pReasonKey, pNoCb, pSystemWar: false);
        }

        public static War TryStartSystemWar(Kingdom pAttacker, Kingdom pDefender, string pWarType,
            string pReasonKey)
        {
            return StartWar(pAttacker, pDefender, pWarType, pReasonKey, pNoCb: false, pSystemWar: true);
        }

        public static bool HasValidCasusBelli(Kingdom pAttacker, Kingdom pDefender, string pWarType)
        {
            if (!IsCivilKingdom(pAttacker) || !IsCivilKingdom(pDefender)) return false;
            string type = string.IsNullOrEmpty(pWarType) ? WAR_NORMAL : pWarType;
            if (type == MandateService.WAR_TIANMING && !MandatePhaseService.CanContestMandate)
                return false;
            if (HasActiveClaim(pAttacker.id, pDefender.id, type)) return true;
            if (type == WAR_NORMAL && WarTerritoryService.HasClaimLikeCasusBelli(pAttacker, pDefender))
                return true;
            return HasIntrinsicCasusBelli(pAttacker, pDefender, type);
        }

        public static long CreateClaim(Kingdom pSource, Kingdom pTarget, City pTargetCity,
            string pClaimType, string pWarType, string pReasonKey, int pYearsValid = DEFAULT_CLAIM_YEARS)
        {
            if (!Ready || !IsCivilKingdom(pSource) || !IsCivilKingdom(pTarget) || pSource == pTarget) return -1L;

            long existing = FindActiveClaimId(pSource.id, pTarget.id, pWarType);
            if (existing >= 0) return existing;

            long claimId = TableIdAllocator.Next(DB, WarClaimTableItem.GetTableName(), "CLAIM_ID");
            double now = LineageService.CurTime();
            double expires = pYearsValid <= 0 ? -1.0 : now + pYearsValid * 365.0;
            Actor king = pSource.king;

            try
            {
                DB.Insert(WarClaimTableItem.GetTableName(),
                    ColumnVal.Create("CLAIM_ID", claimId),
                    ColumnVal.Create("SOURCE_KINGDOM_ID", pSource.id),
                    ColumnVal.Create("SOURCE_KINGDOM_NAME", pSource.name ?? ""),
                    ColumnVal.Create("SOURCE_KINGDOM_COLOR", HistoryColors.FromKingdom(pSource)),
                    ColumnVal.Create("TARGET_KINGDOM_ID", pTarget.id),
                    ColumnVal.Create("TARGET_KINGDOM_NAME", pTarget.name ?? ""),
                    ColumnVal.Create("TARGET_KINGDOM_COLOR", HistoryColors.FromKingdom(pTarget)),
                    ColumnVal.Create("TARGET_CITY_ID", pTargetCity?.data?.id ?? -1L),
                    ColumnVal.Create("TARGET_CITY_NAME", pTargetCity?.data?.name ?? ""),
                    ColumnVal.Create("CLAIM_TYPE", pClaimType ?? ""),
                    ColumnVal.Create("WAR_TYPE", pWarType ?? WAR_NORMAL),
                    ColumnVal.Create("REASON_KEY", pReasonKey ?? ""),
                    ColumnVal.Create("CREATED_TIME", now),
                    ColumnVal.Create("EXPIRES_TIME", expires),
                    ColumnVal.Create("ACTIVE", 1),
                    ColumnVal.Create("CONSUMED", 0),
                    ColumnVal.Create("CREATED_BY_ACTOR_ID", king?.data?.id ?? -1L),
                    ColumnVal.Create("CREATED_BY_NAME", king?.getName() ?? ""));

                HistoryWriter.RecordKingdom(pSource, "war_claim_created",
                    HistoryText.Kingdom(pSource) + H("aw_hist_war_claim_created_mid") +
                    HistoryText.Kingdom(pTarget) + H("aw_hist_war_claim_created_reason") +
                    HistoryText.PlainText(ReasonLabel(pReasonKey, pWarType)),
                    HistoryTarget.Kingdom(pTarget));
                return claimId;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("WarDecisionService.CreateClaim failed: " + e.Message);
                return -1L;
            }
        }

        public static void ConsumeClaim(Kingdom pSource, Kingdom pTarget, string pWarType)
        {
            if (!Ready || pSource?.data == null || pTarget?.data == null) return;
            long claimId = FindActiveClaimId(pSource.id, pTarget.id, pWarType);
            if (claimId < 0) return;

            try
            {
                DB.UpdateValue(WarClaimTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("CLAIM_ID", claimId) },
                    ColumnVal.Create("ACTIVE", 0),
                    ColumnVal.Create("CONSUMED", 1));
            }
            catch { }
        }

        public static bool ShouldBlockWarStart(Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pType)
        {
            if (IsAw3AllowedWarStart) return false;
            if (!IsCivilKingdom(pAttacker) || !IsCivilKingdom(pDefender)) return false;
            string type = pType?.id ?? "";
            if (string.IsNullOrEmpty(type)) return false;
            if (!IsVanillaNormalWar(type)) return false;
            LogBlockedWar(pAttacker, pDefender, type);
            return true;
        }

        private static War StartWar(Kingdom pAttacker, Kingdom pDefender, string pWarType, string pReasonKey,
            bool pNoCb, bool pSystemWar)
        {
            if (!IsCivilKingdom(pAttacker) || !IsCivilKingdom(pDefender) || pAttacker == pDefender) return null;
            string type = string.IsNullOrEmpty(pWarType) ? WAR_NORMAL : pWarType;
            if (type == MandateService.WAR_TIANMING && !MandatePhaseService.CanContestMandate)
                return null;
            WarTypeAsset asset = AssetManager.war_types_library.get(type);
            if (asset == null) return null;

            if (!pSystemWar && !CanPassVassalWarRules(pAttacker, pDefender, type, out _)) return null;
            if (!CanPassAllianceWarRules(pAttacker, pDefender, type, pSystemWar, out _)) return null;
            if (!pSystemWar && !pNoCb && !HasValidCasusBelli(pAttacker, pDefender, type)) return null;
            if (pNoCb && !CanForceNoCb(pAttacker)) return null;

            try
            {
                _allowWarStartDepth++;
                War war = World.world.diplomacy.startWar(pAttacker, pDefender, asset);
                if (war?.data == null) return null;

                if (pNoCb) ApplyNoCbPenalty(pAttacker, pDefender);
                else if (!pSystemWar) ConsumeClaim(pAttacker, pDefender, type);

                RecordWarDecision(pAttacker, pDefender, type, pReasonKey, pNoCb, pSystemWar);
                return war;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("WarDecisionService.TryStartWar failed: " + e.Message);
                return null;
            }
            finally
            {
                _allowWarStartDepth = Mathf.Max(0, _allowWarStartDepth - 1);
            }
        }

        private static bool CanPassVassalWarRules(Kingdom pAttacker, Kingdom pDefender, string pWarType,
            out string pReason)
        {
            Kingdom attackerSuzerain = VassalService.GetSuzerain(pAttacker);
            bool attackerIsVassal = attackerSuzerain?.data != null && !attackerSuzerain.isRekt();
            bool defenderIsSuzerain = attackerSuzerain != null && attackerSuzerain == pDefender;
            Kingdom attackerRoot = attackerIsVassal ? VassalService.GetRootSuzerain(pAttacker) : null;
            Kingdom defenderRoot = VassalService.GetRootSuzerain(pDefender);
            bool sameRootSuzerain = attackerRoot?.data != null && defenderRoot?.data != null &&
                                     attackerRoot == defenderRoot;
            bool blockInternalWar = sameRootSuzerain &&
                                    CentralizationService.ReadSnapshot(attackerRoot)
                                        .effects.BlocksInternalVassalWar;
            return VassalWarPermissionRules.CanDeclareWar(attackerIsVassal, defenderIsSuzerain,
                sameRootSuzerain, blockInternalWar, pWarType, out pReason);
        }

        private static bool CanPassAllianceWarRules(Kingdom pAttacker, Kingdom pDefender, string pWarType,
            bool pSystemWar, out string pReason)
        {
            return WarAllianceRules.CanStartWar(
                pSameAlliance: IsSameAlliance(pAttacker, pDefender),
                pSystemWar: pSystemWar,
                pIndependenceWar: pWarType == "independence_war",
                out pReason);
        }

        private static bool IsSameAlliance(Kingdom pAttacker, Kingdom pDefender)
        {
            try
            {
                Alliance attackerAlliance = pAttacker?.getAlliance();
                Alliance defenderAlliance = pDefender?.getAlliance();
                if (attackerAlliance == null || defenderAlliance == null) return false;
                return Alliance.isSame(attackerAlliance, defenderAlliance);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasIntrinsicCasusBelli(Kingdom pAttacker, Kingdom pDefender, string pWarType)
        {
            switch (pWarType)
            {
                case "independence_war":
                    return VassalService.GetSuzerain(pAttacker) == pDefender;
                case "reclaim":
                    return WarTerritoryService.FindBestCoreTargetCityForDecision(pAttacker, pDefender)?.data != null;
                case "vassal_war":
                    return CanForceVassal(pAttacker, pDefender);
                case WAR_NORMAL:
                    return WarTerritoryService.CanUseMandateConquest(pAttacker, pDefender);
                case MandateService.WAR_TIANMING:
                    return MandatePhaseService.CanContestMandate &&
                           MandateService.GetCurrentMandateKingdom() == pDefender;
                case MandateService.WAR_TIANMING_REBEL:
                case GeneralRebellionService.WAR_GENERAL_REBELLION:
                case GeneralRebellionService.WAR_FIEF_INDEPENDENCE:
                case WAR_RESTORATION:
                    return true;
                default:
                    return false;
            }
        }

        public static bool CanForceVassal(Kingdom pAttacker, Kingdom pDefender)
        {
            if (!VassalService.CanSetVassal(pDefender, pAttacker)) return false;
            if (VassalService.IsVassalKingdom(pAttacker)) return false;
            if (VassalService.IsSuzerain(pDefender)) return false;
            float own = VassalService.GetPowerScore(pAttacker, pIncludeVassals: true);
            float target = Mathf.Max(1f, VassalService.GetPowerScore(pDefender, pIncludeVassals: true));
            return own >= target * 1.25f;
        }

        private static bool HasActiveClaim(long pSourceId, long pTargetId, string pWarType)
        {
            return FindActiveClaimId(pSourceId, pTargetId, pWarType) >= 0;
        }

        private static long FindActiveClaimId(long pSourceId, long pTargetId, string pWarType)
        {
            if (!Ready || pSourceId < 0 || pTargetId < 0) return -1L;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT CLAIM_ID, EXPIRES_TIME FROM " + WarClaimTableItem.GetTableName() +
                                  " WHERE SOURCE_KINGDOM_ID=@s AND TARGET_KINGDOM_ID=@t " +
                                  "AND WAR_TYPE=@w AND ACTIVE=1 AND CONSUMED=0 " +
                                  "ORDER BY CREATED_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@s", pSourceId);
                cmd.Parameters.AddWithValue("@t", pTargetId);
                cmd.Parameters.AddWithValue("@w", pWarType ?? WAR_NORMAL);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                if (!reader.Read()) return -1L;
                long claimId = reader.IsDBNull(0) ? -1L : reader.GetInt64(0);
                double expires = reader.IsDBNull(1) ? -1.0 : reader.GetDouble(1);
                if (expires > 0 && Date.getYear(expires) < Date.getCurrentYear())
                {
                    DeactivateClaim(claimId);
                    return -1L;
                }
                return claimId;
            }
            catch { return -1L; }
        }

        private static void DeactivateClaim(long pClaimId)
        {
            if (!Ready || pClaimId < 0) return;
            try
            {
                DB.UpdateValue(WarClaimTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("CLAIM_ID", pClaimId) },
                    ColumnVal.Create("ACTIVE", 0));
            }
            catch { }
        }

        public static bool CanForceNoCb(Kingdom pAttacker)
        {
            if (pAttacker?.data == null) return false;
            int year = Date.getCurrentYear();
            pAttacker.data.get("aw_no_cb_penalty_until_year", out int until, -99999);
            return year >= until;
        }

        private static void ApplyNoCbPenalty(Kingdom pAttacker, Kingdom pDefender)
        {
            if (pAttacker?.data == null) return;
            int year = Date.getCurrentYear();
            pAttacker.data.get(LineageKeys.POLICY_POINTS, out float policyPoints, 0f);
            pAttacker.data.set(LineageKeys.POLICY_POINTS, Mathf.Max(0f, policyPoints - NO_CB_POLITICAL_COST));
            pAttacker.data.set("aw_no_cb_last_year", year);
            pAttacker.data.set("aw_no_cb_penalty_until_year", year + NO_CB_COOLDOWN_YEARS);
            pAttacker.data.get("aw_war_legitimacy_penalty", out int penalty, 0);
            pAttacker.data.set("aw_war_legitimacy_penalty", Mathf.Clamp(penalty + 15, 0, 100));

            HistoryWriter.RecordKingdom(pAttacker, "no_cb_war",
                HistoryText.Kingdom(pAttacker) + H("aw_hist_no_cb_war_mid") +
                HistoryText.Kingdom(pDefender),
                HistoryTarget.Kingdom(pDefender));
        }

        private static void RecordWarDecision(Kingdom pAttacker, Kingdom pDefender, string pWarType,
            string pReasonKey, bool pNoCb, bool pSystemWar)
        {
            string reason = pNoCb ? T("aw_hist_label_no_cb") : ReasonLabel(pReasonKey, pWarType);
            string eventType = pSystemWar ? "system_war_start" : "war_decision_start";
            HistoryWriter.RecordKingdom(pAttacker, eventType,
                HistoryText.Kingdom(pAttacker) + H("aw_hist_war_decision_with") +
                HistoryText.PlainText(reason) + H("aw_hist_war_decision_against") +
                HistoryText.Kingdom(pDefender) + H("aw_hist_war_decision_suffix"),
                HistoryTarget.Kingdom(pDefender));
        }

        private static string ReasonLabel(string pReasonKey, string pWarType)
        {
            switch (pReasonKey)
            {
                case "fabricate_core": return WarDisplayLabelRules.Label("fabricate_core");
                case "core_reclaim": return WarDisplayLabelRules.Label("core_reclaim");
                case "weak_claim": return WarDisplayLabelRules.Label("weak_claim");
                case "weak_claim_decision": return WarDisplayLabelRules.Label("weak_claim_decision");
                case "strong_claim": return WarDisplayLabelRules.Label("strong_claim");
                case "strong_claim_decision": return WarDisplayLabelRules.Label("strong_claim_decision");
                case "claim_war": return WarDisplayLabelRules.Label("claim_war");
                case "force_vassal": return WarDisplayLabelRules.Label("force_vassal");
                case "restoration": return WarDisplayLabelRules.Label("restoration");
                case "tianming": return WarDisplayLabelRules.Label("tianming");
                case "mandate_conquest": return WarDisplayLabelRules.Label("mandate_conquest");
                case "no_cb": return WarDisplayLabelRules.Label("no_cb");
            }

            switch (pWarType)
            {
                case "vassal_war": return WarDisplayLabelRules.Label("vassal_war");
                case "independence_war": return WarDisplayLabelRules.Label("independence_war");
                case "reclaim": return WarDisplayLabelRules.Label("core_reclaim");
                case WAR_RESTORATION: return WarDisplayLabelRules.Label("restoration_war");
                case MandateService.WAR_TIANMING: return WarDisplayLabelRules.Label("tianming");
                case MandateService.WAR_TIANMING_REBEL: return WarDisplayLabelRules.Label("tianmingrebel");
                case GeneralRebellionService.WAR_GENERAL_REBELLION: return WarDisplayLabelRules.Label("general_rebellion_war");
                case GeneralRebellionService.WAR_FIEF_INDEPENDENCE: return WarDisplayLabelRules.Label("fief_independence_war");
                default: return string.IsNullOrEmpty(pReasonKey) ? T("aw_hist_goal_generic") : pReasonKey;
            }
        }

        private static bool IsVanillaNormalWar(string pType)
        {
            return pType == "normal" || pType == "new_war";
        }

        private static bool IsCivilKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() && pKingdom.isCiv() && !pKingdom.isNeutral();
        }

        private static void LogBlockedWar(Kingdom pAttacker, Kingdom pDefender, string pType)
        {
            try
            {
                int year = Date.getCurrentYear();
                string key = "aw_war_gate_block_" + pDefender.id;
                pAttacker.data.get(key, out int lastYear, -99999);
                if (lastYear == year) return;
                pAttacker.data.set(key, year);
                ModClass.LogInfo("[war gate] blocked vanilla war " +
                                 (pAttacker.name ?? "?") + " -> " + (pDefender.name ?? "?") +
                                 " type=" + pType);
            }
            catch { }
        }
    }
}

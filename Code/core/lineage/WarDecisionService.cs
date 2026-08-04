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
        public const string WAR_TRIBUTARY = "tributary_war";
        public const string WAR_ZHULU = ZhuluWarRules.WarTypeId;

        private const int DEFAULT_CLAIM_YEARS = 30;
        private const int NO_CB_COOLDOWN_YEARS = 20;

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
            if (DiplomacyProposalRules.BlocksWarWithActivePact(
                    DiplomacyProposalService.HasActiveWarBlocker(
                        pAttacker, pDefender), systemWar: false,
                    independenceWar: type == "independence_war"))
            {
                pReason = "non_aggression_pact";
                return false;
            }
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

        public static bool CanQueueWarPair(Kingdom pAttacker,
            Kingdom pDefender, string pWarType, out string pReason,
            bool pSystemWar = false)
        {
            pReason = "";
            if (!IsCivilKingdom(pAttacker) || !IsCivilKingdom(pDefender) || pAttacker == pDefender)
            {
                pReason = "invalid";
                return false;
            }

            string type = string.IsNullOrEmpty(pWarType) ? WAR_NORMAL : pWarType;
            if (DiplomacyProposalRules.BlocksWarWithActivePact(
                    DiplomacyProposalService.HasActiveWarBlocker(
                        pAttacker, pDefender), systemWar: pSystemWar,
                    independenceWar: type == "independence_war"))
            {
                pReason = "non_aggression_pact";
                return false;
            }
            if (!CanPassAllianceWarRules(pAttacker, pDefender, type,
                    pSystemWar, out pReason)) return false;
            if (!pSystemWar &&
                !CanPassVassalWarRules(pAttacker, pDefender, type,
                    out pReason)) return false;
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
            War war = StartWar(pAttacker, pDefender, pWarType, pReasonKey,
                pNoCb, pSystemWar: false, pCasusBelliLocked: false, out _);
            return war?.data != null;
        }

        public static War TryStartWarWithResult(Kingdom pAttacker, Kingdom pDefender, string pWarType,
            string pReasonKey, bool pNoCb = false)
        {
            return StartWar(pAttacker, pDefender, pWarType, pReasonKey,
                pNoCb, pSystemWar: false, pCasusBelliLocked: false, out _);
        }

        public static War TryStartSystemWar(Kingdom pAttacker, Kingdom pDefender, string pWarType,
            string pReasonKey)
        {
            return StartWar(pAttacker, pDefender, pWarType, pReasonKey,
                pNoCb: false, pSystemWar: true, pCasusBelliLocked: false,
                out _);
        }

        public static War TryStartNotifiedWarWithResult(Kingdom pAttacker,
            Kingdom pDefender, string pWarType, string pReasonKey,
            bool pNoCb, bool pSystemWar, out string pFailureReason)
        {
            return StartWar(pAttacker, pDefender, pWarType, pReasonKey,
                pNoCb, pSystemWar, pCasusBelliLocked: true,
                out pFailureReason);
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
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                if (!TryCreateClaimInTransaction(pSource, pTarget,
                        pTargetCity, pClaimType, pWarType, pReasonKey,
                        pYearsValid, transaction, out long claimId))
                {
                    transaction.Rollback();
                    return FindActiveClaimId(pSource.id, pTarget.id,
                        pWarType);
                }
                transaction.Commit();
                RecordClaimCreated(pSource, pTarget, pReasonKey, pWarType);
                return claimId;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("WarDecisionService.CreateClaim failed: " + e.Message);
                return -1L;
            }
        }

        internal static bool TryCreateClaimInTransaction(Kingdom pSource,
            Kingdom pTarget, City pTargetCity, string pClaimType,
            string pWarType, string pReasonKey, int pYearsValid,
            SQLiteTransaction pTransaction, out long pClaimId)
        {
            pClaimId = -1L;
            if (!Ready || pTransaction == null ||
                !IsCivilKingdom(pSource) || !IsCivilKingdom(pTarget) ||
                pSource == pTarget) return false;
            string warType = string.IsNullOrEmpty(pWarType)
                ? WAR_NORMAL
                : pWarType;
            using (var existing = new SQLiteCommand(
                       "SELECT CLAIM_ID FROM WarClaim WHERE " +
                       "SOURCE_KINGDOM_ID=@source AND " +
                       "TARGET_KINGDOM_ID=@target AND WAR_TYPE=@warType " +
                       "AND ACTIVE=1 LIMIT 1", DB, pTransaction))
            {
                existing.Parameters.AddWithValue("@source", pSource.id);
                existing.Parameters.AddWithValue("@target", pTarget.id);
                existing.Parameters.AddWithValue("@warType", warType);
                if (existing.ExecuteScalar() != null) return false;
            }
            using (var allocate = new SQLiteCommand(
                       "SELECT COALESCE(MAX(CLAIM_ID),0)+1 FROM WarClaim",
                       DB, pTransaction))
                pClaimId = Convert.ToInt64(allocate.ExecuteScalar());
            double now = LineageService.CurTime();
            double expires = pYearsValid <= 0
                ? -1d
                : now + pYearsValid * 365d;
            Actor king = pSource.king;
            using var insert = new SQLiteCommand(
                "INSERT INTO WarClaim(CLAIM_ID,SOURCE_KINGDOM_ID," +
                "SOURCE_KINGDOM_NAME,SOURCE_KINGDOM_COLOR," +
                "TARGET_KINGDOM_ID,TARGET_KINGDOM_NAME," +
                "TARGET_KINGDOM_COLOR,TARGET_CITY_ID,TARGET_CITY_NAME," +
                "CLAIM_TYPE,WAR_TYPE,REASON_KEY,CREATED_TIME,EXPIRES_TIME," +
                "ACTIVE,CONSUMED,CREATED_BY_ACTOR_ID,CREATED_BY_NAME) " +
                "VALUES(@id,@source,@sourceName,@sourceColor,@target," +
                "@targetName,@targetColor,@city,@cityName,@claimType," +
                "@warType,@reason,@created,@expires,1,0,@actor,@actorName)",
                DB, pTransaction);
            insert.Parameters.AddWithValue("@id", pClaimId);
            insert.Parameters.AddWithValue("@source", pSource.id);
            insert.Parameters.AddWithValue("@sourceName", pSource.name ?? "");
            insert.Parameters.AddWithValue("@sourceColor",
                HistoryColors.FromKingdom(pSource));
            insert.Parameters.AddWithValue("@target", pTarget.id);
            insert.Parameters.AddWithValue("@targetName", pTarget.name ?? "");
            insert.Parameters.AddWithValue("@targetColor",
                HistoryColors.FromKingdom(pTarget));
            insert.Parameters.AddWithValue("@city",
                pTargetCity?.data?.id ?? -1L);
            insert.Parameters.AddWithValue("@cityName",
                pTargetCity?.data?.name ?? "");
            insert.Parameters.AddWithValue("@claimType", pClaimType ?? "");
            insert.Parameters.AddWithValue("@warType", warType);
            insert.Parameters.AddWithValue("@reason", pReasonKey ?? "");
            insert.Parameters.AddWithValue("@created", now);
            insert.Parameters.AddWithValue("@expires", expires);
            insert.Parameters.AddWithValue("@actor", king?.data?.id ?? -1L);
            insert.Parameters.AddWithValue("@actorName", king?.getName() ?? "");
            return insert.ExecuteNonQuery() == 1;
        }

        internal static void RecordClaimCreated(Kingdom pSource,
            Kingdom pTarget, string pReasonKey, string pWarType)
        {
            HistoryWriter.RecordKingdom(pSource, "war_claim_created",
                HistoryText.Kingdom(pSource) +
                H("aw_hist_war_claim_created_mid") +
                HistoryText.Kingdom(pTarget) +
                H("aw_hist_war_claim_created_reason") +
                HistoryText.PlainText(ReasonLabel(pReasonKey, pWarType)),
                HistoryTarget.Kingdom(pTarget));
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

        internal static bool HasActiveNormalClaim(Kingdom pSource,
            Kingdom pTarget)
        {
            return pSource?.data != null && pTarget?.data != null &&
                   FindActiveClaimId(pSource.id, pTarget.id, WAR_NORMAL) >= 0;
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

        private static War StartWar(Kingdom pAttacker, Kingdom pDefender,
            string pWarType, string pReasonKey, bool pNoCb, bool pSystemWar,
            bool pCasusBelliLocked, out string pFailureReason)
        {
            pFailureReason = "";
            if (!IsCivilKingdom(pAttacker) || !IsCivilKingdom(pDefender) ||
                pAttacker == pDefender)
            {
                pFailureReason = "invalid_participants";
                return null;
            }
            string type = string.IsNullOrEmpty(pWarType) ? WAR_NORMAL : pWarType;
            bool revalidateMutableEligibility =
                DiplomaticWarDeclarationLedgerRules
                    .ShouldRevalidateMutableEligibility(pCasusBelliLocked);
            if (revalidateMutableEligibility &&
                type == MandateService.WAR_TIANMING &&
                !MandatePhaseService.CanContestMandate)
            {
                pFailureReason = "mandate_contest_closed";
                return null;
            }
            if (revalidateMutableEligibility &&
                DiplomacyProposalRules.BlocksWarWithActivePact(
                    DiplomacyProposalService.HasActiveWarBlocker(
                        pAttacker, pDefender), pSystemWar,
                    independenceWar: type == "independence_war"))
            {
                pFailureReason = "active_war_blocker";
                return null;
            }
            WarTypeAsset asset = AssetManager.war_types_library.get(type);
            if (asset == null)
            {
                pFailureReason = "missing_war_type";
                return null;
            }

            if (revalidateMutableEligibility && !pSystemWar &&
                !CanPassVassalWarRules(pAttacker, pDefender, type,
                    out pFailureReason)) return null;
            if (revalidateMutableEligibility &&
                !CanPassAllianceWarRules(pAttacker, pDefender, type,
                    pSystemWar, out pFailureReason)) return null;
            try
            {
                if (World.world?.wars?.getWar(pAttacker, pDefender,
                        pOnlyMain: false) != null)
                {
                    pFailureReason = "already_at_war";
                    return null;
                }
            }
            catch { }
            if (!pCasusBelliLocked && !pSystemWar && !pNoCb &&
                !HasValidCasusBelli(pAttacker, pDefender, type))
            {
                pFailureReason = "missing_cb";
                return null;
            }
            if (!pCasusBelliLocked && pNoCb && !CanForceNoCb(pAttacker))
            {
                pFailureReason = "no_cb_cooldown";
                return null;
            }

            try
            {
                _allowWarStartDepth++;
                using IDisposable zhuluDeclaration =
                    type == ZhuluWarRules.WarTypeId
                        ? ZhuluWarDeclarationScope.Open(pDefender)
                        : null;
                War war = World.world.diplomacy.startWar(pAttacker,
                    pDefender, asset);
                if (war?.data == null)
                {
                    pFailureReason = "engine_rejected_start";
                    return null;
                }

                if (pNoCb) ApplyNoCbPenalty(pAttacker, pDefender);
                else if (!pSystemWar) ConsumeClaim(pAttacker, pDefender, type);

                RecordWarDecision(pAttacker, pDefender, type, pReasonKey, pNoCb, pSystemWar);
                return war;
            }
            catch (Exception e)
            {
                pFailureReason = "start_exception";
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
            Kingdom defenderSuzerain = VassalService.GetSuzerain(pDefender);
            bool defenderIsSubject = defenderSuzerain?.data != null &&
                                     !defenderSuzerain.isRekt();
            bool defenderIsSuzerain = attackerSuzerain != null && attackerSuzerain == pDefender;
            Kingdom attackerRoot = attackerIsVassal ? VassalService.GetRootSuzerain(pAttacker) : null;
            Kingdom defenderRoot = VassalService.GetRootSuzerain(pDefender);
            bool sameRootSuzerain = attackerRoot?.data != null && defenderRoot?.data != null &&
                                     attackerRoot == defenderRoot;
            bool blockInternalWar = sameRootSuzerain &&
                                    CentralizationService.ReadSnapshot(attackerRoot)
                                        .effects.BlocksInternalVassalWar;
            return VassalWarPermissionRules.CanDeclareWar(attackerIsVassal,
                defenderIsSubject, defenderIsSuzerain, sameRootSuzerain,
                blockInternalWar, pWarType, out pReason);
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
                    return VassalService.GetDiplomaticSuzerain(pAttacker) == pDefender;
                case "reclaim":
                    return WarTerritoryService.FindBestCoreTargetCityForDecision(pAttacker, pDefender)?.data != null;
                case "vassal_war":
                    return CanForceVassal(pAttacker, pDefender);
                case WAR_TRIBUTARY:
                    return CanForceTributary(pAttacker, pDefender);
                case WAR_NORMAL:
                    return WarTerritoryService.CanUseMandateConquest(pAttacker, pDefender);
                case WAR_ZHULU:
                    return ZhuluWarService.CanDeclare(pAttacker, pDefender,
                        out _);
                case MandateService.WAR_TIANMING:
                    return MandatePhaseService.CanContestMandate &&
                           MandateService.GetCurrentMandateKingdom() == pDefender;
                case MandateService.WAR_TIANMING_REBEL:
                case GeneralRebellionService.WAR_GENERAL_REBELLION:
                case GeneralRebellionService.WAR_FIEF_INDEPENDENCE:
                case FeudatoryJingnanRules.WarTypeId:
                case WAR_RESTORATION:
                    return true;
                case SuccessionDisputeRules.WarTypeId:
                    return SuccessionDisputeService.CanDeclareReunification(
                        pAttacker, pDefender);
                default:
                    return false;
            }
        }

        public static bool CanForceVassal(Kingdom pAttacker, Kingdom pDefender)
        {
            if (!WarAiGoalSelectionRules.CanAiForceVassal(
                    (int)KingdomTitleService.GetTitle(pAttacker),
                    (int)KingdomTitleService.GetTitle(pDefender)))
                return false;
            if (!VassalService.CanSetVassal(pDefender, pAttacker)) return false;
            if (VassalService.IsVassalKingdom(pAttacker)) return false;
            if (VassalService.IsSuzerain(pDefender)) return false;
            float own = VassalService.GetWarPowerScore(pAttacker, pIncludeVassals: true);
            float target = Mathf.Max(1f, VassalService.GetWarPowerScore(pDefender, pIncludeVassals: true));
            return own >= target * 1.25f;
        }

        public static bool CanForceTributary(Kingdom pAttacker, Kingdom pDefender)
        {
            KingdomTitle attackerTitle = KingdomTitleService.GetTitle(pAttacker);
            bool titleEligible = VassalContractTierRules.
                CanInitiateForcedTributary((int)attackerTitle);
            bool participantsValid = VassalService.CanSetTributary(
                pDefender, pAttacker);
            bool targetIndependent = !VassalService.IsVassalKingdom(pDefender);
            bool targetAlreadyTributary = VassalService.IsTributaryKingdom(pDefender);
            bool adjacent = KingdomAdjacency.AreDirectNeighbors(pAttacker, pDefender);
            float own = VassalService.GetWarPowerScore(pAttacker, pIncludeVassals: true);
            float target = VassalService.GetWarPowerScore(pDefender, pIncludeVassals: true);
            return titleEligible && VassalContractTierRules.CanForceTributary(participantsValid,
                targetIndependent, targetAlreadyTributary, adjacent, own, target);
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
                case "jingnan": return WarDisplayLabelRules.Label("jingnan_war");
                case ZhuluWarRules.GoalTypeId:
                    return WarDisplayLabelRules.Label(ZhuluWarRules.GoalTypeId);
                case "no_cb": return WarDisplayLabelRules.Label("no_cb");
            }

            switch (pWarType)
            {
                case "vassal_war": return WarDisplayLabelRules.Label("vassal_war");
                case WAR_TRIBUTARY: return WarDisplayLabelRules.Label(WAR_TRIBUTARY);
                case "independence_war": return WarDisplayLabelRules.Label("independence_war");
                case "reclaim": return WarDisplayLabelRules.Label("core_reclaim");
                case WAR_RESTORATION: return WarDisplayLabelRules.Label("restoration_war");
                case MandateService.WAR_TIANMING: return WarDisplayLabelRules.Label("tianming");
                case MandateService.WAR_TIANMING_REBEL: return WarDisplayLabelRules.Label("tianmingrebel");
                case GeneralRebellionService.WAR_GENERAL_REBELLION: return WarDisplayLabelRules.Label("general_rebellion_war");
                case GeneralRebellionService.WAR_FIEF_INDEPENDENCE: return WarDisplayLabelRules.Label("fief_independence_war");
                case FeudatoryJingnanRules.WarTypeId: return WarDisplayLabelRules.Label("jingnan_war");
                case SuccessionDisputeRules.WarTypeId: return WarDisplayLabelRules.Label("succession_dispute_war");
                case CoupRestorationRules.WarTypeId: return WarDisplayLabelRules.Label("coup_restoration_war");
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

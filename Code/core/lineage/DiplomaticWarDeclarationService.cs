using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class DiplomaticWarDeclarationService
    {
        private sealed class ExecutionPlan
        {
            public string GoalType = "";
            public string WarType = "";
            public string ReasonKey = "";
            public bool NoCb;
            public bool SystemWar;
            public WarTerritoryService.WarGoalRequest Goal;
        }

        private sealed class ExecutionResult
        {
            public bool Started;
            public string FailureReason = "";
        }

        public static bool Issue(Kingdom pAttacker, Kingdom pDefender,
            string pGoalType, City pTargetCity, string pWarType,
            string pReasonKey, string pReasonLabel, Actor pClaimant = null,
            long pSourceClaimId = -1L, long pSourceCoreId = -1L,
            long pRestorationClaimId = -1L)
        {
            if (pAttacker?.data == null || pDefender?.data == null ||
                pAttacker.isRekt() || pDefender.isRekt()) return false;
            if (DiplomaticWarDeclarationLedgerService.HasPendingForPair(
                    pAttacker, pDefender)) return false;

            string goalType = pGoalType ?? "";
            string warType = pWarType ?? WarDecisionService.WAR_NORMAL;
            if (!CanQueueCurrentGoal(pAttacker, pDefender, goalType,
                    warType, out _))
                return false;

            City displayCity = pTargetCity ?? FindDisplayCity(pAttacker,
                pDefender, goalType);
            int noticeYear = Date.getCurrentYear();
            long cityId = displayCity?.data?.id ?? -1L;
            bool requiresNotice = WarNoticeRules.RequiresNotice(
                LineageService.IsXiaKingdom(pAttacker),
                XiaizationService.GetLevel(pAttacker), deliberateDecision: true,
                goalType, warType, joiningExistingWar: false,
                pairAlreadyAtWar: HasWar(pAttacker, pDefender));
            string signature = WarNoticeRules.BuildSignature(pAttacker.id,
                pDefender.id, goalType, cityId, noticeYear);
            var record = new DiplomaticWarDeclarationRecord
            {
                Signature = signature,
                AttackerId = pAttacker.id,
                DefenderId = pDefender.id,
                GoalType = goalType,
                WarType = warType,
                ReasonKey = pReasonKey ?? "",
                ReasonLabel = pReasonLabel ?? "",
                TargetCityId = cityId,
                TargetCityName = displayCity?.data?.name ?? "",
                SourceClaimId = pSourceClaimId,
                SourceCoreId = pSourceCoreId,
                RestorationClaimId = pRestorationClaimId,
                ClaimantActorId = pClaimant?.data?.id ?? -1L,
                NoticeSignature = requiresNotice ? signature : "",
                NoticeYear = requiresNotice ? noticeYear : -1,
                EarliestWarYear = requiresNotice
                    ? WarNoticeRules.EarliestWarYear(noticeYear)
                    : -1,
                ForcedWarYear = requiresNotice
                    ? WarNoticeRules.ForcedWarYear(noticeYear)
                    : -1
            };
            if (!DiplomaticWarDeclarationLedgerService.Append(pAttacker,
                    record)) return false;
            pAttacker.data.set(
                LineageKeys.DIPLOMATIC_WAR_LAST_CANCEL_REASON, "");
            pAttacker.data.set(
                LineageKeys.DIPLOMATIC_WAR_LAST_CANCEL_YEAR, -1);
            ProjectCompatibilityRecord(pAttacker, record);
            WarNoticeService.EnsureCurrentNotice(pAttacker);
            DiplomaticWarDeclarationLedgerService.SyncNoticeProjection(
                pAttacker, record.Signature);
            RefreshCompatibilityProjection(pAttacker);
            KingdomStrategyRevisionService.MarkChanged(pAttacker.id, pDefender.id);
            return true;
        }

        public static bool Issue(Kingdom pAttacker,
            WarTerritoryService.WarTargetOption pOption)
        {
            if (pOption?.target_kingdom?.data == null) return false;
            return Issue(pAttacker, pOption.target_kingdom, pOption.goal_type,
                pOption.target_city, WarTypeForGoal(pOption.goal_type),
                ReasonKeyForGoal(pOption.goal_type), pOption.label,
                FindActor(pOption.claimant_actor_id), pOption.source_claim_id,
                pOption.source_core_id, pOption.restoration_claim_id);
        }

        public static bool CanIssue(Kingdom pAttacker,
            WarTerritoryService.WarTargetOption pOption,
            out string pFailureReason)
        {
            if (pAttacker?.data == null ||
                pOption?.target_kingdom?.data == null)
            {
                pFailureReason = "invalid_participants";
                return false;
            }
            return CanQueueCurrentGoal(pAttacker, pOption.target_kingdom,
                pOption.goal_type, WarTypeForGoal(pOption.goal_type),
                out pFailureReason);
        }

        public static void OnKingdomYear(Kingdom pAttacker)
        {
            if (pAttacker?.data == null) return;
            List<DiplomaticWarDeclarationRecord> records =
                DiplomaticWarDeclarationLedgerService.GetPending(pAttacker);
            for (int i = 0; i < records.Count; i++)
                ProcessPendingRecord(pAttacker, records[i]);
            RefreshCompatibilityProjection(pAttacker);
        }

        private static void ProcessPendingRecord(Kingdom pAttacker,
            DiplomaticWarDeclarationRecord pRecord)
        {
            if (pAttacker?.data == null || pRecord == null) return;
            ProjectCompatibilityRecord(pAttacker, pRecord);
            Kingdom defender = FindKingdom(pRecord.DefenderId);
            if (defender?.data == null || defender.isRekt() ||
                pAttacker.isRekt())
            {
                TerminateRecord(pAttacker, pRecord, "cancelled",
                    "invalid_participants");
                return;
            }

            if (HasWar(pAttacker, defender))
            {
                TerminateRecord(pAttacker, pRecord, "started", "already_at_war");
                return;
            }

            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_TYPE,
                out string warType, WarDecisionService.WAR_NORMAL);
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_GOAL_TYPE,
                out string goalType, "");
            if (!WarDecisionService.CanQueueWarPair(pAttacker, defender,
                    warType, out string pairFailure,
                    pSystemWar: IsSystemGoal(goalType)))
            {
                TerminateRecord(pAttacker, pRecord, "cancelled", pairFailure);
                return;
            }

            WarNoticeService.EnsureCurrentNotice(pAttacker);
            DiplomaticWarDeclarationLedgerService.SyncNoticeProjection(
                pAttacker, pRecord.Signature);
            if (!WarNoticeService.CanCompleteDiplomaticDeclaration(pAttacker))
                return;
            ExecutionResult result = Execute(pAttacker, defender);
            if (result.Started)
                TerminateRecord(pAttacker, pRecord, "started", "");
            else
                TerminateRecord(pAttacker, pRecord, "cancelled",
                    result.FailureReason);
        }

        public static void OnWarStarted(War pWar)
        {
            if (pWar?.data == null) return;
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (attacker?.data == null || defender?.data == null) return;
            List<DiplomaticWarDeclarationRecord> records =
                DiplomaticWarDeclarationLedgerService.GetPending(attacker);
            for (int i = 0; i < records.Count; i++)
                if (records[i].DefenderId == defender.id)
                    TerminateRecord(attacker, records[i], "started", "");
            RefreshCompatibilityProjection(attacker);
        }

        public static bool HasPending(Kingdom pKingdom)
        {
            return DiplomaticWarDeclarationLedgerService.HasPending(pKingdom);
        }

        public static bool HasPendingForPair(Kingdom pAttacker,
            Kingdom pDefender)
        {
            return DiplomaticWarDeclarationLedgerService.HasPendingForPair(
                pAttacker, pDefender);
        }

        public static void ClearPendingForPair(Kingdom pAttacker,
            Kingdom pDefender, string pReason = "cleared")
        {
            if (pAttacker?.data == null || pDefender?.data == null) return;
            List<DiplomaticWarDeclarationRecord> records =
                DiplomaticWarDeclarationLedgerService.GetPending(pAttacker);
            for (int i = 0; i < records.Count; i++)
                if (records[i].DefenderId == pDefender.id)
                    TerminateRecord(pAttacker, records[i], "cancelled",
                        pReason);
            RefreshCompatibilityProjection(pAttacker);
        }

        public static Kingdom TargetKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.DIPLOMATIC_WAR_TARGET_KINGDOM_ID,
                out long targetId, -1L);
            try { return World.world?.kingdoms?.get(targetId); }
            catch { return null; }
        }

        public static void Clear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            List<DiplomaticWarDeclarationRecord> records =
                DiplomaticWarDeclarationLedgerService.GetPending(pKingdom);
            for (int i = 0; i < records.Count; i++)
                TerminateRecord(pKingdom, records[i], "cleared", "");
            RefreshCompatibilityProjection(pKingdom);
        }

        private static void ClearCompatibilityProjection(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.DIPLOMATIC_WAR_PENDING, false);
            pKingdom.data.set(
                LineageKeys.DIPLOMATIC_WAR_TARGET_KINGDOM_ID, -1L);
            pKingdom.data.set(
                LineageKeys.DIPLOMATIC_WAR_TARGET_KINGDOM_NAME, "");
            pKingdom.data.set(LineageKeys.DIPLOMATIC_WAR_TYPE, "");
            pKingdom.data.set(LineageKeys.DIPLOMATIC_WAR_GOAL_TYPE, "");
            pKingdom.data.set(LineageKeys.DIPLOMATIC_WAR_REASON_KEY, "");
            pKingdom.data.set(LineageKeys.DIPLOMATIC_WAR_REASON_LABEL, "");
            pKingdom.data.set(LineageKeys.DIPLOMATIC_WAR_TARGET_CITY_ID, -1L);
            pKingdom.data.set(LineageKeys.DIPLOMATIC_WAR_TARGET_CITY_NAME, "");
            pKingdom.data.set(LineageKeys.DIPLOMATIC_WAR_SOURCE_CLAIM_ID, -1L);
            pKingdom.data.set(LineageKeys.DIPLOMATIC_WAR_SOURCE_CORE_ID, -1L);
            pKingdom.data.set(
                LineageKeys.DIPLOMATIC_WAR_RESTORATION_CLAIM_ID, -1L);
            pKingdom.data.set(LineageKeys.DIPLOMATIC_WAR_CLAIMANT_ACTOR_ID,
                -1L);
            pKingdom.data.set(LineageKeys.DIPLOMATIC_WAR_NOTICE_SIGNATURE, "");
            pKingdom.data.set(LineageKeys.DIPLOMATIC_WAR_NOTICE_YEAR, -1);
            pKingdom.data.set(
                LineageKeys.DIPLOMATIC_WAR_NOTICE_EARLIEST_YEAR, -1);
            pKingdom.data.set(
                LineageKeys.DIPLOMATIC_WAR_NOTICE_FORCED_YEAR, -1);
            pKingdom.data.set(LineageKeys.DIPLOMATIC_WAR_NOTICE_RECORDED,
                false);
        }

        private static void TerminateRecord(Kingdom pAttacker,
            DiplomaticWarDeclarationRecord pRecord, string pLifecycle,
            string pReason)
        {
            if (pAttacker?.data == null || pRecord == null) return;
            WarNoticeService.OnDiplomaticDeclarationClearing(pAttacker,
                pRecord.NoticeSignature);
            DiplomaticWarDeclarationLedgerService.MarkTerminal(pAttacker,
                pRecord.Signature, pLifecycle, pReason);
            if (pLifecycle == "cancelled")
            {
                pAttacker.data.set(
                    LineageKeys.DIPLOMATIC_WAR_LAST_CANCEL_REASON,
                    string.IsNullOrEmpty(pReason)
                        ? "execution_invalid"
                        : pReason);
                pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_LAST_CANCEL_YEAR,
                    Date.getCurrentYear());
            }
        }

        internal static void EnsureLedgerNotices(Kingdom pAttacker)
        {
            if (pAttacker?.data == null) return;
            List<DiplomaticWarDeclarationRecord> records =
                DiplomaticWarDeclarationLedgerService.GetPending(pAttacker);
            for (int i = 0; i < records.Count; i++)
            {
                ProjectCompatibilityRecord(pAttacker, records[i]);
                WarNoticeService.EnsureCurrentNotice(pAttacker);
                DiplomaticWarDeclarationLedgerService.SyncNoticeProjection(
                    pAttacker, records[i].Signature);
            }
            RefreshCompatibilityProjection(pAttacker);
        }

        private static void RefreshCompatibilityProjection(Kingdom pAttacker)
        {
            if (pAttacker?.data == null) return;
            List<DiplomaticWarDeclarationRecord> records =
                DiplomaticWarDeclarationLedgerService.GetPending(pAttacker);
            DiplomaticWarDeclarationRecord selected = null;
            for (int i = 0; i < records.Count; i++)
            {
                DiplomaticWarDeclarationRecord candidate = records[i];
                if (candidate == null) continue;
                if (selected == null ||
                    DiplomaticWarDeclarationLedgerRules.ComparePriority(
                        PriorityYear(candidate.EarliestWarYear),
                        PriorityYear(candidate.NoticeYear), candidate.Signature,
                        PriorityYear(selected.EarliestWarYear),
                        PriorityYear(selected.NoticeYear), selected.Signature) < 0)
                    selected = candidate;
            }
            if (selected == null)
                ClearCompatibilityProjection(pAttacker);
            else
                ProjectCompatibilityRecord(pAttacker, selected);
        }

        private static int PriorityYear(int pYear)
        {
            return pYear < 0 ? int.MaxValue : pYear;
        }

        private static void ProjectCompatibilityRecord(Kingdom pAttacker,
            DiplomaticWarDeclarationRecord pRecord)
        {
            if (pAttacker?.data == null || pRecord == null) return;
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_PENDING, true);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_TARGET_KINGDOM_ID,
                pRecord.DefenderId);
            Kingdom defender = FindKingdom(pRecord.DefenderId);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_TARGET_KINGDOM_NAME,
                defender?.name ?? "");
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_TYPE,
                pRecord.WarType ?? "");
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_GOAL_TYPE,
                pRecord.GoalType ?? "");
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_REASON_KEY,
                pRecord.ReasonKey ?? "");
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_REASON_LABEL,
                pRecord.ReasonLabel ?? "");
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_TARGET_CITY_ID,
                pRecord.TargetCityId);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_TARGET_CITY_NAME,
                pRecord.TargetCityName ?? "");
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_SOURCE_CLAIM_ID,
                pRecord.SourceClaimId);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_SOURCE_CORE_ID,
                pRecord.SourceCoreId);
            pAttacker.data.set(
                LineageKeys.DIPLOMATIC_WAR_RESTORATION_CLAIM_ID,
                pRecord.RestorationClaimId);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_CLAIMANT_ACTOR_ID,
                pRecord.ClaimantActorId);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_NOTICE_SIGNATURE,
                pRecord.NoticeSignature ?? "");
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_NOTICE_YEAR,
                pRecord.NoticeYear);
            pAttacker.data.set(
                LineageKeys.DIPLOMATIC_WAR_NOTICE_EARLIEST_YEAR,
                pRecord.EarliestWarYear);
            pAttacker.data.set(
                LineageKeys.DIPLOMATIC_WAR_NOTICE_FORCED_YEAR,
                pRecord.ForcedWarYear);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_NOTICE_RECORDED,
                pRecord.NoticeRecorded);
        }

        private static ExecutionResult Execute(Kingdom pAttacker,
            Kingdom pDefender)
        {
            if (!TryBuildExecutionPlan(pAttacker, pDefender,
                    out ExecutionPlan plan, out string failure))
                return Failed(failure);

            if (plan.GoalType ==
                WarTerritoryService.GOAL_REUNIFY_SUCCESSION)
            {
                return SuccessionDisputeService.TryDeclareReunificationWar(
                    pAttacker, pDefender)
                    ? Succeeded()
                    : Failed("reunification_start_failed");
            }

            War war = WarDecisionService.TryStartNotifiedWarWithResult(
                pAttacker, pDefender, plan.WarType, plan.ReasonKey,
                plan.NoCb, plan.SystemWar, out failure);
            if (war?.data == null) return Failed(failure);
            WarGoalCreateResult goalResult =
                WarTerritoryService.TryPersistGoalOrEndWar(war, plan.Goal);
            return goalResult.Success
                ? Succeeded()
                : Failed(goalResult.Reason);
        }

        private static bool TryBuildExecutionPlan(Kingdom pAttacker,
            Kingdom pDefender, out ExecutionPlan pPlan,
            out string pFailureReason)
        {
            pPlan = null;
            pFailureReason = "";
            if (pAttacker?.data == null || pDefender?.data == null ||
                pAttacker.isRekt() || pDefender.isRekt())
            {
                pFailureReason = "invalid_participants";
                return false;
            }

            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_GOAL_TYPE,
                out string goalType, "");
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_TYPE,
                out string warType, WarTypeForGoal(goalType));
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_REASON_KEY,
                out string reasonKey, ReasonKeyForGoal(goalType));

            bool systemWar = IsSystemGoal(goalType);
            if (!CanQueueCurrentGoal(pAttacker, pDefender, goalType,
                    warType, out pFailureReason)) return false;

            City city = null;
            long sourceCoreId = -1L;
            long sourceClaimId = -1L;
            long restorationClaimId = -1L;
            Actor claimant = null;

            switch (goalType)
            {
                case WarTerritoryService.GOAL_TAKE_MANDATE:
                    if (!MandatePhaseService.CanContestMandate ||
                        MandateService.GetCurrentMandateKingdom() != pDefender)
                    {
                        pFailureReason = "mandate_target_changed";
                        return false;
                    }
                    city = pDefender.capital ??
                           WarTerritoryService.FindFirstTargetCity(pDefender);
                    break;
                case WarTerritoryService.GOAL_MANDATE_CONQUEST:
                    if (MandateService.GetCurrentMandateKingdom() != pAttacker)
                    {
                        pFailureReason = "attacker_lost_mandate";
                        return false;
                    }
                    city = ResolveStoredTargetCity(pAttacker, pDefender) ??
                           pDefender.capital ??
                           WarTerritoryService.FindFirstTargetCity(pDefender);
                    break;
                case WarTerritoryService.GOAL_TAKE_CORE_CITY:
                case WarTerritoryService.GOAL_PRESS_CLAIM_CITY:
                case WarTerritoryService.GOAL_RESTORE_KINGDOM:
                {
                    WarTerritoryService.WarTargetOption option =
                        WarTerritoryService.FindBestTargetOption(
                            pAttacker, pDefender, goalType);
                    if (option == null)
                    {
                        pFailureReason = goalType ==
                                         WarTerritoryService.GOAL_TAKE_CORE_CITY
                            ? "missing_core_target"
                            : goalType == WarTerritoryService
                                .GOAL_PRESS_CLAIM_CITY
                                ? "missing_claim_target"
                                : "missing_restoration_target";
                        return false;
                    }
                    city = option.target_city;
                    sourceCoreId = option.source_core_id;
                    sourceClaimId = option.source_claim_id;
                    restorationClaimId = option.restoration_claim_id;
                    claimant = FindActor(option.claimant_actor_id);
                    break;
                }
                case WarTerritoryService.GOAL_FORCE_VASSAL:
                case WarTerritoryService.GOAL_FORCE_TRIBUTARY:
                    if (!pDefender.hasCities())
                    {
                        pFailureReason = "defender_has_no_city";
                        return false;
                    }
                    city = pDefender.capital ??
                           WarTerritoryService.FindFirstTargetCity(pDefender);
                    break;
                case WarTerritoryService.GOAL_INDEPENDENCE:
                    if (VassalService.GetDiplomaticSuzerain(pAttacker) !=
                        pDefender)
                    {
                        pFailureReason = "not_suzerain";
                        return false;
                    }
                    city = pDefender.capital ??
                           WarTerritoryService.FindFirstTargetCity(pDefender);
                    break;
                case WarTerritoryService.GOAL_REUNIFY_SUCCESSION:
                    if (!SuccessionDisputeService.CanDeclareReunification(
                            pAttacker, pDefender))
                    {
                        pFailureReason = "missing_reunification_claim";
                        return false;
                    }
                    city = pDefender.capital ??
                           WarTerritoryService.FindFirstTargetCity(pDefender);
                    break;
                case WarTerritoryService.GOAL_NO_CB:
                    city = pDefender.capital ??
                           WarTerritoryService.FindFirstTargetCity(pDefender);
                    break;
                default:
                    pFailureReason = "unknown_goal";
                    return false;
            }

            if (city?.data == null && goalType !=
                WarTerritoryService.GOAL_INDEPENDENCE)
            {
                pFailureReason = "missing_target_city";
                return false;
            }

            var goal = new WarTerritoryService.WarGoalRequest
            {
                goal_type = goalType,
                target_city = city,
                target_kingdom = pDefender,
                source_core_id = sourceCoreId,
                source_claim_id = goalType ==
                                  WarTerritoryService.GOAL_RESTORE_KINGDOM
                    ? restorationClaimId
                    : sourceClaimId,
                claimant = claimant
            };
            UpdateResolvedTarget(pAttacker, city, sourceCoreId,
                sourceClaimId, restorationClaimId, claimant);
            pPlan = new ExecutionPlan
            {
                GoalType = goalType,
                WarType = string.IsNullOrEmpty(warType)
                    ? WarTypeForGoal(goalType)
                    : warType,
                ReasonKey = string.IsNullOrEmpty(reasonKey)
                    ? ReasonKeyForGoal(goalType)
                    : reasonKey,
                NoCb = goalType == WarTerritoryService.GOAL_NO_CB,
                SystemWar = systemWar,
                Goal = goal
            };
            return true;
        }

        private static bool CanQueueCurrentGoal(Kingdom pAttacker,
            Kingdom pDefender, string pGoalType, string pWarType,
            out string pFailureReason)
        {
            bool basicAllowed = WarDecisionService.CanQueueWarPair(
                pAttacker, pDefender, pWarType, out pFailureReason,
                IsSystemGoal(pGoalType));
            bool hasNormalCb = WarDecisionService.HasValidCasusBelli(
                pAttacker, pDefender, pWarType);
            bool hasCoreTarget = WarTerritoryService
                .FindBestCoreTargetCityForDecision(pAttacker, pDefender)
                ?.data != null;
            bool hasClaimTarget = WarTerritoryService
                .FindBestClaimTargetCityForDecision(pAttacker, pDefender)
                ?.data != null;
            bool canForceVassal = WarDecisionService.CanForceVassal(
                pAttacker, pDefender);
            bool canForceTributary = WarDecisionService.CanForceTributary(
                pAttacker, pDefender);
            bool isIndependenceTarget = VassalService.GetDiplomaticSuzerain(pAttacker) ==
                                        pDefender;
            bool hasRestorationTarget = WarTerritoryService
                .FindBestRestorationTargetCityForDecision(pAttacker,
                    pDefender)?.data != null;
            bool canReunifySuccession = SuccessionDisputeService
                .CanDeclareReunification(pAttacker, pDefender);
            bool canForceNoCb = WarDecisionService.CanForceNoCb(pAttacker);
            return WarDecisionQueueRules.CanQueueGoal(pGoalType,
                basicAllowed, hasNormalCb, canForceNoCb, hasCoreTarget,
                hasClaimTarget, canForceVassal, canForceTributary,
                isIndependenceTarget, hasRestorationTarget,
                canReunifySuccession, out pFailureReason);
        }

        private static City ResolveStoredTargetCity(Kingdom pAttacker,
            Kingdom pDefender)
        {
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_TARGET_CITY_ID,
                out long targetCityId, -1L);
            City city = FindCity(targetCityId);
            return city?.data != null && !city.isRekt() &&
                   city.kingdom == pDefender
                ? city
                : null;
        }

        private static void UpdateResolvedTarget(Kingdom pAttacker,
            City pCity, long pSourceCoreId, long pSourceClaimId,
            long pRestorationClaimId, Actor pClaimant)
        {
            if (pAttacker?.data == null) return;
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_TARGET_CITY_ID,
                pCity?.data?.id ?? -1L);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_TARGET_CITY_NAME,
                pCity?.data?.name ?? "");
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_SOURCE_CORE_ID,
                pSourceCoreId);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_SOURCE_CLAIM_ID,
                pSourceClaimId);
            pAttacker.data.set(
                LineageKeys.DIPLOMATIC_WAR_RESTORATION_CLAIM_ID,
                pRestorationClaimId);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_CLAIMANT_ACTOR_ID,
                pClaimant?.data?.id ?? -1L);
        }

        private static ExecutionResult Succeeded()
        {
            return new ExecutionResult { Started = true };
        }

        private static ExecutionResult Failed(string pReason)
        {
            return new ExecutionResult
            {
                Started = false,
                FailureReason = string.IsNullOrEmpty(pReason)
                    ? "execution_invalid"
                    : pReason
            };
        }

        private static bool HasWar(Kingdom pAttacker, Kingdom pDefender)
        {
            try
            {
                return World.world?.wars?.getWar(pAttacker, pDefender,
                    pOnlyMain: false) != null;
            }
            catch { return false; }
        }

        private static City FindDisplayCity(Kingdom pAttacker,
            Kingdom pDefender, string pGoalType)
        {
            return pGoalType switch
            {
                WarTerritoryService.GOAL_TAKE_MANDATE => pDefender.capital ??
                    WarTerritoryService.FindFirstTargetCity(pDefender),
                WarTerritoryService.GOAL_MANDATE_CONQUEST =>
                    pDefender.capital ??
                    WarTerritoryService.FindFirstTargetCity(pDefender),
                WarTerritoryService.GOAL_TAKE_CORE_CITY =>
                    WarTerritoryService.FindBestCoreTargetCityForDecision(
                        pAttacker, pDefender),
                WarTerritoryService.GOAL_PRESS_CLAIM_CITY =>
                    WarTerritoryService.FindBestClaimTargetCityForDecision(
                        pAttacker, pDefender),
                WarTerritoryService.GOAL_RESTORE_KINGDOM =>
                    WarTerritoryService
                        .FindBestRestorationTargetCityForDecision(pAttacker,
                            pDefender),
                _ => WarTerritoryService.FindFirstTargetCity(pDefender)
            };
        }

        private static bool IsSystemGoal(string pGoalType)
        {
            return pGoalType == WarTerritoryService.GOAL_TAKE_MANDATE ||
                   pGoalType == WarTerritoryService.GOAL_MANDATE_CONQUEST;
        }

        internal static string WarTypeForGoal(string pGoalType)
        {
            return pGoalType switch
            {
                WarTerritoryService.GOAL_TAKE_MANDATE =>
                    MandateService.WAR_TIANMING,
                WarTerritoryService.GOAL_TAKE_CORE_CITY => "reclaim",
                WarTerritoryService.GOAL_FORCE_VASSAL => "vassal_war",
                WarTerritoryService.GOAL_FORCE_TRIBUTARY =>
                    WarDecisionService.WAR_TRIBUTARY,
                WarTerritoryService.GOAL_INDEPENDENCE => "independence_war",
                WarTerritoryService.GOAL_RESTORE_KINGDOM =>
                    WarDecisionService.WAR_RESTORATION,
                WarTerritoryService.GOAL_REUNIFY_SUCCESSION =>
                    SuccessionDisputeRules.WarTypeId,
                _ => WarDecisionService.WAR_NORMAL
            };
        }

        internal static string ReasonKeyForGoal(string pGoalType)
        {
            return pGoalType switch
            {
                WarTerritoryService.GOAL_TAKE_MANDATE => "tianming",
                WarTerritoryService.GOAL_MANDATE_CONQUEST =>
                    "mandate_conquest",
                WarTerritoryService.GOAL_TAKE_CORE_CITY => "core_reclaim",
                WarTerritoryService.GOAL_PRESS_CLAIM_CITY => "claim_war",
                WarTerritoryService.GOAL_FORCE_VASSAL => "force_vassal",
                WarTerritoryService.GOAL_FORCE_TRIBUTARY => "tributary_war",
                WarTerritoryService.GOAL_INDEPENDENCE => "independence_war",
                WarTerritoryService.GOAL_RESTORE_KINGDOM => "restoration",
                WarTerritoryService.GOAL_REUNIFY_SUCCESSION =>
                    "succession_reunification",
                WarTerritoryService.GOAL_NO_CB => "no_cb",
                _ => ""
            };
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0L) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            if (pCityId < 0) return null;
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }
    }
}

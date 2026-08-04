using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal sealed class DiplomaticWarTargetAvailability
    {
        internal DiplomaticWarTargetAvailability(
            WarTerritoryService.WarTargetOption pOption,
            bool pAvailable, string pFailureReason)
        {
            Option = pOption;
            Available = pAvailable;
            FailureReason = pFailureReason ?? "";
        }

        internal WarTerritoryService.WarTargetOption Option { get; }
        internal bool Available { get; }
        internal string FailureReason { get; }

        internal DiplomaticWarAvailabilityCandidate ToCandidate()
        {
            return new DiplomaticWarAvailabilityCandidate(Available,
                FailureReason);
        }
    }

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
            return TryIssue(pAttacker, pDefender, pGoalType, pTargetCity,
                pWarType, pReasonKey, pReasonLabel, out _, pClaimant,
                pSourceClaimId, pSourceCoreId, pRestorationClaimId);
        }

        public static bool TryIssue(Kingdom pAttacker, Kingdom pDefender,
            string pGoalType, City pTargetCity, string pWarType,
            string pReasonKey, string pReasonLabel,
            out string pFailureReason, Actor pClaimant = null,
            long pSourceClaimId = -1L, long pSourceCoreId = -1L,
            long pRestorationClaimId = -1L)
        {
            pFailureReason = "";
            string goalType = pGoalType ?? "";
            string warType = pWarType ?? WarDecisionService.WAR_NORMAL;
            if (!CanIssue(pAttacker, pDefender, goalType, warType,
                    out pFailureReason))
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
                    record))
            {
                pFailureReason = "write_failed";
                return false;
            }
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
            pFailureReason = "";
            return true;
        }

        public static bool Issue(Kingdom pAttacker,
            WarTerritoryService.WarTargetOption pOption)
        {
            return TryIssue(pAttacker, pOption, out _);
        }

        public static bool TryIssue(Kingdom pAttacker,
            WarTerritoryService.WarTargetOption pOption,
            out string pFailureReason)
        {
            if (pOption?.target_kingdom?.data == null)
            {
                pFailureReason = "invalid_participants";
                return false;
            }
            return TryIssue(pAttacker, pOption.target_kingdom,
                pOption.goal_type,
                pOption.target_city, WarTypeForGoal(pOption.goal_type),
                ReasonKeyForGoal(pOption.goal_type), pOption.label,
                out pFailureReason,
                FindActor(pOption.claimant_actor_id), pOption.source_claim_id,
                pOption.source_core_id, pOption.restoration_claim_id);
        }

        public static bool IssueZhulu(Kingdom pAttacker,
            Kingdom pDefender)
        {
            if (!ZhuluWarRules.CanAiDeclare(
                    World.world?.map_stats?.world_age_id)) return false;
            City target = FindDisplayCity(pAttacker, pDefender,
                ZhuluWarRules.GoalTypeId);
            return Issue(pAttacker, pDefender,
                ZhuluWarRules.GoalTypeId, target,
                ZhuluWarRules.WarTypeId, ZhuluWarRules.GoalTypeId,
                HistoryLocalizationRules.Text(
                    "aw_war_goal_zhulu_annexation"));
        }

        public static bool CanIssue(Kingdom pAttacker,
            WarTerritoryService.WarTargetOption pOption,
            out string pFailureReason)
        {
            if (pOption?.target_kingdom?.data == null)
            {
                pFailureReason = "invalid_participants";
                return false;
            }
            return CanIssue(pAttacker, pOption.target_kingdom,
                pOption.goal_type, WarTypeForGoal(pOption.goal_type),
                out pFailureReason);
        }

        internal static bool CanIssue(Kingdom pAttacker,
            Kingdom pDefender, string pGoalType, string pWarType,
            out string pFailureReason)
        {
            if (pAttacker?.data == null || pDefender?.data == null ||
                pAttacker.isRekt() || pDefender.isRekt())
            {
                pFailureReason = "invalid_participants";
                return false;
            }
            if (DiplomaticWarDeclarationLedgerService.HasPendingForPair(
                    pAttacker, pDefender))
            {
                pFailureReason = "war_preparation";
                return false;
            }
            return CanQueueCurrentGoal(pAttacker, pDefender, pGoalType,
                pWarType, out pFailureReason);
        }

        internal static List<DiplomaticWarTargetAvailability>
            BuildTargetAvailabilities(Kingdom pAttacker,
                Kingdom pDefender)
        {
            List<WarTerritoryService.WarTargetOption> options =
                WarTerritoryService.BuildTargetOptions(pAttacker,
                    pDefender, pIncludeUnavailable: true);
            var result = new List<DiplomaticWarTargetAvailability>(
                options.Count);
            for (int index = 0; index < options.Count; index++)
            {
                WarTerritoryService.WarTargetOption option = options[index];
                bool available = CanIssue(pAttacker, option,
                    out string failureReason);
                result.Add(new DiplomaticWarTargetAvailability(option,
                    available, failureReason));
            }
            return result;
        }

        internal static DiplomaticWarAvailabilityResult
            ResolvePairAvailability(Kingdom pAttacker,
                Kingdom pDefender)
        {
            List<DiplomaticWarTargetAvailability> targets =
                BuildTargetAvailabilities(pAttacker, pDefender);
            var candidates = new List<DiplomaticWarAvailabilityCandidate>(
                targets.Count);
            for (int index = 0; index < targets.Count; index++)
                candidates.Add(targets[index].ToCandidate());
            return DiplomaticWarAvailabilityRules.Resolve(
                HasPendingForPair(pAttacker, pDefender), candidates);
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

            WarNoticeService.EnsureCurrentNotice(pAttacker);
            DiplomaticWarDeclarationLedgerService.SyncNoticeProjection(
                pAttacker, pRecord.Signature);
            bool noticeReady = WarNoticeService
                .CanCompleteDiplomaticDeclaration(pAttacker);
            if (!DiplomaticWarDeclarationLedgerRules.ShouldExecute(
                    Date.getCurrentYear(), pRecord.EarliestWarYear,
                    pRecord.ForcedWarYear, noticeReady))
                return;
            ExecutionResult result = Execute(pAttacker, defender, pRecord);
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

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null) return;
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            ReconcileEndedWarPair(attacker, defender);
            ReconcileEndedWarPair(defender, attacker);
        }

        private static void ReconcileEndedWarPair(Kingdom pOwner,
            Kingdom pOpponent)
        {
            if (pOwner?.data == null || pOpponent?.data == null) return;
            List<DiplomaticWarDeclarationRecord> records =
                DiplomaticWarDeclarationLedgerService.GetPending(pOwner);
            for (int i = 0; i < records.Count; i++)
            {
                DiplomaticWarDeclarationRecord record = records[i];
                if (!DiplomaticWarDeclarationLedgerRules.
                        MatchesDirectedWarPair(record?.AttackerId ?? -1L,
                            record?.DefenderId ?? -1L, pOwner.id,
                            pOpponent.id)) continue;
                TerminateRecord(pOwner, record, "ended", "war_ended");
            }
            RefreshCompatibilityProjection(pOwner);
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
            Kingdom pDefender, DiplomaticWarDeclarationRecord pRecord)
        {
            if (!TryBuildExecutionPlan(pAttacker, pDefender, pRecord,
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
            if (plan.GoalType == ZhuluWarRules.GoalTypeId)
                return Succeeded();
            WarGoalCreateResult goalResult =
                WarTerritoryService.TryPersistGoalOrEndWar(war, plan.Goal);
            return goalResult.Success
                ? Succeeded()
                : Failed(goalResult.Reason);
        }

        private static bool TryBuildExecutionPlan(Kingdom pAttacker,
            Kingdom pDefender, DiplomaticWarDeclarationRecord pRecord,
            out ExecutionPlan pPlan,
            out string pFailureReason)
        {
            pPlan = null;
            pFailureReason = "";
            if (pAttacker?.data == null || pDefender?.data == null ||
                pRecord == null || pAttacker.isRekt() ||
                pDefender.isRekt())
            {
                pFailureReason = "invalid_participants";
                return false;
            }

            string goalType = pRecord.GoalType ?? "";
            string warType = string.IsNullOrEmpty(pRecord.WarType)
                ? WarTypeForGoal(goalType)
                : pRecord.WarType;
            string reasonKey = string.IsNullOrEmpty(pRecord.ReasonKey)
                ? ReasonKeyForGoal(goalType)
                : pRecord.ReasonKey;
            bool systemWar = IsSystemGoal(goalType);
            City stored = FindCity(pRecord.TargetCityId);
            bool storedValid = stored?.data != null && !stored.isRekt() &&
                               stored.kingdom == pDefender;
            City capital = pDefender.capital;
            City first = WarTerritoryService.FindFirstTargetCity(pDefender);
            long targetId = DiplomaticWarDeclarationLedgerRules
                .ResolveTargetCityId(storedValid, pRecord.TargetCityId,
                    capital?.data?.id ?? -1L, first?.data?.id ?? -1L);
            City city = FindCity(targetId);
            long sourceCoreId = pRecord.SourceCoreId;
            long sourceClaimId = pRecord.SourceClaimId;
            long restorationClaimId = pRecord.RestorationClaimId;
            Actor claimant = FindActor(pRecord.ClaimantActorId);

            switch (goalType)
            {
                case WarTerritoryService.GOAL_TAKE_MANDATE:
                case WarTerritoryService.GOAL_MANDATE_CONQUEST:
                case WarTerritoryService.GOAL_TAKE_CORE_CITY:
                case WarTerritoryService.GOAL_PRESS_CLAIM_CITY:
                case WarTerritoryService.GOAL_RESTORE_KINGDOM:
                case WarTerritoryService.GOAL_FORCE_VASSAL:
                case WarTerritoryService.GOAL_FORCE_TRIBUTARY:
                case WarTerritoryService.GOAL_INDEPENDENCE:
                case WarTerritoryService.GOAL_REUNIFY_SUCCESSION:
                case ZhuluWarRules.GoalTypeId:
                case WarTerritoryService.GOAL_NO_CB:
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
                pAttacker, pDefender, pWarType, out string pairFailureReason,
                IsSystemGoal(pGoalType));
            if (!basicAllowed)
            {
                pFailureReason = string.IsNullOrWhiteSpace(pairFailureReason)
                    ? "invalid"
                    : pairFailureReason;
                return false;
            }

            bool hasNormalCb = false;
            bool hasCoreTarget = false;
            bool hasClaimTarget = false;
            bool canForceVassal = false;
            bool canForceTributary = false;
            bool isIndependenceTarget = false;
            bool hasRestorationTarget = false;
            bool canReunifySuccession = false;
            bool canForceNoCb = false;
            switch (pGoalType ?? "")
            {
                case WarTerritoryService.GOAL_TAKE_MANDATE:
                case WarTerritoryService.GOAL_MANDATE_CONQUEST:
                case ZhuluWarRules.GoalTypeId:
                    hasNormalCb = WarDecisionService.HasValidCasusBelli(
                        pAttacker, pDefender, pWarType);
                    break;
                case WarTerritoryService.GOAL_TAKE_CORE_CITY:
                    hasCoreTarget = WarTerritoryService
                        .FindBestCoreTargetCityForDecision(pAttacker,
                            pDefender)?.data != null;
                    break;
                case WarTerritoryService.GOAL_PRESS_CLAIM_CITY:
                    if (!WarDecisionService.HasValidCasusBelli(
                            pAttacker, pDefender, pWarType))
                    {
                        pFailureReason = "missing_claim_target";
                        return false;
                    }
                    hasClaimTarget = WarTerritoryService
                        .FindBestClaimTargetCityForDecision(pAttacker,
                            pDefender)?.data != null;
                    break;
                case WarTerritoryService.GOAL_FORCE_VASSAL:
                    canForceVassal = WarDecisionService.CanForceVassal(
                        pAttacker, pDefender);
                    break;
                case WarTerritoryService.GOAL_FORCE_TRIBUTARY:
                    canForceTributary = WarDecisionService.CanForceTributary(
                        pAttacker, pDefender);
                    break;
                case WarTerritoryService.GOAL_INDEPENDENCE:
                    isIndependenceTarget = VassalService
                        .GetDiplomaticSuzerain(pAttacker) == pDefender;
                    break;
                case WarTerritoryService.GOAL_RESTORE_KINGDOM:
                    hasRestorationTarget = WarTerritoryService
                        .FindBestRestorationTargetCityForDecision(pAttacker,
                            pDefender)?.data != null;
                    break;
                case WarTerritoryService.GOAL_REUNIFY_SUCCESSION:
                    canReunifySuccession = SuccessionDisputeService
                        .CanDeclareReunification(pAttacker, pDefender);
                    break;
                case WarTerritoryService.GOAL_NO_CB:
                case WarGoalTypeIds.LegacyNoCb:
                    canForceNoCb = WarDecisionService.CanForceNoCb(pAttacker);
                    break;
                default:
                    pFailureReason = "unknown_goal";
                    return false;
            }
            return WarDecisionQueueRules.CanQueueGoal(pGoalType,
                basicAllowed, pairFailureReason, hasNormalCb, canForceNoCb,
                hasCoreTarget, hasClaimTarget, canForceVassal,
                canForceTributary, isIndependenceTarget,
                hasRestorationTarget, canReunifySuccession,
                out pFailureReason);
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
                ZhuluWarRules.GoalTypeId => ZhuluWarRules.WarTypeId,
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
                ZhuluWarRules.GoalTypeId => ZhuluWarRules.GoalTypeId,
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

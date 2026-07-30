using System;

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
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_PENDING,
                out bool pending, false);
            if (pending) return false;

            string goalType = pGoalType ?? "";
            string warType = pWarType ?? WarDecisionService.WAR_NORMAL;
            if (!CanQueueCurrentGoal(pAttacker, pDefender, goalType,
                    warType, out _))
                return false;

            City displayCity = pTargetCity ?? FindDisplayCity(pAttacker,
                pDefender, goalType);
            pAttacker.data.set(
                LineageKeys.DIPLOMATIC_WAR_LAST_CANCEL_REASON, "");
            pAttacker.data.set(
                LineageKeys.DIPLOMATIC_WAR_LAST_CANCEL_YEAR, -1);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_PENDING, true);
            pAttacker.data.set(
                LineageKeys.DIPLOMATIC_WAR_TARGET_KINGDOM_ID, pDefender.id);
            pAttacker.data.set(
                LineageKeys.DIPLOMATIC_WAR_TARGET_KINGDOM_NAME,
                pDefender.name ?? "");
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_TYPE, warType);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_GOAL_TYPE,
                goalType);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_REASON_KEY,
                pReasonKey ?? "");
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_REASON_LABEL,
                pReasonLabel ?? "");
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_TARGET_CITY_ID,
                displayCity?.data?.id ?? -1L);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_TARGET_CITY_NAME,
                displayCity?.data?.name ?? "");
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_SOURCE_CLAIM_ID,
                pSourceClaimId);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_SOURCE_CORE_ID,
                pSourceCoreId);
            pAttacker.data.set(
                LineageKeys.DIPLOMATIC_WAR_RESTORATION_CLAIM_ID,
                pRestorationClaimId);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_CLAIMANT_ACTOR_ID,
                pClaimant?.data?.id ?? -1L);
            WarNoticeService.EnsureCurrentNotice(pAttacker);
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
            if (!HasPending(pAttacker)) return;
            Kingdom defender = TargetKingdom(pAttacker);
            if (defender?.data == null || defender.isRekt() ||
                pAttacker.isRekt())
            {
                Clear(pAttacker);
                return;
            }

            if (HasWar(pAttacker, defender))
            {
                Clear(pAttacker);
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
                Cancel(pAttacker, pairFailure);
                return;
            }

            WarNoticeService.EnsureCurrentNotice(pAttacker);
            if (!WarNoticeService.CanCompleteDiplomaticDeclaration(pAttacker))
                return;
            ExecutionResult result = Execute(pAttacker, defender);
            if (result.Started)
                Clear(pAttacker);
            else
                Cancel(pAttacker, result.FailureReason);
        }

        public static void OnWarStarted(War pWar)
        {
            if (pWar?.data == null) return;
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (!HasPending(attacker) || defender?.data == null ||
                TargetKingdom(attacker) != defender) return;
            Clear(attacker);
        }

        public static bool HasPending(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.DIPLOMATIC_WAR_PENDING,
                out bool pending, false);
            return pending;
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
            WarNoticeService.OnDiplomaticDeclarationClearing(pKingdom);
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

        private static void Cancel(Kingdom pAttacker, string pReason)
        {
            if (pAttacker?.data == null) return;
            Clear(pAttacker);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_LAST_CANCEL_REASON,
                string.IsNullOrEmpty(pReason) ? "execution_invalid" : pReason);
            pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_LAST_CANCEL_YEAR,
                Date.getCurrentYear());
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

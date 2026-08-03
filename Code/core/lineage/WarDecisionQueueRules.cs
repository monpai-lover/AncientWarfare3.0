namespace AncientWarfare3.core.lineage
{
    public static class WarDecisionQueueRules
    {
        public static bool CanQueueGoal(string pGoalType,
            bool pBasicAllowed,
            bool pHasNormalCb,
            bool pCanForceNoCb,
            bool pHasCoreTarget,
            bool pHasClaimTarget,
            bool pCanForceVassal,
            bool pCanForceTributary,
            bool pIsIndependenceTarget,
            bool pHasRestorationTarget,
            out string pReason)
        {
            return CanQueueGoal(pGoalType, pBasicAllowed,
                pBasicFailureReason: "invalid", pHasNormalCb,
                pCanForceNoCb, pHasCoreTarget, pHasClaimTarget,
                pCanForceVassal, pCanForceTributary,
                pIsIndependenceTarget, pHasRestorationTarget,
                pCanReunifySuccession: false, out pReason);
        }

        public static bool CanQueueGoal(string pGoalType,
            bool pBasicAllowed,
            bool pHasNormalCb,
            bool pCanForceNoCb,
            bool pHasCoreTarget,
            bool pHasClaimTarget,
            bool pCanForceVassal,
            bool pCanForceTributary,
            bool pIsIndependenceTarget,
            bool pHasRestorationTarget,
            bool pCanReunifySuccession,
            out string pReason)
        {
            return CanQueueGoal(pGoalType, pBasicAllowed,
                pBasicFailureReason: "invalid", pHasNormalCb,
                pCanForceNoCb, pHasCoreTarget, pHasClaimTarget,
                pCanForceVassal, pCanForceTributary,
                pIsIndependenceTarget, pHasRestorationTarget,
                pCanReunifySuccession, out pReason);
        }

        public static bool CanQueueGoal(string pGoalType,
            bool pBasicAllowed,
            string pBasicFailureReason,
            bool pHasNormalCb,
            bool pCanForceNoCb,
            bool pHasCoreTarget,
            bool pHasClaimTarget,
            bool pCanForceVassal,
            bool pCanForceTributary,
            bool pIsIndependenceTarget,
            bool pHasRestorationTarget,
            bool pCanReunifySuccession,
            out string pReason)
        {
            if (!pBasicAllowed)
            {
                pReason = string.IsNullOrWhiteSpace(pBasicFailureReason)
                    ? "invalid"
                    : pBasicFailureReason;
                return false;
            }

            switch (pGoalType ?? "")
            {
                case "take_mandate":
                    return Check(pHasNormalCb, "missing_mandate_cb", out pReason);
                case "mandate_conquest":
                    return Check(pHasNormalCb, "missing_mandate_conquest_cb", out pReason);
                case "take_core_city":
                    return Check(pHasCoreTarget, "missing_core_target", out pReason);
                case "press_claim_city":
                    return Check(pHasClaimTarget, "missing_claim_target", out pReason);
                case "force_vassal":
                    return Check(pCanForceVassal, "cannot_force_vassal", out pReason);
                case "force_tributary":
                    return Check(pCanForceTributary, "cannot_force_tributary", out pReason);
                case "independence":
                    return Check(pIsIndependenceTarget, "not_suzerain", out pReason);
                case "restore_kingdom":
                    return Check(pHasRestorationTarget, "missing_restoration_target", out pReason);
                case "reunify_succession":
                    return Check(pCanReunifySuccession,
                        "missing_reunification_claim", out pReason);
                case ZhuluWarRules.GoalTypeId:
                    return Check(pHasNormalCb, "missing_zhulu_cb",
                        out pReason);
                case "no_cb":
                case "no_cb_punitive":
                    return Check(pCanForceNoCb, "cannot_force_no_cb", out pReason);
                default:
                    pReason = "unknown_goal";
                    return false;
            }
        }

        private static bool Check(bool pAllowed, string pBlockedReason, out string pReason)
        {
            if (pAllowed)
            {
                pReason = "";
                return true;
            }

            pReason = pBlockedReason;
            return false;
        }
    }
}

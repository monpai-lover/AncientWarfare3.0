namespace AncientWarfare3.core.lineage
{
    public static class WarGoalControlRules
    {
        public static bool ShouldResolveTransferredCityGoal(string pGoalType,
            bool pTargetCityMatchesGoal,
            bool pNewOwnerIsWarAttacker)
        {
            return pTargetCityMatchesGoal && pNewOwnerIsWarAttacker &&
                   IsTerritorialCityControlGoal(pGoalType);
        }

        public static bool ShouldResolveControlledSettlementGoal(string pGoalType,
            bool pTargetCityMatchesGoal, bool pCapturerIsOnAttackerSide,
            bool pCityStillOwnedByDefender)
        {
            return pTargetCityMatchesGoal && pCapturerIsOnAttackerSide &&
                   pCityStillOwnedByDefender &&
                   IsNonTerritorialSettlementGoal(pGoalType);
        }

        public static bool ShouldResolveControlledCityGoal(string pGoalType,
            bool pTargetCityControlledByAttackerSystem)
        {
            if (!pTargetCityControlledByAttackerSystem) return false;
            return IsCityControlGoal(pGoalType);
        }

        private static bool IsCityControlGoal(string pGoalType)
        {
            return IsTerritorialCityControlGoal(pGoalType) ||
                   IsNonTerritorialSettlementGoal(pGoalType);
        }

        public static bool IsNonTerritorialSettlementGoal(string pGoalType)
        {
            return pGoalType == "force_vassal" ||
                   pGoalType == "force_tributary" ||
                   pGoalType == "take_mandate" ||
                   pGoalType == "independence" ||
                   pGoalType == "reunify_succession" ||
                   pGoalType == "no_cb" ||
                   pGoalType == "no_cb_punitive";
        }

        private static bool IsTerritorialCityControlGoal(string pGoalType)
        {
            return pGoalType == "take_core_city" ||
                   pGoalType == "press_claim_city" ||
                   pGoalType == "take_de_jure_region" ||
                   pGoalType == "mandate_conquest" ||
                   pGoalType == "restore_kingdom";
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    public static class WarGoalControlRules
    {
        public static bool ShouldResolveTransferredCityGoal(string pGoalType,
            bool pTargetCityMatchesGoal,
            bool pNewOwnerIsWarAttacker)
        {
            return pTargetCityMatchesGoal && pNewOwnerIsWarAttacker && IsCityControlGoal(pGoalType);
        }

        public static bool ShouldResolveControlledCityGoal(string pGoalType,
            bool pTargetCityControlledByAttackerSystem)
        {
            if (!pTargetCityControlledByAttackerSystem) return false;
            return IsCityControlGoal(pGoalType);
        }

        private static bool IsCityControlGoal(string pGoalType)
        {
            return pGoalType == "take_core_city" || pGoalType == "press_claim_city";
        }
    }
}

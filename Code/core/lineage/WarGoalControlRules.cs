namespace AncientWarfare3.core.lineage
{
    public static class WarGoalControlRules
    {
        public static bool ShouldResolveControlledCityGoal(string pGoalType,
            bool pTargetCityControlledByAttackerSystem)
        {
            if (!pTargetCityControlledByAttackerSystem) return false;
            return pGoalType == "take_core_city" || pGoalType == "press_claim_city";
        }
    }
}

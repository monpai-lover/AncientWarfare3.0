namespace AncientWarfare3.core.grandstrategy
{
    public static class GrandStrategyRuntimeRules
    {
        public static bool ShouldRun(GrandStrategyArmyMode mode)
        {
            return mode == GrandStrategyArmyMode.GrandStrategy;
        }

        public static bool ShouldRaiseLevies(GrandStrategyArmyMode mode,
            bool warIsActive)
        {
            return ShouldRun(mode) && warIsActive;
        }
    }
}

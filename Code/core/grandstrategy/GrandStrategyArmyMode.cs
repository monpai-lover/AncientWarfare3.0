namespace AncientWarfare3.core.grandstrategy
{
    public enum GrandStrategyArmyMode
    {
        Vanilla = 0,
        ArmyRts = 1,
        GrandStrategy = 2
    }

    public static class GrandStrategyArmyModeRules
    {
        public static GrandStrategyArmyMode Resolve(bool armyRtsEnabled,
            bool grandStrategyEnabled)
        {
            if (grandStrategyEnabled) return GrandStrategyArmyMode.GrandStrategy;
            return armyRtsEnabled
                ? GrandStrategyArmyMode.ArmyRts
                : GrandStrategyArmyMode.Vanilla;
        }

        public static bool IsGrandStrategy(GrandStrategyArmyMode mode)
        {
            return mode == GrandStrategyArmyMode.GrandStrategy;
        }

        public static bool RequiresRestart(GrandStrategyArmyMode previous,
            GrandStrategyArmyMode next)
        {
            return previous != next;
        }

        public static string LogName(GrandStrategyArmyMode mode)
        {
            return mode switch
            {
                GrandStrategyArmyMode.ArmyRts => "army_rts",
                GrandStrategyArmyMode.GrandStrategy => "grand_strategy",
                _ => "vanilla"
            };
        }
    }
}

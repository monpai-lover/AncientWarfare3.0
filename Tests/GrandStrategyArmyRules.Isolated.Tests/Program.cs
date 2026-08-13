using AncientWarfare3.core.grandstrategy;

internal static class Program
{
    private static int Main()
    {
        try
        {
            Equal(GrandStrategyArmyMode.Vanilla,
                GrandStrategyArmyModeRules.Resolve(false, false),
                "default mode is vanilla");
            Equal(GrandStrategyArmyMode.ArmyRts,
                GrandStrategyArmyModeRules.Resolve(true, false),
                "RTS mode resolves");
            Equal(GrandStrategyArmyMode.GrandStrategy,
                GrandStrategyArmyModeRules.Resolve(false, true),
                "grand strategy mode resolves");
            True(GrandStrategyArmyModeRules.RequiresRestart(
                GrandStrategyArmyMode.ArmyRts,
                GrandStrategyArmyMode.GrandStrategy),
                "mode changes require restart");
            True(GrandStrategyArmyModeRules.IsGrandStrategy(
                GrandStrategyArmyMode.GrandStrategy),
                "grand strategy predicate");
            Console.WriteLine("Grand strategy mode tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message +
                $" (expected {expected}, actual {actual})");
    }
}

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
            LedgerConservesManpower();
            ArmyOrganizationConservesManpower();
            Console.WriteLine("Grand strategy mode tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void ArmyOrganizationConservesManpower()
    {
        True(GrandStrategyTroopRules.IsUnlocked(
            GrandStrategyTroopType.Engineers, 3), "engineers unlock");
        False(GrandStrategyTroopRules.IsUnlocked(
            GrandStrategyTroopType.Engineers, 1), "engineers remain gated");
        var ledger = new GrandStrategyKingdomLedger(8, 1200);
        var service = new GrandStrategyArmyService(new GrandStrategyIdAllocator(2));
        var armies = service.RaiseForWar(ledger, warId: 11, manpower: 1000,
            technology: 3, supplyLimit: 400, maximumArmies: 3);
        Equal(3, armies.Count, "raising creates requested army count");
        Equal(1000, Total(armies), "army totals conserve raised manpower");
        Equal(200, ledger.AvailableManpower, "raising leaves ledger remainder");
        var first = armies[0];
        var second = armies[1];
        first.PositionTileId = 42;
        second.PositionTileId = 42;
        True(GrandStrategyArmyRules.CanMerge(first, second), "co-located merge");
        False(GrandStrategyArmyRules.CanMerge(first, armies[2]), "different tile cannot merge");
        var split = service.Split(first, first.TotalStrength / 2);
        Equal(334, first.TotalStrength + split.TotalStrength,
            "split preserves army total");
        True(service.DisbandForWarEnd(armies[2], ledger), "disband succeeds once");
        False(service.DisbandForWarEnd(armies[2], ledger), "duplicate disband rejected");
    }

    private static int Total(IReadOnlyList<GrandStrategyArmy> armies)
    {
        int total = 0;
        for (int i = 0; i < armies.Count; i++) total += armies[i].TotalStrength;
        return total;
    }

    private static void LedgerConservesManpower()
    {
        var ledger = new GrandStrategyKingdomLedger(7, 1000);
        True(GrandStrategyLedgerRules.TryRaise(ledger, 600,
            out string raiseError), raiseError);
        Equal(400, ledger.AvailableManpower, "raise removes available");
        Equal(600, ledger.RaisedManpower, "raise adds raised");
        True(GrandStrategyLedgerRules.ApplyCasualties(ledger, "battle:1:1",
            permanentDeaths: 20, wounded: 30, dispersed: 50, prisoners: 10,
            out string casualtyError), casualtyError);
        Equal(490, ledger.RaisedManpower, "casualties leave raised troops");
        Equal(20, ledger.PermanentDeaths, "deaths recorded");
        Equal(30, ledger.WoundedManpower, "wounded recorded");
        Equal(50, ledger.DispersedManpower, "dispersed recorded");
        Equal(10, ledger.Prisoners, "prisoners recorded");
        Equal(1000, ledger.AccountedManpower, "ledger conserved");
        True(GrandStrategyLedgerRules.ApplyCasualties(ledger, "battle:1:1",
            20, 30, 50, 10, out casualtyError), casualtyError);
        Equal(20, ledger.PermanentDeaths, "duplicate casualty is idempotent");
        Equal(10, ledger.Prisoners, "duplicate prisoners are idempotent");
        Equal(25, GrandStrategyLedgerRules.RecoverWounded(ledger, 25),
            "wounded recovery amount");
        Equal(425, ledger.AvailableManpower, "wounded return to available");
        Equal(25, GrandStrategyLedgerRules.RecoverDispersed(ledger, 25),
            "dispersed recovery amount");
        Equal(450, ledger.AvailableManpower, "dispersed return to available");
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

    private static void False(bool value, string message)
    {
        if (value) throw new InvalidOperationException(message);
    }
}

using AncientWarfare3.core.grandstrategy;
using AncientWarfare3.api.commands;

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
            CommanderSuccessionIsDeterministic();
            MovementBattleAndSiegeRulesAreDeterministic();
            BattleRoundsArePersistentAndIdempotent();
            RuntimeModeIsolationIsExplicit();
            PathAndSiegeServicesCommitOnce();
            CommandsAreAuthorizedAndIdempotent();
            SnapshotRoundTripsAuthoritativeState();
            Console.WriteLine("Grand strategy mode tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void SnapshotRoundTripsAuthoritativeState()
    {
        var ledger = new GrandStrategyKingdomLedger(9, 900);
        GrandStrategyLedgerRules.TryRaise(ledger, 300, out _);
        var army = new GrandStrategyArmy(44, 9, 12,
            GrandStrategyTroopRules.Compose(300, 3)) { PositionTileId = 18 };
        var battle = new GrandStrategyBattleState(55, 12, 44, 45,
            300, 280, 100);
        var snapshot = GrandStrategyPersistence.CreateSnapshot(
            schemaVersion: 1, worldGeneration: 8,
            new[] { ledger }, new[] { army }, new[] { battle },
            committedTransactions: new[] { "battle:55:1" });
        string json = GrandStrategyPersistence.Serialize(snapshot);
        var restored = GrandStrategyPersistence.Deserialize(json,
            expectedWorldGeneration: 8);
        Equal(1, restored.Ledgers.Count, "ledger restored");
        Equal(1, restored.Armies.Count, "army restored");
        Equal(18, restored.Armies[0].PositionTileId,
            "data coordinate restored");
        Equal("battle:55:1", restored.CommittedTransactions[0],
            "transaction key restored");
        bool mismatchThrown = false;
        try { GrandStrategyPersistence.Deserialize(json, 9); }
        catch (InvalidOperationException) { mismatchThrown = true; }
        True(mismatchThrown, "world generation mismatch rejected");
    }

    private static void CommandsAreAuthorizedAndIdempotent()
    {
        var army = new GrandStrategyArmy(88, 7, 9,
            GrandStrategyTroopRules.Compose(200, 2)) { PositionTileId = 5 };
        var paths = new GrandStrategyPathService();
        var service = new GrandStrategyArmyCommandService(paths,
            (start, target) => new[] { start, target });
        var foreign = service.Execute(army, new GrandStrategyArmyCommand(
            armyId: 88, kingdomId: 2, worldGeneration: 1,
            clientSequence: 1, expectedRevision: 0,
            kind: GrandStrategyArmyCommandKind.Move, targetTileId: 10));
        False(foreign.Accepted, "foreign kingdom command rejected");
        var move = service.Execute(army, new GrandStrategyArmyCommand(
            armyId: 88, kingdomId: 7, worldGeneration: 1,
            clientSequence: 2, expectedRevision: 0,
            kind: GrandStrategyArmyCommandKind.Move, targetTileId: 10));
        True(move.Accepted, "authorized move accepted");
        var duplicate = service.Execute(army, new GrandStrategyArmyCommand(
            armyId: 88, kingdomId: 7, worldGeneration: 1,
            clientSequence: 2, expectedRevision: 0,
            kind: GrandStrategyArmyCommandKind.Move, targetTileId: 10));
        True(duplicate.Accepted && duplicate.Duplicate,
            "duplicate sequence is idempotent");
        Equal(1, paths.ActiveRequestCount, "duplicate does not submit route");
    }

    private static void PathAndSiegeServicesCommitOnce()
    {
        var army = new GrandStrategyArmy(1001, 8, 11,
            GrandStrategyTroopRules.Compose(500, 3)) { PositionTileId = 10 };
        var paths = new GrandStrategyPathService();
        True(paths.TrySubmit(army, targetTileId: 20,
            new[] { 10, 11, 15, 20 }, estimatedArrival: 4.5,
            supplyCost: 12), "path accepted");
        False(paths.TrySubmit(army, targetTileId: 30,
            new[] { 10, 30 }, estimatedArrival: 1, supplyCost: 1),
            "second active request rejected");
        Equal(1, paths.ActiveRequestCount, "one route per army");
        True(paths.TryAdvance(army), "route advances");
        Equal(11, army.PositionTileId, "army moves from data coordinate");

        int occupationCommits = 0;
        var sieges = new GrandStrategySiegeService(
            (siegeId, warId, cityId, armyId) =>
            {
                occupationCommits++;
                return true;
            });
        var siege = sieges.Start(5001, warId: 11, cityId: 99,
            armyId: army.Id, defense: 20, maximumProgress: 10);
        var resolved = sieges.ResolveMonthlyRound(siege.SiegeId,
            engineers: 50, equipment: 3, officerSkill: 5,
            manpower: 500, supply: 1, technology: 3,
            assault: true, roll: 10);
        True(resolved.Complete, "assault completes weak siege");
        True(sieges.CommitOccupationOnce(siege.SiegeId),
            "occupation commits");
        True(sieges.CommitOccupationOnce(siege.SiegeId),
            "duplicate occupation is idempotent");
        Equal(1, occupationCommits, "occupation bridge called once");
    }

    private static void RuntimeModeIsolationIsExplicit()
    {
        False(GrandStrategyRuntimeRules.ShouldRun(
            GrandStrategyArmyMode.Vanilla), "vanilla is isolated");
        False(GrandStrategyRuntimeRules.ShouldRun(
            GrandStrategyArmyMode.ArmyRts), "RTS is isolated");
        True(GrandStrategyRuntimeRules.ShouldRun(
            GrandStrategyArmyMode.GrandStrategy), "grand strategy runs");
        True(GrandStrategyRuntimeRules.ShouldRaiseLevies(
            GrandStrategyArmyMode.GrandStrategy, warIsActive: true),
            "active war raises levies");
        False(GrandStrategyRuntimeRules.ShouldRaiseLevies(
            GrandStrategyArmyMode.GrandStrategy, warIsActive: false),
            "peace has no numeric armies");
    }

    private static void BattleRoundsArePersistentAndIdempotent()
    {
        var state = new GrandStrategyBattleState(9, 77, 101, 202,
            attackerStrength: 500, defenderStrength: 480, frontage: 100);
        var service = new GrandStrategyBattleService();
        var first = service.ResolveRound(state, new GrandStrategyBattleRoundInput(
            worldSeed: 3, terrainModifier: 0, attackerTechnology: 2,
            defenderTechnology: 2, attackerTraining: 60,
            defenderTraining: 60, attackerEquipment: 1,
            defenderEquipment: 1, attackerCommanderBonus: 2,
            defenderCommanderBonus: 2, weatherModifier: 0));
        True(first.AttackerLosses > 0 && first.DefenderLosses > 0,
            "round produces numeric losses");
        int attackerAfter = state.AttackerStrength;
        int defenderAfter = state.DefenderStrength;
        var duplicate = service.ResolveRound(state, new GrandStrategyBattleRoundInput(
            worldSeed: 3, terrainModifier: 0, attackerTechnology: 2,
            defenderTechnology: 2, attackerTraining: 60,
            defenderTraining: 60, attackerEquipment: 1,
            defenderEquipment: 1, attackerCommanderBonus: 2,
            defenderCommanderBonus: 2, weatherModifier: 0));
        Equal(attackerAfter, state.AttackerStrength,
            "duplicate round does not repeat attacker loss");
        Equal(defenderAfter, state.DefenderStrength,
            "duplicate round does not repeat defender loss");
        Equal(first.AttackerLosses, duplicate.AttackerLosses,
            "duplicate returns committed result");
        True(service.AddReinforcement(state, 303, 100, isAttacker: true,
            arriveRound: 2), "reinforcement queued");
        Equal(1, state.PendingReinforcements.Count,
            "reinforcement waits for next round");
        service.OrderWithdrawal(state, isAttacker: true);
        Equal(GrandStrategyBattlePhase.Rout, state.Phase,
            "withdrawal enters rout");
        True(service.ResolvePursuit(state), "pursuit completes rout");
        Equal(GrandStrategyBattlePhase.Completed, state.Phase,
            "battle report is completed");
        True(state.Report != null && state.Report.Rounds.Count == 1,
            "report retains committed rounds only");
    }

    private static void MovementBattleAndSiegeRulesAreDeterministic()
    {
        True(GrandStrategyPathRules.IsLowerCost(12, 8, 4),
            "road-adjusted route comparison");
        Equal(GrandStrategyMovementState.Fleet,
            GrandStrategyPathRules.NextMovementState(
                GrandStrategyMovementState.Land, reachedCoast: true,
                validLanding: false), "coast enters fleet projection");
        Equal(4, GrandStrategyBattleRules.Roll(3, 4, 5, 2),
            "battle roll is stable");
        Equal(30, GrandStrategyBattleRules.ResolveFrontline(100, 30),
            "frontage caps committed troops");
        False(GrandStrategyBattleRules.IsRout(0.2, 0.5),
            "morale above rout threshold");
        True(GrandStrategyBattleRules.IsRout(0.0, 0.5),
            "zero morale routes");
        var siege = new GrandStrategySiegeState(1, 10, 25, 100);
        var first = GrandStrategySiegeRules.ResolveRound(siege,
            engineers: 20, equipment: 3, officerSkill: 4,
            manpower: 800, supply: 0.9, technology: 3,
            assault: false, roll: 6);
        True(first.Progress > 0 && first.Defense < 100,
            "steady siege advances");
        var assault = GrandStrategySiegeRules.ResolveRound(siege,
            engineers: 20, equipment: 3, officerSkill: 4,
            manpower: 800, supply: 0.9, technology: 3,
            assault: true, roll: 6);
        True(assault.Progress > first.Progress,
            "assault advances faster");
    }

    private static void CommanderSuccessionIsDeterministic()
    {
        var assignments = new List<GrandStrategyCommanderAssignment>
        {
            new GrandStrategyCommanderAssignment(12,
                GrandStrategyCommanderPosition.Commander, 90, true),
            new GrandStrategyCommanderAssignment(24,
                GrandStrategyCommanderPosition.Vanguard, 70, true),
            new GrandStrategyCommanderAssignment(31,
                GrandStrategyCommanderPosition.RearGuard, 80, true)
        };
        var successor = GrandStrategyCommanderRules.SelectSuccessor(
            assignments, unavailableActorId: 12);
        Equal(24L, successor.ActorId, "vanguard succeeds commander");
        Equal(GrandStrategyCommanderOutcome.Captured,
            GrandStrategyCommanderRules.ResolveRisk(9, routed: true,
                prowess: 2, lossesPercent: .7), "routed commander captured");
        Equal(GrandStrategyCommanderOutcome.Safe,
            GrandStrategyCommanderRules.ResolveRisk(0, routed: false,
                prowess: 10, lossesPercent: .1), "strong commander safe");
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

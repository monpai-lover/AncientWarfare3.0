using ArmyRtsAdversarialSimulation;
using AncientWarfare3.core.lineage;

try
{
    RunnerOptions options = RunnerOptions.Parse(args);
    if (options.All) RunAllScenarios(options);
    else switch (options.Scenario)
    {
        case "war":
            RunWarProbe(options.Seed);
            break;
        case "rally-recruitment":
            RunRallyRecruitmentProbe(options.Seed);
            break;
        case "synthetic-mobilization":
            RunSyntheticMobilizationProbe(options.Seed);
            break;
        case "large-step-equivalence":
            RunLargeStepEquivalenceProbe(options.Seed);
            break;
        case "battle-10x20":
            RunTenCityTwentyArmyProbe(options.Seed);
            break;
        case "transport":
            RunTransportProbe(options.Seed);
            break;
        case "route":
            RunRouteProbe(options.Seed);
            break;
        case "ownership":
            RunOwnershipProbe(options.Seed);
            break;
        case "land":
            RunLandProbe(options.Seed);
            break;
        case "oracle":
            RunOracleProbe();
            break;
        case "continuity":
            RunContinuityProbe(options.Seed);
            break;
        case "":
            RunFoundationProbe();
            break;
        default:
            throw new ArgumentOutOfRangeException("--scenario",
                options.Scenario, "unknown scenario");
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

static void RunAllScenarios(RunnerOptions options)
{
    string[] scenarioFamilies =
        { "land", "ownership", "route", "transport", "war",
          "rally-recruitment" };
    int endSeed = checked(options.FirstSeed + options.Seeds);
    for (int seed = options.FirstSeed; seed < endSeed; seed++)
    {
        foreach (string scenario in scenarioFamilies)
        {
            ScenarioResult result = ScenarioRunner.Run(
                scenario, seed, options.Ticks);
            bool validCompletion = scenario == "ownership"
                ? result.Ticks == options.Ticks
                : result.ValidSettlement;
            Check.True(validCompletion,
                $"{scenario} seed={seed} did not reach its required " +
                "completion boundary\n" + result.FinalStateSummary);
            ValidateAllScenarioResult(scenario, result, options.Ticks);
            Console.WriteLine(
                $"PASS {scenario} seed={seed} ticks={result.Ticks} " +
                $"objectives={result.CompletedObjectives} " +
                $"recoveries={result.RecoveryActions}");
        }
    }
}

static void ValidateAllScenarioResult(string scenario,
    ScenarioResult result, int maxActiveTicks)
{
    switch (scenario)
    {
        case "land":
            Check.Equal(4, result.CompletedObjectives,
                "land campaign resolves all four cities");
            Check.True(result.DistinctTargetAssignments >= 3,
                "land reservations distribute armies across fronts");
            Check.Equal(0, result.RepeatedOccupiedTargets,
                "land armies never reassault a cleared occupied city");
            RequireVisitedStates(result, ArmyRtsState.Rally,
                ArmyRtsState.March, ArmyRtsState.Deploy,
                ArmyRtsState.Assault, ArmyRtsState.Pursue,
                ArmyRtsState.Hold, ArmyRtsState.Replenish);
            break;
        case "ownership":
            Check.Equal(maxActiveTicks, result.Ticks,
                "ownership scenario runs the complete soak");
            Check.Equal(0, result.AcceptedStrategicWrites,
                "RTS actors reject every vanilla strategic write");
            Check.True(result.RejectedStrategicDecisionWrites > 0,
                "strategic Decision interference is injected");
            Check.True(result.RepairedForeignTasks > 0,
                "bounded ownership passes repair foreign tasks");
            Check.True(result.EatingTaskWrites > 0,
                "eating task interference is injected");
            Check.True(result.SocialTaskWrites > 0,
                "social task interference is injected");
            Check.Equal(0, result.ImmediateTaskOverwrites,
                "immediate combat remains engine-owned");
            Check.Equal(0, result.BoatTaskOverwrites,
                "required boat work remains transport-owned");
            Check.True(result.ImmediateTaskWrites > 0,
                "immediate combat yielding is exercised");
            Check.True(result.StaleCombatTaskRepairs > 0,
                "dead combat targets cannot strand an RTS task");
            Check.True(result.BoatTaskWrites > 0,
                "required boat yielding is exercised");
            Check.True(result.TrainingTaskWrites > 0,
                "training interference is injected");
            Check.True(result.RejectedCityTargetWrites > 0,
                "differing city targets are rejected");
            Check.True(result.RejectedCaptainMovementWrites > 0,
                "random captain movement is rejected");
            Check.True(result.RejectedFollowerMovementWrites > 0,
                "vanilla follower movement is rejected");
            Check.Equal(0, result.MovementOwnershipConflicts,
                "strategic movement has one owner per tick");
            Check.Equal(1, result.CaptainReplacements,
                "only an invalid captain is replaced");
            Check.Equal(1, result.RejectedValidCaptainReplacements,
                "a living valid captain replacement is rejected");
            Check.Equal(0, result.EmptyShells,
                "roster churn never creates an empty mission shell");
            break;
        case "route":
            Check.True(result.RecoveryActions >= 3,
                "route failures exercise every recovery rung");
            Check.True(result.RouteRecoverySequence.StartsWith(
                    "RebuildRoute,AlternateEndpoint,ChangeTarget",
                    StringComparison.Ordinal),
                "route recovery escalates in order");
            Check.Equal(0, result.RepeatedRecoveryCycles,
                "route recovery never repeats its full failure cycle");
            RequireVisitedStates(result, ArmyRtsState.Rally,
                ArmyRtsState.March, ArmyRtsState.Deploy,
                ArmyRtsState.Assault);
            break;
        case "transport":
            Check.True(result.TransportBuildAttempts > 0,
                "missing fleet reaches a dock build attempt");
            Check.True(result.CombatShipTransportAssignments > 0,
                "combat ships can accept transport priority");
            Check.Equal(0, result.StrandedMembers,
                "partial trips retain and deliver every member");
            Check.True(result.ReusedBoatTrips >= 2,
                "the bounded fleet serves repeated queue trips");
            Check.Equal(0, result.NavalPreemptions,
                "naval combat cannot preempt assigned transport");
            Check.Equal(0, result.Teleports,
                "transport advances only through bounded movement");
            break;
        case "war":
            Check.Equal(2, result.CompletedObjectives,
                "the multi-goal bundle remains complete");
            Check.Equal(0, result.OffensiveMissionsAfterPeace,
                "settlement prevents post-peace offense");
            Check.Equal(2, result.SettlementAttempts,
                "goal and exhaustion paths each queue peace once");
            break;
        case "rally-recruitment":
            ValidateRallyRecruitmentResult(result);
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(scenario),
                scenario, "unknown all-scenario validation");
    }
}

static void ValidateRallyRecruitmentResult(ScenarioResult result)
{
    Check.Equal(10, result.InitialAttackingStrength,
        "rally stress starts with ten attacking soldiers");
    Check.Equal(10, result.InitialDefendingStrength,
        "rally stress starts against ten defending soldiers");
    Check.True(result.RallyQuorumTick >= 0,
        "the scattered ten-person army reaches the Rally quorum");
    Check.True(result.FirstMarchTick >= result.RallyQuorumTick,
        "the commander does not leave before the Rally quorum");
    Check.Equal(0, result.CommanderDeparturesBeforeRallyQuorum,
        "the commander never departs while the scattered formation is incomplete");
    Check.True(result.RalliedMembersAtFirstMarch >= 8,
        "the commander leaves only after at least eighty percent of the scattered Army reaches him");
    Check.Equal(0, result.UnrejoinedMembersAtFirstAssault,
        "stragglers follow the shared route instead of teleporting or leaving the captain to attack alone");
    Check.Equal(4, result.PartialBattleLosses,
        "the first ten-versus-ten engagement records four losses");
    Check.Equal(result.PartialBattleLosses, result.PartialBattleRecruits,
        "wartime replenishment restores exactly the recorded losses");
    Check.Equal(1, result.ReplacementArmiesCreated,
        "a fully lost army is replaced exactly once during the war");
    Check.Equal(10, result.ReplacementArmyStrength,
        "the replacement army is rebuilt to its requested strength");
    Check.Equal(2, result.CompletedObjectives,
        "the replenished and rebuilt formations finish both objectives");
}

static void RunOwnershipProbe(int seed)
{
    Check.True(ArmyRtsTaskOwnershipRules.ShouldReassertMissionTask(
            ArmyRtsMode.On, pOwnsActor: true, pActorAlive: true,
            pExpectedJobActive: true, pExpectedTaskActive: false,
            pImmediateCombat: false, pRequiredBoatWork: false),
        "foreign task inside the RTS job is reclaimed");
    Check.True(!ArmyRtsTaskOwnershipRules.ShouldReassertMissionTask(
            ArmyRtsMode.On, pOwnsActor: true, pActorAlive: true,
            pExpectedJobActive: true, pExpectedTaskActive: false,
            pImmediateCombat: true, pRequiredBoatWork: false),
        "immediate combat is not overwritten");
    Check.True(!ArmyRtsTaskOwnershipRules.ShouldReassertMissionTask(
            ArmyRtsMode.On, pOwnsActor: true, pActorAlive: true,
            pExpectedJobActive: true, pExpectedTaskActive: false,
            pImmediateCombat: false, pRequiredBoatWork: true),
        "required boat work is not overwritten");
    Check.True(!ArmyRtsTaskOwnershipRules.ShouldReassertMissionTask(
            ArmyRtsMode.Shadow, pOwnsActor: true, pActorAlive: true,
            pExpectedJobActive: true, pExpectedTaskActive: false,
            pImmediateCombat: false, pRequiredBoatWork: false),
        "shadow mode remains vanilla");
    Check.True(ArmyRtsRuntimeModeRules.
            ShouldAllowVanillaDecisionEvaluation(
                ArmyRtsMode.On, rtsOwnsActor: false),
        "actors outside RTS ownership remain vanilla");
    Check.True(ArmyRtsRuntimeModeRules.
            ShouldAllowVanillaDecisionEvaluation(
                ArmyRtsMode.Off, rtsOwnsActor: true),
        "RTS off mode restores vanilla decisions");
    ScenarioResult result = ScenarioRunner.Run(
        "ownership", seed, maxActiveTicks: 10_000);
    Check.Equal(10_000, result.Ticks,
        "non-settlement ownership scenario runs the full soak");
    Check.Equal(0, result.AcceptedStrategicWrites,
        "RTS-owned actors reject strategic foreign decisions");
    Check.True(result.RejectedStrategicDecisionWrites > 0,
        "strategic Decision interference is exercised");
    Check.True(result.RepairedForeignTasks > 0,
        "controller repairs foreign tasks inside the correct RTS job");
    Check.True(result.EatingTaskWrites > 0,
        "eating task interference is exercised");
    Check.True(result.SocialTaskWrites > 0,
        "social task interference is exercised");
    Check.Equal(0, result.ImmediateTaskOverwrites,
        "immediate combat work is not overwritten");
    Check.Equal(0, result.BoatTaskOverwrites,
        "required boat work is not overwritten");
    Check.True(result.ImmediateTaskWrites > 0,
        "immediate combat yielding is exercised");
    Check.True(result.StaleCombatTaskRepairs > 0,
        "a dead target with a residual combat task is reclaimed");
    Check.True(result.BoatTaskWrites > 0,
        "required boat yielding is exercised");
    Check.True(result.TrainingTaskWrites > 0,
        "training task interference is exercised");
    Check.True(result.RejectedCityTargetWrites > 0,
        "a differing vanilla city target is rejected");
    Check.True(result.RejectedCaptainMovementWrites > 0,
        "random vanilla captain movement is rejected");
    Check.True(result.RejectedFollowerMovementWrites > 0,
        "vanilla follower movement is rejected");
    Check.Equal(0, result.MovementOwnershipConflicts,
        "vanilla and RTS never own the same strategic movement tick");
    Check.Equal(1, result.CaptainReplacements,
        "captain changes once and only after invalidation");
    Check.Equal(1, result.RejectedValidCaptainReplacements,
        "a living valid captain replacement is rejected once");
    Check.Equal(0, result.EmptyShells,
        "retirement and replenishment preserve the live army mission");
    Console.WriteLine(
        $"PASS ownership seed={seed} ticks={result.Ticks} " +
        $"repairs={result.RepairedForeignTasks} " +
        $"captain_replacements={result.CaptainReplacements}");
}

static void RunWarProbe(int seed)
{
    ScenarioResult goals = ScenarioRunner.Run(
        "war-goals", seed, maxActiveTicks: 10_000);
    Check.True(goals.ValidSettlement,
        "complete multi-term goals queue peace");
    Check.Equal(0, goals.OffensiveMissionsAfterPeace,
        "goal settlement blocks further conquest assignment");

    ScenarioResult exhaustion = ScenarioRunner.Run(
        "war-exhaustion", seed, maxActiveTicks: 10_000);
    Check.True(exhaustion.ValidSettlement,
        "a war beyond five years settles by authoritative score");
    Check.Equal(1, exhaustion.SettlementAttempts,
        "forced exhaustion settlement is queued once");
    Check.Equal(WarScoreSide.Defenders,
        WarExhaustionSettlementRules.WinnerSide(-35),
        "negative authoritative score selects the defenders");

    ScenarioResult family = ScenarioRunner.Run(
        "war", seed, maxActiveTicks: 10_000);
    Check.True(family.ValidSettlement,
        "the combined war family requires both settlement paths");
    Console.WriteLine(
        $"PASS war seed={seed} ticks={family.Ticks} " +
        $"objectives={family.CompletedObjectives} " +
        $"settlements={family.SettlementAttempts}");
}

static void RunTransportProbe(int seed)
{
    Check.True(ArmyRtsTransportRules.ShouldRetainActiveVoyageMission(
            activeVoyage: true, currentMissionValid: true,
            currentTargetComplete: false, currentTargetCoolingDown: false,
            currentHomelandEmergency: false,
            proposedHomelandEmergency: false),
        "a pending cross-sea transport keeps its target instead of rebuilding requests");
    Check.True(!ArmyRtsTransportRules.ShouldRetainActiveVoyageMission(
            activeVoyage: true, currentMissionValid: true,
            currentTargetComplete: false, currentTargetCoolingDown: false,
            currentHomelandEmergency: false,
            proposedHomelandEmergency: true),
        "an urgent homeland defense may replace a pending transport mission");
    ScenarioResult result = ScenarioRunner.Run(
        "transport", seed, maxActiveTicks: 10_000);
    Check.True(result.ValidSettlement,
        "both transported armies finish their objectives");
    Check.True(result.TransportBuildAttempts > 0,
        "no-boat demand reaches original dock production attempts");
    Check.True(result.CombatShipTransportAssignments > 0,
        "combat ships can serve transport requests");
    Check.Equal(0, result.StrandedMembers,
        "partial trips retain all remaining members in the queue");
    Check.True(result.ReusedBoatTrips >= 2,
        "the bounded fleet is reused instead of growing per request");
    Check.Equal(0, result.NavalPreemptions,
        "naval combat cannot preempt an assigned transport");
    Check.Equal(0, result.Teleports,
        "the modeled expected path never teleports actors or ships");
    Console.WriteLine(
        $"PASS transport seed={seed} ticks={result.Ticks} " +
        $"builds={result.TransportBuildAttempts} " +
        $"reused_trips={result.ReusedBoatTrips}");
}

static void RunRouteProbe(int seed)
{
    ScenarioResult result = ScenarioRunner.Run(
        "route", seed, maxActiveTicks: 10_000);
    Check.True(result.ValidSettlement,
        "a later valid target resumes the war");
    Check.True(result.RecoveryActions >= 3,
        "waiting, no movement, and endpoint failure all recover");
    Check.True(result.RouteRecoverySequence.StartsWith(
            "RebuildRoute,AlternateEndpoint,ChangeTarget",
            StringComparison.Ordinal),
        "recovery cools down an unreachable target without resetting the failure ladder");
    Check.Equal(0, result.RepeatedRecoveryCycles,
        "the route failure ladder cannot cycle forever");
    RequireVisitedStates(result, ArmyRtsState.Rally,
        ArmyRtsState.March, ArmyRtsState.Deploy,
        ArmyRtsState.Assault);
    Console.WriteLine(
        $"PASS route seed={seed} ticks={result.Ticks} " +
        $"sequence={result.RouteRecoverySequence} " +
        $"recoveries={result.RecoveryActions}");
}

static void RunLandProbe(int seed)
{
    ScenarioResult result = ScenarioRunner.Run(
        "land", seed, maxActiveTicks: 10_000);
    Check.True(result.ValidSettlement,
        "land campaign reaches a valid settlement\n" +
        result.FinalStateSummary);
    Check.Equal(4, result.CompletedObjectives,
        "all four target cities are resolved");
    Check.True(result.DistinctTargetAssignments >= 3,
        "target reservations distribute armies across the two fronts");
    Check.Equal(0, result.RepeatedOccupiedTargets,
        "friendly occupied cities without enemies are never assaulted again");
    Check.True(OccupiedCityCivilianProtectionRules.ShouldSuppressActorCombat(
            activeWar: true,
            attackerIsActor: true,
            targetIsActor: true,
            attackerIsWarrior: true,
            targetIsWarrior: false),
        "military actor cannot attack a civilian");
    Check.True(!OccupiedCityCivilianProtectionRules.
            CanActorContributeCapturePoints(
                actorValid: true,
                currentProfessionIsWarrior: false,
                hasValidKingdom: true),
        "civilian cannot add occupation progress");
    Check.True(!ArmyLifecycleRules.CanAssignArmyToAuthorityRole(
            pAssigningArmy: true,
            pIsKing: true,
            pIsLeader: false),
        "a king cannot remain assigned to an army");
    Check.True(OccupiedCityCivilianProtectionRules.
            ShouldSuppressWartimeHostility(
                activeWar: true,
                attackerIsMilitary: true,
                attackerBelongsToCityOwner: false,
                targetBelongsToCityOwner: true,
                targetInsideHomeCity: true,
                targetIsCivilian: false,
                targetIsCivilianBuilding: true),
        "military actor cannot attack a protected civilian building");
    RequireVisitedStates(result,
        ArmyRtsState.Rally,
        ArmyRtsState.March,
        ArmyRtsState.Deploy,
        ArmyRtsState.Assault,
        ArmyRtsState.Pursue,
        ArmyRtsState.Hold,
        ArmyRtsState.Replenish);
    Console.WriteLine(
        $"PASS land seed={seed} ticks={result.Ticks} " +
        $"objectives={result.CompletedObjectives} " +
        $"targets={result.DistinctTargetAssignments} " +
        $"recoveries={result.RecoveryActions}");
}

static void RunRallyRecruitmentProbe(int seed)
{
    ScenarioResult result = ScenarioRunner.Run(
        "rally-recruitment", seed, maxActiveTicks: 10_000);
    Check.True(result.ValidSettlement,
        "the strict Rally and recruitment scenario settles\n" +
        result.FinalStateSummary);
    ValidateRallyRecruitmentResult(result);
    Console.WriteLine(
        $"PASS rally-recruitment seed={seed} ticks={result.Ticks} " +
        $"quorum={result.RallyQuorumTick} march={result.FirstMarchTick} " +
        $"losses={result.PartialBattleLosses} " +
        $"recruits={result.PartialBattleRecruits} " +
        $"replacement={result.ReplacementArmyStrength}");
}

static void RunSyntheticMobilizationProbe(int seed)
{
    SyntheticMobilizationProbeResult result =
        ScenarioFactory.RunSyntheticMobilizationProbe(seed);
    Check.Equal(result.Quota, result.MaximumLive,
        "generated soldiers never exceed the city-war quota");
    Check.Equal(0, result.FinalLive,
        "all generated soldiers are removed after demobilization");
    Check.True(result.RestoredDuringDemobilization,
        "demobilization resumes after a simulated save/load boundary");
    Console.WriteLine(
        $"PASS synthetic-mobilization seed={seed} quota={result.Quota} " +
        $"replacements={result.Replacements} final={result.FinalLive}");
}

static void RunLargeStepEquivalenceProbe(int seed)
{
    SchedulerEquivalenceProbeResult result =
        ScenarioFactory.RunLargeStepEquivalenceProbe(seed);
    Check.Equal(result.NativeLogicalPasses, result.LargeLogicalPasses,
        "native and large-step modes accept the same logical pass count");
    Check.Equal(0, result.DuplicateLargePasses,
        "large-step scheduling never accepts a duplicate logical token");
    Console.WriteLine(
        $"PASS large-step-equivalence seed={seed} " +
        $"passes={result.LargeLogicalPasses}");
}

static void RunTenCityTwentyArmyProbe(int seed)
{
    ScenarioResult result = ScenarioRunner.Run(
        "battle-10x20", seed, maxActiveTicks: 10_000);
    Check.True(result.ValidSettlement,
        "ten-city, twenty-army campaign settles\n" +
        result.FinalStateSummary);
    Check.Equal(10, result.CompletedObjectives,
        "all ten defended cities are resolved");
    Check.Equal(10, result.DistinctTargetAssignments,
        "the campaign assigns every city as an RTS target");
    Check.Equal(20, result.DistinctArmyAssignments,
        "every scattered army receives an RTS target before settlement");
    Check.Equal(0, result.RepeatedOccupiedTargets,
        "no army reassaults an occupied city after its defenders leave");
    RequireVisitedStates(result,
        ArmyRtsState.Rally,
        ArmyRtsState.March,
        ArmyRtsState.Deploy,
        ArmyRtsState.Assault,
        ArmyRtsState.Pursue);
    Console.WriteLine(
        $"PASS battle-10x20 seed={seed} ticks={result.Ticks} " +
        $"objectives={result.CompletedObjectives} " +
        $"targets={result.DistinctTargetAssignments} " +
        $"recoveries={result.RecoveryActions}");
}

static void RunFoundationProbe()
{
    ScenarioState smoke = ScenarioState.CreateSmoke(seed: 17);
    Check.Equal(17, smoke.Seed, "seed is retained");
    for (int i = 0; i < 80; i++) smoke.Trace.Append("change:" + i);
    Check.Equal(64, smoke.Trace.Count, "trace is capped at 64 entries");
    Check.Equal("change:16", smoke.Trace.Entries[0],
        "trace discards the oldest entry first");
    RunContinuityProbe(seed: 17);
    Console.WriteLine("PASS foundation seed=17 trace=64");
}

static void RunContinuityProbe(int seed)
{
    ContinuityAcceptanceResult result =
        ContinuityAcceptanceSuite.Run(seed);
    Check.Equal(10, result.CompletedScenarios,
        "all RTS continuity failure modes complete");
    Check.Equal(80, result.LargeArmiesAdvanced,
        "Large mode advances every army captured at pass entry");
    Check.Equal(0, result.DuplicateAssignments,
        "recovery never duplicates an army assignment");
    Check.True(result.RouteWorkersUsed <= result.RouteWorkerLimit,
        "Large army budgets do not expand low-level route workers");
    Console.WriteLine(
        $"PASS continuity seed={seed} scenarios={result.CompletedScenarios} " +
        $"large_armies={result.LargeArmiesAdvanced} " +
        $"route_workers={result.RouteWorkersUsed}");
}

static void RunOracleProbe()
{
    ScenarioState state = ScenarioFactory.OracleProbe(seed: 17);
    var engine = new SimulationEngine(state);
    engine.Step();
    Check.Equal(
        "events>vanilla>ownership>strategy>movement>watchdog>invariants>trace",
        string.Join(">", engine.LastStageOrder),
        "tick order is authoritative");

    int activeBeforePause = state.ActiveTicks;
    state.Paused = true;
    for (int i = 0; i < 500; i++) engine.Step();
    Check.Equal(activeBeforePause, state.ActiveTicks,
        "paused ticks do not consume progress deadlines");

    SimArmy stalled = state.Armies.Values.Single();
    state.Paused = false;
    stalled.State = ArmyRtsState.March;
    stalled.RouteState = SimRouteState.Ready;
    for (int i = 0; i <= ProgressOracle.MarchDeadlineTicks; i++)
        engine.Step();
    Check.True(stalled.LastRecovery != ArmyStallRecoveryAction.None,
        "deadline expiry triggers recovery on the next controller opportunity");
    Check.True(stalled.Trace.Count <= 64,
        "failure trace remains bounded");
    Console.WriteLine(
        $"PASS oracle seed=17 active={state.ActiveTicks} " +
        $"recovery={stalled.LastRecovery} trace={stalled.Trace.Count}");
}

static void RequireVisitedStates(ScenarioResult result,
    params ArmyRtsState[] required)
{
    HashSet<string> visited = result.VisitedStates.Split(
            new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
        .ToHashSet(StringComparer.Ordinal);
    foreach (ArmyRtsState state in required)
        Check.True(visited.Contains(state.ToString()),
            $"{result.Name} did not visit required state {state}; " +
            $"visited={result.VisitedStates}");
}

internal static class Check
{
    public static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"{message}: expected={expected} actual={actual}");
    }
}

internal sealed class RunnerOptions
{
    public bool All { get; private set; }
    public string Scenario { get; private set; } = "";
    public int Seed { get; private set; }
    public int FirstSeed { get; private set; }
    public int Seeds { get; private set; } = 32;
    public int Ticks { get; private set; } = 10_000;

    public static RunnerOptions Parse(string[] arguments)
    {
        var options = new RunnerOptions();
        for (int i = 0; i < arguments.Length; i++)
        {
            string argument = arguments[i];
            switch (argument)
            {
                case "--all":
                    options.All = true;
                    break;
                case "--scenario":
                    options.Scenario = ReadValue(arguments, ref i,
                        argument);
                    break;
                case "--seed":
                    options.Seed = ReadInt(arguments, ref i, argument);
                    break;
                case "--first-seed":
                    options.FirstSeed = ReadInt(arguments, ref i,
                        argument);
                    break;
                case "--seeds":
                    options.Seeds = ReadInt(arguments, ref i, argument);
                    break;
                case "--ticks":
                    options.Ticks = ReadInt(arguments, ref i, argument);
                    break;
                default:
                    throw new ArgumentException(
                        "unknown argument: " + argument);
            }
        }

        if (arguments.Length > 0 &&
            options.All == !string.IsNullOrEmpty(options.Scenario))
            throw new ArgumentException(
                "select exactly one of --all or --scenario");
        if (options.Seeds < 1)
            throw new ArgumentOutOfRangeException("--seeds");
        if (options.Ticks < 1 || options.All && options.Ticks < 10_000)
            throw new ArgumentOutOfRangeException("--ticks",
                "the deployment gate requires at least 10000 active ticks");
        return options;
    }

    private static string ReadValue(string[] arguments, ref int index,
        string argument)
    {
        if (++index >= arguments.Length)
            throw new ArgumentException("missing value for " + argument);
        return arguments[index];
    }

    private static int ReadInt(string[] arguments, ref int index,
        string argument)
    {
        string value = ReadValue(arguments, ref index, argument);
        if (!int.TryParse(value, out int parsed))
            throw new ArgumentException(
                $"invalid integer for {argument}: {value}");
        return parsed;
    }
}

using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;

namespace ArmyRtsAdversarialSimulation;

internal sealed class SyntheticMobilizationProbeResult
{
    public int Quota { get; init; }
    public int MaximumLive { get; init; }
    public int Replacements { get; init; }
    public int FinalLive { get; init; }
    public bool RestoredDuringDemobilization { get; init; }
}

internal sealed class SchedulerEquivalenceProbeResult
{
    public int NativeLogicalPasses { get; init; }
    public int LargeLogicalPasses { get; init; }
    public int DuplicateLargePasses { get; init; }
}

internal static class ScenarioFactory
{
    public static SyntheticMobilizationProbeResult
        RunSyntheticMobilizationProbe(int seed)
    {
        var random = new Random(seed);
        int quota = SyntheticMobilizationRules.Quota(
            cityPopulation: 600, knownSynthetic: 0, lawPercent: 50);
        int live = 0;
        int initialCreated = 0;
        int maximumLive = 0;
        while (initialCreated < quota)
        {
            int batch = SyntheticMobilizationRules.Batch(
                quota - initialCreated,
                SyntheticMobilizationRules.SpawnBatchLimit);
            initialCreated += batch;
            live += batch;
            maximumLive = Math.Max(maximumLive, live);
        }

        int casualties = random.Next(24, 65);
        live -= casualties;
        int replacementReserve = quota;
        int replacements = SyntheticMobilizationRules.ReplacementDemand(
            quota, live, replacementReserve);
        int replacementCreated = 0;
        while (replacementCreated < replacements)
        {
            int batch = SyntheticMobilizationRules.Batch(
                replacements - replacementCreated,
                SyntheticMobilizationRules.ReplacementBatchLimit);
            replacementCreated += batch;
            replacementReserve -= batch;
            live += batch;
            maximumLive = Math.Max(maximumLive, live);
        }

        // City capture starts demobilization. Persist after the first batch,
        // then resume from the restored live counter.
        int firstRemoval = SyntheticMobilizationRules.Batch(live,
            SyntheticMobilizationRules.DemobilizationBatchLimit);
        live -= firstRemoval;
        int restoredLive = live;
        bool restoredDuringDemobilization = restoredLive > 0;
        while (restoredLive > 0)
            restoredLive -= SyntheticMobilizationRules.Batch(restoredLive,
                SyntheticMobilizationRules.DemobilizationBatchLimit);

        return new SyntheticMobilizationProbeResult
        {
            Quota = quota,
            MaximumLive = maximumLive,
            Replacements = replacementCreated,
            FinalLive = restoredLive,
            RestoredDuringDemobilization = restoredDuringDemobilization
        };
    }

    public static SchedulerEquivalenceProbeResult
        RunLargeStepEquivalenceProbe(int seed)
    {
        int passes = new Random(seed).Next(32, 97);
        var native = new ArmyRtsSchedulingGate();
        native.StartSession(configAw3: false);
        int nativeAccepted = 0;
        for (long token = 1L; token <= passes; token++)
            if (native.TryEnter(ArmyRtsSchedulerOwner.NativeArmyManager,
                    token, allowed: true)) nativeAccepted++;

        var large = new ArmyRtsSchedulingGate();
        large.StartSession(configAw3: true);
        int largeAccepted = 0;
        int duplicateAccepted = 0;
        for (long token = 1L; token <= passes; token++)
        {
            large.TryEnter(ArmyRtsSchedulerOwner.NativeArmyManager,
                token, allowed: true);
            if (large.TryEnter(ArmyRtsSchedulerOwner.Aw3Authority,
                    token, allowed: true)) largeAccepted++;
            if (large.TryEnter(ArmyRtsSchedulerOwner.Aw3Authority,
                    token, allowed: true)) duplicateAccepted++;
        }
        return new SchedulerEquivalenceProbeResult
        {
            NativeLogicalPasses = nativeAccepted,
            LargeLogicalPasses = largeAccepted,
            DuplicateLargePasses = duplicateAccepted
        };
    }

    public static ScenarioState OracleProbe(int seed)
    {
        ScenarioState state = ScenarioState.CreateSmoke(seed);
        state.Armies[1L] = new SimArmy
        {
            Id = 1L,
            KingdomId = 1L,
            WarId = 1L,
            CaptainId = 11L,
            OriginalValidCaptainId = 11L,
            Living = 10,
            Rallied = 10,
            TargetStrength = 10,
            TargetCityId = 2L
        };
        state.Actors[11L] = new SimActor
        {
            Id = 11L,
            ArmyId = 1L,
            KingdomId = 1L,
            Task = SimTaskClass.RtsMission
        };
        state.Cities[2L] = new SimCity
        {
            Id = 2L,
            HomeKingdomId = 2L,
            ControllerKingdomId = 2L,
            Island = 0,
            EnemyMilitaryPresent = true
        };
        return state;
    }

    public static void ApplyEvents(ScenarioState state)
    {
        if (state.War.PeaceQueued && !state.War.Settled &&
            state.ActiveTicks > state.War.PeaceQueuedActiveTick)
        {
            state.War.Settled = true;
            foreach (SimArmy army in state.Armies.Values)
                army.MissionValid = false;
        }
        if (state.Kind == ScenarioKind.OwnershipLifecycle)
            ApplyOwnershipEvents(state);
        else if (state.Kind == ScenarioKind.WarCompletion &&
                 state.Runtime.WarCase == SimWarCase.Exhaustion)
            ApplyWarExhaustionEvents(state);
    }

    public static void ApplyOwnership(ScenarioState state)
    {
        state.LastOwnershipDecision = "none";
        if (state.Kind != ScenarioKind.OwnershipLifecycle) return;
        ApplyTaskOwnership(state);
    }

    public static void ApplyStrategy(ScenarioState state)
    {
        if (state.War.PeaceQueued) return;
        if (IsLandCampaign(state))
            ApplyLandStrategy(state);
        else if (state.Kind == ScenarioKind.RallyRecruitment)
            ApplyRallyRecruitmentStrategy(state);
        else if (state.Kind == ScenarioKind.RouteFailure)
            ApplyRouteStrategy(state);
        else if (state.Kind == ScenarioKind.WarCompletion)
            ApplyWarCompletionStrategy(state);
    }

    public static ScenarioState CreateWarGoalCompletion(int seed)
    {
        ScenarioState state = NewState(seed, ScenarioKind.WarCompletion);
        state.Runtime.WarCase = SimWarCase.Goals;
        state.War.SignedScore = 45;
        state.War.ExpectedGoalCount = 2;
        state.War.Goals.Add(new WarGoalSettlementFacts(
            pAchievedScore: 45,
            pWarGoalId: 501L,
            pRequiredScore: 25,
            pGoalCompleted: true,
            pRequestedGoalTermWarGoalId: 501L));
        state.War.Goals.Add(new WarGoalSettlementFacts(
            pAchievedScore: 45,
            pWarGoalId: 502L,
            pRequiredScore: 20,
            pGoalCompleted: true,
            pRequestedGoalTermWarGoalId: 502L));
        state.Result.CompletedObjectives = 2;
        return state;
    }

    public static ScenarioState CreateWarExhaustion(int seed)
    {
        ScenarioState state = NewState(seed, ScenarioKind.WarCompletion);
        state.Runtime.WarCase = SimWarCase.Exhaustion;
        state.Runtime.AttackerLosses = 400;
        state.Runtime.DefenderLosses = 400;
        state.War.AgeYears = WarScoreRules.LongWarGraceYears;
        state.War.SignedScore = -35;
        return state;
    }

    private static void ApplyWarCompletionStrategy(ScenarioState state)
    {
        if (state.War.PeaceQueued)
        {
            return;
        }
        if (state.Runtime.WarCase != SimWarCase.Goals) return;

        if (!WarGoalSettlementRules.TryValidateForceBundle(
                state.War.SignedScore,
                state.War.Goals,
                state.War.ExpectedGoalCount,
                out string reason))
            throw new InvalidOperationException(
                "completed goal bundle rejected: " + reason);
        state.War.SettlementAttempts++;
        QueuePeace(state);
    }

    private static void ApplyWarExhaustionEvents(ScenarioState state)
    {
        if (state.War.PeaceQueued || state.ActiveTicks == 0 ||
            state.ActiveTicks % 100 != 0) return;

        state.War.AgeYears++;
        state.War.AttackerExhaustion = WarScoreRules.WarExhaustion(
            state.War.AgeYears, state.Runtime.AttackerLosses);
        state.War.DefenderExhaustion = WarScoreRules.WarExhaustion(
            state.War.AgeYears, state.Runtime.DefenderLosses);
        if (state.War.AgeYears <= WarScoreRules.LongWarGraceYears ||
            !WarExhaustionSettlementRules.CanForceSettlement(
                state.War.AttackerExhaustion,
                state.War.DefenderExhaustion)) return;

        WarScoreSide winner = WarExhaustionSettlementRules.WinnerSide(
            state.War.SignedScore);
        if (winner != WarScoreSide.Defenders)
            throw new InvalidOperationException(
                "authoritative negative score selected the wrong winner");
        state.War.SettlementAttempts++;
        QueuePeace(state);
    }

    public static void AdvanceWorld(ScenarioState state)
    {
        if (IsLandCampaign(state))
            AdvanceLandWorld(state);
        else if (state.Kind == ScenarioKind.RallyRecruitment)
            AdvanceRallyRecruitmentWorld(state);
        else if (state.Kind == ScenarioKind.OwnershipLifecycle)
            AdvanceOwnershipWorld(state);
        else if (state.Kind == ScenarioKind.RouteFailure)
            AdvanceRouteWorld(state);
        else if (state.Kind == ScenarioKind.CrossOceanQueue)
            AdvanceTransportWorld(state);
    }

    public static ScenarioState CreateCrossOceanQueue(int seed)
    {
        ScenarioState state = NewState(seed,
            ScenarioKind.CrossOceanQueue);
        AddCity(state, 401L, home: 2L, controller: 2L,
            island: 1, position: 20, frontId: 1,
            enemyMilitary: true, warGoal: true, capital: true);
        AddCity(state, 402L, home: 2L, controller: 2L,
            island: 1, position: 24, frontId: 2,
            enemyMilitary: true, warGoal: true, capital: false);
        AddArmy(state, 41L, captainId: 411L, living: 13,
            rallied: 13, targetStrength: 13, position: 0);
        AddArmy(state, 42L, captainId: 421L, living: 9,
            rallied: 9, targetStrength: 9, position: 0);
        AddTransportRequest(state, state.Armies[41L],
            state.Cities[401L], requestedTick: 0);
        AddTransportRequest(state, state.Armies[42L],
            state.Cities[402L], requestedTick: 1);
        state.Runtime.BuildFailuresRemaining = 1 + seed % 2;
        state.Result.StrandedMembers = 22;
        return state;
    }

    private static void AddTransportRequest(ScenarioState state,
        SimArmy army, SimCity target, int requestedTick)
    {
        army.TargetCityId = target.Id;
        army.RouteDestinationPosition = target.Position;
        army.TransportState = SimTransportState.Requested;
        target.ReservedArmyIds.Add(army.Id);
        state.Runtime.TransportRequests.Add(new SimTransportRequest
        {
            ArmyId = army.Id,
            TargetCityId = target.Id,
            RequestedTick = requestedTick
        });
    }

    private static void AdvanceTransportWorld(ScenarioState state)
    {
        TryProduceTransportBoat(state);
        ReturnIdleBoatsToPickup(state);
        TryAssignTransportBoat(state);
        foreach (SimTransportRequest request in state.Runtime.
                     TransportRequests.OrderBy(item => item.RequestedTick).
                     ThenBy(item => item.ArmyId).ToArray())
            AdvanceTransportRequest(state, request);
        AdvanceTransportAssaults(state);
        state.Result.StrandedMembers = state.Runtime.TransportRequests.Sum(
            request =>
            {
                SimArmy army = state.Armies[request.ArmyId];
                return Math.Max(0,
                    army.Living - request.LandedActorIds.Count);
            });
    }

    private static void ReturnIdleBoatsToPickup(ScenarioState state)
    {
        foreach (SimBoat boat in state.Boats.Values)
        {
            if (boat.ReservedArmyId >= 0L || boat.Position == 0) continue;
            int previous = boat.Position;
            boat.Position -= Math.Min(2, boat.Position);
            if (Math.Abs(boat.Position - previous) > 2)
                state.Result.Teleports++;
            if (boat.Position == 0) boat.Island = 0;
        }
    }

    private static void TryProduceTransportBoat(ScenarioState state)
    {
        if (state.Boats.Count > 0 || state.Runtime.TransportRequests.All(
                request => request.State ==
                           SimTransportState.Completed)) return;
        SimTransportRequest oldest = state.Runtime.TransportRequests
            .Where(request => request.State !=
                              SimTransportState.Completed)
            .OrderBy(request => request.RequestedTick)
            .ThenBy(request => request.ArmyId)
            .First();
        if (oldest.LastBuildAttemptTick >= 0 &&
            state.ActiveTicks - oldest.LastBuildAttemptTick < 3)
        {
            oldest.LastOutcome = "cooldown";
            return;
        }

        oldest.LastBuildAttemptTick = state.ActiveTicks;
        oldest.State = SimTransportState.BuildAttempted;
        oldest.LastOutcome = "build_attempted";
        state.Result.TransportBuildAttempts++;
        if (state.Runtime.BuildFailuresRemaining > 0)
        {
            state.Runtime.BuildFailuresRemaining--;
            oldest.State = SimTransportState.ClassifiedFailure;
            oldest.LastOutcome = "build_failed";
            state.Armies[oldest.ArmyId].Trace.Append(
                $"tick={state.Tick} transport=build_failed");
            return;
        }

        long boatId = state.Runtime.NextBoatId++;
        state.Boats[boatId] = new SimBoat
        {
            Id = boatId,
            CombatShip = true,
            Capacity = 5,
            Island = 0,
            Position = 0
        };
        oldest.State = SimTransportState.Requested;
        oldest.LastOutcome = "build_succeeded";
        state.Armies[oldest.ArmyId].Trace.Append(
            $"tick={state.Tick} transport=build_succeeded boat={boatId}");
    }

    private static void TryAssignTransportBoat(ScenarioState state)
    {
        SimBoat boat = state.Boats.Values
            .Where(item => item.ReservedArmyId < 0L && item.Position == 0)
            .OrderByDescending(item =>
                ArmyRtsTransportRules.BoatTransportPriority(
                    isBoat: true,
                    isDedicatedTransport: !item.CombatShip,
                    skipsFightLogic: false))
            .ThenBy(item => item.Id)
            .FirstOrDefault();
        if (boat == null) return;
        SimTransportRequest request = state.Runtime.TransportRequests
            .Where(item => item.State != SimTransportState.Completed &&
                           item.AssignedBoatId < 0L)
            .OrderBy(item => item.RequestedTick)
            .ThenBy(item => item.ArmyId)
            .FirstOrDefault();
        if (request == null) return;

        SimTransportRequest older = state.Runtime.TransportRequests
            .Where(item => item.State != SimTransportState.Completed &&
                           item.AssignedBoatId < 0L)
            .OrderBy(item => item.RequestedTick)
            .ThenBy(item => item.ArmyId)
            .First();
        if (!ReferenceEquals(request, older))
            throw new InvalidOperationException(
                "transport queue violated oldest-request-first ordering");

        int priority = ArmyRtsTransportRules.BoatTransportPriority(
            isBoat: true,
            isDedicatedTransport: !boat.CombatShip,
            skipsFightLogic: false);
        if (priority == ArmyRtsTransportRules.InvalidBoatPriority)
            throw new InvalidOperationException(
                "a valid combat ship was rejected for transport");
        boat.ReservedArmyId = request.ArmyId;
        request.AssignedBoatId = boat.Id;
        request.AssignedTick = state.ActiveTicks;
        request.State = SimTransportState.Loading;
        SimArmy army = state.Armies[request.ArmyId];
        army.AssignedBoatId = boat.Id;
        army.TransportState = SimTransportState.Loading;
        if (boat.CombatShip)
            state.Result.CombatShipTransportAssignments++;
    }

    private static void AdvanceTransportRequest(ScenarioState state,
        SimTransportRequest request)
    {
        if (request.AssignedBoatId < 0L ||
            !state.Boats.TryGetValue(request.AssignedBoatId,
                out SimBoat boat)) return;
        SimArmy army = state.Armies[request.ArmyId];
        SimCity target = state.Cities[request.TargetCityId];

        bool navalCombatAccepted = boat.ReservedArmyId < 0L ||
            ArmyRtsTransportRules.BoatTransportPriority(
                isBoat: true,
                isDedicatedTransport: !boat.CombatShip,
                skipsFightLogic: false) ==
            ArmyRtsTransportRules.InvalidBoatPriority;
        if (navalCombatAccepted) state.Result.NavalPreemptions++;

        switch (request.State)
        {
            case SimTransportState.Loading:
                LoadTransportTrip(state, army, boat, request);
                break;
            case SimTransportState.Sailing:
                MoveTransportBoat(state, boat, request,
                    target.Position);
                if (boat.Position == target.Position)
                    request.State = SimTransportState.Unloading;
                break;
            case SimTransportState.Unloading:
                UnloadTransportTrip(state, army, boat, request, target);
                break;
            case SimTransportState.Returning:
                MoveTransportBoat(state, boat, request, destination: 0);
                if (boat.Position == 0)
                    request.State = SimTransportState.Loading;
                break;
        }
        army.TransportState = request.State;
    }

    private static void LoadTransportTrip(ScenarioState state,
        SimArmy army, SimBoat boat, SimTransportRequest request)
    {
        if (boat.Position != 0) return;
        request.EmbarkedActorIds.Clear();
        foreach (long actorId in army.Members)
        {
            if (request.EmbarkedActorIds.Count >= boat.Capacity) break;
            if (request.LandedActorIds.Contains(actorId) ||
                !state.Actors.TryGetValue(actorId, out SimActor actor) ||
                !actor.Alive) continue;
            actor.InsideBoat = true;
            actor.Position = boat.Position;
            actor.Task = SimTaskClass.RequiredBoat;
            request.EmbarkedActorIds.Add(actorId);
        }
        if (request.EmbarkedActorIds.Count == 0)
            throw new InvalidOperationException(
                "live transport request had no loadable members");
        request.TripCount++;
        boat.TotalTrips++;
        if (boat.TotalTrips > 1) state.Result.ReusedBoatTrips++;
        army.Embarked = request.EmbarkedActorIds.Count;
        request.State = SimTransportState.Sailing;
        request.LastOutcome = "loading_complete";
    }

    private static void MoveTransportBoat(ScenarioState state,
        SimBoat boat, SimTransportRequest request, int destination)
    {
        int previous = boat.Position;
        int delta = Math.Sign(destination - boat.Position);
        boat.Position += delta * Math.Min(2,
            Math.Abs(destination - boat.Position));
        if (Math.Abs(boat.Position - previous) > 2)
            state.Result.Teleports++;
        foreach (long actorId in request.EmbarkedActorIds)
        {
            SimActor actor = state.Actors[actorId];
            int actorPrevious = actor.Position;
            actor.Position = boat.Position;
            if (Math.Abs(actor.Position - actorPrevious) > 2)
                state.Result.Teleports++;
        }
    }

    private static void UnloadTransportTrip(ScenarioState state,
        SimArmy army, SimBoat boat, SimTransportRequest request,
        SimCity target)
    {
        foreach (long actorId in request.EmbarkedActorIds)
        {
            SimActor actor = state.Actors[actorId];
            if (actor.Position != boat.Position ||
                boat.Position != target.Position)
                state.Result.Teleports++;
            actor.InsideBoat = false;
            actor.Position = target.Position;
            actor.Task = actorId == army.CaptainId
                ? SimTaskClass.RtsMission
                : SimTaskClass.RtsFormation;
            request.LandedActorIds.Add(actorId);
        }
        request.EmbarkedActorIds.Clear();
        army.Embarked = 0;
        army.Landed = request.LandedActorIds.Count;
        if (request.LandedActorIds.Count < army.Living)
        {
            request.State = SimTransportState.Returning;
            request.LastOutcome = "partial_unload_queued";
            return;
        }

        request.State = SimTransportState.Completed;
        request.LastOutcome = "transport_completed";
        request.AssignedBoatId = -1L;
        boat.ReservedArmyId = -1L;
        boat.Island = target.Island;
        army.AssignedBoatId = -1L;
        army.TransportState = SimTransportState.Completed;
        army.Position = target.Position;
        army.RouteState = SimRouteState.Arrived;
        SetState(army, ArmyRtsState.Assault);
    }

    private static void AdvanceTransportAssaults(ScenarioState state)
    {
        foreach (SimTransportRequest request in
                 state.Runtime.TransportRequests)
        {
            if (request.State != SimTransportState.Completed) continue;
            SimArmy army = state.Armies[request.ArmyId];
            if (!state.Cities.TryGetValue(request.TargetCityId,
                    out SimCity target) ||
                target.ControllerKingdomId == army.KingdomId) continue;
            AdvanceAssault(state, army);
        }
    }

    public static ScenarioState CreateRouteFailure(int seed)
    {
        ScenarioState state = NewState(seed, ScenarioKind.RouteFailure);
        AddCity(state, 301L, home: 2L, controller: 2L,
            island: 0, position: 50, frontId: 1,
            enemyMilitary: true, warGoal: false, capital: false);
        AddArmy(state, 31L, captainId: 311L, living: 12,
            rallied: 12, targetStrength: 12,
            position: state.Random.Next(1, 4));
        SimArmy army = state.Armies[31L];
        army.TargetCityId = 301L;
        army.RouteDestinationPosition = 50;
        army.RouteState = SimRouteState.Waiting;
        army.State = ArmyRtsState.March;
        army.Organization = 40;
        state.Cities[301L].ReservedArmyIds.Add(army.Id);
        state.Runtime.WatchdogsByArmy[army.Id] =
            new ArmyStallWatchdogState();
        state.Runtime.RecoveryActionsByArmy[army.Id] =
            new List<ArmyStallRecoveryAction>();
        return state;
    }

    private static void ApplyRouteStrategy(ScenarioState state)
    {
        SimArmy army = state.Armies[31L];
        if (state.Runtime.RoutePhase < 3) return;

        if (army.State == ArmyRtsState.Retreat)
        {
            var retreatFacts = new ArmyRtsTransitionFacts
            {
                CurrentState = ArmyRtsState.Retreat,
                HasMission = true,
                TargetValid = true,
                RetreatArrived = army.RouteState == SimRouteState.Arrived,
                Supply = army.Supply,
                Organization = army.Organization
            };
            SetState(army, ArmyRtsRules.ResolveState(retreatFacts));
            return;
        }

        if (army.State == ArmyRtsState.Regroup)
        {
            var regroupFacts = new ArmyRtsTransitionFacts
            {
                CurrentState = ArmyRtsState.Regroup,
                HasMission = true,
                TargetValid = true,
                RegroupReady = army.Organization >=
                               ArmyRtsRules.RegroupOrganization,
                Supply = army.Supply,
                Organization = army.Organization
            };
            SetState(army, ArmyRtsRules.ResolveState(regroupFacts));
            return;
        }

        if (army.TargetCityId < 0L ||
            !state.Cities.TryGetValue(army.TargetCityId,
                out SimCity target)) return;

        bool deployed = army.State == ArmyRtsState.Assault ||
                        army.State == ArmyRtsState.Deploy &&
                        army.StateTicks >= 2;
        var facts = new ArmyRtsTransitionFacts
        {
            CurrentState = army.State,
            Role = army.Role,
            Posture = army.Posture,
            HasMission = true,
            TargetValid = target.EligibleTarget,
            RallyReady = ArmyRtsRules.HasDeploymentQuorum(
                army.Rallied, army.Living),
            FormationObservationComplete = true,
            RouteArrived = army.RouteState == SimRouteState.Arrived,
            DeploymentReady = deployed,
            EnemyContact = target.EnemyMilitaryPresent,
            ForceReady = ArmyLogisticsRules.HasMinimumOperationalForce(
                army.Living),
            TargetComplete = target.ControllerKingdomId ==
                             army.KingdomId &&
                             !target.EnemyMilitaryPresent,
            Supply = army.Supply,
            Organization = army.Organization
        };
        SetState(army, ArmyRtsRules.ResolveState(facts));
    }

    private static void AdvanceRouteWorld(ScenarioState state)
    {
        SimArmy army = state.Armies[31L];
        ArmyStallWatchdogState watchdog =
            state.Runtime.WatchdogsByArmy[army.Id];
        switch (state.Runtime.RoutePhase)
        {
            case 0:
            {
                ArmyStallRecoveryAction action =
                    ArmyStallWatchdogRules.RecordRouteFailure(watchdog);
                RecordRecovery(state, army, action);
                state.Runtime.RoutePhase = 1;
                army.RouteState = SimRouteState.Failed;
                break;
            }
            case 1:
            {
                ArmyStallRecoveryAction action =
                    ArmyStallWatchdogRules.RecordRouteFailure(watchdog);
                RecordRecovery(state, army, action);
                state.Runtime.RoutePhase = 2;
                army.RouteState = SimRouteState.Failed;
                break;
            }
            case 2:
            {
                ArmyStallRecoveryAction action =
                    ArmyStallWatchdogRules.RecordRouteFailure(watchdog);
                RecordRecovery(state, army, action);
                if (action != ArmyStallRecoveryAction.AlternateEndpoint)
                    break;

                // Local endpoint recovery must not release the mission city.
                army.RouteDestinationPosition =
                    state.Cities[army.TargetCityId].Position;
                army.RouteState = SimRouteState.Ready;
                state.Runtime.RoutePhase = 3;
                break;
            }
            case 3:
                AdvanceRecoveredOffense(state, army);
                break;
            default:
                AdvanceRecoveredOffense(state, army);
                break;
        }
    }

    private static void AdvanceRetreatRoute(ScenarioState state,
        SimArmy army)
    {
        if (army.State != ArmyRtsState.Retreat) return;
        int delta = Math.Sign(army.RouteDestinationPosition - army.Position);
        if (delta == 0)
        {
            army.RouteState = SimRouteState.Arrived;
            return;
        }
        army.Position += delta;
        army.RouteCursor++;
        UpdateActorPositions(state, army);
        if (army.Position == army.RouteDestinationPosition)
            army.RouteState = SimRouteState.Arrived;
    }

    private static void AdvanceRecoveredOffense(ScenarioState state,
        SimArmy army)
    {
        army.StateTicks++;
        switch (army.State)
        {
            case ArmyRtsState.Rally:
                AdvanceRally(army);
                break;
            case ArmyRtsState.March:
                AdvanceMarch(state, army);
                break;
            case ArmyRtsState.Assault:
                AdvanceAssault(state, army);
                break;
        }
    }

    private static void RecordRecovery(ScenarioState state,
        SimArmy army, ArmyStallRecoveryAction action)
    {
        if (action == ArmyStallRecoveryAction.None) return;
        army.LastRecovery = action;
        army.RecoveryCount++;
        List<ArmyStallRecoveryAction> actions =
            state.Runtime.RecoveryActionsByArmy[army.Id];
        actions.Add(action);
        state.Result.RouteRecoverySequence = string.Join(",", actions);
        const string ladder =
            "RebuildRoute,AlternateEndpoint,ChangeTarget";
        string sequence = state.Result.RouteRecoverySequence;
        int first = sequence.IndexOf(ladder, StringComparison.Ordinal);
        if (first >= 0 && sequence.IndexOf(ladder,
                first + ladder.Length,
                StringComparison.Ordinal) >= 0)
            state.Result.RepeatedRecoveryCycles++;
    }

    public static ScenarioState CreateOwnershipLifecycle(int seed)
    {
        ScenarioState state = NewState(seed,
            ScenarioKind.OwnershipLifecycle);
        AddCity(state, 201L, home: 2L, controller: 2L,
            island: 0, position: 10_000, frontId: 1,
            enemyMilitary: true, warGoal: true, capital: true);
        AddCity(state, 202L, home: 2L, controller: 2L,
            island: 0, position: 9_500, frontId: 2,
            enemyMilitary: true, warGoal: false, capital: false);
        AddArmy(state, 21L, captainId: 211L, living: 24,
            rallied: 24, targetStrength: 24,
            position: state.Random.Next(0, 3));
        SimArmy army = state.Armies[21L];
        army.TargetCityId = 201L;
        army.RouteDestinationPosition = 10_000;
        army.RouteState = SimRouteState.Ready;
        army.State = ArmyRtsState.March;
        return state;
    }

    private static void ApplyOwnershipEvents(ScenarioState state)
    {
        SimArmy army = state.Armies[21L];
        foreach (SimActor actor in state.Actors.Values)
        {
            if (actor.TemporaryTaskUntilTick < 0 ||
                state.ActiveTicks <= actor.TemporaryTaskUntilTick) continue;
            if (actor.Task == SimTaskClass.ImmediateCombat)
            {
                if (state.Actors.TryGetValue(actor.AttackTargetId,
                        out SimActor combatTarget))
                    combatTarget.Alive = false;
                actor.InImmediateCombat = false;
                actor.TemporaryTaskUntilTick = -1;
                continue;
            }
            if (actor.Task == SimTaskClass.RequiredBoat)
                actor.InsideBoat = false;
            actor.Task = actor.Id == army.CaptainId
                ? SimTaskClass.RtsMission
                : SimTaskClass.RtsFormation;
            actor.TemporaryTaskUntilTick = -1;
        }

        if (state.ActiveTicks == 500)
        {
            long attemptedReplacement = army.Members
                .First(id => id != army.CaptainId &&
                             state.Actors[id].Alive);
            if (!state.Actors[army.CaptainId].Alive)
                army.CaptainId = attemptedReplacement;
            else
                state.Result.RejectedValidCaptainReplacements++;
        }

        if (state.ActiveTicks == 3_000)
        {
            long retiredId = army.Members.Last(id =>
                id != army.CaptainId && state.Actors[id].Alive);
            state.Actors[retiredId].Alive = false;
            army.Members.Remove(retiredId);
            army.Living--;
            long actorId = state.Runtime.NextActorId++;
            army.Members.Add(actorId);
            state.Actors[actorId] = new SimActor
            {
                Id = actorId,
                ArmyId = army.Id,
                KingdomId = army.KingdomId,
                Position = army.Position,
                RtsJobActive = false,
                Task = SimTaskClass.Social,
                ForeignTaskAssignedTick = state.ActiveTicks
            };
            army.Living++;
            army.TaskRepairCursor = 0;
        }

        if (state.ActiveTicks == 5_000)
        {
            SimActor oldCaptain = state.Actors[army.CaptainId];
            oldCaptain.Alive = false;
            army.Living--;
            long replacementId = army.Members.First(id =>
                id != oldCaptain.Id && state.Actors[id].Alive &&
                !state.Actors[id].King && !state.Actors[id].CityLeader);
            army.CaptainId = replacementId;
            state.Result.CaptainReplacements++;
            army.TaskRepairCursor = 0;
        }

        bool hasLinkedLiveUnit = army.Members.Any(id =>
            state.Actors.TryGetValue(id, out SimActor actor) &&
            actor.Alive && actor.ArmyId == army.Id);
        if (ArmyLifecycleRules.ShouldRemoveEmptyArmy(
                pHasData: true,
                pIsAlive: true,
                pListedUnitCount: army.Living,
                pHasLinkedLiveUnit: hasLinkedLiveUnit,
                pCreationInProgress: false))
            state.Result.EmptyShells++;
    }

    private static void ApplyTaskOwnership(ScenarioState state)
    {
        SimArmy army = state.Armies[21L];
        SimActor captain = state.Actors[army.CaptainId];
        ApplyTaskRepair(state, captain, isCaptain: true);

        if (army.TaskRepairCursor >= army.Members.Count)
            army.TaskRepairCursor = 0;
        int end = Math.Min(army.Members.Count,
            army.TaskRepairCursor + 8);
        for (int i = army.TaskRepairCursor; i < end; i++)
        {
            SimActor actor = state.Actors[army.Members[i]];
            if (!actor.Alive || actor.Id == army.CaptainId) continue;
            ApplyTaskRepair(state, actor, isCaptain: false);
        }
        army.TaskRepairCursor = end;
    }

    private static void ApplyTaskRepair(ScenarioState state,
        SimActor actor, bool isCaptain)
    {
        SimTaskClass expectedTask = isCaptain
            ? SimTaskClass.RtsMission
            : SimTaskClass.RtsFormation;
        bool hasAttackTarget = state.Actors.TryGetValue(
            actor.AttackTargetId, out SimActor attackTarget);
        long targetDelta = hasAttackTarget
            ? (long)attackTarget.Position - actor.Position
            : 0L;
        bool immediateCombatPriority =
            ArmyRtsTaskOwnershipRules.HasImmediateCombatPriority(
                hasAttackTarget,
                targetAlive: hasAttackTarget && attackTarget.Alive,
                targetHostile: hasAttackTarget &&
                    attackTarget.KingdomId != actor.KingdomId,
                targetCombatant: hasAttackTarget && attackTarget.Warrior,
                distanceSquared: (double)targetDelta * targetDelta);
        bool staleCombatTask = actor.Task == SimTaskClass.ImmediateCombat &&
                               !immediateCombatPriority;
        bool shouldRepair = ArmyRtsTaskOwnershipRules.
            ShouldReassertMissionTask(
                ArmyRtsMode.On,
                pOwnsActor: actor.Alive && actor.Warrior &&
                            actor.ArmyId >= 0L,
                pActorAlive: actor.Alive,
                pExpectedJobActive: actor.RtsJobActive,
                pExpectedTaskActive: actor.Task == expectedTask,
                pImmediateCombat: immediateCombatPriority,
                pRequiredBoatWork: actor.InsideBoat);
        if (!shouldRepair) return;
        if (actor.Task == SimTaskClass.ImmediateCombat &&
            immediateCombatPriority)
            state.Result.ImmediateTaskOverwrites++;
        if (staleCombatTask)
            state.Result.StaleCombatTaskRepairs++;
        if (actor.Task == SimTaskClass.RequiredBoat)
            state.Result.BoatTaskOverwrites++;
        actor.RtsJobActive = true;
        actor.Task = expectedTask;
        actor.AttackTargetId = -1L;
        actor.InImmediateCombat = false;
        actor.ForeignTaskAssignedTick = -1;
        state.Result.RepairedForeignTasks++;
    }

    private static void AdvanceOwnershipWorld(ScenarioState state)
    {
        SimArmy army = state.Armies[21L];
        if (state.ActiveTicks % 20 != 0) return;
        state.RtsStrategicMovementCommitted = true;
        army.Position++;
        army.RouteCursor++;
        UpdateActorPositions(state, army);
    }

    public static ScenarioState CreateLandContinuation(int seed)
    {
        ScenarioState state = NewState(seed,
            ScenarioKind.LandContinuation);
        AddCity(state, 101L, home: 2L, controller: 2L,
            island: 0, position: 12, frontId: 1,
            enemyMilitary: true, warGoal: true, capital: true);
        AddCity(state, 102L, home: 2L, controller: 1L,
            island: 0, position: 18, frontId: 1,
            enemyMilitary: false, warGoal: false, capital: false);
        AddCity(state, 103L, home: 2L, controller: 2L,
            island: 0, position: 26, frontId: 2,
            enemyMilitary: true, warGoal: true, capital: false);
        AddCity(state, 104L, home: 2L, controller: 2L,
            island: 0, position: 38, frontId: 2,
            enemyMilitary: true, warGoal: false, capital: false);

        AddArmy(state, 11L, captainId: 111L, living: 20,
            rallied: 15, targetStrength: 20,
            position: state.Random.Next(0, 3));
        AddArmy(state, 12L, captainId: 121L, living: 16,
            rallied: 16, targetStrength: 20,
            position: state.Random.Next(0, 3));
        AddArmy(state, 13L, captainId: 131L, living: 12,
            rallied: 12, targetStrength: 15,
            position: state.Random.Next(0, 3));

        state.Runtime.CompletedCityIds.Add(102L);
        state.Result.CompletedObjectives = 1;
        return state;
    }

    public static ScenarioState CreateTenCityTwentyArmyBattle(int seed)
    {
        ScenarioState state = NewState(seed, ScenarioKind.LargeLandStress);
        for (int index = 0; index < 10; index++)
        {
            long cityId = 1_001L + index;
            AddCity(state, cityId, home: 2L, controller: 2L,
                island: 0, position: 400 + index * 360,
                frontId: index % 3 + 1, enemyMilitary: true,
                warGoal: index % 3 == 0, capital: index == 0);
        }

        for (int index = 0; index < 20; index++)
        {
            int strength = 12 + index % 4;
            int rallied = index % 3 == 0 ? 1 : strength;
            AddArmy(state, 2_001L + index, captainId: 3_001L + index,
                living: strength, rallied: rallied,
                targetStrength: strength,
                position: -1_600 + index * 150);
        }
        return state;
    }

    public static ScenarioState CreateRallyRecruitmentStress(int seed)
    {
        ScenarioState state = NewState(seed, ScenarioKind.RallyRecruitment);
        AddCity(state, 601L, home: 2L, controller: 2L,
            island: 0, position: 1200, frontId: 1,
            enemyMilitary: true, warGoal: true, capital: true);
        AddCity(state, 602L, home: 2L, controller: 2L,
            island: 0, position: 1800, frontId: 1,
            enemyMilitary: true, warGoal: true, capital: false);
        state.Cities[601L].EnemyMilitaryStrength = 10;
        state.Cities[602L].EnemyMilitaryStrength = 10;

        AddArmy(state, 31L, captainId: 311L, living: 10,
            rallied: 1, targetStrength: 10, position: 0);
        SimArmy army = state.Armies[31L];
        ScatterRallyRecruitmentMembers(state, army);
        state.Runtime.RallyRecruitmentOriginalArmyId = army.Id;
        state.Result.InitialAttackingStrength = army.Living;
        state.Result.InitialDefendingStrength =
            state.Cities[601L].EnemyMilitaryStrength;
        return state;
    }

    private static void ApplyRallyRecruitmentStrategy(ScenarioState state)
    {
        EnsureRallyRecruitmentReplacement(state);
        foreach (SimArmy army in state.Armies.Values.OrderBy(pArmy =>
                     pArmy.Id).ToArray())
        {
            if (!army.MissionValid || army.Living <= 0) continue;
            if (army.TargetCityId < 0L)
                AssignRallyRecruitmentTarget(state, army);
            if (!state.Cities.TryGetValue(army.TargetCityId,
                    out SimCity target)) continue;

            int rallied = CountRalliedMembers(state, army);
            army.Rallied = rallied;
            bool captainPresent = state.Actors.TryGetValue(army.CaptainId,
                out SimActor captain) && captain.Alive;
            int ralliedFollowers = Math.Max(0, rallied -
                (captainPresent ? 1 : 0));
            bool formationRallyReady = ArmyRtsRules.
                HasIncrementalRallyReadiness(
                    departureStrengthReady: true,
                    rosterLiving: army.Living,
                    ralliedFollowers: ralliedFollowers,
                    captainPresent: captainPresent);
            if (formationRallyReady && state.Result.RallyQuorumTick < 0)
                state.Result.RallyQuorumTick = state.ActiveTicks;
            bool needsReplenishment = ArmyRtsRules.NeedsReplenishment(
                army.Living, army.TargetStrength) ||
                ArmyRtsRules.ShouldContinueRequestedReplenishment(
                    army.ReplenishmentRequested, army.Living,
                    army.TargetStrength);
            bool targetComplete = target.ControllerKingdomId ==
                                  army.KingdomId &&
                                  !target.EnemyMilitaryPresent;
            bool deploymentReady = army.State == ArmyRtsState.Deploy &&
                                   army.StateTicks >= 1 &&
                                   ArmyRtsRules.
                                   HasIncrementalEscortQuorum(
                                       rosterLiving: army.Living,
                                       ralliedFollowers:
                                           ralliedFollowers,
                                       captainPresent: captainPresent);
            var facts = new ArmyRtsTransitionFacts
            {
                CurrentState = army.State,
                Role = army.Role,
                Posture = army.Posture,
                HasMission = true,
                TargetValid = true,
                FormationObservationComplete = true,
                RallyReady = formationRallyReady,
                RouteArrived = army.RouteState == SimRouteState.Arrived,
                DeploymentReady = deploymentReady,
                EnemyContact = target.EnemyMilitaryPresent,
                MinimumForceReady = ArmyLogisticsRules.
                    HasMinimumOperationalForce(army.Living),
                ForceReady = ArmyLogisticsRules.
                    HasMinimumOperationalForce(army.Living),
                NeedsReplenishment = needsReplenishment,
                WartimeRecovery = army.ReplenishmentRequested,
                TargetComplete = targetComplete,
                Supply = army.Supply,
                Organization = army.Organization
            };
            ArmyRtsState next = ArmyRtsRules.ResolveState(facts);
            bool leavesAssembly = next == ArmyRtsState.March ||
                                  next == ArmyRtsState.Deploy ||
                                  next == ArmyRtsState.Assault;
            if (leavesAssembly && !formationRallyReady)
                state.Result.CommanderDeparturesBeforeRallyQuorum++;
            if (next == ArmyRtsState.March &&
                state.Result.FirstMarchTick < 0)
            {
                state.Result.FirstMarchTick = state.ActiveTicks;
                state.Result.RalliedMembersAtFirstMarch = rallied;
            }
            if (army.State == ArmyRtsState.Replenish &&
                next != ArmyRtsState.Replenish &&
                army.Living >= army.TargetStrength)
                army.ReplenishmentRequested = false;
            SetState(army, next);
            if (next == ArmyRtsState.Idle && targetComplete)
                CompleteLandTarget(state, army, target);
        }
    }

    private static void EnsureRallyRecruitmentReplacement(
        ScenarioState state)
    {
        if (!state.Runtime.RallyRecruitmentFullWipeInjected ||
            state.Runtime.RallyRecruitmentReplacementCreated) return;
        int operationalArmies = state.Armies.Values.Count(army =>
            army.MissionValid && ArmyLogisticsRules.
                HasMinimumOperationalForce(army.Living));
        if (!TemporaryLevyRules.ShouldRequestZeroArmyRecovery(
                emergencyActive: true, usableFieldArmies: operationalArmies,
                recoveryPending: false))
            throw new InvalidOperationException(
                "zero-army wartime recovery was not requested");

        AddArmy(state, 32L, captainId: 321L, living: 10,
            rallied: 1, targetStrength: 10, position: 0);
        SimArmy replacement = state.Armies[32L];
        ScatterRallyRecruitmentMembers(state, replacement);
        state.Runtime.RallyRecruitmentReplacementCreated = true;
        state.Runtime.RallyRecruitmentReplacementArmyId = replacement.Id;
        state.Result.ReplacementArmiesCreated++;
        state.Result.ReplacementArmyStrength = replacement.Living;
    }

    private static void AssignRallyRecruitmentTarget(ScenarioState state,
        SimArmy army)
    {
        SimCity target = state.Cities.Values
            .Where(city => city.HomeKingdomId ==
                           state.War.DefenderKingdomId)
            .Where(city => city.EligibleTarget)
            .Where(city => city.ControllerKingdomId != army.KingdomId ||
                           city.EnemyMilitaryPresent)
            .OrderBy(city => city.Position)
            .ThenBy(city => city.Id)
            .FirstOrDefault();
        if (target == null) return;
        target.ReservedArmyIds.Add(army.Id);
        army.TargetCityId = target.Id;
        army.RouteDestinationPosition = target.Position;
        army.RouteState = SimRouteState.None;
        army.PursuitCompleted = false;
        army.StateTicks = 0;
    }

    private static void AdvanceRallyRecruitmentWorld(ScenarioState state)
    {
        foreach (SimArmy army in state.Armies.Values.OrderBy(pArmy =>
                     pArmy.Id).ToArray())
        {
            if (!army.MissionValid || army.Living <= 0) continue;
            army.StateTicks++;
            switch (army.State)
            {
                case ArmyRtsState.Rally:
                    AdvanceRallyRecruitmentRally(state, army);
                    break;
                case ArmyRtsState.Replenish:
                    AdvanceRallyRecruitmentReplenishment(state, army);
                    break;
                case ArmyRtsState.March:
                    AdvanceRallyRecruitmentMarch(state, army);
                    break;
                case ArmyRtsState.Assault:
                    AdvanceRallyRecruitmentAssault(state, army);
                    break;
            }
        }
    }

    private static void AdvanceRallyRecruitmentRally(ScenarioState state,
        SimArmy army)
    {
        if (!state.Actors.TryGetValue(army.CaptainId,
                out SimActor captain) || !captain.Alive) return;
        army.Position = captain.Position;
        foreach (long actorId in army.Members)
        {
            if (actorId == army.CaptainId ||
                !state.Actors.TryGetValue(actorId, out SimActor actor) ||
                !actor.Alive) continue;
            int distance = Math.Abs(captain.Position - actor.Position);
            if (distance <= 12) continue;
            actor.Position += Math.Sign(captain.Position - actor.Position) *
                              Math.Min(48, distance);
        }
        army.Rallied = CountRalliedMembers(state, army);
    }

    private static void AdvanceRallyRecruitmentReplenishment(
        ScenarioState state, SimArmy army)
    {
        if (army.StateTicks % 2 != 0 ||
            army.Living >= army.TargetStrength) return;
        long actorId = state.Runtime.NextActorId++;
        army.Members.Add(actorId);
        army.Living++;
        state.Result.PartialBattleRecruits++;
        state.Actors[actorId] = new SimActor
        {
            Id = actorId,
            ArmyId = army.Id,
            KingdomId = army.KingdomId,
            Position = 0,
            Task = SimTaskClass.RtsFormation
        };
    }

    private static void AdvanceRallyRecruitmentMarch(ScenarioState state,
        SimArmy army)
    {
        if (!state.Actors.TryGetValue(army.CaptainId,
                out SimActor captain) || !captain.Alive) return;
        if (army.RouteState == SimRouteState.None)
            army.RouteState = SimRouteState.Ready;
        if (army.RouteState == SimRouteState.Ready)
        {
            int delta = Math.Sign(army.RouteDestinationPosition -
                                 army.Position);
            if (delta == 0)
                army.RouteState = SimRouteState.Arrived;
            else
            {
                army.Position += delta * Math.Min(2,
                    Math.Abs(army.RouteDestinationPosition -
                             army.Position));
                army.RouteCursor++;
            }
            captain.Position = army.Position;
        }
        foreach (long actorId in army.Members)
        {
            if (actorId == army.CaptainId ||
                !state.Actors.TryGetValue(actorId, out SimActor actor) ||
                !actor.Alive) continue;
            int distance = Math.Abs(captain.Position - actor.Position);
            if (distance == 0) continue;
            int speed = distance > 12 ? 4 : 2;
            actor.Position += Math.Sign(captain.Position - actor.Position) *
                              Math.Min(speed, distance);
        }
        if (army.Position == army.RouteDestinationPosition)
            army.RouteState = SimRouteState.Arrived;
    }

    private static void AdvanceRallyRecruitmentAssault(ScenarioState state,
        SimArmy army)
    {
        if (!state.Cities.TryGetValue(army.TargetCityId,
                out SimCity target) || !target.EnemyMilitaryPresent) return;
        if (!state.Runtime.RallyRecruitmentFirstAssaultObserved)
        {
            state.Runtime.RallyRecruitmentFirstAssaultObserved = true;
            state.Result.UnrejoinedMembersAtFirstAssault =
                Math.Max(0, army.Living -
                    CountRalliedMembers(state, army));
        }
        if (target.Id == 601L &&
            !state.Runtime.RallyRecruitmentPartialLossInjected)
        {
            const int losses = 4;
            state.Runtime.RallyRecruitmentPartialLossInjected = true;
            state.Result.PartialBattleLosses = losses;
            target.EnemyMilitaryStrength -= losses;
            KillRallyRecruitmentMembers(state, army, losses);
            army.ReplenishmentRequested = true;
            return;
        }
        if (target.Id == 602L &&
            !state.Runtime.RallyRecruitmentFullWipeInjected)
        {
            state.Runtime.RallyRecruitmentFullWipeInjected = true;
            target.ReservedArmyIds.Remove(army.Id);
            KillRallyRecruitmentMembers(state, army, army.Living);
            army.TargetCityId = -1L;
            army.RouteState = SimRouteState.None;
            army.MissionValid = false;
            return;
        }

        target.EnemyMilitaryStrength = 0;
        target.Occupation = 100;
        target.ControllerKingdomId = army.KingdomId;
        target.EnemyMilitaryPresent = false;
        CompleteLandTarget(state, army, target);
    }

    private static void KillRallyRecruitmentMembers(ScenarioState state,
        SimArmy army, int losses)
    {
        int remaining = Math.Max(0, losses);
        for (int index = army.Members.Count - 1; index >= 0 &&
             remaining > 0; index--)
        {
            long actorId = army.Members[index];
            army.Members.RemoveAt(index);
            if (state.Actors.TryGetValue(actorId, out SimActor actor))
                actor.Alive = false;
            remaining--;
        }
        army.Living = army.Members.Count;
        army.Rallied = CountRalliedMembers(state, army);
    }

    private static int CountRalliedMembers(ScenarioState state,
        SimArmy army)
    {
        if (!state.Actors.TryGetValue(army.CaptainId,
                out SimActor captain) || !captain.Alive) return 0;
        return army.Members.Count(actorId =>
            state.Actors.TryGetValue(actorId, out SimActor actor) &&
            actor.Alive && Math.Abs(actor.Position - captain.Position) <= 12);
    }

    private static void ScatterRallyRecruitmentMembers(ScenarioState state,
        SimArmy army)
    {
        int[] positions = { 0, -960, 900, -840, 780, -660, 600, -480,
                            420, -360 };
        for (int index = 0; index < army.Members.Count; index++)
        {
            long actorId = army.Members[index];
            if (state.Actors.TryGetValue(actorId, out SimActor actor))
                actor.Position = positions[index % positions.Length];
        }
        army.Position = positions[0];
        army.Rallied = CountRalliedMembers(state, army);
    }

    private static void ApplyLandStrategy(ScenarioState state)
    {
        foreach (SimArmy army in state.Armies.Values.OrderBy(pArmy =>
                     pArmy.Id))
        {
            if (!army.MissionValid) continue;
            if (army.TargetCityId < 0L) AssignLandTarget(state, army);
            if (army.TargetCityId < 0L ||
                !state.Cities.TryGetValue(army.TargetCityId,
                    out SimCity target))
                continue;

            bool complete = target.ControllerKingdomId == army.KingdomId &&
                            target.Occupation >= 100;
            bool pursuitAllowed = ArmyRtsRules.
                ShouldPursueCompletedTarget(
                    complete,
                    army.PursuitCompleted,
                    army.Role == ArmyRtsRole.Assault,
                    army.Supply > ArmyRtsRules.CriticalSupply,
                    army.Position == target.Position,
                    target.EnemyMilitaryPresent);
            bool deployed = army.State == ArmyRtsState.Assault ||
                            army.State == ArmyRtsState.Hold ||
                            army.State == ArmyRtsState.Pursue ||
                            army.State == ArmyRtsState.Deploy &&
                            army.StateTicks >= 2;
            var facts = new ArmyRtsTransitionFacts
            {
                CurrentState = army.State,
                Role = army.Role,
                Posture = army.Posture,
                HasMission = army.MissionValid,
                TargetValid = true,
                FormationObservationComplete = true,
                RallyReady = ArmyRtsRules.HasDeploymentQuorum(
                    army.Rallied, army.Living),
                RouteArrived = army.RouteState == SimRouteState.Arrived,
                DeploymentReady = deployed,
                EnemyContact = target.EnemyMilitaryPresent,
                ForceReady = ArmyLogisticsRules.HasMinimumOperationalForce(
                    army.Living),
                NeedsReplenishment = ArmyRtsRules.NeedsReplenishment(
                    army.Living, army.TargetStrength) ||
                    ArmyRtsRules.ShouldContinueRequestedReplenishment(
                        army.ReplenishmentRequested, army.Living,
                        army.TargetStrength),
                WartimeRecovery = army.ReplenishmentRequested,
                TargetComplete = complete,
                HoldRequired = complete && !target.EnemyMilitaryPresent,
                PursuitAllowed = pursuitAllowed,
                PursuitComplete = army.PursuitCompleted,
                Supply = army.Supply,
                Organization = army.Organization
            };
            ArmyRtsState next = ArmyRtsRules.ResolveState(facts);
            if (army.State == ArmyRtsState.Replenish &&
                next != ArmyRtsState.Replenish &&
                army.Living >= army.TargetStrength)
                army.ReplenishmentRequested = false;
            SetState(army, next);
            if (next == ArmyRtsState.Idle && complete &&
                !target.EnemyMilitaryPresent)
                CompleteLandTarget(state, army, target);
        }
    }

    private static void AssignLandTarget(ScenarioState state,
        SimArmy army)
    {
        SimCity target = state.Cities.Values
            .Where(city => city.HomeKingdomId ==
                           state.War.DefenderKingdomId)
            .Where(city => city.EligibleTarget)
            .Where(city => city.ControllerKingdomId != army.KingdomId ||
                           city.EnemyMilitaryPresent)
            .Where(city => city.ReservedArmyIds.Count <
                           ArmyRtsRules.AssaultReservationCap(
                               city.Capital, city.WarGoal))
            .OrderBy(city => city.ReservedArmyIds.Count)
            .ThenByDescending(city => city.WarGoal || city.Capital)
            .ThenBy(city => Math.Abs(city.Position - army.Position))
            .ThenBy(city => city.Id)
            .FirstOrDefault();
        if (target == null) return;

        if (target.ControllerKingdomId == army.KingdomId &&
            !target.EnemyMilitaryPresent)
        {
            state.Result.RepeatedOccupiedTargets++;
            return;
        }

        target.ReservedArmyIds.Add(army.Id);
        state.Runtime.AssignedArmyIds.Add(army.Id);
        state.Result.DistinctArmyAssignments =
            state.Runtime.AssignedArmyIds.Count;
        army.TargetCityId = target.Id;
        army.RouteDestinationPosition = target.Position;
        army.RouteState = SimRouteState.None;
        army.StateTicks = 0;
        army.PursuitCompleted = false;
        state.Runtime.DistinctTargetIds.Add(target.Id);
        state.Result.DistinctTargetAssignments =
            state.Runtime.DistinctTargetIds.Count;
    }

    private static void AdvanceLandWorld(ScenarioState state)
    {
        foreach (SimArmy army in state.Armies.Values.OrderBy(pArmy =>
                     pArmy.Id))
        {
            army.StateTicks++;
            switch (army.State)
            {
                case ArmyRtsState.Rally:
                    AdvanceRally(army);
                    break;
                case ArmyRtsState.Replenish:
                    AdvanceReplenishment(state, army);
                    break;
                case ArmyRtsState.March:
                    AdvanceMarch(state, army);
                    break;
                case ArmyRtsState.Assault:
                    AdvanceAssault(state, army);
                    break;
                case ArmyRtsState.Pursue:
                    AdvancePursuit(state, army);
                    break;
                case ArmyRtsState.Hold:
                    AdvanceHold(state, army);
                    break;
            }
        }
    }

    private static void AdvanceRally(SimArmy army)
    {
        if (ArmyRtsRules.HasDeploymentQuorum(army.Rallied, army.Living))
            return;
        if (army.StateTicks % 2 == 0)
            army.Rallied = Math.Min(army.Living, army.Rallied + 1);
    }

    private static void AdvanceReplenishment(ScenarioState state,
        SimArmy army)
    {
        if (army.StateTicks % 3 != 0) return;
        if (army.Living < army.TargetStrength)
        {
            long actorId = state.Runtime.NextActorId++;
            army.Living++;
            army.Rallied++;
            army.Members.Add(actorId);
            state.Actors[actorId] = new SimActor
            {
                Id = actorId,
                ArmyId = army.Id,
                KingdomId = army.KingdomId,
                Position = army.Position,
                Task = SimTaskClass.RtsFormation
            };
        }
    }

    private static void AdvanceMarch(ScenarioState state, SimArmy army)
    {
        if (army.RouteState == SimRouteState.None)
            army.RouteState = SimRouteState.Ready;
        if (army.RouteState != SimRouteState.Ready) return;

        int delta = Math.Sign(army.RouteDestinationPosition - army.Position);
        if (delta == 0)
        {
            army.RouteState = SimRouteState.Arrived;
            return;
        }
        army.Position += delta * Math.Min(2,
            Math.Abs(army.RouteDestinationPosition - army.Position));
        army.RouteCursor++;
        UpdateActorPositions(state, army);
        if (army.Position == army.RouteDestinationPosition)
            army.RouteState = SimRouteState.Arrived;
    }

    private static void AdvanceAssault(ScenarioState state, SimArmy army)
    {
        if (!state.Cities.TryGetValue(army.TargetCityId,
                out SimCity target)) return;
        if (target.ControllerKingdomId == army.KingdomId &&
            !target.EnemyMilitaryPresent)
        {
            state.Result.RepeatedOccupiedTargets++;
            throw new InvalidOperationException(
                "assault attempted against completed occupied city");
        }
        if (!target.EnemyMilitaryPresent) return;

        if (!OccupiedCityCivilianProtectionRules.
                ShouldSuppressActorCombat(
                    activeWar: true,
                    attackerIsActor: true,
                    targetIsActor: true,
                    attackerIsWarrior: true,
                    targetIsWarrior: false))
            throw new InvalidOperationException(
                "production rules did not protect a civilian target");
        target.Occupation = Math.Min(100, target.Occupation + 10);
        if (state.Kind == ScenarioKind.LandContinuation &&
            !state.Runtime.CasualtiesInjected && target.Occupation >= 90)
            InjectLandCasualties(state, army);
        if (target.Occupation < 100) return;
        if (IsLandCampaign(state))
        {
            target.ControllerKingdomId = army.KingdomId;
            return;
        }
        target.EnemyMilitaryPresent = false;
        CompleteLandTarget(state, army, target);
    }

    private static void AdvancePursuit(ScenarioState state, SimArmy army)
    {
        if (army.StateTicks < 2 ||
            !state.Cities.TryGetValue(army.TargetCityId,
                out SimCity target)) return;
        target.EnemyMilitaryPresent = false;
        army.PursuitCompleted = true;
    }

    private static void AdvanceHold(ScenarioState state, SimArmy army)
    {
        if (army.StateTicks < 2 ||
            !state.Cities.TryGetValue(army.TargetCityId,
                out SimCity target) || target.EnemyMilitaryPresent) return;
        CompleteLandTarget(state, army, target);
    }

    private static void CompleteLandTarget(ScenarioState state,
        SimArmy army, SimCity target)
    {
        target.ControllerKingdomId = army.KingdomId;
        target.EnemyMilitaryPresent = false;
        foreach (long reservedArmyId in target.ReservedArmyIds.ToArray())
        {
            if (state.Armies.TryGetValue(reservedArmyId,
                    out SimArmy reservedArmy) &&
                reservedArmy.TargetCityId == target.Id)
            {
                reservedArmy.TargetCityId = -1L;
                reservedArmy.RouteState = SimRouteState.None;
                reservedArmy.PursuitCompleted = false;
                SetState(reservedArmy, ArmyRtsState.Idle);
            }
        }
        target.ReservedArmyIds.Clear();

        if (state.Runtime.CompletedCityIds.Add(target.Id))
        {
            state.Result.CompletedObjectives =
                state.Runtime.CompletedCityIds.Count;
        }

        if (state.Cities.Values.All(city =>
                city.HomeKingdomId != state.War.DefenderKingdomId ||
                !city.EligibleTarget ||
                city.ControllerKingdomId == state.War.AttackerKingdomId &&
                !city.EnemyMilitaryPresent))
            QueuePeace(state);
    }

    private static void InjectLandCasualties(ScenarioState state,
        SimArmy army)
    {
        if (state.Runtime.CasualtiesInjected) return;
        state.Runtime.CasualtiesInjected = true;
        int survivors = Math.Max(
            ArmyLogisticsRules.MinimumOperationalForce,
            army.TargetStrength * 3 / 5);
        while (army.Members.Count > survivors)
        {
            long actorId = army.Members[^1];
            army.Members.RemoveAt(army.Members.Count - 1);
            state.Actors[actorId].Alive = false;
        }
        army.Living = survivors;
        army.Rallied = Math.Min(army.Rallied, survivors);
        army.ReplenishmentRequested = true;
    }

    private static void QueuePeace(ScenarioState state)
    {
        if (state.War.PeaceQueued) return;
        state.War.PeaceQueued = true;
        state.War.PeaceQueuedActiveTick = state.ActiveTicks;
        foreach (SimArmy army in state.Armies.Values)
            army.SettlementPending = true;
    }

    private static void SetState(SimArmy army, ArmyRtsState state)
    {
        if (army.State == state) return;
        army.State = state;
        army.StateTicks = 0;
    }

    private static void UpdateActorPositions(ScenarioState state,
        SimArmy army)
    {
        foreach (long actorId in army.Members)
            if (state.Actors.TryGetValue(actorId, out SimActor actor) &&
                actor.Alive)
                actor.Position = army.Position;
    }

    private static ScenarioState NewState(int seed, ScenarioKind kind)
    {
        return new ScenarioState
        {
            Seed = seed,
            Kind = kind,
            Random = new Random(seed),
            War = new SimWar()
        };
    }

    private static bool IsLandCampaign(ScenarioState pState)
    {
        return pState?.Kind == ScenarioKind.LandContinuation ||
               pState?.Kind == ScenarioKind.LargeLandStress;
    }

    private static void AddCity(ScenarioState state, long id, long home,
        long controller, int island, int position, int frontId,
        bool enemyMilitary, bool warGoal, bool capital)
    {
        state.Cities[id] = new SimCity
        {
            Id = id,
            HomeKingdomId = home,
            ControllerKingdomId = controller,
            Island = island,
            Position = position,
            FrontId = frontId,
            EnemyMilitaryPresent = enemyMilitary,
            WarGoal = warGoal,
            Capital = capital
        };
    }

    private static void AddArmy(ScenarioState state, long id,
        long captainId, int living, int rallied, int targetStrength,
        int position)
    {
        var army = new SimArmy
        {
            Id = id,
            KingdomId = state.War.AttackerKingdomId,
            WarId = state.War.Id,
            CaptainId = captainId,
            OriginalValidCaptainId = captainId,
            Living = living,
            Rallied = Math.Min(living, rallied),
            TargetStrength = targetStrength,
            Position = position
        };
        state.Armies[id] = army;
        for (int i = 0; i < living; i++)
        {
            long actorId = i == 0 ? captainId : state.Runtime.NextActorId++;
            army.Members.Add(actorId);
            state.Actors[actorId] = new SimActor
            {
                Id = actorId,
                ArmyId = id,
                KingdomId = army.KingdomId,
                Position = position,
                Task = i == 0
                    ? SimTaskClass.RtsMission
                    : SimTaskClass.RtsFormation
            };
        }
    }
}

internal static class ScenarioRunner
{
    public static ScenarioResult Run(string scenario, int seed,
        int maxActiveTicks)
    {
        if (scenario == "war")
        {
            ScenarioResult goals = Run("war-goals", seed,
                maxActiveTicks);
            ScenarioResult exhaustion = Run("war-exhaustion", seed,
                maxActiveTicks);
            return new ScenarioResult
            {
                Name = "war",
                Seed = seed,
                Ticks = goals.Ticks + exhaustion.Ticks,
                ValidSettlement = goals.ValidSettlement &&
                                  exhaustion.ValidSettlement,
                CompletedObjectives = goals.CompletedObjectives +
                                      exhaustion.CompletedObjectives,
                RecoveryActions = goals.RecoveryActions +
                                  exhaustion.RecoveryActions,
                OffensiveMissionsAfterPeace =
                    goals.OffensiveMissionsAfterPeace +
                    exhaustion.OffensiveMissionsAfterPeace,
                SettlementAttempts = goals.SettlementAttempts +
                                     exhaustion.SettlementAttempts
            };
        }
        ScenarioState state = scenario switch
        {
            "land" => ScenarioFactory.CreateLandContinuation(seed),
            "battle-10x20" =>
                ScenarioFactory.CreateTenCityTwentyArmyBattle(seed),
            "ownership" => ScenarioFactory.CreateOwnershipLifecycle(seed),
            "route" => ScenarioFactory.CreateRouteFailure(seed),
            "transport" => ScenarioFactory.CreateCrossOceanQueue(seed),
            "rally-recruitment" =>
                ScenarioFactory.CreateRallyRecruitmentStress(seed),
            "war-goals" => ScenarioFactory.CreateWarGoalCompletion(seed),
            "war-exhaustion" =>
                ScenarioFactory.CreateWarExhaustion(seed),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario),
                scenario, "unknown adversarial scenario")
        };
        state.Result.Name = scenario;
        state.Result.Seed = seed;
        var engine = new SimulationEngine(state);
        while (!state.War.Settled && state.ActiveTicks < maxActiveTicks)
        {
            if (state.Tick > maxActiveTicks * 2)
                throw new InvalidOperationException(
                    $"paused tick budget did not resume: {scenario} " +
                    $"seed={seed}");
            engine.Step();
        }
        state.Result.Ticks = state.ActiveTicks;
        state.Result.ValidSettlement = state.War.Settled;
        state.Result.RecoveryActions = state.Armies.Values.Sum(army =>
            army.RecoveryCount);
        state.Result.SettlementAttempts = state.War.SettlementAttempts;
        state.Result.VisitedStates = string.Join(",",
            state.Runtime.VisitedStates.OrderBy(value => (int)value));
        state.Result.FinalStateSummary = BuildFinalStateSummary(state);
        return state.Result;
    }

    private static string BuildFinalStateSummary(ScenarioState pState)
    {
        IEnumerable<string> cities = pState.Cities.Values
            .OrderBy(city => city.Id)
            .Select(city =>
                $"city={city.Id} home={city.HomeKingdomId} " +
                $"controller={city.ControllerKingdomId} " +
                $"occupation={city.Occupation} enemy={city.EnemyMilitaryPresent} " +
                $"eligible={city.EligibleTarget} " +
                $"reserved={string.Join(',', city.ReservedArmyIds.Order())}");
        IEnumerable<string> armies = pState.Armies.Values
            .OrderBy(army => army.Id)
            .Select(army =>
                $"army={army.Id} state={army.State} target={army.TargetCityId} " +
                $"position={army.Position} route={army.RouteState}:" +
                $"{army.RouteCursor} living={army.Living} " +
                $"rallied={army.Rallied} supply={army.Supply} " +
                $"organization={army.Organization} " +
                $"recovery={army.LastRecovery}\n" +
                string.Join('\n', army.Trace.Entries));
        return string.Join('\n', cities.Concat(armies));
    }
}

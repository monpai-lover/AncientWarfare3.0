using AncientWarfare3.core.lineage;

namespace ArmyRtsAdversarialSimulation;

internal sealed class ProgressOracle
{
    public const int RallyDeadlineTicks = 120;
    public const int MarchDeadlineTicks = 180;
    public const int DeployDeadlineTicks = 120;
    public const int AssaultDeadlineTicks = 240;
    public const int RegroupDeadlineTicks = 180;

    private readonly ScenarioState _state;
    private readonly Dictionary<long, ArmySnapshot> _last = new();
    private readonly Dictionary<long, int> _lastProgressTick = new();
    private readonly Dictionary<long, ArmyStallWatchdogState> _watchdogs =
        new();

    public ProgressOracle(ScenarioState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public void SampleDeadlines()
    {
        foreach (SimArmy army in _state.Armies.Values)
        {
            ArmySnapshot current = ArmySnapshot.From(army);
            if (!_last.TryGetValue(army.Id, out ArmySnapshot prior) ||
                !current.Equals(prior))
                _lastProgressTick[army.Id] = _state.ActiveTicks;

            bool hasOperationalWork = army.TargetCityId >= 0L ||
                                      army.State == ArmyRtsState.Replenish ||
                                      army.TransportState !=
                                      SimTransportState.None;
            bool eligible = army.MissionValid && !army.SettlementPending &&
                            hasOperationalWork && army.Living >=
                            ArmyLogisticsRules.MinimumOperationalForce;
            if (!eligible || !_lastProgressTick.TryGetValue(army.Id,
                    out int lastProgressTick) ||
                _state.ActiveTicks - lastProgressTick < DeadlineFor(army))
                continue;

            if (!_watchdogs.TryGetValue(army.Id,
                    out ArmyStallWatchdogState watchdog))
            {
                watchdog = new ArmyStallWatchdogState();
                _watchdogs[army.Id] = watchdog;
            }

            ArmyStallRecoveryAction action = ArmyStallWatchdogRules.Observe(
                watchdog,
                movementTiles: 0d,
                routeCursor: army.RouteCursor,
                routeReady: army.RouteState == SimRouteState.Ready,
                routePending: army.RouteState == SimRouteState.Waiting);
            if (action == ArmyStallRecoveryAction.None)
                action = ArmyStallWatchdogRules.RecordRouteFailure(watchdog);
            if (action == ArmyStallRecoveryAction.None)
                Fail(army, "deadline_without_recovery");

            army.LastRecovery = action;
            army.RecoveryCount++;
            _lastProgressTick[army.Id] = _state.ActiveTicks;
        }
    }

    public void AssertHardInvariants()
    {
        AssertTransportDeadlines();
        if (_state.VanillaStrategicMovementCommitted &&
            _state.RtsStrategicMovementCommitted)
        {
            _state.Result.MovementOwnershipConflicts++;
            Fail(_state.Armies.Values.First(),
                "dual_strategic_movement_ownership");
        }
        foreach (SimArmy army in _state.Armies.Values)
        {
            if (army.OriginalValidCaptainId >= 0L &&
                _state.Actors.TryGetValue(army.OriginalValidCaptainId,
                    out SimActor captain) && captain.Alive &&
                army.CaptainId != army.OriginalValidCaptainId)
                Fail(army, "valid_captain_replaced");

            if (_state.Cities.TryGetValue(army.TargetCityId,
                    out SimCity target) &&
                target.ControllerKingdomId == army.KingdomId &&
                !target.EnemyMilitaryPresent &&
                army.Role == ArmyRtsRole.Assault &&
                army.State == ArmyRtsState.Assault)
                Fail(army, "occupied_target_repeated");

            foreach (long actorId in army.Members)
            {
                if (!_state.Actors.TryGetValue(actorId,
                        out SimActor actor) || !actor.Alive) continue;
                if (actor.King || actor.CityLeader)
                    Fail(army, "authority_role_in_army");
                if (_state.Kind == ScenarioKind.OwnershipLifecycle &&
                    IsForeignTask(actor.Task) &&
                    actor.ForeignTaskAssignedTick >= 0 &&
                    _state.ActiveTicks - actor.ForeignTaskAssignedTick >
                    Math.Max(2, (army.Members.Count + 7) / 8 + 1))
                    Fail(army, "foreign_task_not_reclaimed");
            }
        }
    }

    private void AssertTransportDeadlines()
    {
        foreach (SimTransportRequest request in
                 _state.Runtime.TransportRequests)
        {
            if (request.State == SimTransportState.Completed) continue;
            bool assigned = request.AssignedBoatId >= 0L;
            double started = assigned
                ? request.AssignedTick
                : request.RequestedTick;
            if (!ArmyRtsTransportRules.TransportWaitTimedOut(
                    started, _state.ActiveTicks, assigned)) continue;
            if (_state.Armies.TryGetValue(request.ArmyId,
                    out SimArmy army))
                Fail(army, assigned
                    ? "assigned_transport_timeout"
                    : "pending_transport_timeout");
        }
    }

    private static bool IsForeignTask(SimTaskClass task)
    {
        return task == SimTaskClass.ForeignDecision ||
               task == SimTaskClass.Eating ||
               task == SimTaskClass.Social ||
               task == SimTaskClass.Training;
    }

    public void AppendChangedState()
    {
        foreach (SimArmy army in _state.Armies.Values)
        {
            _state.Runtime.VisitedStates.Add(army.State);
            ArmySnapshot current = ArmySnapshot.From(army);
            if (_last.TryGetValue(army.Id, out ArmySnapshot prior) &&
                current.Equals(prior)) continue;

            army.Trace.Append(
                $"tick={_state.Tick} state={army.State} " +
                $"target={army.TargetCityId} pos={army.Position} " +
                $"route={army.RouteState}:{army.RouteCursor} " +
                $"living={army.Living} rallied={army.Rallied} " +
                $"embarked={army.Embarked} landed={army.Landed} " +
                $"recovery={army.LastRecovery}");
            _last[army.Id] = current;
        }
    }

    private static int DeadlineFor(SimArmy army)
    {
        return army.State switch
        {
            ArmyRtsState.Rally or ArmyRtsState.Replenish =>
                RallyDeadlineTicks,
            ArmyRtsState.March or ArmyRtsState.Retreat =>
                MarchDeadlineTicks,
            ArmyRtsState.Deploy => DeployDeadlineTicks,
            ArmyRtsState.Assault or ArmyRtsState.Pursue or
                ArmyRtsState.Hold => AssaultDeadlineTicks,
            ArmyRtsState.Regroup => RegroupDeadlineTicks,
            _ => MarchDeadlineTicks
        };
    }

    private void Fail(SimArmy army, string reason)
    {
        throw new InvalidOperationException(
            $"reason={reason} seed={_state.Seed} tick={_state.Tick} " +
            $"paused={_state.Paused} war_age={_state.War.AgeYears} " +
            $"army={army.Id} kingdom={army.KingdomId} war={army.WarId} " +
            $"target={army.TargetCityId} role={army.Role} " +
            $"posture={army.Posture} state={army.State} " +
            $"captain={army.CaptainId} living={army.Living} " +
            $"rallied={army.Rallied} embarked={army.Embarked} " +
            $"landed={army.Landed} supply={army.Supply} " +
            $"organization={army.Organization} " +
            $"route={army.RouteState}:{army.RouteCursor} " +
            $"recoveries={army.RecoveryCount} " +
            $"transport={army.TransportState} " +
            $"vanilla_write={_state.LastVanillaWrite} " +
            $"ownership={_state.LastOwnershipDecision}\n" +
            string.Join("\n", army.Trace.Entries));
    }

    private readonly record struct ArmySnapshot(
        ArmyRtsState State,
        long Target,
        int Position,
        int RouteCursor,
        int Living,
        int Rallied,
        int Embarked,
        int Landed,
        int Supply,
        int Organization,
        ArmyStallRecoveryAction Recovery)
    {
        public static ArmySnapshot From(SimArmy army)
        {
            return new ArmySnapshot(
                army.State,
                army.TargetCityId,
                army.Position,
                army.RouteCursor,
                army.Living,
                army.Rallied,
                army.Embarked,
                army.Landed,
                army.Supply,
                army.Organization,
                army.LastRecovery);
        }
    }
}

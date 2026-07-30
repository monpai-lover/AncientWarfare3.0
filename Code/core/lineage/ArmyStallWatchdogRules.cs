using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum ArmyStallRecoveryAction
    {
        None = 0,
        ReassertCommand = 1,
        RebuildRoute = 2,
        AlternateEndpoint = 3,
        ChangeTarget = 4,
        EnterTransport = 5,
        Retreat = 6
    }

    public enum ArmyFollowerStallRecoveryAction
    {
        None = 0,
        ResetRoute = 1,
        TeleportToCaptain = 2
    }

    public sealed class ArmyFollowerStallRecoveryState
    {
        internal long ActorId = -1L;
        internal bool HasPosition;
        internal bool HasProgressRealtime;
        internal double LastX;
        internal double LastY;
        internal double LastProgressRealtime;
        internal bool RouteResetIssued;
        public double NoProgressSeconds { get; internal set; }
    }

    public sealed class ArmyFollowerStallSample
    {
        public long ActorId { get; set; } = -1L;
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public bool RecoveryEligible { get; set; }
        public bool CombatActive { get; set; }
        public bool TransportActive { get; set; }
    }

    public static class ArmyFollowerStallRecoveryRules
    {
        public const double MinimumProgressTiles = 0.25d;
        public const int RouteResetAfterSeconds = 5;
        public const int TeleportAfterSeconds = 20;

        public static ArmyFollowerStallRecoveryAction Observe(
            ArmyFollowerStallRecoveryState pState, long actorId,
            double positionX, double positionY, bool recoveryEligible,
            bool combatActive, bool transportActive)
        {
            if (pState == null) return ArmyFollowerStallRecoveryAction.None;
            if (actorId < 0L || !recoveryEligible || combatActive ||
                transportActive)
            {
                Reset(pState);
                return ArmyFollowerStallRecoveryAction.None;
            }

            if (pState.ActorId != actorId || !pState.HasPosition)
            {
                pState.ActorId = actorId;
                pState.LastX = positionX;
                pState.LastY = positionY;
                pState.HasPosition = true;
                pState.NoProgressSeconds = 0d;
                pState.RouteResetIssued = false;
                return ArmyFollowerStallRecoveryAction.None;
            }

            double deltaX = positionX - pState.LastX;
            double deltaY = positionY - pState.LastY;
            pState.LastX = positionX;
            pState.LastY = positionY;
            if (deltaX * deltaX + deltaY * deltaY >=
                MinimumProgressTiles * MinimumProgressTiles)
            {
                pState.NoProgressSeconds = 0d;
                pState.RouteResetIssued = false;
                return ArmyFollowerStallRecoveryAction.None;
            }

            pState.NoProgressSeconds +=
                ArmyStallWatchdogRules.SampleIntervalSeconds;
            if (!pState.RouteResetIssued && pState.NoProgressSeconds >=
                RouteResetAfterSeconds)
            {
                pState.RouteResetIssued = true;
                return ArmyFollowerStallRecoveryAction.ResetRoute;
            }
            return pState.NoProgressSeconds >= TeleportAfterSeconds
                ? ArmyFollowerStallRecoveryAction.TeleportToCaptain
                : ArmyFollowerStallRecoveryAction.None;
        }

        public static ArmyFollowerStallRecoveryAction ObserveAt(
            ArmyFollowerStallRecoveryState pState, long actorId,
            double positionX, double positionY, bool recoveryEligible,
            bool combatActive, bool transportActive,
            double observedRealtime)
        {
            if (pState == null) return ArmyFollowerStallRecoveryAction.None;
            if (actorId < 0L || !recoveryEligible || combatActive ||
                transportActive)
            {
                Reset(pState);
                return ArmyFollowerStallRecoveryAction.None;
            }

            double now = double.IsNaN(observedRealtime) ||
                         double.IsInfinity(observedRealtime)
                ? 0d
                : Math.Max(0d, observedRealtime);
            if (pState.ActorId != actorId || !pState.HasPosition)
            {
                pState.ActorId = actorId;
                pState.LastX = positionX;
                pState.LastY = positionY;
                pState.HasPosition = true;
                pState.HasProgressRealtime = true;
                pState.LastProgressRealtime = now;
                pState.NoProgressSeconds = 0d;
                pState.RouteResetIssued = false;
                return ArmyFollowerStallRecoveryAction.None;
            }

            double deltaX = positionX - pState.LastX;
            double deltaY = positionY - pState.LastY;
            pState.LastX = positionX;
            pState.LastY = positionY;
            if (deltaX * deltaX + deltaY * deltaY >=
                MinimumProgressTiles * MinimumProgressTiles)
            {
                pState.HasProgressRealtime = true;
                pState.LastProgressRealtime = now;
                pState.NoProgressSeconds = 0d;
                pState.RouteResetIssued = false;
                return ArmyFollowerStallRecoveryAction.None;
            }

            if (!pState.HasProgressRealtime)
            {
                pState.HasProgressRealtime = true;
                pState.LastProgressRealtime = now;
            }
            pState.NoProgressSeconds = Math.Max(0d,
                now - pState.LastProgressRealtime);
            if (pState.NoProgressSeconds >= TeleportAfterSeconds)
                return ArmyFollowerStallRecoveryAction.TeleportToCaptain;
            if (!pState.RouteResetIssued && pState.NoProgressSeconds >=
                RouteResetAfterSeconds)
            {
                pState.RouteResetIssued = true;
                return ArmyFollowerStallRecoveryAction.ResetRoute;
            }
            return ArmyFollowerStallRecoveryAction.None;
        }

        public static void Reset(ArmyFollowerStallRecoveryState pState)
        {
            if (pState == null) return;
            pState.ActorId = -1L;
            pState.HasPosition = false;
            pState.HasProgressRealtime = false;
            pState.LastX = 0d;
            pState.LastY = 0d;
            pState.LastProgressRealtime = 0d;
            pState.NoProgressSeconds = 0d;
            pState.RouteResetIssued = false;
        }
    }

    /// <summary>
    /// Tracks follower recovery independently for each actor. The watchdog
    /// samples an Army in bounded batches, so timeout accounting must use real
    /// time rather than the number of times a particular member was sampled.
    /// </summary>
    public sealed class ArmyFollowerStallRecoveryIndex
    {
        private readonly Dictionary<long, ArmyFollowerStallRecoveryState>
            _stateByActor =
                new Dictionary<long, ArmyFollowerStallRecoveryState>();

        public ArmyFollowerStallRecoveryAction Observe(long actorId,
            double positionX, double positionY, bool recoveryEligible,
            bool combatActive, bool transportActive,
            double observedRealtime)
        {
            if (actorId < 0L || !recoveryEligible || combatActive ||
                transportActive)
            {
                if (actorId >= 0L) _stateByActor.Remove(actorId);
                return ArmyFollowerStallRecoveryAction.None;
            }
            if (!_stateByActor.TryGetValue(actorId,
                    out ArmyFollowerStallRecoveryState state))
            {
                state = new ArmyFollowerStallRecoveryState();
                _stateByActor[actorId] = state;
            }
            return ArmyFollowerStallRecoveryRules.ObserveAt(state, actorId,
                positionX, positionY, recoveryEligible, combatActive,
                transportActive, observedRealtime);
        }

        public void Remove(long actorId)
        {
            if (actorId >= 0L) _stateByActor.Remove(actorId);
        }

        public void Clear()
        {
            _stateByActor.Clear();
        }
    }

    public sealed class ArmyStallWatchdogState
    {
        public int ConsecutiveSlowSamples { get; internal set; }
        public int FailedReplans { get; internal set; }
        internal int LastRouteCursor = int.MinValue;
        internal bool AwaitingReplanResult;
        internal bool HasRoutePhase;
        internal bool LastRouteReady;
        internal bool LastRoutePending;
        internal bool LastCommandExpected;
        internal bool LastCommandOwned;
        internal bool CommandRecoveryIssued;
        internal bool HasObjectiveProgress;
        internal double BestObjectiveProgress;
        public double NoProgressSeconds { get; internal set; }
        internal double CombatNoProgressSeconds;
        internal int LastRecoveryStage;
    }

    public sealed class ArmyWatchdogControllerSample
    {
        public long ArmyId { get; set; } = -1L;
        public long KingdomId { get; set; } = -1L;
        public long WarId { get; set; } = -1L;
        public long TargetCityId { get; set; } = -1L;
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public int RouteCursor { get; set; }
        public bool RouteReady { get; set; }
        public bool RoutePending { get; set; }
        public bool CommandExpected { get; set; }
        public bool CommandOwned { get; set; }
        public bool CombatActive { get; set; }
        public bool ObjectiveOpen { get; set; } = true;
        public bool ObjectiveProgressExpected { get; set; }
        public double ObjectiveProgress { get; set; }
        public bool RequiresTransport { get; set; }
        public bool TransportOwned { get; set; }
        public long PositionActorId { get; set; } = -1L;
        public int FormationLiving { get; set; }
        public int FormationRallied { get; set; }
        public ArmyWatchdogPositionSource PositionSource { get; set; }
        public ArmyRtsState State { get; set; }
        public ArmyRtsRole Role { get; set; }
        public bool DirectorForceReady { get; set; }
        public bool MinimumForceReady { get; set; }
        public bool DepartureReady { get; set; }
        public int TargetStrength { get; set; }
        public int RosterLiving { get; set; }
        public int Supply { get; set; }
        public int Organization { get; set; }
        public bool RouteSubmitted { get; set; }
        public bool RouteArrived { get; set; }
        public bool FormationObserved { get; set; }
        public bool ReplenishmentBypass { get; set; }
        public string LocalPathStatus { get; set; } = "Unavailable";
        public int LocalPathCount { get; set; }
        public int LocalPathIndex { get; set; }
        public bool LocalPathFollowing { get; set; }
        public bool LocalPathMoving { get; set; }
        public int LocalTargetTileId { get; set; } = -1;
    }

    public enum ArmyWatchdogPositionSource
    {
        None = 0,
        Captain = 1,
        FormationAnchor = 2,
        FormationMember = 3
    }

    public sealed class ArmyRecoveryDiagnosticGate
    {
        private readonly struct Entry
        {
            public Entry(long pTargetCityId, double pRealtime)
            {
                TargetCityId = pTargetCityId;
                Realtime = pRealtime;
            }

            public long TargetCityId { get; }
            public double Realtime { get; }
        }

        private readonly double _minimumIntervalSeconds;
        private readonly Dictionary<(long ArmyId,
                ArmyStallRecoveryAction Action), Entry> _entries =
            new Dictionary<(long ArmyId,
                ArmyStallRecoveryAction Action), Entry>();

        public ArmyRecoveryDiagnosticGate(double pMinimumIntervalSeconds)
        {
            _minimumIntervalSeconds = Math.Max(0d,
                pMinimumIntervalSeconds);
        }

        public bool ShouldLog(long pArmyId, ArmyStallRecoveryAction pAction,
            long pTargetCityId, double pRealtime)
        {
            if (pArmyId < 0L || pAction == ArmyStallRecoveryAction.None)
                return false;
            double now = double.IsNaN(pRealtime) ||
                         double.IsInfinity(pRealtime)
                ? 0d
                : Math.Max(0d, pRealtime);
            var key = (pArmyId, pAction);
            if (_entries.TryGetValue(key, out Entry entry) &&
                entry.TargetCityId == pTargetCityId &&
                now >= entry.Realtime &&
                now - entry.Realtime < _minimumIntervalSeconds)
                return false;
            _entries[key] = new Entry(pTargetCityId, now);
            return true;
        }

        public void RemoveArmy(long pArmyId)
        {
            if (pArmyId < 0L || _entries.Count == 0) return;
            var remove = new List<(long ArmyId,
                ArmyStallRecoveryAction Action)>();
            foreach (KeyValuePair<(long ArmyId,
                         ArmyStallRecoveryAction Action), Entry> pair in
                     _entries)
                if (pair.Key.ArmyId == pArmyId) remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++)
                _entries.Remove(remove[i]);
        }

        public void Clear()
        {
            _entries.Clear();
        }
    }

    public sealed class ArmyWatchdogLifecycleState
    {
        public bool Active { get; private set; }

        public void AssignMission()
        {
            Active = true;
        }

        public ArmyWatchdogPositionSource ResolveSample(bool missionValid,
            bool captainAvailable, bool formationAnchorAvailable)
        {
            if (!Active) return ArmyWatchdogPositionSource.None;
            if (!missionValid)
            {
                Active = false;
                return ArmyWatchdogPositionSource.None;
            }
            return ArmyStallWatchdogRules.SelectPositionSource(
                captainAvailable, formationAnchorAvailable);
        }
    }

    public sealed class ArmyWatchdogRecoveryFlow
    {
        private readonly ArmyStallWatchdogState _stall =
            new ArmyStallWatchdogState();
        private readonly ArmyWatchdogLifecycleState _lifecycle =
            new ArmyWatchdogLifecycleState();

        public bool Active => _lifecycle.Active;
        public ArmyStallWatchdogState StallState => _stall;

        public void AssignMission()
        {
            _lifecycle.AssignMission();
        }

        public ArmyStallRecoveryAction ObserveUnavailable(bool missionValid)
        {
            _lifecycle.ResolveSample(missionValid,
                captainAvailable: false,
                formationAnchorAvailable: false);
            return ArmyStallRecoveryAction.None;
        }

        public ArmyStallRecoveryAction ObserveSample(bool missionValid,
            ArmyWatchdogPositionSource pPositionSource,
            double movementTiles, int routeCursor, bool routeReady,
            bool routePending = false, bool commandExpected = false,
            bool commandOwned = false, bool combatActive = false,
            bool objectiveOpen = true, bool requiresTransport = false,
            bool objectiveProgressExpected = false,
            double objectiveProgress = 0d)
        {
            ArmyWatchdogPositionSource source = _lifecycle.ResolveSample(
                missionValid,
                captainAvailable: pPositionSource ==
                    ArmyWatchdogPositionSource.Captain,
                formationAnchorAvailable: pPositionSource ==
                    ArmyWatchdogPositionSource.FormationAnchor ||
                    pPositionSource ==
                    ArmyWatchdogPositionSource.FormationMember);
            if (source == ArmyWatchdogPositionSource.None)
                return ArmyStallRecoveryAction.None;
            if (combatActive)
            {
                if (movementTiles >=
                    ArmyStallWatchdogRules.MinimumProgressTiles)
                {
                    ArmyStallWatchdogRules.ResetForCombat(_stall);
                    return ArmyStallRecoveryAction.None;
                }
                if (_stall.CombatNoProgressSeconds <= 0d)
                    ArmyStallWatchdogRules.ResetForCombat(_stall);
                _stall.CombatNoProgressSeconds +=
                    ArmyStallWatchdogRules.SampleIntervalSeconds;
                if (_stall.CombatNoProgressSeconds >=
                    ArmyStallWatchdogRules.MaximumCombatPreemptionSeconds)
                {
                    _stall.CombatNoProgressSeconds = 0d;
                    return ArmyStallRecoveryAction.ReassertCommand;
                }
                return ArmyStallRecoveryAction.None;
            }
            _stall.CombatNoProgressSeconds = 0d;
            return ArmyStallWatchdogRules.Observe(_stall, movementTiles,
                routeCursor, routeReady, routePending, commandExpected,
                commandOwned, objectiveOpen, requiresTransport,
                objectiveProgressExpected, objectiveProgress);
        }

        public ArmyStallRecoveryAction RecordRouteFailure()
        {
            return ArmyStallWatchdogRules.RecordRouteFailure(_stall);
        }

        public ArmyStallRecoveryAction RecordReplanResult(bool succeeded)
        {
            return ArmyStallWatchdogRules.RecordReplanResult(_stall,
                succeeded);
        }

        public void SuspendForExternalOwnership()
        {
            ArmyStallWatchdogRules.ResetForCombat(_stall);
        }
    }

    public sealed class ArmyWatchdogSamplingClock
    {
        private bool _initialized;
        private bool _paused;
        private double _nextSampleTime;
        private double _lastRealtime;

        public bool ResumedThisUpdate { get; private set; }

        public bool TryStartSample(double realtime, bool paused)
        {
            double now = NormalizeRealtime(realtime);
            ResumedThisUpdate = false;
            if (paused)
            {
                _paused = true;
                return false;
            }
            if (_paused)
            {
                _paused = false;
                _initialized = true;
                _nextSampleTime = now +
                    ArmyStallWatchdogRules.SampleIntervalSeconds;
                ResumedThisUpdate = true;
                return false;
            }
            if (!_initialized)
            {
                _initialized = true;
                _nextSampleTime = now +
                    ArmyStallWatchdogRules.SampleIntervalSeconds;
                return true;
            }
            if (now < _nextSampleTime) return false;
            _nextSampleTime = now +
                ArmyStallWatchdogRules.SampleIntervalSeconds;
            return true;
        }

        public void Clear()
        {
            _initialized = false;
            _paused = false;
            _nextSampleTime = 0d;
            _lastRealtime = 0d;
            ResumedThisUpdate = false;
        }

        private double NormalizeRealtime(double pRealtime)
        {
            double now = double.IsNaN(pRealtime) ||
                         double.IsInfinity(pRealtime)
                ? _lastRealtime
                : Math.Max(0d, pRealtime);
            if (now < _lastRealtime) now = _lastRealtime;
            _lastRealtime = now;
            return now;
        }
    }

    public static class ArmyStallWatchdogRules
    {
        public const double SampleIntervalSeconds = 1d;
        public const double MinimumProgressTiles = 0.25d;
        public const int SlowSamplesBeforeRecovery = 3;
        public const int RoutePlanningSamplesBeforeRecovery = 6;
        public const int FailedRecoveryAttemptsBeforeHandoff = 3;
        public const int TargetCooldownWorldDays = 30;
        public const double MaximumCombatPreemptionSeconds = 10d;

        public static ArmyWatchdogPositionSource SelectPositionSource(
            bool captainAvailable, bool formationAnchorAvailable)
        {
            return SelectPositionSource(captainAvailable,
                formationAnchorAvailable,
                strandedFormationMemberAvailable: false);
        }

        public static ArmyWatchdogPositionSource SelectPositionSource(
            bool captainAvailable, bool formationAnchorAvailable,
            bool strandedFormationMemberAvailable)
        {
            if (strandedFormationMemberAvailable)
                return ArmyWatchdogPositionSource.FormationMember;
            if (captainAvailable) return ArmyWatchdogPositionSource.Captain;
            return formationAnchorAvailable
                ? ArmyWatchdogPositionSource.FormationAnchor
                : ArmyWatchdogPositionSource.None;
        }

        public static ArmyWatchdogPositionSource SelectMissionPositionSource(
            bool captainAvailable, bool formationAnchorAvailable,
            bool formationMemberAvailable,
            bool formationProgressExpected)
        {
            if (formationProgressExpected && formationMemberAvailable)
                return ArmyWatchdogPositionSource.FormationMember;
            if (captainAvailable) return ArmyWatchdogPositionSource.Captain;
            if (formationMemberAvailable)
                return ArmyWatchdogPositionSource.FormationMember;
            return formationAnchorAvailable
                ? ArmyWatchdogPositionSource.FormationAnchor
                : ArmyWatchdogPositionSource.None;
        }

        public static bool ShouldUseMemberRecovery(
            ArmyWatchdogPositionSource pSource,
            ArmyStallRecoveryAction pAction, bool objectiveOpen)
        {
            return pSource == ArmyWatchdogPositionSource.FormationMember &&
                   objectiveOpen &&
                   pAction != ArmyStallRecoveryAction.None &&
                   pAction != ArmyStallRecoveryAction.ReassertCommand &&
                   pAction != ArmyStallRecoveryAction.Retreat;
        }

        public static bool ShouldUseAlternateEndpoint(
            ArmyStallRecoveryAction pAction, bool commandExpected,
            bool commandOwned)
        {
            return pAction == ArmyStallRecoveryAction.AlternateEndpoint ||
                   pAction == ArmyStallRecoveryAction.EnterTransport;
        }

        public static bool ShouldExpectFormationProgress(
            ArmyRtsState pState, bool targetComplete)
        {
            return pState == ArmyRtsState.Deploy && !targetComplete;
        }

        public static double ResolveObjectiveProgress(
            ArmyRtsObjectiveState pState, double captureTicks)
        {
            double capture = double.IsNaN(captureTicks) ||
                             double.IsInfinity(captureTicks)
                ? 0d
                : Math.Max(0d, Math.Min(100d, captureTicks));
            if (pState == ArmyRtsObjectiveState.OpenAttack)
                return capture;
            return pState == ArmyRtsObjectiveState.OpenDefense
                ? 100d - capture
                : 0d;
        }

        public static ArmyStallRecoveryAction Observe(
            ArmyStallWatchdogState pState, double movementTiles,
            int routeCursor, bool routeReady, bool routePending = false,
            bool commandExpected = false, bool commandOwned = false,
            bool objectiveOpen = true, bool requiresTransport = false,
            bool objectiveProgressExpected = false,
            double objectiveProgress = 0d)
        {
            if (pState == null) return ArmyStallRecoveryAction.None;
            pState.HasRoutePhase = true;
            pState.LastRouteReady = routeReady;
            pState.LastRoutePending = routePending;
            pState.LastCommandExpected = commandExpected;
            pState.LastCommandOwned = commandOwned;
            if (!objectiveOpen)
                return ArmyStallRecoveryAction.ChangeTarget;
            bool objectiveAdvanced = ObserveObjectiveProgress(pState,
                objectiveProgressExpected, objectiveProgress);
            bool routeAdvanced = pState.LastRouteCursor != int.MinValue &&
                                 routeCursor != pState.LastRouteCursor;
            pState.LastRouteCursor = routeCursor;
            bool progressExpected = routeReady || routePending ||
                                    commandExpected ||
                                    objectiveProgressExpected;
            if (!progressExpected || routeAdvanced || objectiveAdvanced)
            {
                pState.ConsecutiveSlowSamples = 0;
                pState.CommandRecoveryIssued = false;
                pState.NoProgressSeconds = 0d;
                pState.LastRecoveryStage = 0;
                return ArmyStallRecoveryAction.None;
            }
            if (movementTiles >= MinimumProgressTiles)
            {
                ResetAfterMovement(pState);
                return ArmyStallRecoveryAction.None;
            }

            if (pState.ConsecutiveSlowSamples < int.MaxValue)
                pState.ConsecutiveSlowSamples++;
            pState.NoProgressSeconds += SampleIntervalSeconds;
            bool pureRoutePlanning = routePending && !routeReady &&
                                     !commandExpected;
            if (pureRoutePlanning &&
                pState.ConsecutiveSlowSamples <
                RoutePlanningSamplesBeforeRecovery)
                return ArmyStallRecoveryAction.None;
            if (pureRoutePlanning && pState.LastRecoveryStage < 2)
            {
                pState.LastRecoveryStage = 2;
                pState.AwaitingReplanResult = true;
                return ArmyStallRecoveryAction.RebuildRoute;
            }
            if (pState.NoProgressSeconds >= 10d &&
                pState.LastRecoveryStage < 3)
            {
                pState.LastRecoveryStage = 3;
                return requiresTransport
                    ? ArmyStallRecoveryAction.EnterTransport
                    : ArmyStallRecoveryAction.AlternateEndpoint;
            }
            if (pState.NoProgressSeconds >=
                    10d + RoutePlanningSamplesBeforeRecovery &&
                pState.LastRecoveryStage < 4)
            {
                pState.LastRecoveryStage = 4;
                return ArmyStallRecoveryAction.ChangeTarget;
            }
            if (pState.NoProgressSeconds >= 6d &&
                pState.LastRecoveryStage < 2)
            {
                pState.LastRecoveryStage = 2;
                pState.AwaitingReplanResult = true;
                return ArmyStallRecoveryAction.RebuildRoute;
            }
            if (pState.NoProgressSeconds >= 3d &&
                pState.LastRecoveryStage < 1)
            {
                pState.LastRecoveryStage = 1;
                pState.CommandRecoveryIssued = true;
                return ArmyStallRecoveryAction.ReassertCommand;
            }
            return ArmyStallRecoveryAction.None;
        }

        private static bool ObserveObjectiveProgress(
            ArmyStallWatchdogState pState, bool pExpected,
            double pProgress)
        {
            if (!pExpected)
            {
                pState.HasObjectiveProgress = false;
                pState.BestObjectiveProgress = 0d;
                return false;
            }
            double current = double.IsNaN(pProgress) ||
                             double.IsInfinity(pProgress)
                ? 0d
                : Math.Max(0d, Math.Min(100d, pProgress));
            if (!pState.HasObjectiveProgress)
            {
                pState.HasObjectiveProgress = true;
                pState.BestObjectiveProgress = current;
                return false;
            }
            if (current <= pState.BestObjectiveProgress + 0.001d)
                return false;
            pState.BestObjectiveProgress = current;
            return true;
        }

        public static ArmyStallRecoveryAction RecordReplanResult(
            ArmyStallWatchdogState pState, bool succeeded)
        {
            if (pState == null) return ArmyStallRecoveryAction.None;
            if (succeeded)
            {
                ResetAfterMovement(pState);
                return ArmyStallRecoveryAction.None;
            }
            if (!pState.AwaitingReplanResult)
                return ArmyStallRecoveryAction.None;
            pState.AwaitingReplanResult = false;
            if (pState.FailedReplans < int.MaxValue)
                pState.FailedReplans++;
            if (pState.FailedReplans >=
                FailedRecoveryAttemptsBeforeHandoff)
            {
                pState.LastRecoveryStage = Math.Max(4,
                    pState.LastRecoveryStage);
                return ArmyStallRecoveryAction.ChangeTarget;
            }
            pState.LastRecoveryStage = Math.Max(3,
                pState.LastRecoveryStage);
            return ArmyStallRecoveryAction.AlternateEndpoint;
        }

        public static ArmyStallRecoveryAction RecordRouteFailure(
            ArmyStallWatchdogState pState)
        {
            if (pState == null) return ArmyStallRecoveryAction.None;
            if (pState.FailedReplans < int.MaxValue)
                pState.FailedReplans++;
            pState.AwaitingReplanResult = true;
            if (pState.FailedReplans == 1)
            {
                pState.LastRecoveryStage = Math.Max(2,
                    pState.LastRecoveryStage);
                return ArmyStallRecoveryAction.RebuildRoute;
            }
            if (pState.FailedReplans >=
                FailedRecoveryAttemptsBeforeHandoff)
            {
                pState.AwaitingReplanResult = false;
                pState.LastRecoveryStage = Math.Max(4,
                    pState.LastRecoveryStage);
                return ArmyStallRecoveryAction.ChangeTarget;
            }
            pState.LastRecoveryStage = Math.Max(3,
                pState.LastRecoveryStage);
            return ArmyStallRecoveryAction.AlternateEndpoint;
        }

        public static long CooldownUntil(long currentWorldDay)
        {
            long day = Math.Max(0L, currentWorldDay);
            return day > long.MaxValue - TargetCooldownWorldDays
                ? long.MaxValue
                : day + TargetCooldownWorldDays;
        }

        public static bool IsCoolingDown(long currentWorldDay,
            long cooldownUntilWorldDay)
        {
            return Math.Max(0L, currentWorldDay) <
                   cooldownUntilWorldDay;
        }

        public static void ResetForCombat(ArmyStallWatchdogState pState)
        {
            if (pState == null) return;
            pState.ConsecutiveSlowSamples = 0;
            pState.CommandRecoveryIssued = false;
            pState.AwaitingReplanResult = false;
            pState.FailedReplans = 0;
            pState.LastRouteCursor = int.MinValue;
            pState.HasRoutePhase = false;
            pState.HasObjectiveProgress = false;
            pState.BestObjectiveProgress = 0d;
            pState.NoProgressSeconds = 0d;
            pState.CombatNoProgressSeconds = 0d;
            pState.LastRecoveryStage = 0;
        }

        private static void ResetAfterMovement(
            ArmyStallWatchdogState pState)
        {
            pState.ConsecutiveSlowSamples = 0;
            pState.CommandRecoveryIssued = false;
            pState.NoProgressSeconds = 0d;
            pState.LastRecoveryStage = 0;
            if (!pState.AwaitingReplanResult) return;
            pState.AwaitingReplanResult = false;
            pState.FailedReplans = 0;
        }
    }

    public sealed class ArmyTargetCooldownIndex
    {
        private readonly Dictionary<(long KingdomId, long TargetCityId),
            long> _untilByTarget =
                new Dictionary<(long KingdomId, long TargetCityId), long>();

        public void CoolDown(long kingdomId, long targetCityId,
            long currentWorldDay)
        {
            if (kingdomId < 0L || targetCityId < 0L) return;
            _untilByTarget[(kingdomId, targetCityId)] =
                ArmyStallWatchdogRules.CooldownUntil(currentWorldDay);
        }

        public bool IsCoolingDown(long kingdomId, long targetCityId,
            long currentWorldDay)
        {
            if (!_untilByTarget.TryGetValue((kingdomId, targetCityId),
                    out long until)) return false;
            if (ArmyStallWatchdogRules.IsCoolingDown(currentWorldDay,
                    until)) return true;
            _untilByTarget.Remove((kingdomId, targetCityId));
            return false;
        }

        public void Remove(long kingdomId, long targetCityId)
        {
            _untilByTarget.Remove((kingdomId, targetCityId));
        }

        public void Clear()
        {
            _untilByTarget.Clear();
        }
    }
}

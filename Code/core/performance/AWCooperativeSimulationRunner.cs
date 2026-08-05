using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using AncientWarfare3.api.multiplayer;
using life.taxi;

namespace AncientWarfare3.core.performance
{
    internal sealed class AWCooperativeSimulationRunner
    {
        private const int MaximumStagesPerBurst = 256;
        private const double MinimumBurstMilliseconds = 0.25d;
        private const double MaximumBurstMilliseconds = 2d;
        private const double TargetFrameBurstRatio = 0.01d;
        private const double InitialActorParallelStageMilliseconds = 2d;
        private const double InitialBuildingParallelStageMilliseconds = 0.5d;
        private const double SynchronousStageHeadroomRatio = 1.25d;

        private enum SimulationStage
        {
            Idle,
            DirtyCleanup,
            Maintenance,
            Explosions,
            CityZones,
            NutritionTimer,
            WorldTime,
            Taxi,
            MetaHistory,
            AnimationTime,
            EnemyCache,
            ControllableUnit,
            Heat,
            MapChunks,
            MapLayersUpdate,
            MapLayersDraw,
            MapModules,
            Cities,
            ActorsStart,
            Actors,
            BuildingsStart,
            Buildings,
            Drops,
            Cultures,
            StackEffects,
            ResourceThrows,
            WorldBehaviours,
            Armies,
            Kingdoms,
            Diplomacy,
            Subspecies,
            Plots,
            Clans,
            Alliances,
            Wars,
            Languages,
            Religions,
            Projectiles,
            Statuses,
            Era,
            DelayedActions,
            Aw3Authority,
            Complete
        }

        private enum StageBurstStopReason
        {
            None,
            Completed,
            AsyncBoundary,
            DomainBoundary,
            Deadline,
            StageLimit
        }

        private static readonly string[] StagePhaseNames =
        {
            "vanilla.idle",
            "vanilla.dirty_cleanup",
            "vanilla.maintenance",
            "vanilla.explosions",
            "vanilla.city_zones",
            "vanilla.nutrition_timer",
            "vanilla.world_time",
            "vanilla.taxi",
            "vanilla.meta_history",
            "vanilla.animation_time",
            "vanilla.enemy_cache",
            "vanilla.controllable_unit",
            "vanilla.heat",
            "vanilla.map_chunks",
            "vanilla.map_layers_update",
            "vanilla.map_layers_draw",
            "vanilla.map_modules",
            "vanilla.cities",
            "vanilla.actors_start",
            "vanilla.actors",
            "vanilla.buildings_start",
            "vanilla.buildings",
            "vanilla.drops",
            "vanilla.cultures",
            "vanilla.stack_effects",
            "vanilla.resource_throws",
            "vanilla.world_behaviours",
            "vanilla.armies",
            "vanilla.kingdoms",
            "vanilla.diplomacy",
            "vanilla.subspecies",
            "vanilla.plots",
            "vanilla.clans",
            "vanilla.alliances",
            "vanilla.wars",
            "vanilla.languages",
            "vanilla.religions",
            "vanilla.projectiles",
            "vanilla.statuses",
            "vanilla.era",
            "vanilla.delayed_actions",
            "aw3.authority",
            "vanilla.complete"
        };

        public static AWCooperativeSimulationRunner Instance { get; } =
            new AWCooperativeSimulationRunner();

        private readonly AWCooperativeBatchRunner<BatchActors, Actor>
            _actorRunner = new AWCooperativeBatchRunner<BatchActors, Actor>(
                "vanilla.actors", pAllowWorkerParallelism: true,
                pDeferParallelToPresentation: true);
        private readonly AWCooperativeBatchRunner<BatchBuildings, Building>
            _buildingRunner =
                new AWCooperativeBatchRunner<BatchBuildings, Building>(
                    "vanilla.buildings", pAllowWorkerParallelism: true,
                    pDeferParallelToPresentation: true);
        private readonly AWCooperativeWorldMaintenanceRunner
            _maintenanceRunner = new AWCooperativeWorldMaintenanceRunner();
        private readonly List<MapLayer> _mapLayers = new List<MapLayer>();
        private readonly List<BaseModule> _mapModules = new List<BaseModule>();
        private readonly AWSchedulerResourceOwnership<MapBox>
            _resourceOwnership;
        private readonly AWPresentationRefreshGate<MapBox>
            _presentationRefresh = new AWPresentationRefreshGate<MapBox>();
        private readonly List<WorldBehaviourAsset> _worldBehaviours =
            new List<WorldBehaviourAsset>();
        private readonly Action _startAdmissionCycleAction;
        private readonly Action _executeCurrentStageBurstAction;
        private readonly Action _executeCurrentStageCoreAction;
        private readonly Action _executeVanillaStageBurstCoreAction;

        private MapBox _world;
        private MapBox _pendingAdmissionMap;
        private WorldTimeScaleAsset _cycleTimeScale;
        private SimulationStage _stage;
        private AWSimulationMode _cycleMode;
        private float _cycleElapsed;
        private bool _cyclePaused;
        private int _simulationPassesRemaining;
        private int _listIndex;
        private double _admissionCredits;
        private double _lastRequestedSpeed = -1d;
        private AWSimulationMode _lastMode;
        private WorldTimeScaleAsset _lastTimeScaleAsset;
        private int _lastControlledFrame = -1;
        private bool _advancingGameDelayedActions;
        private long _logicalTicksAdmitted;
        private long _logicalTicksCompleted;
        private double _simulatedSecondsCompleted;
        private double _simulatedSecondsAtRateWindowStart;
        private float _rateWindowStartedAt = -1f;
        private float _requestedSpeed;
        private float _actualSpeed;
        private long _actorPresentationOverlapLaunches;
        private long _actorPresentationOverlapEagerLaunches;
        private long _actorPresentationSynchronousRuns;
        private long _actorPresentationOverlapCompletions;
        private long _actorPresentationOverlapFallbacks;
        private long _actorPresentationOverlapForcedJoins;
        private long _actorPresentationOverlapWallTicks;
        private long _actorPresentationOverlapWaitTicks;
        private long _lastActorPresentationOverlapWallTicks;
        private long _lastActorPresentationOverlapWaitTicks;
        private string _lastActorPresentationBoundaryReason = "none";
        private long _buildingPresentationOverlapLaunches;
        private long _buildingPresentationOverlapEagerLaunches;
        private long _buildingPresentationSynchronousRuns;
        private long _buildingPresentationOverlapCompletions;
        private long _buildingPresentationOverlapFallbacks;
        private long _buildingPresentationOverlapForcedJoins;
        private long _buildingPresentationOverlapWallTicks;
        private long _buildingPresentationOverlapWaitTicks;
        private long _lastBuildingPresentationOverlapWallTicks;
        private long _lastBuildingPresentationOverlapWaitTicks;
        private string _lastBuildingPresentationBoundaryReason = "none";
        private long _vanillaStageBursts;
        private long _vanillaStageBurstSteps;
        private int _maximumVanillaStageBurstSteps;
        private long _vanillaStageBurstCompletedStops;
        private long _vanillaStageBurstAsyncStops;
        private long _vanillaStageBurstDomainStops;
        private long _vanillaStageBurstDeadlineStops;
        private long _vanillaStageBurstLimitStops;
        private long _activeStageBurstDeadline;
        private int _activeStageBurstSteps;
        private StageBurstStopReason _activeStageBurstStopReason;
        private double _actorParallelStageEstimateMilliseconds =
            InitialActorParallelStageMilliseconds;
        private double _buildingParallelStageEstimateMilliseconds =
            InitialBuildingParallelStageMilliseconds;

        private AWCooperativeSimulationRunner()
        {
            _resourceOwnership =
                new AWSchedulerResourceOwnership<MapBox>(
                    ReadParallelism, WriteParallelism);
            _startAdmissionCycleAction = StartPendingAdmissionCycle;
            _executeCurrentStageBurstAction = ExecuteCurrentStageBurst;
            _executeCurrentStageCoreAction = ExecuteCurrentStageCore;
            _executeVanillaStageBurstCoreAction =
                ExecuteVanillaStageBurstCore;
        }

        public bool Active => _stage != SimulationStage.Idle;
        public bool IsAtCycleBoundary => !Active;
        public bool RequiresControl =>
            AWPerformanceSettings.Mode != AWSimulationMode.Native || Active;
        public bool ControlledThisFrame =>
            _lastControlledFrame == UnityEngine.Time.frameCount;
        public bool IsAdvancingGameDelayedActions =>
            _advancingGameDelayedActions;
        public long LogicalTicksAdmitted => _logicalTicksAdmitted;
        public long LogicalTicksCompleted => _logicalTicksCompleted;
        public float RequestedSpeed => _requestedSpeed;
        public float ActualSpeed => _actualSpeed;
        public double AdmissionCredits => _admissionCredits;
        public AWSimulationMode ActiveMode => Active
            ? _cycleMode
            : AWPerformanceSettings.Mode;
        internal bool HasMutatingPresentationWorkInFlight =>
            _actorRunner.HasParallelPresentationWorkInFlight ||
            _buildingRunner.HasParallelPresentationWorkInFlight;

        public void RunFrame(MapBox pMap, bool pAllowNewCycles = true)
        {
            if (pMap == null)
            {
                // Teardown can race the final scheduler callback. Cancel the
                // active cycle before releasing its world-owned resources.
                if (Active || HasMutatingPresentationWorkInFlight)
                    Abort();
                _presentationRefresh.Clear();
                RestoreNativeParallelism();
                return;
            }

            try
            {
                AWFramePriorityGovernor.BeginFrame();
                _presentationRefresh.ClearIfWorldMismatch(pMap);
                if (Active && !ReferenceEquals(_world, pMap)) Abort();
                _lastControlledFrame = UnityEngine.Time.frameCount;

                bool replicaSession =
                    AW3MultiplayerReplicaScope.IsReplicaSession;
                AWPausedFrameAction pausedAction =
                    AWFrameSchedulerRules.ResolvePausedFrameAction(
                        AWPerformanceSettings.Mode, pMap.isPaused(),
                        replicaSession, Active);
                if (pausedAction == AWPausedFrameAction.AbortReplicaCycle)
                {
                    Abort();
                    return;
                }
                if (pausedAction == AWPausedFrameAction.RefreshPresentation)
                {
                    _presentationRefresh.Request(pMap, UnityEngine.Time.frameCount);
                    RestoreNativeParallelism();
                    return;
                }

                if (!replicaSession)
                    _resourceOwnership.Acquire(pMap,
                        AWPerformanceSettings.ForegroundParallelism);
                if (pausedAction == AWPausedFrameAction.CompleteActiveCycle)
                {
                    CompleteActiveCycleAtBoundary();
                    RestoreNativeParallelism();
                    return;
                }
                PrepareAdmissionCredits(pMap, pAllowNewCycles,
                    replicaSession);

                while (true)
                {
                    if ((_stage == SimulationStage.Actors &&
                         _actorRunner.WaitingForPresentationDispatch) ||
                        (_stage == SimulationStage.Buildings &&
                         _buildingRunner.WaitingForPresentationDispatch))
                    {
                        string dispatchPhase = GetNextPhaseName();
                        if (!AWFramePriorityGovernor.CanRun(
                                AWSimulationDomain.Vanilla,
                                dispatchPhase))
                        {
                            AWFramePriorityGovernor.SetPhase(
                                AWSimulationDomain.Vanilla,
                                dispatchPhase);
                            break;
                        }

                        bool dispatched = false;
                        AWFramePriorityGovernor.RunPhase(
                            AWSimulationDomain.Vanilla,
                            dispatchPhase,
                            () => dispatched =
                                TryBeginDeferredParallelWorkEagerly());
                        if (!dispatched) break;
                        continue;
                    }

                    if (_actorRunner.HasParallelPresentationWorkInFlight &&
                        _actorRunner.IsBackgroundWorkCompleted)
                    {
                        CompleteActorPresentationWork(true,
                            "run_frame.completed");
                        continue;
                    }

                    if (_buildingRunner.HasParallelPresentationWorkInFlight &&
                        _buildingRunner.IsBackgroundWorkCompleted)
                    {
                        CompleteBuildingPresentationWork(true,
                            "run_frame.completed");
                        continue;
                    }

                    bool actorBackgroundPending =
                        _actorRunner.WaitingForBackgroundWork;
                    bool buildingBackgroundPending =
                        _buildingRunner.WaitingForBackgroundWork;
                    if (actorBackgroundPending || buildingBackgroundPending)
                    {
                        string awaitPhase = actorBackgroundPending
                            ? _actorRunner.GetNextPhaseName()
                            : _buildingRunner.GetNextPhaseName();
                        double remainingMilliseconds =
                            AWFramePriorityGovernor
                                .GetRemainingSimulationBudgetMilliseconds();
                        if (!AWFramePriorityGovernor.CanRun(
                                AWSimulationDomain.Vanilla, awaitPhase))
                        {
                            AWFramePriorityGovernor.SetPhase(
                                AWSimulationDomain.Vanilla, awaitPhase);
                            break;
                        }

                        bool joined = false;
                        double joinMilliseconds = Math.Max(
                            AWPerformanceSettings.BackgroundJoinMilliseconds,
                            remainingMilliseconds);
                        AWFramePriorityGovernor.RunPhase(
                            AWSimulationDomain.Vanilla,
                            awaitPhase,
                            () => joined = actorBackgroundPending
                                ? _actorRunner.TryJoinBackgroundWork(
                                    joinMilliseconds)
                                : _buildingRunner.TryJoinBackgroundWork(
                                    joinMilliseconds));
                        if (!joined)
                        {
                            AWFramePriorityGovernor.SetPhase(
                                AWSimulationDomain.Vanilla, awaitPhase);
                            break;
                        }

                        if (_actorRunner
                            .HasParallelPresentationWorkInFlight)
                            CompleteActorPresentationWork(false,
                                "run_frame.join");
                        else if (_buildingRunner
                            .HasParallelPresentationWorkInFlight)
                            CompleteBuildingPresentationWork(false,
                                "run_frame.join");
                        continue;
                    }

                    if (!Active)
                    {
                        if (!CanAdmitCycle(pMap, pAllowNewCycles,
                                replicaSession))
                            break;

                        const string startPhase = "vanilla.cycle.start";
                        if (!AWFramePriorityGovernor.CanRun(startPhase))
                        {
                            AWFramePriorityGovernor.SetPhase(startPhase);
                            break;
                        }

                        _admissionCredits -= 1d;
                        _pendingAdmissionMap = pMap;
                        try
                        {
                            AWFramePriorityGovernor.RunPhase(startPhase,
                                _startAdmissionCycleAction);
                        }
                        finally
                        {
                            _pendingAdmissionMap = null;
                        }
                        continue;
                    }

                    string phase = GetNextPhaseName();
                    AWSimulationDomain domain = GetCurrentDomain();
                    if (!AWFramePriorityGovernor.CanRun(domain, phase))
                    {
                        AWFramePriorityGovernor.SetPhase(domain, phase);
                        break;
                    }

                    AWFramePriorityGovernor.RunPhase(domain, phase,
                        _executeCurrentStageBurstAction);
                }

                if (!Active) RestoreNativeParallelism();
            }
            catch
            {
                Abort();
                throw;
            }
        }

        public bool TryBeginActorPresentationOverlap()
        {
            if (!RequiresControl ||
                !AWActorPresentationSnapshots.HasPublishedSnapshot ||
                _stage != SimulationStage.Actors ||
                !_actorRunner.BeginParallelPresentationWork())
                return false;

            Interlocked.Increment(
                ref _actorPresentationOverlapLaunches);
            return true;
        }

        public bool TryBeginBuildingPresentationOverlap()
        {
            if (!RequiresControl ||
                !AWActorPresentationSnapshots.HasPublishedSnapshot ||
                _stage != SimulationStage.Buildings ||
                !_buildingRunner.BeginParallelPresentationWork())
                return false;

            Interlocked.Increment(
                ref _buildingPresentationOverlapLaunches);
            return true;
        }

        private bool TryBeginDeferredParallelWorkEagerly()
        {
            if (_stage == SimulationStage.Actors)
            {
                if (!AWActorPresentationSnapshots.HasPublishedSnapshot ||
                    CanRunDeferredParallelWorkSynchronously(
                        _actorParallelStageEstimateMilliseconds))
                {
                    long startedAt = Stopwatch.GetTimestamp();
                    if (_actorRunner
                        .RunDeferredParallelWorkSynchronously())
                    {
                        UpdateParallelStageEstimate(
                            ref _actorParallelStageEstimateMilliseconds,
                            startedAt);
                        Interlocked.Increment(
                            ref _actorPresentationSynchronousRuns);
                        return true;
                    }
                }

                if (!TryBeginActorPresentationOverlap()) return false;
                Interlocked.Increment(
                    ref _actorPresentationOverlapEagerLaunches);
                return true;
            }

            if (_stage != SimulationStage.Buildings) return false;
            if (!AWActorPresentationSnapshots.HasPublishedSnapshot ||
                CanRunDeferredParallelWorkSynchronously(
                    _buildingParallelStageEstimateMilliseconds))
            {
                long startedAt = Stopwatch.GetTimestamp();
                if (_buildingRunner
                    .RunDeferredParallelWorkSynchronously())
                {
                    UpdateParallelStageEstimate(
                        ref _buildingParallelStageEstimateMilliseconds,
                        startedAt);
                    Interlocked.Increment(
                        ref _buildingPresentationSynchronousRuns);
                    return true;
                }
            }

            if (!TryBeginBuildingPresentationOverlap()) return false;
            Interlocked.Increment(
                ref _buildingPresentationOverlapEagerLaunches);
            return true;
        }

        private static bool CanRunDeferredParallelWorkSynchronously(
            double pEstimatedMilliseconds)
        {
            double requiredMilliseconds = Math.Max(
                AWPerformanceSettings.BackgroundJoinMilliseconds,
                Math.Max(AWPerformanceSettings.MinimumSliceMilliseconds,
                    pEstimatedMilliseconds *
                    SynchronousStageHeadroomRatio));
            return AWFramePriorityGovernor
                       .GetRemainingSimulationBudgetMilliseconds() >=
                   requiredMilliseconds;
        }

        private static void UpdateParallelStageEstimate(
            ref double pEstimateMilliseconds, long pStartedAt)
        {
            double elapsedMilliseconds = TicksToMilliseconds(
                Stopwatch.GetTimestamp() - pStartedAt);
            if (elapsedMilliseconds >= pEstimateMilliseconds)
            {
                pEstimateMilliseconds = elapsedMilliseconds;
                return;
            }

            pEstimateMilliseconds = Math.Max(
                AWPerformanceSettings.MinimumSliceMilliseconds,
                pEstimateMilliseconds * 0.9d +
                elapsedMilliseconds * 0.1d);
        }

        public bool EnsureActorReadBoundary(string pReason)
        {
            if (!_actorRunner.HasParallelPresentationWorkInFlight)
                return false;

            bool completedBeforeWait =
                _actorRunner.IsBackgroundWorkCompleted;
            CompleteActorPresentationWork(completedBeforeWait, pReason);
            return true;
        }

        public bool EnsureBuildingReadBoundary(string pReason)
        {
            if (!_buildingRunner.HasParallelPresentationWorkInFlight)
                return false;

            bool completedBeforeWait =
                _buildingRunner.IsBackgroundWorkCompleted;
            CompleteBuildingPresentationWork(completedBeforeWait, pReason);
            return true;
        }

        private void CompleteActorPresentationWork(
            bool pCompletedBeforeWait, string pReason)
        {
            AWSimulationCoordinatorThread.WorkResult result =
                _actorRunner.CompleteParallelPresentationWork();
            Interlocked.Increment(
                ref _actorPresentationOverlapCompletions);
            if (!pCompletedBeforeWait)
                Interlocked.Increment(
                    ref _actorPresentationOverlapForcedJoins);
            Interlocked.Add(ref _actorPresentationOverlapWallTicks,
                result.WallTicks);
            Interlocked.Add(ref _actorPresentationOverlapWaitTicks,
                result.WaitTicks);
            Interlocked.Exchange(
                ref _lastActorPresentationOverlapWallTicks,
                result.WallTicks);
            Interlocked.Exchange(
                ref _lastActorPresentationOverlapWaitTicks,
                result.WaitTicks);
            _lastActorPresentationBoundaryReason =
                string.IsNullOrEmpty(pReason) ? "unknown" : pReason;
        }

        private void CompleteBuildingPresentationWork(
            bool pCompletedBeforeWait, string pReason)
        {
            AWSimulationCoordinatorThread.WorkResult result =
                _buildingRunner.CompleteParallelPresentationWork();
            Interlocked.Increment(
                ref _buildingPresentationOverlapCompletions);
            if (!pCompletedBeforeWait)
                Interlocked.Increment(
                    ref _buildingPresentationOverlapForcedJoins);
            Interlocked.Add(ref _buildingPresentationOverlapWallTicks,
                result.WallTicks);
            Interlocked.Add(ref _buildingPresentationOverlapWaitTicks,
                result.WaitTicks);
            Interlocked.Exchange(
                ref _lastBuildingPresentationOverlapWallTicks,
                result.WallTicks);
            Interlocked.Exchange(
                ref _lastBuildingPresentationOverlapWaitTicks,
                result.WaitTicks);
            _lastBuildingPresentationBoundaryReason =
                string.IsNullOrEmpty(pReason) ? "unknown" : pReason;
        }

        public void FinishPresentationFrame()
        {
            if (_stage == SimulationStage.Actors &&
                _actorRunner.WaitingForPresentationDispatch &&
                _actorRunner.BeginParallelPresentationWork())
            {
                Interlocked.Increment(
                    ref _actorPresentationOverlapLaunches);
                Interlocked.Increment(
                    ref _actorPresentationOverlapFallbacks);
            }

            if (_stage == SimulationStage.Buildings &&
                _buildingRunner.WaitingForPresentationDispatch &&
                _buildingRunner.BeginParallelPresentationWork())
            {
                Interlocked.Increment(
                    ref _buildingPresentationOverlapLaunches);
                Interlocked.Increment(
                    ref _buildingPresentationOverlapFallbacks);
            }
        }

        public string GetPresentationOverlapDiagnostics()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "launch={0}(eager={1},sync={2}) complete={3} " +
                "fallback={4} forced_join={5} wall={6:0.0}ms " +
                "wait={7:0.0}ms last={8:0.00}/{9:0.00}ms " +
                "estimate={10:0.00}ms boundary={11} dispatch_wait={12} " +
                "inflight={13}",
                Interlocked.Read(ref _actorPresentationOverlapLaunches),
                Interlocked.Read(
                    ref _actorPresentationOverlapEagerLaunches),
                Interlocked.Read(
                    ref _actorPresentationSynchronousRuns),
                Interlocked.Read(
                    ref _actorPresentationOverlapCompletions),
                Interlocked.Read(
                    ref _actorPresentationOverlapFallbacks),
                Interlocked.Read(
                    ref _actorPresentationOverlapForcedJoins),
                TicksToMilliseconds(Interlocked.Read(
                    ref _actorPresentationOverlapWallTicks)),
                TicksToMilliseconds(Interlocked.Read(
                    ref _actorPresentationOverlapWaitTicks)),
                TicksToMilliseconds(Interlocked.Read(
                    ref _lastActorPresentationOverlapWallTicks)),
                TicksToMilliseconds(Interlocked.Read(
                    ref _lastActorPresentationOverlapWaitTicks)),
                _actorParallelStageEstimateMilliseconds,
                _lastActorPresentationBoundaryReason,
                _actorRunner.WaitingForPresentationDispatch,
                _actorRunner.HasParallelPresentationWorkInFlight);
        }

        public string GetBuildingPresentationOverlapDiagnostics()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "launch={0}(eager={1},sync={2}) complete={3} " +
                "fallback={4} forced_join={5} wall={6:0.0}ms " +
                "wait={7:0.0}ms last={8:0.00}/{9:0.00}ms " +
                "estimate={10:0.00}ms boundary={11} dispatch_wait={12} " +
                "inflight={13}",
                Interlocked.Read(
                    ref _buildingPresentationOverlapLaunches),
                Interlocked.Read(
                    ref _buildingPresentationOverlapEagerLaunches),
                Interlocked.Read(
                    ref _buildingPresentationSynchronousRuns),
                Interlocked.Read(
                    ref _buildingPresentationOverlapCompletions),
                Interlocked.Read(
                    ref _buildingPresentationOverlapFallbacks),
                Interlocked.Read(
                    ref _buildingPresentationOverlapForcedJoins),
                TicksToMilliseconds(Interlocked.Read(
                    ref _buildingPresentationOverlapWallTicks)),
                TicksToMilliseconds(Interlocked.Read(
                    ref _buildingPresentationOverlapWaitTicks)),
                TicksToMilliseconds(Interlocked.Read(
                    ref _lastBuildingPresentationOverlapWallTicks)),
                TicksToMilliseconds(Interlocked.Read(
                    ref _lastBuildingPresentationOverlapWaitTicks)),
                _buildingParallelStageEstimateMilliseconds,
                _lastBuildingPresentationBoundaryReason,
                _buildingRunner.WaitingForPresentationDispatch,
                _buildingRunner.HasParallelPresentationWorkInFlight);
        }

        public string GetStageBurstDiagnostics()
        {
            long bursts = _vanillaStageBursts;
            return string.Format(CultureInfo.InvariantCulture,
                "bursts={0} steps={1} avg={2:0.00} max={3} " +
                "stops={4}/{5}/{6}/{7}/{8}" +
                "(completed/async/domain/deadline/limit)",
                bursts, _vanillaStageBurstSteps,
                bursts == 0L
                    ? 0d
                    : _vanillaStageBurstSteps / (double)bursts,
                _maximumVanillaStageBurstSteps,
                _vanillaStageBurstCompletedStops,
                _vanillaStageBurstAsyncStops,
                _vanillaStageBurstDomainStops,
                _vanillaStageBurstDeadlineStops,
                _vanillaStageBurstLimitStops);
        }

        public void Abort()
        {
            try
            {
                AWSimulationTickBenchmark.AbortCurrentTick();
            }
            finally
            {
                try
                {
                    ResetAfterAbort();
                }
                finally
                {
                    _presentationRefresh.Clear();
                    ReleaseControl();
                }
            }
        }

        internal bool TryConsumePresentationRefresh(MapBox pMap, int pFrame)
        {
            return _presentationRefresh.TryConsume(pMap, pFrame);
        }

        internal void ClearPresentationRefreshRequest()
        {
            _presentationRefresh.Clear();
        }

        private void ResetAfterAbort()
        {
            _actorRunner.Abort();
            _buildingRunner.Abort();
            _maintenanceRunner.Abort();
            AWSimulationTime.CancelTick();
            AWActorPresentationSnapshots.Reset();
            AWPresentationCommandQueue.Clear();
            _mapLayers.Clear();
            _mapModules.Clear();
            _worldBehaviours.Clear();
            _world = null;
            _pendingAdmissionMap = null;
            _cycleTimeScale = null;
            _stage = SimulationStage.Idle;
            _listIndex = 0;
            _admissionCredits = 0d;
            _simulationPassesRemaining = 0;
            _cycleMode = AWSimulationMode.Native;
            _advancingGameDelayedActions = false;
            AWFramePriorityGovernor.SetPhase(
                AWSimulationDomain.Vanilla, "idle");
            AWFramePriorityGovernor.SetPhase(
                AWSimulationDomain.Aw3Authority, "idle");
        }

        public void ReleaseControl()
        {
            try
            {
                RestoreNativeParallelism();
            }
            finally
            {
                _admissionCredits = 0d;
                _lastRequestedSpeed = -1d;
                _lastMode = AWSimulationMode.Native;
                _lastTimeScaleAsset = null;
                _requestedSpeed = 0f;
            }
        }

        public void RestoreNativeParallelism()
        {
            _resourceOwnership.Release();
        }

        public void DrainToBoundary()
        {
            AWSaveBoundaryAction action =
                AWFrameSchedulerRules.ResolveSaveBoundary(Active,
                    Active && (_world == null || _world.isPaused()),
                    AW3MultiplayerReplicaScope.IsReplicaSession);
            if (action == AWSaveBoundaryAction.AbortReplicaCycle)
            {
                Abort();
                return;
            }
            if (action == AWSaveBoundaryAction.Ready)
            {
                RestoreNativeParallelism();
                return;
            }
            AWSimulationTickBenchmark.Suspend();
            try
            {
                CompleteActiveCycleAtBoundary();
            }
            catch
            {
                Abort();
                throw;
            }
            finally
            {
                AWSimulationTickBenchmark.Resume();
                RestoreNativeParallelism();
            }
        }

        private void StartAdmissionCycle(MapBox pMap)
        {
            _world = pMap;
            _cyclePaused = pMap.isPaused();
            _cycleMode = AWPerformanceSettings.Mode;
            _cycleTimeScale = Config.time_scale_asset;
            if (_cycleTimeScale == null)
                throw new InvalidOperationException(
                    "AW scheduler cannot start without a time scale asset.");

            AWSimulationCycle cycle = AWFrameSchedulerRules.BuildCycle(
                _cycleMode, _cycleTimeScale.multiplier,
                _cycleTimeScale.ticks);
            _cycleElapsed = cycle.ElapsedSeconds;
            _simulationPassesRemaining = cycle.Passes;
            StartSimulationPass();
        }

        private void CompleteActiveCycleAtBoundary()
        {
            while (Active)
            {
                if (_stage == SimulationStage.Actors &&
                    _actorRunner.WaitingForPresentationDispatch &&
                    _actorRunner.BeginParallelPresentationWork())
                    Interlocked.Increment(
                        ref _actorPresentationOverlapLaunches);
                if (_stage == SimulationStage.Buildings &&
                    _buildingRunner.WaitingForPresentationDispatch &&
                    _buildingRunner.BeginParallelPresentationWork())
                    Interlocked.Increment(
                        ref _buildingPresentationOverlapLaunches);

                EnsureActorReadBoundary("cycle_boundary");
                EnsureBuildingReadBoundary("cycle_boundary");
                ExecuteCurrentStage();
            }
        }

        private void StartPendingAdmissionCycle()
        {
            MapBox map = _pendingAdmissionMap;
            if (map == null)
                throw new InvalidOperationException(
                    "AW scheduler admission map is not available.");
            StartAdmissionCycle(map);
        }

        private static int ReadParallelism(MapBox pMap)
        {
            return pMap.parallel_options.MaxDegreeOfParallelism;
        }

        private static void WriteParallelism(MapBox pMap, int pParallelism)
        {
            pMap.parallel_options.MaxDegreeOfParallelism = pParallelism;
        }

        private void StartSimulationPass()
        {
            AWSimulationTime.BeginTick(_world, _cycleElapsed);
            AWSimulationTickBenchmark.BeginTick(_cycleElapsed, _cycleMode);
            _mapLayers.Clear();
            _mapLayers.AddRange(_world._map_layers);
            _mapModules.Clear();
            _mapModules.AddRange(_world._map_modules);
            _worldBehaviours.Clear();
            _worldBehaviours.AddRange(AssetManager.world_behaviours.list);
            _listIndex = 0;
            _stage = SimulationStage.DirtyCleanup;
            _logicalTicksAdmitted++;
            AWFramePriorityGovernor.RecordCycleStarted();
        }

        private string GetNextPhaseName()
        {
            switch (_stage)
            {
                case SimulationStage.Actors:
                    return _actorRunner.GetNextPhaseName();
                case SimulationStage.Buildings:
                    return _buildingRunner.GetNextPhaseName();
                case SimulationStage.Maintenance:
                    return _maintenanceRunner.GetNextPhaseName();
                case SimulationStage.MapLayersUpdate:
                    if (_listIndex < _mapLayers.Count)
                        return "vanilla.map_layer.update." +
                               _mapLayers[_listIndex].GetType().Name;
                    break;
                case SimulationStage.MapLayersDraw:
                    if (_listIndex < _mapLayers.Count)
                        return "vanilla.map_layer.draw." +
                               _mapLayers[_listIndex].GetType().Name;
                    break;
                case SimulationStage.MapModules:
                    if (_listIndex < _mapModules.Count)
                        return "vanilla.map_module." +
                               _mapModules[_listIndex].GetType().Name;
                    break;
                case SimulationStage.WorldBehaviours:
                    if (_listIndex < _worldBehaviours.Count)
                        return "vanilla.world_behaviour." +
                               _worldBehaviours[_listIndex].id;
                    break;
            }
            return StagePhaseNames[(int)_stage];
        }

        private AWSimulationDomain GetCurrentDomain()
        {
            return _stage == SimulationStage.Aw3Authority
                ? AWSimulationDomain.Aw3Authority
                : AWSimulationDomain.Vanilla;
        }

        private void ExecuteCurrentStage()
        {
            AWSimulationStepContext.Run(_world, _cyclePaused,
                _cycleElapsed,
                _cycleMode == AWSimulationMode.Fixed,
                _cycleTimeScale, _executeCurrentStageCoreAction);
        }

        private void ExecuteCurrentStageBurst()
        {
            if (AWSimulationTickBenchmark.IsCapturing ||
                GetCurrentDomain() != AWSimulationDomain.Vanilla)
            {
                ExecuteCurrentStage();
                return;
            }

            double targetFrameMilliseconds =
                1000d / AWPerformanceSettings.TargetRenderFps;
            double desiredBurstMilliseconds = Math.Max(
                MinimumBurstMilliseconds,
                Math.Min(MaximumBurstMilliseconds,
                    targetFrameMilliseconds * TargetFrameBurstRatio));
            double remainingMilliseconds = AWFramePriorityGovernor
                .GetRemainingSimulationBudgetMilliseconds();
            double burstMilliseconds = remainingMilliseconds > 0d
                ? Math.Min(desiredBurstMilliseconds,
                    Math.Max(MinimumBurstMilliseconds,
                        remainingMilliseconds))
                : MinimumBurstMilliseconds;
            long burstStartedAt = Stopwatch.GetTimestamp();
            _activeStageBurstDeadline = burstStartedAt + Math.Max(1L,
                (long)(burstMilliseconds * Stopwatch.Frequency / 1000d));
            _activeStageBurstSteps = 0;
            _activeStageBurstStopReason = StageBurstStopReason.None;

            AWSimulationStepContext.Run(_world, _cyclePaused,
                _cycleElapsed,
                _cycleMode == AWSimulationMode.Fixed,
                _cycleTimeScale, _executeVanillaStageBurstCoreAction);

            _vanillaStageBursts++;
            _vanillaStageBurstSteps += _activeStageBurstSteps;
            if (_activeStageBurstSteps > _maximumVanillaStageBurstSteps)
                _maximumVanillaStageBurstSteps = _activeStageBurstSteps;
            switch (_activeStageBurstStopReason)
            {
                case StageBurstStopReason.Completed:
                    _vanillaStageBurstCompletedStops++;
                    break;
                case StageBurstStopReason.AsyncBoundary:
                    _vanillaStageBurstAsyncStops++;
                    break;
                case StageBurstStopReason.DomainBoundary:
                    _vanillaStageBurstDomainStops++;
                    break;
                case StageBurstStopReason.Deadline:
                    _vanillaStageBurstDeadlineStops++;
                    break;
                case StageBurstStopReason.StageLimit:
                    _vanillaStageBurstLimitStops++;
                    break;
            }
        }

        private void ExecuteVanillaStageBurstCore()
        {
            while (true)
            {
                ExecuteCurrentStageCore();
                _activeStageBurstSteps++;

                if (!Active)
                {
                    _activeStageBurstStopReason =
                        StageBurstStopReason.Completed;
                    return;
                }
                if (GetCurrentDomain() != AWSimulationDomain.Vanilla)
                {
                    _activeStageBurstStopReason =
                        StageBurstStopReason.DomainBoundary;
                    return;
                }
                if ((_stage == SimulationStage.Actors &&
                     (_actorRunner.WaitingForPresentationDispatch ||
                      _actorRunner.WaitingForBackgroundWork)) ||
                    (_stage == SimulationStage.Buildings &&
                     (_buildingRunner.WaitingForPresentationDispatch ||
                      _buildingRunner.WaitingForBackgroundWork)))
                {
                    _activeStageBurstStopReason =
                        StageBurstStopReason.AsyncBoundary;
                    return;
                }
                if (_activeStageBurstSteps >= MaximumStagesPerBurst)
                {
                    _activeStageBurstStopReason =
                        StageBurstStopReason.StageLimit;
                    return;
                }
                if ((_activeStageBurstSteps & 3) == 0 &&
                    Stopwatch.GetTimestamp() >= _activeStageBurstDeadline)
                {
                    _activeStageBurstStopReason =
                        StageBurstStopReason.Deadline;
                    return;
                }
            }
        }

        private void ExecuteCurrentStageCore()
        {
            switch (_stage)
            {
                case SimulationStage.DirtyCleanup:
                    _maintenanceRunner.Start(_world);
                    Advance(SimulationStage.Maintenance);
                    break;
                case SimulationStage.Maintenance:
                    if (_maintenanceRunner.Step())
                        Advance(SimulationStage.Explosions);
                    break;
                case SimulationStage.Explosions:
                    _world.explosion_checker.update(_cycleElapsed);
                    Advance(SimulationStage.CityZones);
                    break;
                case SimulationStage.CityZones:
                    _world.city_zone_helper.update(_cycleElapsed);
                    Advance(SimulationStage.NutritionTimer);
                    break;
                case SimulationStage.NutritionTimer:
                    if (!_cyclePaused)
                        _world.updateTimerNutrition(_cycleElapsed);
                    Advance(SimulationStage.WorldTime);
                    break;
                case SimulationStage.WorldTime:
                    if (!_cyclePaused)
                        _world.map_stats.updateWorldTime(_cycleElapsed);
                    Advance(SimulationStage.Taxi);
                    break;
                case SimulationStage.Taxi:
                    if (!_cyclePaused) TaxiManager.update(_cycleElapsed);
                    Advance(SimulationStage.MetaHistory);
                    break;
                case SimulationStage.MetaHistory:
                    if (!_cyclePaused) _world.updateMetaHistory();
                    Advance(SimulationStage.AnimationTime);
                    break;
                case SimulationStage.AnimationTime:
                    Advance(SimulationStage.EnemyCache);
                    break;
                case SimulationStage.EnemyCache:
                    EnemiesFinder.clear();
                    Advance(SimulationStage.ControllableUnit);
                    break;
                case SimulationStage.ControllableUnit:
                    ControllableUnit.updateControllableUnit();
                    Advance(SimulationStage.Heat);
                    break;
                case SimulationStage.Heat:
                    _world.heat.update(_cycleElapsed);
                    Advance(SimulationStage.MapChunks);
                    break;
                case SimulationStage.MapChunks:
                    _world.map_chunk_manager.update(_cycleElapsed);
                    _listIndex = 0;
                    _stage = SimulationStage.MapLayersUpdate;
                    break;
                case SimulationStage.MapLayersUpdate:
                    if (_listIndex < _mapLayers.Count)
                        _mapLayers[_listIndex++].update(_cycleElapsed);
                    else
                    {
                        _listIndex = 0;
                        _stage = SimulationStage.MapLayersDraw;
                    }
                    break;
                case SimulationStage.MapLayersDraw:
                    if (_listIndex < _mapLayers.Count)
                        _mapLayers[_listIndex++].draw(_cycleElapsed);
                    else
                    {
                        _listIndex = 0;
                        _stage = SimulationStage.MapModules;
                    }
                    break;
                case SimulationStage.MapModules:
                    if (_listIndex < _mapModules.Count)
                        _mapModules[_listIndex++].update(_cycleElapsed);
                    else
                    {
                        _listIndex = 0;
                        _stage = SimulationStage.Cities;
                    }
                    break;
                case SimulationStage.Cities:
                    if (DebugConfig.isOn(DebugOption.SystemUpdateCities))
                        _world.cities.update(_cycleElapsed);
                    Advance(SimulationStage.ActorsStart);
                    break;
                case SimulationStage.ActorsStart:
                    if (!DebugConfig.isOn(DebugOption.SystemUpdateUnits))
                    {
                        Advance(SimulationStage.BuildingsStart);
                        break;
                    }
                    _world.units.checkContainer();
                    JobManagerActors actorManager =
                        _world.units.getJobManager();
                    _actorRunner.Start(actorManager,
                        actorManager.active_batches, _cycleElapsed,
                        _world.parallel_options);
                    _stage = SimulationStage.Actors;
                    break;
                case SimulationStage.Actors:
                    if (_actorRunner.Step())
                    {
                        _world.units.checkContainer();
                        Advance(SimulationStage.BuildingsStart);
                    }
                    break;
                case SimulationStage.BuildingsStart:
                    if (!DebugConfig.isOn(DebugOption.SystemUpdateBuildings))
                    {
                        Advance(SimulationStage.Drops);
                        break;
                    }
                    _world.buildings.checkContainer();
                    JobManagerBuildings buildingManager =
                        _world.buildings.getJobManager();
                    _buildingRunner.Start(buildingManager,
                        buildingManager._batches_active, _cycleElapsed,
                        _world.parallel_options);
                    _stage = SimulationStage.Buildings;
                    break;
                case SimulationStage.Buildings:
                    if (_buildingRunner.Step())
                    {
                        _world.buildings.checkContainer();
                        Advance(SimulationStage.Drops);
                    }
                    break;
                case SimulationStage.Drops:
                    _world.drop_manager.update(_cycleElapsed);
                    Advance(SimulationStage.Cultures);
                    break;
                case SimulationStage.Cultures:
                    _world.cultures.update(_cycleElapsed);
                    Advance(SimulationStage.StackEffects);
                    break;
                case SimulationStage.StackEffects:
                    AWActiveStackEffectsUpdater.Update(
                        _world.stack_effects,
                        _cycleElapsed);
                    Advance(SimulationStage.ResourceThrows);
                    break;
                case SimulationStage.ResourceThrows:
                    _world.resource_throw_manager.update(_cycleElapsed);
                    _listIndex = 0;
                    _stage = SimulationStage.WorldBehaviours;
                    break;
                case SimulationStage.WorldBehaviours:
                    if (!DebugConfig.isOn(DebugOption.SystemWorldBehaviours))
                    {
                        _listIndex = 0;
                        _stage = SimulationStage.Armies;
                        break;
                    }
                    if (_listIndex < _worldBehaviours.Count)
                    {
                        WorldBehaviourAsset behaviour =
                            _worldBehaviours[_listIndex++];
                        if (behaviour.enabled)
                        {
                            if (AWSimulationTickBenchmark.IsCapturing)
                            {
                                long startedAt = Stopwatch.GetTimestamp();
                                behaviour.manager.update(_cycleElapsed);
                                AWSimulationTickBenchmark.RecordWorldBehaviour(
                                    behaviour.id,
                                    Stopwatch.GetTimestamp() - startedAt);
                            }
                            else
                            {
                                behaviour.manager.update(_cycleElapsed);
                            }
                        }
                    }
                    else
                    {
                        _listIndex = 0;
                        _stage = SimulationStage.Armies;
                    }
                    break;
                case SimulationStage.Armies:
                    _world.armies.update(_cycleElapsed);
                    Advance(SimulationStage.Kingdoms);
                    break;
                case SimulationStage.Kingdoms:
                    _world.kingdoms.update(_cycleElapsed);
                    Advance(SimulationStage.Diplomacy);
                    break;
                case SimulationStage.Diplomacy:
                    _world.diplomacy.update(_cycleElapsed);
                    Advance(SimulationStage.Subspecies);
                    break;
                case SimulationStage.Subspecies:
                    _world.subspecies.update(_cycleElapsed);
                    Advance(SimulationStage.Plots);
                    break;
                case SimulationStage.Plots:
                    _world.plots.update(_cycleElapsed);
                    Advance(SimulationStage.Clans);
                    break;
                case SimulationStage.Clans:
                    _world.clans.update(_cycleElapsed);
                    Advance(SimulationStage.Alliances);
                    break;
                case SimulationStage.Alliances:
                    _world.alliances.update(_cycleElapsed);
                    Advance(SimulationStage.Wars);
                    break;
                case SimulationStage.Wars:
                    _world.wars.update(_cycleElapsed);
                    Advance(SimulationStage.Languages);
                    break;
                case SimulationStage.Languages:
                    _world.languages.update(_cycleElapsed);
                    Advance(SimulationStage.Religions);
                    break;
                case SimulationStage.Religions:
                    _world.religions.update(_cycleElapsed);
                    Advance(SimulationStage.Projectiles);
                    break;
                case SimulationStage.Projectiles:
                    _world.projectiles.update(_cycleElapsed);
                    Advance(SimulationStage.Statuses);
                    break;
                case SimulationStage.Statuses:
                    _world.statuses.update(_cycleElapsed);
                    Advance(SimulationStage.Era);
                    break;
                case SimulationStage.Era:
                    _world.era_manager.update(_cycleElapsed);
                    Advance(_cycleMode == AWSimulationMode.Large &&
                            _simulationPassesRemaining > 1
                        ? SimulationStage.Complete
                        : SimulationStage.DelayedActions);
                    break;
                case SimulationStage.DelayedActions:
                    _advancingGameDelayedActions = true;
                    try
                    {
                        _world.delayed_actions_manager.update(
                            _cycleElapsed, 0f);
                    }
                    finally
                    {
                        _advancingGameDelayedActions = false;
                    }
                    Advance(SimulationStage.Aw3Authority);
                    break;
                case SimulationStage.Aw3Authority:
                    AWAuthorityCycleService.ProcessCooperativeCycle(
                        _logicalTicksAdmitted, _cyclePaused);
                    Advance(SimulationStage.Complete);
                    break;
                case SimulationStage.Complete:
                    CompleteCycle();
                    break;
                case SimulationStage.Idle:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void CompleteCycle()
        {
            if (_cycleMode == AWSimulationMode.Fixed &&
                _world.timer_nutrition_decay <= 0f)
                _world.timer_nutrition_decay =
                    SimGlobals.m.interval_nutrition_decay;

            AWSimulationTime.CompleteTick(_world);
            AWActorPresentationSnapshots.CaptureIfRequested(
                _world, _logicalTicksCompleted + 1);
            AWSimulationTickBenchmark.MarkTickCompleted();
            _simulatedSecondsCompleted += _cycleElapsed;
            _mapLayers.Clear();
            _mapModules.Clear();
            _worldBehaviours.Clear();
            _listIndex = 0;
            _logicalTicksCompleted++;
            AWFramePriorityGovernor.RecordCycleCompleted();

            _simulationPassesRemaining--;
            if (_simulationPassesRemaining > 0)
            {
                StartSimulationPass();
                return;
            }

            _world = null;
            _cycleTimeScale = null;
            _stage = SimulationStage.Idle;
            _cycleMode = AWSimulationMode.Native;
            AWFramePriorityGovernor.SetPhase(
                AWSimulationDomain.Vanilla, "idle");
            AWFramePriorityGovernor.SetPhase(
                AWSimulationDomain.Aw3Authority, "idle");
        }

        private void Advance(SimulationStage pNextStage)
        {
            _stage = pNextStage;
        }

        private void PrepareAdmissionCredits(MapBox pMap,
            bool pAllowNewCycles, bool pReplicaSession)
        {
            UpdateActualSpeed();

            WorldTimeScaleAsset timeScale = Config.time_scale_asset;
            AWSimulationMode mode = AWPerformanceSettings.Mode;
            if (timeScale == null)
            {
                _admissionCredits = 0d;
                return;
            }
            double nextRequestedSpeed =
                AWFrameSchedulerRules.RequestedSpeed(timeScale.multiplier,
                    timeScale.ticks);
            if (!ReferenceEquals(timeScale, _lastTimeScaleAsset) ||
                mode != _lastMode ||
                Math.Abs(nextRequestedSpeed - _lastRequestedSpeed) > 0.001d)
            {
                _admissionCredits = 0d;
                _lastTimeScaleAsset = timeScale;
                _lastMode = mode;
                _lastRequestedSpeed = nextRequestedSpeed;
            }

            _requestedSpeed = (float)nextRequestedSpeed;
            if (!pAllowNewCycles || mode == AWSimulationMode.Native ||
                pMap.isPaused() || pReplicaSession ||
                _requestedSpeed <= 0f)
            {
                _admissionCredits = 0d;
                return;
            }

            _admissionCredits = AWFrameSchedulerRules.AddCredits(
                _admissionCredits,
                Math.Max(0f, UnityEngine.Time.unscaledDeltaTime),
                mode, nextRequestedSpeed);
        }

        private bool CanAdmitCycle(MapBox pMap, bool pAllowNewCycles,
            bool pReplicaSession)
        {
            return AWFrameSchedulerRules.CanAdmit(
                AWPerformanceSettings.Mode, pAllowNewCycles,
                pMap.isPaused(), pReplicaSession, _admissionCredits,
                modCycleActive: false);
        }

        private void UpdateActualSpeed()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (_rateWindowStartedAt < 0f)
            {
                _rateWindowStartedAt = now;
                _simulatedSecondsAtRateWindowStart =
                    _simulatedSecondsCompleted;
                return;
            }

            float elapsed = now - _rateWindowStartedAt;
            if (elapsed < 0.5f) return;

            double completedSimulationSeconds =
                _simulatedSecondsCompleted -
                _simulatedSecondsAtRateWindowStart;
            _actualSpeed = (float)(completedSimulationSeconds / elapsed);
            _rateWindowStartedAt = now;
            _simulatedSecondsAtRateWindowStart =
                _simulatedSecondsCompleted;
        }

        private static double TicksToMilliseconds(long pTicks)
        {
            return pTicks * 1000d / Stopwatch.Frequency;
        }
    }
}

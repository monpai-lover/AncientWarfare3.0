using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.api.multiplayer;
using life.taxi;

namespace AncientWarfare3.core.performance
{
    internal sealed class AWCooperativeSimulationRunner
    {
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
            Aw3Authority,
            DelayedActions,
            Complete
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
            "aw3.authority",
            "vanilla.delayed_actions",
            "vanilla.complete"
        };

        public static AWCooperativeSimulationRunner Instance { get; } =
            new AWCooperativeSimulationRunner();

        private readonly AWCooperativeBatchRunner<BatchActors, Actor>
            _actorRunner = new AWCooperativeBatchRunner<BatchActors, Actor>(
                "vanilla.actors", pAllowWorkerParallelism: true);
        private readonly AWCooperativeBatchRunner<BatchBuildings, Building>
            _buildingRunner =
                new AWCooperativeBatchRunner<BatchBuildings, Building>(
                    "vanilla.buildings", pAllowWorkerParallelism: false);
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
        private readonly Action _executeCurrentStageAction;
        private readonly Action _executeCurrentStageCoreAction;

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

        private AWCooperativeSimulationRunner()
        {
            _resourceOwnership =
                new AWSchedulerResourceOwnership<MapBox>(
                    ReadParallelism, WriteParallelism);
            _startAdmissionCycleAction = StartPendingAdmissionCycle;
            _executeCurrentStageAction = ExecuteCurrentStage;
            _executeCurrentStageCoreAction = ExecuteCurrentStageCore;
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

        public void RunFrame(MapBox pMap, bool pAllowNewCycles = true)
        {
            if (pMap == null)
            {
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
                    if (!AWFramePriorityGovernor.CanRun(phase))
                    {
                        AWFramePriorityGovernor.SetPhase(phase);
                        break;
                    }

                    AWFramePriorityGovernor.RunPhase(phase,
                        _executeCurrentStageAction);
                }

                if (!Active) RestoreNativeParallelism();
            }
            catch
            {
                Abort();
                throw;
            }
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
                    ReleaseControl();
                }
                finally
                {
                    _presentationRefresh.Clear();
                    ResetAfterAbort();
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
            AWFramePriorityGovernor.SetPhase("idle");
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
            while (Active) ExecuteCurrentStage();
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
                default:
                    return StagePhaseNames[(int)_stage];
            }
        }

        private void ExecuteCurrentStage()
        {
            AWSimulationStepContext.Run(_world, _cyclePaused,
                _cycleElapsed,
                _cycleMode == AWSimulationMode.Fixed,
                _cycleTimeScale, _executeCurrentStageCoreAction);
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
                    _world.stack_effects.update(_cycleElapsed);
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
                        : SimulationStage.Aw3Authority);
                    break;
                case SimulationStage.Aw3Authority:
                    AWAuthorityCycleService.ProcessCooperativeCycle(
                        _logicalTicksAdmitted, _cyclePaused);
                    Advance(SimulationStage.DelayedActions);
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
            AWFramePriorityGovernor.SetPhase("idle");
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
    }
}

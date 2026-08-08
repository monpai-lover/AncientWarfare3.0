using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AncientWarfare3.core.lineage;
using life.taxi;

namespace AncientWarfare3.core.performance
{
    internal sealed class AWCooperativeWorldMaintenanceRunner
    {
        private const string DirtyManagerPhasePrefix =
            "vanilla.maintenance.dirtymanagers.";
        private const int ActorMetaPartitionStride = 4;
        private const int AlivePartitionIndex = 0;
        private const int WildPartitionIndex = 1;
        private const int CivilizedPartitionIndex = 2;
        private const int DyingPartitionIndex = 3;

        private static readonly Dictionary<Type, string>
            DirtyManagerPhaseNames = new Dictionary<Type, string>();

        private enum MaintenanceStage
        {
            Idle,
            BuildingZones,
            CheckListsBefore,
            UnitContainer,
            BuildingContainer,
            SimObjectZones,
            PrepareActorsStart,
            PrepareActors,
            PrepareActorsIncremental,
            DirtyActorIndex,
            DirtyManagersStart,
            DirtyManagers,
            DirtyManagersParallel,
            DirtyMetaObjectsFirst,
            DestroyMetaObjects,
            DestroyObjects,
            CheckListsAfter,
            UnitDestroyStart,
            UnitDestroy,
            BuildingDestroyStart,
            BuildingDestroy,
            HousesStart,
            HousesBuildings,
            HousesActorsStart,
            HousesActors,
            DirtyMetaObjectsSecond,
            AnythingChanged,
            Complete
        }

        private static readonly string[] StagePhaseNames =
        {
            "vanilla.maintenance.idle",
            "vanilla.maintenance.building_zones",
            "vanilla.maintenance.check_lists_before",
            "vanilla.maintenance.unit_container",
            "vanilla.maintenance.building_container",
            "vanilla.maintenance.sim_object_zones",
            "vanilla.maintenance.prepare_actors_start",
            "vanilla.maintenance.prepare_actors",
            "vanilla.maintenance.prepare_actors_incremental",
            "vanilla.maintenance.dirty_actor_index",
            "vanilla.maintenance.dirty_managers_start",
            "vanilla.maintenance.dirty_managers",
            "vanilla.maintenance.dirty_managers_parallel",
            "vanilla.maintenance.dirty_meta_objects_first",
            "vanilla.maintenance.destroy_meta_objects",
            "vanilla.maintenance.destroy_objects",
            "vanilla.maintenance.check_lists_after",
            "vanilla.maintenance.unit_destroy_start",
            "vanilla.maintenance.unit_destroy",
            "vanilla.maintenance.building_destroy_start",
            "vanilla.maintenance.building_destroy",
            "vanilla.maintenance.houses_start",
            "vanilla.maintenance.houses_buildings",
            "vanilla.maintenance.houses_actors_start",
            "vanilla.maintenance.houses_actors",
            "vanilla.maintenance.dirty_meta_objects_second",
            "vanilla.maintenance.anything_changed",
            "vanilla.maintenance.complete"
        };

        private readonly List<Actor> _actors = new List<Actor>();
        private readonly List<Actor> _dirtyActorPartitions =
            new List<Actor>();
        private readonly Dictionary<Actor, int> _actorMetaIndices =
            new Dictionary<Actor, int>();
        private readonly List<Building> _occupiedBuildings =
            new List<Building>();
        private readonly List<BaseSystemManager> _metaManagers =
            new List<BaseSystemManager>();
        private readonly ParallelOptions _parallelOptions;
        private readonly Action<int> _dirtyManagerWorkItemAction;
        private readonly Action<int> _classifyActorMetaWorkItemAction;
        private readonly Action<int> _scatterActorMetaWorkItemAction;

        private MapBox _world;
        private MaintenanceStage _stage;
        private int _index;
        private bool _windowOnScreen;
        private int _preparedWorldGeneration = -1;
        private int _preparedActorVersion = -1;
        private int _preparedActorPartitionVersion = -1;
        private int _pendingActorPartitionVersion = -1;
        private int _lastAnythingChangedFrame = -1;
        private bool _actorPartitionsReady;
        private bool _hasDirtyMetaManagers;
        private int _dirtyMetaManagerCount;
        private ActorMetaPartitionKind[] _actorMetaPartitions =
            Array.Empty<ActorMetaPartitionKind>();
        private int[] _actorMetaPartitionCounts = Array.Empty<int>();
        private int[] _actorMetaPartitionOffsets = Array.Empty<int>();
        private Actor[] _aliveActors = Array.Empty<Actor>();
        private Actor[] _wildActors = Array.Empty<Actor>();
        private Actor[] _civilizedActors = Array.Empty<Actor>();
        private Actor[] _dyingActors = Array.Empty<Actor>();
        private int _bufferedAliveActorCount;
        private int _bufferedWildActorCount;
        private int _bufferedCivilizedActorCount;
        private int _bufferedDyingActorCount;
        private int _actorMetaWorkCount;

        internal AWCooperativeWorldMaintenanceRunner()
        {
            _parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism =
                    AWPerformanceSettings.ForegroundParallelism
            };
            _dirtyManagerWorkItemAction = RunDirtyManagerAt;
            _classifyActorMetaWorkItemAction = ClassifyActorMetaRange;
            _scatterActorMetaWorkItemAction = ScatterActorMetaRange;
        }

        public bool Active => _stage != MaintenanceStage.Idle;

        public void Start(MapBox pMap)
        {
            Abort();
            _world = pMap ?? throw new ArgumentNullException(nameof(pMap));
            int worldGeneration = AWSimulationTime.Generation;
            if (_preparedWorldGeneration != worldGeneration)
                ClearWorldState(worldGeneration);

            _windowOnScreen = pMap.isWindowOnScreen();
            _stage = MaintenanceStage.BuildingZones;
        }

        public string GetNextPhaseName()
        {
            if (_stage == MaintenanceStage.DirtyManagers &&
                _index < _metaManagers.Count)
                return GetDirtyManagerPhaseName(_metaManagers[_index]);

            return StagePhaseNames[(int)_stage];
        }

        public bool Step()
        {
            switch (_stage)
            {
                case MaintenanceStage.Idle:
                    return true;
                case MaintenanceStage.BuildingZones:
                    BuildingZonesSystem.update();
                    _stage = MaintenanceStage.CheckListsBefore;
                    break;
                case MaintenanceStage.CheckListsBefore:
                    _world.checkSimManagerLists();
                    _stage = MaintenanceStage.UnitContainer;
                    break;
                case MaintenanceStage.UnitContainer:
                    _world.units.checkContainer();
                    _stage = MaintenanceStage.BuildingContainer;
                    break;
                case MaintenanceStage.BuildingContainer:
                    _world.buildings.checkContainer();
                    _stage = MaintenanceStage.SimObjectZones;
                    break;
                case MaintenanceStage.SimObjectZones:
                    _world.sim_object_zones.update();
                    _stage = MaintenanceStage.PrepareActorsStart;
                    break;
                case MaintenanceStage.PrepareActorsStart:
                    BeginActorMetaMaintenance();
                    break;
                case MaintenanceStage.PrepareActors:
                    RebuildActorMetaPartitions();
                    RebuildActorMetaIndices();
                    _preparedActorVersion =
                        AWActorMetaPartitionVersion.GetStructuralVersion(
                            _world.units.version);
                    _preparedActorPartitionVersion =
                        _pendingActorPartitionVersion;
                    _dirtyActorPartitions.Clear();
                    _actorPartitionsReady = true;
                    AdvanceToDirtyManagers();
                    break;
                case MaintenanceStage.PrepareActorsIncremental:
                    ApplyActorMetaPartitionChanges();
                    _preparedActorVersion =
                        AWActorMetaPartitionVersion.GetStructuralVersion(
                            _world.units.version);
                    _preparedActorPartitionVersion =
                        _pendingActorPartitionVersion;
                    _dirtyActorPartitions.Clear();
                    AdvanceToDirtyManagers();
                    break;
                case MaintenanceStage.DirtyActorIndex:
                    AWDirtyMetaActorIndex.Prepare(
                        _metaManagers,
                        _aliveActors,
                        _bufferedAliveActorCount,
                        _dyingActors,
                        _bufferedDyingActorCount);
                    _stage = MaintenanceStage.DirtyManagersStart;
                    break;
                case MaintenanceStage.DirtyManagersStart:
                    _index = 0;
                    if (!_hasDirtyMetaManagers)
                    {
                        _stage = MaintenanceStage.DirtyMetaObjectsFirst;
                    }
                    else if (_dirtyMetaManagerCount >= 3)
                    {
                        _stage = MaintenanceStage.DirtyManagersParallel;
                    }
                    else
                    {
                        _stage = MaintenanceStage.DirtyManagers;
                    }

                    break;
                case MaintenanceStage.DirtyManagers:
                    if (_index < _metaManagers.Count)
                    {
                        BaseSystemManager manager = _metaManagers[_index++];
                        if (manager.isUnitsDirty())
                            manager.parallelDirtyUnitsCheck();
                    }
                    else
                    {
                        AWDirtyMetaActorIndex.End();
                        _stage = MaintenanceStage.DirtyMetaObjectsFirst;
                    }

                    break;
                case MaintenanceStage.DirtyManagersParallel:
                    RunDirtyManagersParallel();
                    AWDirtyMetaActorIndex.End();
                    _stage = MaintenanceStage.DirtyMetaObjectsFirst;
                    break;
                case MaintenanceStage.DirtyMetaObjectsFirst:
                    _world.checkDirtyMetaObjects();
                    _stage = MaintenanceStage.DestroyMetaObjects;
                    break;
                case MaintenanceStage.DestroyMetaObjects:
                    if (!_windowOnScreen) _world.checkMetaObjectsDestroy();
                    _stage = MaintenanceStage.DestroyObjects;
                    break;
                case MaintenanceStage.DestroyObjects:
                    if (!_windowOnScreen) _world.checkObjectsToDestroy();
                    _stage = MaintenanceStage.CheckListsAfter;
                    break;
                case MaintenanceStage.CheckListsAfter:
                    _world.checkSimManagerLists();
                    _stage = MaintenanceStage.UnitDestroyStart;
                    break;
                case MaintenanceStage.UnitDestroyStart:
                    _index = 0;
                    if (_world.units.event_destroy)
                    {
                        _world.units.event_destroy = false;
                        RefreshActors();
                        _stage = MaintenanceStage.UnitDestroy;
                    }
                    else
                    {
                        _stage = MaintenanceStage.BuildingDestroyStart;
                    }

                    break;
                case MaintenanceStage.UnitDestroy:
                    ProcessUnitDestroyBatch();
                    if (_index >= _actors.Count)
                    {
                        TaxiManager.removeDeadUnits();
                        _stage = MaintenanceStage.BuildingDestroyStart;
                    }

                    break;
                case MaintenanceStage.BuildingDestroyStart:
                    _index = 0;
                    if (_world.buildings.event_destroy)
                    {
                        _world.buildings.event_destroy = false;
                        RefreshActors();
                        _stage = MaintenanceStage.BuildingDestroy;
                    }
                    else
                    {
                        _stage = MaintenanceStage.HousesStart;
                    }

                    break;
                case MaintenanceStage.BuildingDestroy:
                    ProcessBuildingDestroyBatch();
                    if (_index >= _actors.Count)
                        _stage = MaintenanceStage.HousesStart;
                    break;
                case MaintenanceStage.HousesStart:
                    _index = 0;
                    _occupiedBuildings.Clear();
                    if (_world.buildings.event_houses)
                    {
                        _world.buildings.event_houses = false;
                        _occupiedBuildings.AddRange(
                            _world.buildings.occupied_buildings);
                        _stage = MaintenanceStage.HousesBuildings;
                    }
                    else
                    {
                        _stage = MaintenanceStage.DirtyMetaObjectsSecond;
                    }

                    break;
                case MaintenanceStage.HousesBuildings:
                    ProcessOccupiedBuildingBatch();
                    if (_index >= _occupiedBuildings.Count)
                        _stage = MaintenanceStage.HousesActorsStart;
                    break;
                case MaintenanceStage.HousesActorsStart:
                    RefreshActors();
                    _index = 0;
                    _stage = MaintenanceStage.HousesActors;
                    break;
                case MaintenanceStage.HousesActors:
                    ProcessHouseActorBatch();
                    if (_index >= _actors.Count)
                        _stage = MaintenanceStage.DirtyMetaObjectsSecond;
                    break;
                case MaintenanceStage.DirtyMetaObjectsSecond:
                    _world.checkDirtyMetaObjects();
                    _stage = MaintenanceStage.AnythingChanged;
                    break;
                case MaintenanceStage.AnythingChanged:
                    int frame = UnityEngine.Time.frameCount;
                    if (_lastAnythingChangedFrame != frame)
                    {
                        _world.checkAnyMetaAddedRemoved();
                        _lastAnythingChangedFrame = frame;
                    }

                    _stage = MaintenanceStage.Complete;
                    break;
                case MaintenanceStage.Complete:
                    Abort();
                    return true;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return false;
        }

        public void Abort()
        {
            AWDirtyMetaActorIndex.End();
            _actors.Clear();
            _dirtyActorPartitions.Clear();
            _occupiedBuildings.Clear();
            _metaManagers.Clear();
            _world = null;
            _stage = MaintenanceStage.Idle;
            _index = 0;
            _actorMetaWorkCount = 0;
        }

        private void ClearWorldState(int pWorldGeneration)
        {
            _preparedWorldGeneration = pWorldGeneration;
            _preparedActorVersion = -1;
            _preparedActorPartitionVersion = -1;
            _pendingActorPartitionVersion = -1;
            _lastAnythingChangedFrame = -1;
            _actorPartitionsReady = false;
            _hasDirtyMetaManagers = false;
            _dirtyMetaManagerCount = 0;
            _actorMetaIndices.Clear();
            ClearActorBuffer(_aliveActors);
            ClearActorBuffer(_wildActors);
            ClearActorBuffer(_civilizedActors);
            ClearActorBuffer(_dyingActors);
            _bufferedAliveActorCount = 0;
            _bufferedWildActorCount = 0;
            _bufferedCivilizedActorCount = 0;
            _bufferedDyingActorCount = 0;
            AWActorMetaPartitionVersion.Clear();
            AWDirtyMetaActorIndex.ClearWorldState();
        }

        private void BeginActorMetaMaintenance()
        {
            _metaManagers.Clear();
            _metaManagers.AddRange(_world._list_meta_main_managers);
            int actorStructuralVersion =
                AWActorMetaPartitionVersion.GetStructuralVersion(
                    _world.units.version);
            bool actorStructureDirty =
                !_actorPartitionsReady ||
                _preparedActorVersion != actorStructuralVersion;
            bool actorPartitionsDirty =
                actorStructureDirty ||
                _preparedActorPartitionVersion !=
                    AWActorMetaPartitionVersion.Version;
            if (!actorPartitionsDirty)
            {
                AdvanceToDirtyManagers();
                return;
            }

            _pendingActorPartitionVersion =
                AWActorMetaPartitionVersion.ConsumeDirtyActors(
                    _dirtyActorPartitions);
            if (!actorStructureDirty)
            {
                _stage = MaintenanceStage.PrepareActorsIncremental;
                return;
            }

            _actors.Clear();
            _actors.AddRange(_world.units.getSimpleList());
            _world.units.units_only_wild.Clear();
            _world.units.units_only_alive.Clear();
            _world.units.units_only_dying.Clear();
            _world.units.units_only_civ.Clear();
            _world.units.have_dying_units = false;
            _index = 0;
            _stage = MaintenanceStage.PrepareActors;
        }

        private void AdvanceToDirtyManagers()
        {
            _hasDirtyMetaManagers = HasDirtyMetaManagers();
            _stage = _hasDirtyMetaManagers
                ? MaintenanceStage.DirtyActorIndex
                : MaintenanceStage.DirtyManagersStart;
        }

        private bool HasDirtyMetaManagers()
        {
            _dirtyMetaManagerCount = 0;
            for (int i = 0; i < _metaManagers.Count; i++)
            {
                if (_metaManagers[i].isUnitsDirty())
                    _dirtyMetaManagerCount++;
            }

            return _dirtyMetaManagerCount > 0;
        }

        private void RunDirtyManagersParallel()
        {
            AWSimulationWorkerPool.Instance.RunIndexed(0,
                _metaManagers.Count, _dirtyManagerWorkItemAction);
        }

        private void RunDirtyManagerAt(int pManagerIndex)
        {
            BaseSystemManager manager = _metaManagers[pManagerIndex];
            if (manager.isUnitsDirty())
                manager.parallelDirtyUnitsCheck();
        }

        private void RebuildActorMetaPartitions()
        {
            int count = _actors.Count;
            if (_actorMetaPartitions.Length < count)
            {
                _actorMetaPartitions = new ActorMetaPartitionKind[
                    Math.Max(AWPerformanceSettings.SimulationBatchSize,
                        count)];
            }

            _actorMetaWorkCount =
                (count + AWPerformanceSettings.SimulationBatchSize - 1) /
                AWPerformanceSettings.SimulationBatchSize;
            int partitionSlotCount =
                _actorMetaWorkCount * ActorMetaPartitionStride;
            if (_actorMetaPartitionCounts.Length < partitionSlotCount)
            {
                _actorMetaPartitionCounts = new int[partitionSlotCount];
                _actorMetaPartitionOffsets = new int[partitionSlotCount];
            }

            if (_actorMetaWorkCount > 1)
            {
                AWSimulationWorkerPool.Instance.RunIndexed(0,
                    _actorMetaWorkCount, _classifyActorMetaWorkItemAction);
            }
            else if (_actorMetaWorkCount == 1)
            {
                ClassifyActorMetaRange(0);
            }

            int aliveCount = 0;
            int wildCount = 0;
            int civilizedCount = 0;
            int dyingCount = 0;
            for (int workIndex = 0;
                 workIndex < _actorMetaWorkCount;
                 workIndex++)
            {
                int slot = workIndex * ActorMetaPartitionStride;
                _actorMetaPartitionOffsets[slot + AlivePartitionIndex] =
                    aliveCount;
                _actorMetaPartitionOffsets[slot + WildPartitionIndex] =
                    wildCount;
                _actorMetaPartitionOffsets[slot + CivilizedPartitionIndex] =
                    civilizedCount;
                _actorMetaPartitionOffsets[slot + DyingPartitionIndex] =
                    dyingCount;
                aliveCount += _actorMetaPartitionCounts[
                    slot + AlivePartitionIndex];
                wildCount += _actorMetaPartitionCounts[
                    slot + WildPartitionIndex];
                civilizedCount += _actorMetaPartitionCounts[
                    slot + CivilizedPartitionIndex];
                dyingCount += _actorMetaPartitionCounts[
                    slot + DyingPartitionIndex];
            }

            EnsureActorBufferCapacity(ref _aliveActors, aliveCount);
            EnsureActorBufferCapacity(ref _wildActors, wildCount);
            EnsureActorBufferCapacity(ref _civilizedActors, civilizedCount);
            EnsureActorBufferCapacity(ref _dyingActors, dyingCount);

            if (_actorMetaWorkCount > 1)
            {
                AWSimulationWorkerPool.Instance.RunIndexed(0,
                    _actorMetaWorkCount, _scatterActorMetaWorkItemAction);
            }
            else if (_actorMetaWorkCount == 1)
            {
                ScatterActorMetaRange(0);
            }

            ClearStaleActorReferences(_aliveActors, aliveCount,
                ref _bufferedAliveActorCount);
            ClearStaleActorReferences(_wildActors, wildCount,
                ref _bufferedWildActorCount);
            ClearStaleActorReferences(_civilizedActors, civilizedCount,
                ref _bufferedCivilizedActorCount);
            ClearStaleActorReferences(_dyingActors, dyingCount,
                ref _bufferedDyingActorCount);

            AddActorRange(_world.units.units_only_alive,
                _aliveActors, aliveCount);
            AddActorRange(_world.units.units_only_wild,
                _wildActors, wildCount);
            AddActorRange(_world.units.units_only_civ,
                _civilizedActors, civilizedCount);
            AddActorRange(_world.units.units_only_dying,
                _dyingActors, dyingCount);
            _world.units.have_dying_units = dyingCount > 0;
            _index = count;
            _actorMetaWorkCount = 0;
        }

        private void RebuildActorMetaIndices()
        {
            _actorMetaIndices.Clear();
            for (int i = 0; i < _actors.Count; i++)
                _actorMetaIndices.Add(_actors[i], i);
        }

        private void ApplyActorMetaPartitionChanges()
        {
            List<Actor> alive = _world.units.units_only_alive;
            List<Actor> wild = _world.units.units_only_wild;
            List<Actor> civilized = _world.units.units_only_civ;
            List<Actor> dying = _world.units.units_only_dying;

            for (int i = 0; i < _dirtyActorPartitions.Count; i++)
            {
                Actor actor = _dirtyActorPartitions[i];
                int actorIndex = _actorMetaIndices[actor];
                ActorMetaPartitionKind previous =
                    _actorMetaPartitions[actorIndex];
                ActorMetaPartitionKind next = GetActorMetaPartition(actor);
                if (previous == next) continue;

                bool previousAlive =
                    previous != ActorMetaPartitionKind.Dying;
                bool nextAlive = next != ActorMetaPartitionKind.Dying;
                if (previousAlive != nextAlive)
                {
                    if (previousAlive)
                        RemoveActorAtRank(alive, actor, actorIndex);
                    else
                        InsertActorAtRank(alive, actor, actorIndex);
                }

                switch (previous)
                {
                    case ActorMetaPartitionKind.AliveWild:
                        RemoveActorAtRank(wild, actor, actorIndex);
                        break;
                    case ActorMetaPartitionKind.AliveCivilized:
                        RemoveActorAtRank(civilized, actor, actorIndex);
                        break;
                    case ActorMetaPartitionKind.Dying:
                        RemoveActorAtRank(dying, actor, actorIndex);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                _actorMetaPartitions[actorIndex] = next;
                switch (next)
                {
                    case ActorMetaPartitionKind.AliveWild:
                        InsertActorAtRank(wild, actor, actorIndex);
                        break;
                    case ActorMetaPartitionKind.AliveCivilized:
                        InsertActorAtRank(civilized, actor, actorIndex);
                        break;
                    case ActorMetaPartitionKind.Dying:
                        InsertActorAtRank(dying, actor, actorIndex);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            CopyActorListToBuffer(alive, ref _aliveActors,
                ref _bufferedAliveActorCount);
            CopyActorListToBuffer(dying, ref _dyingActors,
                ref _bufferedDyingActorCount);
            _world.units.have_dying_units = dying.Count > 0;
        }

        private void RemoveActorAtRank(
            List<Actor> pSource,
            Actor pActor,
            int pActorIndex)
        {
            int indexAtRank = FindActorRankIndex(pSource, pActorIndex);
            if (indexAtRank >= pSource.Count ||
                !ReferenceEquals(pSource[indexAtRank], pActor))
            {
                throw new InvalidOperationException(
                    "Actor metadata partition order does not match its " +
                    "container index.");
            }

            pSource.RemoveAt(indexAtRank);
        }

        private void InsertActorAtRank(
            List<Actor> pTarget,
            Actor pActor,
            int pActorIndex)
        {
            pTarget.Insert(FindActorRankIndex(pTarget, pActorIndex), pActor);
        }

        private int FindActorRankIndex(
            List<Actor> pSource,
            int pActorIndex)
        {
            int low = 0;
            int high = pSource.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                int middleActorIndex = _actorMetaIndices[pSource[middle]];
                if (middleActorIndex < pActorIndex)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }

        private static void CopyActorListToBuffer(
            List<Actor> pSource,
            ref Actor[] pBuffer,
            ref int pPreviousCount)
        {
            int count = pSource.Count;
            EnsureActorBufferCapacity(ref pBuffer, count);
            if (count > 0) pSource.CopyTo(pBuffer, 0);
            ClearStaleActorReferences(pBuffer, count, ref pPreviousCount);
        }

        private void ClassifyActorMetaRange(int pWorkIndex)
        {
            int start =
                pWorkIndex * AWPerformanceSettings.SimulationBatchSize;
            int end = Math.Min(_actors.Count,
                start + AWPerformanceSettings.SimulationBatchSize);
            int aliveCount = 0;
            int wildCount = 0;
            int civilizedCount = 0;
            int dyingCount = 0;
            for (int i = start; i < end; i++)
            {
                Actor actor = _actors[i];
                ActorMetaPartitionKind partition =
                    GetActorMetaPartition(actor);
                switch (partition)
                {
                    case ActorMetaPartitionKind.AliveWild:
                        aliveCount++;
                        wildCount++;
                        break;
                    case ActorMetaPartitionKind.AliveCivilized:
                        aliveCount++;
                        civilizedCount++;
                        break;
                    case ActorMetaPartitionKind.Dying:
                        dyingCount++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                _actorMetaPartitions[i] = partition;
            }

            int slot = pWorkIndex * ActorMetaPartitionStride;
            _actorMetaPartitionCounts[slot + AlivePartitionIndex] =
                aliveCount;
            _actorMetaPartitionCounts[slot + WildPartitionIndex] =
                wildCount;
            _actorMetaPartitionCounts[slot + CivilizedPartitionIndex] =
                civilizedCount;
            _actorMetaPartitionCounts[slot + DyingPartitionIndex] =
                dyingCount;
        }

        private static ActorMetaPartitionKind GetActorMetaPartition(
            Actor pActor)
        {
            Kingdom kingdom = pActor?.kingdom;
            if (kingdom == null || kingdom.data == null || kingdom.isRekt())
            {
                ActorKingdomSafetyService.QueueRepair(pActor);
                return ActorMetaPartitionKind.Dying;
            }
            if (!pActor.isAlive()) return ActorMetaPartitionKind.Dying;
            return kingdom.wild
                ? ActorMetaPartitionKind.AliveWild
                : ActorMetaPartitionKind.AliveCivilized;
        }

        private void ScatterActorMetaRange(int pWorkIndex)
        {
            int start =
                pWorkIndex * AWPerformanceSettings.SimulationBatchSize;
            int end = Math.Min(_actors.Count,
                start + AWPerformanceSettings.SimulationBatchSize);
            int slot = pWorkIndex * ActorMetaPartitionStride;
            int aliveIndex =
                _actorMetaPartitionOffsets[slot + AlivePartitionIndex];
            int wildIndex =
                _actorMetaPartitionOffsets[slot + WildPartitionIndex];
            int civilizedIndex =
                _actorMetaPartitionOffsets[slot + CivilizedPartitionIndex];
            int dyingIndex =
                _actorMetaPartitionOffsets[slot + DyingPartitionIndex];
            for (int i = start; i < end; i++)
            {
                Actor actor = _actors[i];
                switch (_actorMetaPartitions[i])
                {
                    case ActorMetaPartitionKind.AliveWild:
                        _aliveActors[aliveIndex++] = actor;
                        _wildActors[wildIndex++] = actor;
                        break;
                    case ActorMetaPartitionKind.AliveCivilized:
                        _aliveActors[aliveIndex++] = actor;
                        _civilizedActors[civilizedIndex++] = actor;
                        break;
                    case ActorMetaPartitionKind.Dying:
                        _dyingActors[dyingIndex++] = actor;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private static void EnsureActorBufferCapacity(
            ref Actor[] pBuffer,
            int pRequired)
        {
            if (pBuffer.Length >= pRequired) return;
            pBuffer = new Actor[Math.Max(
                AWPerformanceSettings.SimulationBatchSize,
                pRequired)];
        }

        private static void ClearStaleActorReferences(
            Actor[] pBuffer,
            int pCurrentCount,
            ref int pPreviousCount)
        {
            if (pPreviousCount > pCurrentCount)
            {
                Array.Clear(pBuffer, pCurrentCount,
                    pPreviousCount - pCurrentCount);
            }

            pPreviousCount = pCurrentCount;
        }

        private static void ClearActorBuffer(Actor[] pBuffer)
        {
            if (pBuffer.Length > 0)
                Array.Clear(pBuffer, 0, pBuffer.Length);
        }

        private static void AddActorRange(
            List<Actor> pTarget,
            Actor[] pSource,
            int pCount)
        {
            if (pCount == 0) return;
            pTarget.AddRange(new ArraySegment<Actor>(pSource, 0, pCount));
        }

        private void ProcessUnitDestroyBatch()
        {
            int end = Math.Min(_actors.Count,
                _index + AWPerformanceSettings.SimulationBatchSize);
            for (; _index < end; _index++)
            {
                Actor actor = _actors[_index];
                if (actor.beh_actor_target != null &&
                    !actor.beh_actor_target.isAlive())
                    actor.beh_actor_target = null;
                if (actor.attackedBy != null && !actor.attackedBy.isAlive())
                    actor.attackedBy = null;
                if (actor.hasLover() && !actor.lover.isAlive())
                {
                    actor.lover.lover = null;
                    actor.lover = null;
                }
            }
        }

        private void ProcessBuildingDestroyBatch()
        {
            int end = Math.Min(_actors.Count,
                _index + AWPerformanceSettings.SimulationBatchSize);
            for (; _index < end; _index++)
            {
                Actor actor = _actors[_index];
                if (actor.beh_building_target != null &&
                    !actor.beh_building_target.isAlive())
                    actor.beh_building_target = null;
                if (actor.attackedBy != null && !actor.attackedBy.isAlive())
                    actor.attackedBy = null;
            }
        }

        private void ProcessOccupiedBuildingBatch()
        {
            int end = Math.Min(_occupiedBuildings.Count,
                _index + AWPerformanceSettings.SimulationBatchSize);
            for (; _index < end; _index++)
            {
                Building building = _occupiedBuildings[_index];
                building.residents.Clear();
                if (building.asset.docks)
                    building.component_docks.clearBoatCounter();
            }
        }

        private void ProcessHouseActorBatch()
        {
            int end = Math.Min(_actors.Count,
                _index + AWPerformanceSettings.SimulationBatchSize);
            for (; _index < end; _index++)
            {
                Actor actor = _actors[_index];
                actor.checkHomeBuilding();
                Building home = actor.home_building;
                if (home != null)
                {
                    if (home.asset.docks)
                        home.component_docks.increaseBoatCounter(actor);
                    else
                        home.residents.Add(actor.data.id);
                }

                Building inside = actor.inside_building;
                if (inside != null &&
                    (!inside.isUsable() || inside.isAbandoned()))
                {
                    actor.exitBuilding();
                    actor.cancelAllBeh();
                }
            }
        }

        private void RefreshActors()
        {
            _actors.Clear();
            _actors.AddRange(_world.units.getSimpleList());
        }

        private static string GetDirtyManagerPhaseName(
            BaseSystemManager pManager)
        {
            Type type = pManager.GetType();
            if (!DirtyManagerPhaseNames.TryGetValue(type, out string phase))
            {
                phase = DirtyManagerPhasePrefix +
                        (type.FullName ?? type.Name);
                DirtyManagerPhaseNames.Add(type, phase);
            }

            return phase;
        }

        private enum ActorMetaPartitionKind : byte
        {
            AliveWild,
            AliveCivilized,
            Dying
        }
    }
}

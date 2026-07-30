using System;
using System.Collections.Generic;
using life.taxi;

namespace AncientWarfare3.core.performance
{
    internal sealed class AWCooperativeWorldMaintenanceRunner
    {
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
            DirtyManagersStart,
            DirtyManagers,
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
            "vanilla.maintenance.dirty_managers_start",
            "vanilla.maintenance.dirty_managers",
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
        private readonly List<Building> _occupiedBuildings =
            new List<Building>();
        private readonly List<BaseSystemManager> _metaManagers =
            new List<BaseSystemManager>();
        private MapBox _world;
        private MaintenanceStage _stage;
        private int _index;
        private bool _windowOnScreen;

        public bool Active => _stage != MaintenanceStage.Idle;

        public void Start(MapBox pMap)
        {
            Abort();
            _world = pMap ?? throw new ArgumentNullException(nameof(pMap));
            _windowOnScreen = pMap.isWindowOnScreen();
            _stage = MaintenanceStage.BuildingZones;
        }

        public string GetNextPhaseName()
        {
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
                    _actors.Clear();
                    _actors.AddRange(_world.units.getSimpleList());
                    _world.units.units_only_wild.Clear();
                    _world.units.units_only_alive.Clear();
                    _world.units.units_only_dying.Clear();
                    _world.units.units_only_civ.Clear();
                    _world.units.have_dying_units = false;
                    _index = 0;
                    _stage = MaintenanceStage.PrepareActors;
                    break;
                case MaintenanceStage.PrepareActors:
                    ProcessActorMetaBatch();
                    if (_index >= _actors.Count)
                        _stage = MaintenanceStage.DirtyManagersStart;
                    break;
                case MaintenanceStage.DirtyManagersStart:
                    _metaManagers.Clear();
                    _metaManagers.AddRange(_world._list_meta_main_managers);
                    _index = 0;
                    _stage = MaintenanceStage.DirtyManagers;
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
                        _stage = MaintenanceStage.DirtyMetaObjectsFirst;
                    }
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
                    _world.checkAnyMetaAddedRemoved();
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
            _actors.Clear();
            _occupiedBuildings.Clear();
            _metaManagers.Clear();
            _world = null;
            _stage = MaintenanceStage.Idle;
            _index = 0;
        }

        private void ProcessActorMetaBatch()
        {
            int end = Math.Min(_actors.Count,
                _index + AWPerformanceSettings.SimulationBatchSize);
            for (; _index < end; _index++)
            {
                Actor actor = _actors[_index];
                if (actor.isAlive())
                {
                    if (actor.kingdom.wild)
                        _world.units.units_only_wild.Add(actor);
                    else
                        _world.units.units_only_civ.Add(actor);
                    _world.units.units_only_alive.Add(actor);
                }
                else
                {
                    _world.units.units_only_dying.Add(actor);
                    _world.units.have_dying_units = true;
                }
            }
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
    }
}

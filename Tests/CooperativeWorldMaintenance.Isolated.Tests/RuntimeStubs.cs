// Game boundaries model the native checkHomeBuilding/clearHomeBuilding and
// exitBuilding contracts. In particular disposed buildings retain their object
// identity while data, asset and component_docks are cleared.
public class Data { public long id; }
public class Asset { public bool docks, city_building; }
public class BaseSimObject { public bool Alive = true; public bool isAlive() => Alive; }
public class Kingdom
{
    public Data data = new(); public Asset asset = new(); public bool wild, Destroyed;
    public bool isRekt() => Destroyed;
}
public class Actor : BaseSimObject
{
    public Data data = new(); public Asset asset = new(); public Kingdom kingdom = new(); public object city;
    public Actor beh_actor_target, lover; public BaseSimObject attackedBy; public Building beh_building_target;
    public Building home_building, inside_building; public bool is_inside_building;
    public int Checks, Clears, HappinessChanges, Exits, Cancellations, RelationshipChecks;
    public bool hasLover() { EnsureState(); RelationshipChecks++; return lover != null; }
    public void checkHomeBuilding()
    {
        EnsureState(); Checks++;
        if (home_building == null) return;
        if (!home_building.isUsable() || home_building.isAbandoned() || home_building.asset.city_building && home_building.city != city)
        {
            clearHomeBuilding(); changeHappiness("just_lost_house");
        }
    }
    public void clearHomeBuilding() { EnsureState(); Clears++; home_building = null; World.world.buildings.event_houses = true; }
    public bool changeHappiness(string id, int value = 0) { EnsureState(); if (id == "just_lost_house") HappinessChanges++; return true; }
    public void exitBuilding()
    {
        EnsureState(); Exits++;
        if (is_inside_building) { inside_building = null; is_inside_building = false; }
    }
    public void cancelAllBeh() { EnsureState(); Cancellations++; }
    public void Dispose() { data = null; asset = null; }
    private void EnsureState() { if (data == null || asset == null) throw new NullReferenceException("disposed actor state"); }
}
public class Docks
{
    public int Resets; public readonly List<Actor> Boats = new();
    public void clearBoatCounter() { Resets++; Boats.Clear(); }
    public void increaseBoatCounter(Actor actor) => Boats.Add(actor);
}
public class Building : BaseSimObject
{
    public Data data = new(); public Asset asset = new(); public Docks component_docks;
    public HashSet<long> residents = new(); public bool Abandoned, Usable = true; public object city;
    public bool isUsable() { EnsureState(); return Alive && Usable; }
    public bool isAbandoned() { EnsureState(); return Abandoned; }
    public void Dispose() { data = null; asset = null; component_docks = null; }
    private void EnsureState() { if (data == null || asset == null) throw new NullReferenceException("disposed building state"); }
}
public class UnitManager
{
    public int version;
    public List<Actor> Source = new(); public readonly List<Actor> units_only_wild = new(), units_only_alive = new(), units_only_dying = new(), units_only_civ = new();
    public bool have_dying_units, event_destroy = true;
    public List<Actor> getSimpleList() => Source;
    public void checkContainer() {}
}
public class BuildingManager
{
    public bool event_destroy = true, event_houses = true;
    public readonly List<Building> occupied_buildings = new();
    public void checkContainer() {}
}
public class BaseSystemManager
{
    public bool Dirty; public int Runs;
    public bool isUnitsDirty() => Dirty;
    public void parallelDirtyUnitsCheck() => Runs++;
}
public class SimObjectZones { public int Runs; public void update() => Runs++; }
public class MapBox
{
    public UnitManager units = new(); public BuildingManager buildings = new(); public SimObjectZones sim_object_zones = new();
    public readonly List<BaseSystemManager> _list_meta_main_managers = new();
    public int Checks, DirtyChecks, MetaDestroy, ObjectDestroy, AnythingChanged;
    public bool Window;
    public bool isWindowOnScreen() => Window;
    public void checkSimManagerLists() => Checks++;
    public void checkDirtyMetaObjects() => DirtyChecks++;
    public void checkMetaObjectsDestroy() => MetaDestroy++;
    public void checkObjectsToDestroy() => ObjectDestroy++;
    public void checkAnyMetaAddedRemoved() => AnythingChanged++;
}
public static class World { public static MapBox world; }
public static class BuildingZonesSystem { public static void update() {} }
namespace life.taxi { public static class TaxiManager { public static int Runs; public static void removeDeadUnits() => Runs++; } }
namespace AncientWarfare3.core.lineage
{
    internal static class ActorKingdomSafetyService
    {
        internal static readonly List<Actor> Repairs = new();
        internal static void QueueRepair(Actor actor) => Repairs.Add(actor);
    }
}
namespace UnityEngine { public static class Time { public static int frameCount; } }
namespace AncientWarfare3.core.performance
{
    public static class AWPerformanceSettings
    {
        public const int SimulationBatchSize = 2;
        public const int ForegroundParallelism = 2;
    }
    public static class AWSimulationTime { public static int Generation; }
    // Execution boundary: use real parallel tasks so the linked production
    // partition counting/scattering still runs across multiple workers.
    public sealed class AWSimulationWorkerPool
    {
        public static readonly AWSimulationWorkerPool Instance = new();
        public int Dispatches;
        public void RunIndexed(int start, int count, Action<int> action)
        {
            Dispatches++;
            Parallel.For(start, count, new ParallelOptions { MaxDegreeOfParallelism = 2 }, action);
        }
    }
    public static class AWDirtyMetaActorIndex
    {
        public static void Prepare(List<BaseSystemManager> managers, Actor[] alive, int aliveCount, Actor[] dying, int dyingCount) { }
        public static void End() { }
        public static void ClearWorldState() { }
    }
}

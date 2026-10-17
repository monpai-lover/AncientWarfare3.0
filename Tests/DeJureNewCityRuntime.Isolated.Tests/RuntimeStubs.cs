// Only game and persistence boundaries are doubled. The assignment service,
// region selection and deferred queue execute their production implementations.
public sealed class Data
{
    public long id;
    public string name = "city";
    public bool custom_name;
    public void get(string key, out string value, string fallback) => value = fallback;
    public void set(string key, string value) { }
}
public sealed class Kingdom
{
    public Data data = new();
    public City capital;
    public bool Destroyed, Neutral;
    public bool isRekt() => Destroyed;
    public bool isNeutral() => Neutral;
}
public sealed class City
{
    public Data data = new();
    public Kingdom kingdom;
    public HashSet<City> neighbours_cities = new();
    public WorldTile Tile = new();
    public bool Destroyed, Stronghold, NeighboursReady = true;
    public int NeighbourChecks;
    public bool isRekt() => Destroyed;
    public WorldTile getTile() => Tile;
    public void recalculateNeighbourZones()
    {
        NeighbourChecks++;
        if (!NeighboursReady) throw new InvalidOperationException("city zones not ready");
    }
    public void recalculateNeighbourCities() { }
}
public sealed class WorldTile { public Position pos = new(); }
public sealed class Position { public int x, y; }
public sealed class CityManager : List<City>
{
    public City get(long id) => this.FirstOrDefault(city => city?.data?.id == id);
}
public sealed class MapBox
{
    public static int current_world_seed_id = 123;
    public CityManager cities = new();
}
public static class World { public static MapBox world = new(); }
public static class Config { public static bool game_loaded = true; }
public static class SmoothLoader { public static bool isLoading() => false; }
public static class ModClass
{
    public static List<string> Warnings = new();
    public static void LogWarning(string message) => Warnings.Add(message);
    public static void LogError(string message) => throw new Exception(message);
}
namespace UnityEngine { public static class Time { public static int frameCount; } }
namespace AncientWarfare3.core.court
{
    [Flags] internal enum DeJureDirtyReason { CityRoster = 1 }
    internal static class DeJureRegionMaintenanceService
    {
        internal static void MarkKingdomDirty(long id, DeJureDirtyReason reason) { }
    }
    internal static class RegionalGovernmentRules { internal const int MaximumRegionCityCount = 6; }
    internal static class DeJureRegionStore
    {
        internal static List<DeJureRegion> Regions = new();
        internal static bool FailAssignment, ExplicitRemoval, FailCreate;
        internal static int Attempts, Assigned, CapitalRepairs;
        internal static bool TryGetForCity(long id, out DeJureRegion region)
        {
            region = Regions.FirstOrDefault(item => item.MemberCityIds.Contains(id));
            return region != null;
        }
        internal static bool CreateState(City city, string reason, out long id, out string error)
        {
            Attempts++;
            id = 20; error = FailCreate ? "invalid_city" : "";
            if (FailCreate) return false;
            Regions.Add(new DeJureRegion { RegionId = id, SeatCityId = city.data.id, MemberCityIds = new() { city.data.id } });
            Assigned++;
            return true;
        }
        internal static bool HasExplicitDeJureRemoval(long id) => ExplicitRemoval;
        internal static void EnsureKingdomCapitalSeat(Kingdom kingdom) => CapitalRepairs++;
        internal static bool IsEligibleCityId(long id) => World.world?.cities?.get(id)?.data != null;
        internal static IReadOnlyList<DeJureRegion> ActiveRegions() => Regions;
        internal static bool AssignCityAutomatically(long regionId, City city, string reason, out string error)
        {
            Attempts++;
            error = FailAssignment ? "invalid_city" : string.Empty;
            if (FailAssignment) return false;
            Regions.Single(region => region.RegionId == regionId).MemberCityIds.Add(city.data.id);
            Assigned++;
            return true;
        }
    }
}
namespace AncientWarfare3.core.lineage
{
    public static class PeasantRebelBanditStrongholdService
    {
        public static bool IsStrongholdCity(City city) => city?.Stronghold == true;
    }
    public static class LineageService { public static bool IsXiaKingdom(Kingdom kingdom) => false; }
}
namespace AncientWarfare3.core.policy
{
    public static class HierarchicalVassalMapModeService
    {
        public static void MarkHierarchyDirty(Kingdom kingdom) { }
        public static void RefreshAfterDeJureMutation() { }
    }
    public static class RuntimePerformanceDiagnostic
    {
        public static long BeginDeferredItemScope() => 0;
        public static void EndDeferredItem(string key, long start) { }
    }
}
namespace AncientWarfare3.core.performance
{
    public static class AWPerformanceSettings { public static bool EnableHistoricalDeJureCityNames; }
}
namespace AncientWarfare3.core.naming
{
    public static class AWNameDataKeys { public const string ChineseName = "chinese"; }
    public static class AWLocalizedNameService
    {
        public static void CaptureNative(Data data) { }
        public static void ProjectStored(Data data) { }
    }
    public static class XiaHistoricalDeJureCatalogService { public static object Current; }
    public sealed class XiaHistoricalDeJureProfile { public string StateId = string.Empty; }
    public static class XiaHistoricalDeJureRules
    {
        public static bool ShouldNameCity(bool enabled, bool unnamed) => false;
        public static bool IsHistoricalCityName(object catalog, string name) => false;
        public static XiaHistoricalDeJureProfile SelectProfile(object catalog, IEnumerable<string> names, int selector) => new();
        public static string SelectHistoricalCityName(object catalog, string state, IEnumerable<string> names, int selector) => string.Empty;
    }
}

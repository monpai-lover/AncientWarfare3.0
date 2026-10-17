$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
function Read-Source([string]$name) {
    return Get-Content -Raw -LiteralPath (Join-Path $repo "Code/core/lineage/$name.cs")
}
function Section([string]$text, [string]$start, [string]$end) {
    $first = $text.IndexOf($start, [StringComparison]::Ordinal)
    if ($first -lt 0) { throw "Missing production section: $start" }
    $last = $text.IndexOf($end, $first, [StringComparison]::Ordinal)
    if ($last -le $first) { throw "Missing production section end: $end" }
    return $text.Substring($first, $last - $first)
}
$bandit = Read-Source 'PeasantRebelBanditIslandMigrationService'
$mandate = Read-Source 'MandateIslandExileService'
$stronghold = Read-Source 'PeasantRebelBanditStrongholdService'
$banditMethods = (Section $bandit 'internal static void RestoreRuntime()' 'private static bool TryStart(') +
    (Section $bandit 'private static void FailMigration(' 'private static List<Actor> CollectResidents(') +
    (Section $bandit 'private static BanditMigrationStage ToBanditStage(' 'private static bool HasWarAgainst(')
$mandateMethods = (Section $mandate 'internal static void RestoreRuntime()' 'internal static void ClearRuntime()') +
    (Section $mandate 'private static void Fail(Kingdom' 'private static bool TrySelect(')
$orphan = Section $stronghold 'internal static void QueueOrphanCleanup(Kingdom' 'private static City ResolveCity('
$harness = @'
#pragma warning disable CS0649
public sealed class ActorData { public long id; }
public sealed class Actor {
    public ActorData data;
    public City city;
    public WorldTile current_tile;
    public bool is_inside_boat, Dead, Rekt;
    public ActorAi ai = new ActorAi();
    public long getID() => data.id;
    public bool isAlive() => !Dead;
    public bool isRekt() => Rekt;
    public string getNextJob() => "ordinary";
}
public sealed class ActorAi { public string Job; public void setJob(string job) { Job = job; } }
public sealed class CityData { public long id; }
public sealed class City {
    public CityData data;
    public WorldTile Tile;
    public bool Rekt;
    public long getID() => data.id;
    public WorldTile getTile() => Tile;
    public bool isRekt() => Rekt;
}
public sealed class WorldTileData { public int tile_id; }
public sealed class TileIsland { public long id; }
public sealed class MapRegion { public TileIsland island = new TileIsland(); }
public sealed class TileZone { public City city; }
public sealed class WorldTile {
    public WorldTileData data;
    public TileZone zone = new TileZone();
    public MapRegion region = new MapRegion();
    public bool isSameIsland(WorldTile other) => region.island.id == other.region.island.id;
}
public sealed class KingdomData {
    public long id;
    public Dictionary<string, object> Values = new Dictionary<string, object>();
    public void set<T>(string key, T value) { Values[key] = value; }
    public T Read<T>(string key, T fallback) => Values.TryGetValue(key, out object value) ? (T)value : fallback;
}
public sealed class Kingdom {
    public KingdomData data;
    public long id => data.id;
    public bool Mandate, Rekt;
    public int Cities;
    internal AncientWarfare3.core.lineage.PeasantRebelBanditStrongholdState State;
    public bool isRekt() => Rekt;
    public long getID() => data.id;
}
public sealed class ActorManager : Dictionary<long, Actor> { public Actor get(long id) => TryGetValue(id, out Actor actor) ? actor : null; }
public sealed class CityManager : Dictionary<long, City> { public City get(long id) => TryGetValue(id, out City city) ? city : null; }
public sealed class KingdomManager : List<Kingdom> { public Kingdom get(long id) => this.FirstOrDefault(k => k.data?.id == id); }
public sealed class MapBox {
    public ActorManager units = new ActorManager();
    public CityManager cities = new CityManager();
    public KingdomManager kingdoms = new KingdomManager();
    public WorldTile[] tiles_list;
}
public static class World { public static MapBox world; }
namespace life.taxi {
    public sealed class TaxiRequest { }
    public static class TaxiManager {
        public static readonly Dictionary<Actor, TaxiRequest> Requests = new Dictionary<Actor, TaxiRequest>();
        public static TaxiRequest getRequestForActor(Actor actor) => Requests.TryGetValue(actor, out TaxiRequest request) ? request : null;
    }
}
namespace AncientWarfare3.content { public static class IslandEscapeContent { public const string JobId = "island"; } }
namespace AncientWarfare3.core.pathfinding {
    public static class AWDockTaxiRouteService {
        public static bool TryCreateOrJoinRequest(Actor actor, WorldTile entry, WorldTile landing, out life.taxi.TaxiRequest request) {
            request = new life.taxi.TaxiRequest(); life.taxi.TaxiManager.Requests[actor] = request; return true;
        }
    }
}
namespace AncientWarfare3.core.lineage {
    internal static class ModClass { internal static void LogWarning(string text) { } internal static void LogError(string text) { } }
    internal static class LineageKeys {
        internal const string MANDATE_ISLAND_EXILE_STATE = "stage";
        internal const string MANDATE_ISLAND_EXILE_ORIGIN_CITY_ID = "origin";
        internal const string MANDATE_ISLAND_EXILE_TARGET_TILE_ID = "landing";
        internal const string MANDATE_ISLAND_EXILE_LEADER_ID = "leader";
        internal const string MANDATE_ISLAND_EXILE_MEMBER_IDS = "members";
    }
    internal static class MandateService { internal static bool IsMandateKingdom(Kingdom kingdom) => kingdom.Mandate; }
    internal static class PeasantRebelBanditStateStore {
        internal static bool TryRead(Kingdom kingdom, out PeasantRebelBanditStrongholdState state) { state = kingdom.State; return state != null; }
        internal static bool TryResolveOperational(Kingdom kingdom, out PeasantRebelBanditStrongholdState state) => TryRead(kingdom, out state);
        internal static bool Write(Kingdom kingdom, PeasantRebelBanditStrongholdState state) { kingdom.State = state; return true; }
        internal static void Clear(Kingdom kingdom) { kingdom.State = null; RuntimeProbe.Events.Add("clear"); }
    }
    internal sealed class PeasantRebelBanditIslandCandidate { internal WorldTile LandingTile, FoundingTile; internal TileIsland Island; }
    internal static class PeasantRebelBanditIslandMigrationService {
        private static bool CanMutate() => RuntimeProbe.Authority;
        private static bool IsMigrationActive(BanditIslandMigrationState state) => state.Stage != BanditMigrationStage.None && state.Stage != BanditMigrationStage.Completed && state.Stage != BanditMigrationStage.Failed;
        private static City ResolveCity(long id) => World.world.cities.get(id);
        private static Actor ResolveActor(long id) => World.world.units.get(id);
        private static WorldTile ResolveTile(int id) => id >= 0 && id < World.world.tiles_list.Length ? World.world.tiles_list[id] : null;
        private static List<Actor> ResolveMembers(IEnumerable<long> ids) => ids.Select(ResolveActor).Where(a => a?.data != null && a.isAlive() && !a.isRekt()).ToList();
        private static void PersistStage(Kingdom kingdom, PeasantRebelBanditStrongholdState state, IslandEscapeStage stage) { state.Migration.Stage = ToBanditStage(stage); }
        private static void FinishMigration(Kingdom kingdom, PeasantRebelBanditStrongholdState state, City origin,
            PeasantRebelBanditIslandCandidate candidate, IReadOnlyList<Actor> survivors) { state.Migration.Stage = BanditMigrationStage.Completed; RuntimeProbe.Founded++; }
// BANDIT_METHODS
    }
    internal static class MandateIslandExileService {
        private sealed class Candidate { }
        private static bool CanMutate() => RuntimeProbe.Authority;
        private static MandateIslandExileStage ReadStage(Kingdom kingdom) => (MandateIslandExileStage)ReadInt(kingdom, LineageKeys.MANDATE_ISLAND_EXILE_STATE);
        private static long ReadLong(Kingdom kingdom, string key) => kingdom.data.Read(key, -1L);
        private static int ReadInt(Kingdom kingdom, string key) => kingdom.data.Read(key, -1);
        private static IEnumerable<long> ReadIds(Kingdom kingdom) => kingdom.data.Read(LineageKeys.MANDATE_ISLAND_EXILE_MEMBER_IDS, "").Split(',').Select(long.Parse);
        private static City ResolveCity(long id) => World.world.cities.get(id);
        private static Actor ResolveActor(long id) => World.world.units.get(id);
        private static WorldTile ResolveTile(int id) => id >= 0 && id < World.world.tiles_list.Length ? World.world.tiles_list[id] : null;
        private static List<Actor> ResolveMembers(IEnumerable<long> ids) => ids.Select(ResolveActor).Where(a => a?.data != null && a.isAlive() && !a.isRekt()).ToList();
        private static Candidate FindCandidate(WorldTile tile) => new Candidate();
        private static void Finish(Kingdom kingdom, City origin, Candidate candidate, IReadOnlyList<Actor> survivors) {
            kingdom.data.set(LineageKeys.MANDATE_ISLAND_EXILE_STATE, (int)MandateIslandExileStage.Completed); RuntimeProbe.Founded++;
        }
// MANDATE_METHODS
    }
    internal enum DeferredWorkClass { CriticalRuntime }
    internal static class DeferredRuntimeWorkService {
        internal static Action Pending;
        internal static void EnqueueCoalesced(string key, DeferredWorkClass kind, Action action) { Pending = action; }
    }
    internal static class PeasantRebelRouteService { internal static int SafeCityCount(Kingdom kingdom) => kingdom.Cities; }
    internal static class PeasantRebelBanditPressureService { internal static void InvalidateTargetIndex() { RuntimeProbe.Events.Add("invalidate"); } }
    internal static class PeasantRebelBanditStrongholdService {
        private static bool CanMutate() => RuntimeProbe.Authority;
        private static Kingdom ResolveKingdom(long id) => World.world.kingdoms.get(id);
        private static bool RemoveStrongholdTowers(PeasantRebelBanditStrongholdState state) { RuntimeProbe.Events.Add("towers"); return !RuntimeProbe.FailTowers; }
        private static bool RestoreWalls(PeasantRebelBanditStrongholdState state) { RuntimeProbe.Events.Add("walls"); return !RuntimeProbe.FailWalls; }
// ORPHAN_METHOD
    }
    public static class RuntimeProbe {
        internal static bool Authority = true, FailTowers, FailWalls;
        internal static int Founded;
        internal static readonly List<string> Events = new List<string>();
        private static Kingdom Setup(bool mandate, bool boats, bool foundedStage = false) {
            IslandEscapeService.Clear();
            life.taxi.TaxiManager.Requests.Clear();
            World.world = new MapBox();
            Authority = true; FailTowers = FailWalls = false; Founded = 0; Events.Clear();
            DeferredRuntimeWorkService.Pending = null;
            var entry = new WorldTile { data = new WorldTileData { tile_id = 0 } };
            var landing = new WorldTile { data = new WorldTileData { tile_id = 1 } };
            landing.region.island.id = 1;
            World.world.tiles_list = new[] { entry, landing };
            var city = new City { data = new CityData { id = 10 }, Tile = entry };
            entry.zone.city = city;
            World.world.cities[10] = city;
            var kingdom = new Kingdom { data = new KingdomData { id = 7 }, Mandate = mandate };
            World.world.kingdoms.Add(kingdom);
            for (long id = 100; id <= 101; id++) World.world.units[id] = new Actor {
                data = new ActorData { id = id }, city = foundedStage ? null : city,
                current_tile = foundedStage ? landing : entry, is_inside_boat = boats };
            kingdom.State = new PeasantRebelBanditStrongholdState();
            kingdom.State.Migration.Stage = foundedStage ? BanditMigrationStage.Founding : BanditMigrationStage.Voyaging;
            kingdom.State.Migration.OldStrongholdCityId = 10;
            kingdom.State.Migration.TargetLandingTileId = 1;
            kingdom.State.Migration.LeaderActorId = 100;
            kingdom.State.Migration.MemberActorIds = new List<long> { 100, 101 };
            kingdom.data.set(LineageKeys.MANDATE_ISLAND_EXILE_STATE, (int)(foundedStage ? MandateIslandExileStage.Founding : MandateIslandExileStage.Voyaging));
            kingdom.data.set(LineageKeys.MANDATE_ISLAND_EXILE_ORIGIN_CITY_ID, 10L);
            kingdom.data.set(LineageKeys.MANDATE_ISLAND_EXILE_TARGET_TILE_ID, 1);
            kingdom.data.set(LineageKeys.MANDATE_ISLAND_EXILE_LEADER_ID, 100L);
            kingdom.data.set(LineageKeys.MANDATE_ISLAND_EXILE_MEMBER_IDS, "100,101");
            return kingdom;
        }
        private static void Restore(bool mandate) { if (mandate) MandateIslandExileService.RestoreRuntime(); else PeasantRebelBanditIslandMigrationService.RestoreRuntime(); }
        private static bool Failed(Kingdom kingdom) => kingdom.Mandate ? kingdom.data.Read("stage", -1) == (int)MandateIslandExileStage.Failed : kingdom.State.Migration.Stage == BanditMigrationStage.Failed;
        public static void Run() {
            FreshDepartureStillRejectsPassengers();
            foreach (bool mandate in new[] { false, true }) {
                ResumeAndComplete(mandate, false);
                ResumeAndComplete(mandate, true);
                RestoreFailureConverges(mandate, false);
                RestoreFailureConverges(mandate, true);
                RestoreReplacesDeadLeader(mandate);
                RestoreRecreatesLostTaxiRequest(mandate);
            }
            InvalidatedRouteReleasesJobs();
            RevivedRealmIsNotDismantled();
            ReplicaCannotRunQueuedCleanup();
            FailedCleanupRetainsStateForRetry(true);
            FailedCleanupRetainsStateForRetry(false);
            Console.WriteLine("PASS bandit/island lifecycle runtime regressions (18 cases)");
        }
        private static void FreshDepartureStillRejectsPassengers() {
            Setup(false, true);
            var spec = new IslandEscapeGroupSpec { GroupKey = "new", OriginCity = World.world.cities[10],
                EntryTile = World.world.tiles_list[0], LandingTile = World.world.tiles_list[1], Members = World.world.units.Values };
            Check(!IslandEscapeService.TryBegin(spec, out _), "fresh departure must not enlist actors already aboard");
            World.world.units[101].is_inside_boat = false;
            Check(IslandEscapeService.TryBegin(spec, out IslandEscapeGroupState group) && group.MemberActorIds.SequenceEqual(new long[] { 101 }),
                "fresh departure must retain only eligible origin residents");
        }
        private static void ResumeAndComplete(bool mandate, bool landed) {
            Setup(mandate, !landed, landed);
            Restore(mandate);
            Actor leader = World.world.units[100];
            Check(IslandEscapeService.TryGetForActor(leader, out IslandEscapeGroupState state),
                "saved voyage must re-register aboard or landed members");
            Check(state.Stage == (landed ? IslandEscapeStage.Founding : IslandEscapeStage.Voyaging), "restore must preserve progress instead of restarting boarding");
            foreach (Actor actor in World.world.units.Values) { actor.is_inside_boat = false; actor.current_tile = World.world.tiles_list[1]; }
            IslandEscapeService.TryExecute(leader, out _);
            Check(Founded == 1 && !IslandEscapeService.TryGetForActor(leader, out _), "restored voyage must complete once and release members");
            IslandEscapeService.TryExecute(leader, out _);
            Check(Founded == 1, "completion must remain idempotent");
        }
        private static void RestoreFailureConverges(bool mandate, bool invalidEntry) {
            Kingdom kingdom = Setup(mandate, true);
            if (invalidEntry) World.world.cities[10].Tile = null;
            else World.world.cities.Clear();
            Restore(mandate);
            Check(Failed(kingdom), "unrestorable voyage must fail instead of permanently remaining active");
        }
        private static void RestoreReplacesDeadLeader(bool mandate) {
            Kingdom kingdom = Setup(mandate, true);
            World.world.units[100].Dead = true;
            Restore(mandate);
            Check(IslandEscapeService.TryGetForActor(World.world.units[101], out IslandEscapeGroupState state) && state.LeaderActorId == 101,
                "restore must choose a living saved member after the leader died");
            long persisted = mandate ? kingdom.data.Read("leader", -1L) : kingdom.State.Migration.LeaderActorId;
            Check(persisted == 101, "founding callback must read the replacement leader identity from persisted state");
        }
        private static void InvalidatedRouteReleasesJobs() {
            Kingdom kingdom = Setup(false, false);
            kingdom.State.Migration.Stage = BanditMigrationStage.Boarding;
            Restore(false);
            World.world.tiles_list[0].data = null;
            Check(!IslandEscapeService.TryExecute(World.world.units[100], out _), "destroyed route tile must stop the job safely");
            Check(Failed(kingdom) && World.world.units[100].ai.Job == "ordinary", "invalid route must release job and persist terminal migration state");
        }
        private static void RestoreRecreatesLostTaxiRequest(bool mandate) {
            Setup(mandate, false);
            Restore(mandate);
            Actor waiting = World.world.units[100];
            Check(IslandEscapeService.TryGetForActor(waiting, out IslandEscapeGroupState state) && state.Stage == IslandEscapeStage.Voyaging,
                "saved waiting-for-boat members must retain voyage progress");
            IslandEscapeService.TryExecute(waiting, out _);
            Check(life.taxi.TaxiManager.getRequestForActor(waiting) != null,
                "runtime-only taxi requests lost on load must be recreated for waiting members");
            Actor landed = World.world.units[101];
            landed.current_tile = World.world.tiles_list[1];
            IslandEscapeService.TryExecute(landed, out WorldTile move);
            Check(move == null && life.taxi.TaxiManager.getRequestForActor(landed) == null,
                "already landed members must never be sent back to the departure dock");
        }
        private static void RevivedRealmIsNotDismantled() {
            Kingdom kingdom = Setup(false, false);
            PeasantRebelBanditStrongholdService.QueueOrphanCleanup(kingdom);
            kingdom.Cities = 1;
            DeferredRuntimeWorkService.Pending();
            Check(Events.Count == 0 && kingdom.State != null, "queued orphan cleanup must not dismantle a realm that regained a city");
        }
        private static void ReplicaCannotRunQueuedCleanup() {
            Kingdom kingdom = Setup(false, false);
            PeasantRebelBanditStrongholdService.QueueOrphanCleanup(kingdom);
            Authority = false;
            DeferredRuntimeWorkService.Pending();
            Check(Events.Count == 0 && kingdom.State != null, "authority must be rechecked when deferred cleanup executes");
        }
        private static void FailedCleanupRetainsStateForRetry(bool towers) {
            Kingdom kingdom = Setup(false, false);
            PeasantRebelBanditStrongholdService.QueueOrphanCleanup(kingdom);
            FailTowers = towers; FailWalls = !towers;
            bool threw = false;
            try { DeferredRuntimeWorkService.Pending(); } catch (InvalidOperationException) { threw = true; }
            Check(threw && kingdom.State != null && !Events.Contains("clear"), "partial teardown must keep restoration state and report failure to the deferred retry queue");
            FailTowers = FailWalls = false; Events.Clear();
            DeferredRuntimeWorkService.Pending();
            Check(string.Join(",", Events) == "towers,walls,clear,invalidate" && kingdom.State == null,
                "successful retry must remove towers, restore walls, then clear state and invalidate pressure");
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
'@
$harness = $harness.Replace('// BANDIT_METHODS', $banditMethods).Replace('// MANDATE_METHODS', $mandateMethods).Replace('// ORPHAN_METHOD', $orphan)
# Compile the full production escape service, its rule/state types, and the actual
# restore/cleanup entry points. The harness replaces native Unity boundaries only.
$sources = @('IslandEscapeService', 'IslandEscapeBehaviourRules', 'PeasantRebelBanditIslandRules',
    'PeasantRebelBanditStrongholdState', 'MandateIslandExileRules', 'PeasantRebelBanditPressureRules') |
    ForEach-Object { Read-Source $_ }
$imports = @('using System;', 'using System.Collections.Generic;', 'using System.Linq;')
foreach ($source in $sources) {
    $imports += [regex]::Matches($source, '(?m)^using [^;]+;').Value
}
$body = ($sources | ForEach-Object { [regex]::Replace($_, '(?m)^using [^;]+;\r?\n', '') }) -join "`n"
$combined = (($imports | Sort-Object -Unique) -join "`n") + "`n" + $body + "`n" + $harness
Add-Type -TypeDefinition $combined -Language CSharp
[AncientWarfare3.core.lineage.RuntimeProbe]::Run()

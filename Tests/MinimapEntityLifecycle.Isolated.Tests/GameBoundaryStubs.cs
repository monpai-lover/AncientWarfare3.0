// Game boundaries only; both complete production patches, the marker index,
// and all marker selection/scale rules are linked directly by the project.
using System.Collections;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type, string name) { }
    }
    public sealed class HarmonyPrefix : Attribute { }
    public sealed class HarmonyPostfix : Attribute { }
}
namespace UnityEngine
{
    public sealed class Sprite { public string Path; }
    public struct Vector3 { public float x, y, z; }
    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) => Math.Min(Math.Max(value, min), max);
    }
}
public sealed class ActorData { public long id; }
public sealed class KingdomData
{
    public long id;
    public long HeirId = -1;
    public void get(string key, out long value, long fallback) => value = HeirId;
}
public sealed class KingdomAsset { public bool civ = true; }
public sealed class ColorAsset { public long OwnerId; }
public sealed class Culture { public bool Integrated; }
public sealed class Kingdom
{
    public KingdomData data = new();
    public KingdomAsset asset = new();
    public City capital;
    public Culture culture = new();
    public bool Rekt;
    // Actual game contracts: NanoObject.id -> CoreSystemObject.getID -> data.id;
    // Kingdom.isCiv -> asset.civ. Dispose clears both data and asset.
    public long id => data.id;
    public bool isCiv() => asset.civ;
    public bool isRekt() => Rekt;
    public bool hasCities() => capital != null;
    public ColorAsset getColor()
    {
        if (asset == null) throw new NullReferenceException("disposed kingdom asset");
        return new() { OwnerId = data.id };
    }
    public void Dispose() { data = null; asset = null; Rekt = true; }
}
public sealed class City { public Kingdom kingdom; public float mark_scale_effect = 0.5f; }
public sealed class TileZone { public bool visible = true; }
public sealed class WorldTile { }
public sealed class Actor
{
    public ActorData data = new() { id = 11 };
    public Kingdom kingdom;
    public City city;
    public TileZone current_zone = new();
    public WorldTile current_tile = new();
    public Vector3 current_position;
    public bool Alive = true, King, CityLeader, Magnet, Baby, Egg;
    public bool isAlive() => Alive;
    public bool isKing() => King;
    public bool isCityLeader() => CityLeader;
    public bool isInMagnet() => Magnet;
    public bool isBaby() => Baby;
    public bool isEgg() => Egg;
}
public sealed class FavoriteUnits { public Actor[] array = Array.Empty<Actor>(); public int count; }
public sealed class ActorManager
{
    public FavoriteUnits visible_units_with_favorite = new();
    public readonly Dictionary<long, Actor> Entries = new();
    public Actor get(long id) => Entries.GetValueOrDefault(id);
}
public sealed class KingdomManager : IEnumerable<Kingdom>
{
    public readonly Dictionary<long, Kingdom> Entries = new();
    public Kingdom get(long id) => Entries.GetValueOrDefault(id);
    public IEnumerator<Kingdom> GetEnumerator() => Entries.Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
public sealed class MapBox { public KingdomManager kingdoms = new(); public ActorManager units = new(); }
public static class World { public static MapBox world; }
public sealed class QuantumSprite
{
    public Sprite Sprite;
    public float Scale;
    public void set(ref Vector3 position, float scale) => Scale = scale;
    public void setSprite(Sprite sprite) => Sprite = sprite;
}
public sealed class QuantumSpriteGroup
{
    public bool is_within_active_index = true;
    public readonly List<QuantumSprite> Drawn = new();
    public QuantumSprite getNext() { var sprite = new QuantumSprite(); Drawn.Add(sprite); return sprite; }
}
public sealed class QuantumSpriteAsset
{
    public QuantumSpriteGroup group_system = new();
    public float base_scale = 1, add_camera_zoom_multiplier_min = 0.5f, add_camera_zoom_multiplier_max = 2;
    public bool add_camera_zoom_multiplier, selected_city_scale;
}
public sealed class QuantumSpriteLibrary { }
public sealed class Camera { public float orthographicSize = 30; }
public sealed class MoveCamera { public static MoveCamera instance; public Camera main_camera; }
public sealed class BabyMaker { public Actor makeBaby() => null; }
public static class SpriteTextureLoader { public static Sprite getSprite(string path) => new() { Path = path }; }
public static class DynamicSprites
{
    public static readonly List<long> ColorOwners = new();
    public static Sprite getIcon(Sprite sprite, ColorAsset color) { ColorOwners.Add(color.OwnerId); return sprite; }
}
public static class PlayerConfig { public static bool Enabled = true; public static bool optionBoolEnabled(string key) => Enabled; }
namespace AncientWarfare3
{
    public static class ModClass { public static void LogError(string message) { } }
}
namespace AncientWarfare3.api.multiplayer
{
    public static class AW3MultiplayerReplicaScope { public static bool IsApplying; }
}
namespace AncientWarfare3.core.db { }
namespace AncientWarfare3.core.schools
{
    public static class HistoricalSchoolActorSpawnCapture { public static bool IsTargetActor(Actor actor) => false; }
}
namespace AncientWarfare3.content.figures
{
    public static class HistoricalFigureService { public static void TrySpawnOn(Actor actor, string source) { } }
    public static class FigureStateStore { public static int IndexOfActor(long id) => 0; }
}
namespace AncientWarfare3.core.policy
{
    public static class RecentFeatureBenchmark { public static long Begin() => 0; public static void End(int index, long start) { } }
    public static class RecentFeatureBenchmarkRules { public const int MinimapMarkersIndex = 0; }
}
namespace AncientWarfare3.core.lineage
{
    public static class SyntheticLevyService { public static bool IsSynthetic(Actor actor) => false; }
    public static class HistoricalFigureCardDeploymentService { public static bool IsActive => false; }
    public static class HierarchicalVassalMapModeService { public static bool IsActive() => false; }
    public static class XiaCultureIntegrationService { public static bool IsIntegrated(Culture culture) => culture?.Integrated == true; }
    public static class LineageKeys { public const string KINGDOM_HEIR_ID = "heir"; }
    public static class HeirService
    {
        public static Actor PeekStoredHeirForMinimap(Kingdom kingdom)
        {
            if (kingdom?.data == null) return null;
            Actor actor = World.world.units.get(kingdom.data.HeirId);
            return actor?.data != null && actor.isAlive() ? actor : null;
        }
    }
}

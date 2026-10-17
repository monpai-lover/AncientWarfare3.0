// Only the unavailable WorldBox/Unity boundary is substituted. The two
// production Harmony patches and their fallback/index logic are compiled intact.
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type, string method) { }
    }
    public sealed class HarmonyPrefix : Attribute { }
    public sealed class HarmonyPostfix : Attribute { }
    public sealed class HarmonyFinalizer : Attribute { }
}

namespace UnityEngine
{
    public sealed class Sprite { }
    public struct Vector2 { public float y; }
    public static class Mathf
    {
        public static float Clamp(float value, float minimum, float maximum) =>
            value < minimum ? minimum : value > maximum ? maximum : value;
    }
}

public sealed class ActorData
{
    public long id = 7;
    public int head = -1;
    public void get<T>(string key, out T value, T fallback) => value = fallback;
    public void set<T>(string key, T value) { }
}
public sealed class ActorAsset
{
    public string id = "xia";
    public bool has_override_sprite;
    public Func<Actor, Sprite> get_override_sprite;
    public float animation_swim_speed = 1;
    public float animation_walk_speed = 1;
    public float animation_idle_speed = 1;
    public bool animation_speed_based_on_walk_speed;
}
public sealed class ActorAnimation { public Sprite[] frames; }
public sealed class AnimationContainerUnit
{
    public string id = "test";
    public ActorAnimation idle, walking, swimming;
    public bool has_idle, has_walking, has_swimming, render_heads_for_children;
    public Dictionary<string, Sprite> sprites = new();
    public Sprite[] heads, heads_male, heads_female;
}
public sealed class ActorAction { public bool force_animation; public string force_animation_id; }
public sealed class ActorAi { public ActorAction action; }
public sealed class ActorFrameData { public bool show_head = true; }
public sealed class ActorTextureSubAsset
{
    public string texture_path_base = "xia/";
    public bool has_advanced_textures;
    public string texture_heads_male = "male", texture_heads_female = "female";
    public string texture_head_warrior = "warrior", texture_head_king = "king";
    public string texture_heads_old_male = "old_male", texture_heads_old_female = "old_female";
}
public sealed class EquipmentSlot { public bool isEmpty() => true; }
public sealed class ActorEquipment { public EquipmentSlot helmet = new(); }
public sealed class Actor
{
    public ActorData data = new();
    public ActorAsset asset = new();
    public AnimationContainerUnit animation_container;
    public ActorAi ai = new();
    public ActorFrameData frame_data = new();
    public ActorTextureSubAsset Texture = new();
    public ActorEquipment equipment = new();
    public Dictionary<string, float> stats = new() { ["speed"] = 5, ["lifespan"] = 80 };
    public bool Xia = true, Alive = true, Rekt, Sapient = true, Baby, HanEmperor;
    public bool _has_stop_idle_animation, _has_status_drowning, is_moving, is_in_magnet;
    public bool dirty_sprite_head = true, is_profession_warrior, is_profession_king;
    public float timer_jump_animation, position_height, Age = 20;
    public Vector2 move_jump_offset;
    public Sprite cached_sprite_head;
    public string TexturePath = "actor";
    public Action CheckContainer;
    public bool isAlive() => Alive;
    public bool isRekt() => Rekt;
    public bool isBaby() => Baby;
    public bool isEgg() => false;
    public bool isSapient() => Sapient;
    public bool isSexMale() => true;
    public bool isKing() => is_profession_king;
    public bool isCityLeader() => false;
    public bool isWarrior() => is_profession_warrior;
    public bool hasTrait(string key) => false;
    public bool isAffectedByLiquid() => false;
    public string getName() => "Actor";
    public string getUnitTexturePath() => TexturePath;
    public float getAge() => Age;
    public ActorTextureSubAsset getTextureAsset() => Texture;
    public void checkAnimationContainer() => CheckContainer?.Invoke();
    public void clearGraphicsFully() { }
    public void getHitFullHealth(AttackType type) { }
    public Sprite calculateMainSprite() => throw new NotSupportedException("Tests call the production prefix/finalizer directly.");
    public bool checkNaturalDeath() => false;
}
public enum AttackType { Age }
public static class AnimationHelper
{
    public static Sprite getSpriteFromList(int hash, Sprite[] frames, float speed) => frames[0];
    public static int getSpriteIndex(long id, int count) => (int)(id % count);
}
public static class ActorAnimationLoader
{
    public static Sprite getHeadSpecial(string path) => new();
    public static Sprite getHead(string path, int index) => new();
}
public static class SpriteTextureLoader
{
    public static Func<string, Sprite[]> Load = _ => Array.Empty<Sprite>();
    public static Func<string, Sprite> LoadSingle = _ => null;
    public static Sprite getSprite(string path) => LoadSingle(path);
    public static Sprite[] getSpriteList(string path, bool load) => Load(path);
}
public static class World { public static TestWorld world; }
public sealed class TestWorld { public ActorManager units = new(); }
public sealed class ActorManager { public Actor get(long id) => null; }
public static class WorldLawLibrary { public static WorldLaw world_law_old_age = new(); }
public sealed class WorldLaw { public bool isEnabled() => true; }
public static class Randy { public static bool randomChance(float chance) => false; }
namespace AncientWarfare3
{
    public static class ModClass { public static void LogWarning(string message) { } }
}
namespace AncientWarfare3.content
{
    public static class XiaRace { public const string TEXTURE_PATH = "xia/", ID = "xia"; }
}
namespace AncientWarfare3.core.lineage
{
    public static class HistoricalFigureCardIdentityService
    {
        public static bool IsHanEmperorCardActor(Actor actor) => actor?.HanEmperor == true;
    }
    public static class LineageService { public static bool IsXia(Actor actor) => actor?.Xia == true; }
    public static class LineageKeys
    {
        public const string IS_HEIR = "heir", TRAIT_SLAVE = "slave", DEATH_CAUSE = "death";
    }
    public static class ActorAgeWorkService { public static void Process(Actor actor) { } }
    public static class HistoryLocalizationRules { public static string Text(string key) => key; }
}
namespace AncientWarfare3.core.policy
{
    public static class UpdateAgeBenchmark
    {
        public static long Begin() => 0;
        public static void End(int index, long start) { }
    }
    public static class UpdateAgeBenchmarkRules { public const int ActorOldHeadIndex = 0; }
}

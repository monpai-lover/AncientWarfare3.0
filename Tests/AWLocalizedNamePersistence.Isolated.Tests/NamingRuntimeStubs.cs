// The parent iterator follows the shipped Assembly-CSharp Actor.getParents:
// it dereferences World.world.units even when both parent IDs are absent.
#pragma warning disable CS0649 // Unused game-model fields are intentionally left at their default values.
internal sealed partial class BaseSystemData
{
    internal long parent_id_1 = -1, parent_id_2 = -1;
    internal string original_actor_asset, founder_city_name, founder_kingdom_name,
        started_by_actor_name;
}

internal sealed partial class World
{
    internal UnitManager units = new UnitManager();
    internal EraManager era_manager;
}

public enum MetaType { Unit, City, Clan, Kingdom, Culture, Language, Religion }
public sealed class Actor
{
    internal BaseSystemData data = new BaseSystemData();
    internal ActorAsset asset = new ActorAsset();
    internal Culture culture;
    internal City city;
    internal Kingdom kingdom;
    internal bool Alive = true;
    internal bool hasCulture() => culture != null;
    internal string getName() => data.name;
    internal IEnumerable<Actor> getParents()
    {
        Actor first = World.world.units.get(data.parent_id_1);
        if (first != null && first.Alive) yield return first;
        Actor second = World.world.units.get(data.parent_id_2);
        if (second != null && second.Alive) yield return second;
    }
}

public sealed class UnitManager
{
    internal readonly Dictionary<long, Actor> Actors = new Dictionary<long, Actor>();
    internal Actor get(long id) => Actors.GetValueOrDefault(id);
}
public sealed class ActorAsset
{
    internal string id, name_locale;
    internal string Template = "species_name";
    internal string getNameTemplate(MetaType type) => Template;
    internal string getTranslatedName() => name_locale;
}
public sealed class Culture
{
    internal BaseSystemData data = new BaseSystemData();
    internal string Template = "culture_name";
    internal string getNameTemplate(MetaType type)
    {
        _ = data.name;
        return Template;
    }
}
public sealed class City
{
    internal BaseSystemData data;
    internal Actor leader;
    internal string name => data?.name;
    internal bool isRekt() => data == null;
}
public sealed class Kingdom
{
    internal BaseSystemData data;
    internal City capital;
    internal Actor king;
    internal readonly List<City> cities = new List<City>();
    internal long getID() => 1;
    internal IEnumerable<Kingdom> getEnemiesKingdoms() => Array.Empty<Kingdom>();
}
public sealed class Clan
{
    internal BaseSystemData data;
    internal readonly List<Actor> units = new List<Actor>();
}
public sealed class Alliance
{
    internal readonly HashSet<Kingdom> kingdoms_hashset = new HashSet<Kingdom>();
}
public sealed class War
{
    internal Kingdom main_attacker, main_defender;
    internal BaseSystemData data;
}
public sealed class Subspecies { internal string species_id; }
public sealed class Book { }
public sealed class Language { }
public sealed class Religion { }
public sealed class ItemData { internal string material, asset_id; }
public sealed class ItemAsset
{
    internal string name_class;
    internal string getLocaleID() => string.Empty;
}
internal static class AssetManager
{
    internal static ActorLibrary actor_library = new ActorLibrary();
    internal static MonthLibrary months = new MonthLibrary();
}
public sealed class ActorLibrary { internal ActorAsset get(string id) => null; }
public sealed class MonthLibrary { internal MonthAsset getMonth(int month) => null; }
public sealed class MonthAsset { internal string english_name; }
public sealed class EraManager { internal EraAsset getCurrentAge() => null; }
public sealed class EraAsset { internal string id; }
internal static class Date
{
    internal static int getCurrentMonth() => 1;
    internal static int getCurrentYear() => 1;
}
internal static class LocalizedTextManager
{
    internal static bool stringExists(string key) => false;
}
internal static class ModClass { internal static void LogWarning(string message) { } }
namespace NeoModLoader.General
{
    internal static class LM { internal static string Get(string key) => key; }
}

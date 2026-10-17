using System.Data.SQLite;

// The exam service, persistence, rules and table models are production code.
// These doubles supply only the native world and unrelated service boundaries.
internal sealed class Data
{
    private readonly Dictionary<string, int> values = new();
    public long id;
    public string name = "entity";
    public void get(string key, out int value, int fallback) =>
        value = values.TryGetValue(key, out int stored) ? stored : fallback;
    public void set(string key, int value) => values[key] = value;
}
internal sealed class Kingdom
{
    public Data data = new();
    public long id => data.id;
    public string name => data.name;
    public Actor king;
    public Actor heir = null;
    public IEnumerable<Actor> Units = Array.Empty<Actor>();
    public List<City> Cities = new();
    public bool isRekt() => false;
    public bool isNeutral() => false;
    public bool isCiv() => true;
    public IEnumerable<Actor> getUnits() => Units;
    public IEnumerable<City> getCities() => Cities;
}
internal sealed class Actor
{
    public Data data = new();
    public Kingdom kingdom;
    public City city;
    public Army army = null;
    public Dictionary<string, float> stats = new();
    public float Age = 30;
    public int Merit;
    public bool Alive = true, Adult = true, Male = true;
    public bool King = false, Prince = false, Slave = false, Retired = false, General = false, Captain = false;
    public bool isRekt() => data == null;
    public bool isAlive() => Alive;
    public bool isSexMale() => Male;
    public bool isAdult() => Adult;
    public float getAge() => Age;
    public bool isKing() => King;
    public string getName() => data.name;
    public bool isArmyGroupLeader() => Captain;
    public bool hasArmy() => army != null;
}
internal sealed class City
{
    public Data data = new();
    public Kingdom kingdom;
    public Army army = null;
    public bool isRekt() => data == null;
    public bool hasArmy() => army != null;
    public Army getArmy() => army;
}
internal sealed class Army
{
    public Actor captain = null;
    public Actor getCaptain() => captain;
}
internal static class Date
{
    public static int Year = 100;
    public static int getCurrentYear() => Year;
}
namespace AncientWarfare3.core.lineage
{
    internal sealed class LineageArchiveManager
    {
        public static LineageArchiveManager Instance = new();
        public SQLiteConnection OperatingDB;
    }
    internal static class LineageKeys
    {
        public const string CIVIL_SERVICE_EXAM_ANCHOR_YEAR = "anchor";
    }
    internal static class LineageService { public static double CurTime() => Date.Year * 360d; }
    internal static class MandateService { public static bool IsMandateKingdom(Kingdom kingdom) => false; }
    internal static class KingdomTitleService { public static bool IsEmperor(Kingdom kingdom) => false; }
    internal static class HeirService { public static Actor PeekRegisteredHeir(Kingdom kingdom) => kingdom.heir; }
    internal static class FeudatoryService { public static bool IsActivePrince(Actor actor) => actor.Prince; }
    internal static class SlaveService
    {
        public static bool IsSlave(Actor actor) => actor.Slave;
        public static bool IsRetiredSoldier(Actor actor) => actor.Retired;
    }
    internal static class GeneralService
    {
        public static bool IsGeneral(Actor actor) => actor.General;
        public static int GetMerit(Actor actor) => actor.Merit;
        public static List<Actor> GetActiveGenerals(Kingdom kingdom) => new();
    }
}

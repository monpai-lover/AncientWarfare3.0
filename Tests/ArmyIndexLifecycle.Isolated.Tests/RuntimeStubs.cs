using System.Collections;

// Match NanoObject.id -> getID() -> data.id: disposal makes id unsafe to read.
internal sealed class EntityData
{
    public long id;
    public void get(string key, out bool value, bool fallback) => value = fallback;
}

internal class Entity
{
    public EntityData data = new EntityData();
    public long id => data.id;
}

internal sealed class Kingdom : Entity
{
    public bool Rekt;
    public bool isRekt() => Rekt;
}

internal sealed class City : Entity
{
    public Kingdom kingdom;
    public Army Army;
    public bool hasArmy() => Army != null;
    public Army getArmy() => Army;
}

internal sealed class Actor : Entity
{
    public Kingdom kingdom { get; set; }
    public Army army;
    public bool isAlive() => data != null;
    public bool isRekt() => data == null;
}

internal sealed class Army : Entity
{
    public Kingdom Kingdom;
    public City Anchor;
    public Actor Captain;
    public bool Alive = true;
    public List<Actor> units = new List<Actor>();
    public Kingdom getKingdom() => Kingdom;
    public bool isAlive() => Alive;
    public Actor getCaptain() => Captain;
    public void Dispose()
    {
        data = null;
        Alive = false;
        Kingdom = null;
        Anchor = null;
        Captain = null;
        units.Clear();
    }
}

internal sealed class EntityStore<T> : IEnumerable<T> where T : Entity
{
    private readonly Dictionary<long, T> entities = new Dictionary<long, T>();
    public void Add(T entity) => entities.Add(entity.id, entity);
    public T get(long id) => entities.TryGetValue(id, out T entity) ? entity : null;
    public IEnumerator<T> GetEnumerator() => entities.Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class World
{
    public static World world;
    public EntityStore<Army> armies = new EntityStore<Army>();
    public EntityStore<Kingdom> kingdoms = new EntityStore<Kingdom>();
}

namespace AncientWarfare3.core.lineage
{
    internal static class Notices
    {
        public static readonly List<string> Calls = new List<string>();
        public static void Kingdom(string operation, Kingdom kingdom)
        {
            if (kingdom?.data != null) Calls.Add(operation + ":" + kingdom.id);
        }
    }

    internal static class KingdomWarDirectorService
    {
        public static void QueueArmyChanged(Kingdom kingdom) => Notices.Kingdom("war", kingdom);
    }

    internal static class ArmyReplenishmentOperationService
    {
        // Adjacent production services need a valid id too; catch unsafe forwarding.
        public static void OnArmyKingdomChanged(Army army) => Notices.Calls.Add("owner:" + army.id);
        public static void OnArmyDisposed(Army army) => Notices.Calls.Add("dispose:" + army.id);
    }

    internal static class CoalitionWarTaskService
    {
        public static void OnArmyInvalidated(long id) => Notices.Calls.Add("invalidate:" + id);
    }

    internal static class StandingArmyService
    {
        public static void OnFieldArmyChanged(Kingdom kingdom) => Notices.Kingdom("field", kingdom);
        public static void ScheduleCityDuplicateMerge(Kingdom kingdom, Army duplicate, Army canonical)
            => Notices.Calls.Add("merge:" + duplicate.id + ":" + canonical.id);
    }

    internal static class AWArmyService
    {
        public static bool IsSpecialArmy(Army army) => false;
        public static bool IsSpecialArmyCreationInProgress(Army army) => false;
        public static long GetAnchorCityId(Army army) => army.Anchor?.id ?? -1;
        public static void AddToArmy(Actor actor, Army army) => actor.army = army;
    }

    internal static class GarrisonSortieService
    {
        public static bool IsSortieArmy(Army army) => false;
    }

    internal static class TemporarySlaveVanguardService
    {
        public static bool IsMember(Actor actor) => false;
    }

    internal enum MilitaryRecruitmentKind { None, StandingArmy, TemporaryLevy }
    internal static class MilitaryRecruitmentScope
    {
        public static MilitaryRecruitmentKind Current => MilitaryRecruitmentKind.None;
    }
}

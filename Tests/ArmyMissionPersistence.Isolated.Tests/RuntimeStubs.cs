using System.Collections;
using AncientWarfare3.core.lineage;

internal sealed class EntityData
{
    public long id;
    private readonly Dictionary<string, object> values = new();
    public void set<T>(string key, T value) => values[key] = value;
    public void get<T>(string key, out T value, T fallback) => value = values.TryGetValue(key, out var found) ? (T)found : fallback;
    public void removeLong(string key) => values.Remove(key);
    public void removeInt(string key) => values.Remove(key);
    public void removeBool(string key) => values.Remove(key);
    public void removeString(string key) => values.Remove(key);
}
internal class Entity
{
    public EntityData data = new();
    // Native NanoObject.id -> CoreSystemObject.getID -> data.id.
    public long id => data.id;
    public bool Alive = true;
    public bool isAlive() => Alive && data != null;
    public bool isRekt() => data == null || !Alive;
}
internal sealed class Kingdom : Entity { }
internal sealed class City : Entity { public Kingdom kingdom; }
internal sealed class Army : Entity
{
    public Kingdom Kingdom;
    public Kingdom getKingdom() => Kingdom;
}
internal sealed class War : Entity
{
    public bool Ended;
    public readonly HashSet<Kingdom> Participants = new();
    public bool hasEnded() => Ended;
    public bool hasKingdom(Kingdom kingdom) => Participants.Contains(kingdom);
}
internal sealed class EntityStore<T> : IEnumerable<T> where T : Entity
{
    private readonly Dictionary<long, T> values = new();
    public void Add(T entity) => values[entity.id] = entity;
    public T get(long id) => values.TryGetValue(id, out var entity) ? entity : null;
    public IEnumerator<T> GetEnumerator() => values.Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
internal sealed class World
{
    public static World world;
    public EntityStore<Army> armies = new();
    public EntityStore<War> wars = new();
    public EntityStore<City> cities = new();
}
namespace AncientWarfare3.api.multiplayer
{
    internal static class AW3MultiplayerReplicaScope { public static bool IsReplicaSession; }
}
namespace AncientWarfare3.core.lineage
{
    internal static class LineageService { public static double Now = 123; public static double CurTime() => Now; }
    internal static class KingdomWarDirectorService
    {
        public static readonly List<Kingdom> Changed = new();
        public static void OnArmyChanged(Kingdom kingdom)
        {
            if (kingdom?.data == null) throw new InvalidOperationException("Invalid kingdom notified.");
            Changed.Add(kingdom);
        }
    }
    internal static class ArmyRtsControllerService
    {
        public static bool TargetAllowed = true;
        public static bool ValidateMissionTarget(Army army, ArmyRtsMission mission) => TargetAllowed;
    }
}

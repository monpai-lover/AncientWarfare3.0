using System.Collections;
using AncientWarfare3.core.lineage;

// These stubs expose game storage and callbacks. Transaction preparation,
// persistence, recovery, army selection and demobilization run in production.
public sealed class Data
{
    public long id;
    private readonly Dictionary<string, object> values = new();
    private readonly Dictionary<string, int> readCounts = new();
    public int ReadCount(string key) => readCounts.GetValueOrDefault(key);
    public void set<T>(string key, T value) => values[key] = value;
    public void get<T>(string key, out T value, T fallback = default)
    {
        readCounts[key] = ReadCount(key) + 1;
        value = values.TryGetValue(key, out object stored) && stored is T typed
            ? typed : fallback;
    }
}

public class Entity
{
    public Data data = new();
    public long id => data.id;
}

public sealed class Store<T> : IEnumerable<T> where T : Entity
{
    private readonly Dictionary<long, T> values = new();
    public int Count => values.Count;
    public T get(long id) => values.GetValueOrDefault(id);
    public void Add(T value) => values.Add(value.id, value);
    public void Remove(long id) => values.Remove(id);
    public void Clear() => values.Clear();
    public IEnumerator<T> GetEnumerator() => values.Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class World
{
    public static World world;
    public Store<Army> armies = new();
    public Store<Actor> units = new();
    public Store<City> cities = new();
    public Store<Kingdom> kingdoms = new();
    public Store<War> wars = new();
}

public sealed class Kingdom : Entity { }
public sealed class War : Entity
{
    public HashSet<Kingdom> Participants = new();
    public bool Ended;
    public bool hasEnded() => Ended;
    public bool hasKingdom(Kingdom kingdom) => Participants.Contains(kingdom);
}

public sealed class City : Entity
{
    public Kingdom kingdom;
    public Army Army;
    public int TransferCalls;
    public bool FailTransfer;
    public Action BeforeTransfer;
    public Action AfterTransfer;
    public bool isRekt() => false;
    public void joinAnotherKingdom(Kingdom receiver, bool pCaptured, bool pRebellion)
    {
        TransferCalls++;
        BeforeTransfer?.Invoke();
        if (FailTransfer) throw new InvalidOperationException("injected transfer failure");
        kingdom = receiver;
        AfterTransfer?.Invoke();
    }
}

public sealed class Army : Entity
{
    public Kingdom Kingdom;
    public City Anchor;
    public string Role;
    public List<Actor> units = new();
    public Actor Captain;
    public int countUnits() => units.Count;
    public Actor getCaptain() => Captain;
    public Kingdom getKingdom() => Kingdom;
}

public enum AttackType { Other }
public sealed class Actor : Entity
{
    public Army army;
    public Kingdom kingdom;
    public int warfare;
    public bool Alive = true;
    public bool Warrior = true;
    public bool King;
    public bool CityLeader;
    public bool Synthetic;
    public bool SyntheticRemoved;
    public int DeathCalls;
    public bool isAlive() => Alive;
    public bool isRekt() => !Alive;
    public bool isKing() => King;
    public bool isCityLeader() => CityLeader;
    public bool isWarrior() => Warrior;
    public void stopBeingWarrior() => Warrior = false;
    public void removeFromArmy()
    {
        army?.units.Remove(this);
        army = null;
    }
    public void die(bool a, AttackType b, bool c, bool d)
    {
        DeathCalls++;
        Alive = false;
        removeFromArmy();
    }
}

namespace AncientWarfare3.api.multiplayer
{
    internal static class AW3MultiplayerReplicaScope
    {
        public static bool IsReplicaSession;
    }
}

namespace AncientWarfare3.core.court
{
    public readonly struct CustomCourtEffectModifier
    {
        public static CustomCourtEffectModifier Identity => default;
        public float AdditiveFlat => 0;
        public float AdditivePercent => 0;
        public float MultiplicativeFactor => 1;
        public bool HasMultiplier => false;
        public float Apply(float value) => value;
    }
    internal static class CustomCourtRuntimeEffectService
    {
        public static CustomCourtEffectModifier GetArmyModifier(Kingdom kingdom) => default;
    }
}

namespace AncientWarfare3.core.pathfinding
{
    internal enum ArmyRouteCancelReason { TargetReplaced }
    internal static class ArmyRouteProviderService
    {
        public static readonly HashSet<long> Cancelled = new();
        public static void Cancel(long id, ArmyRouteCancelReason reason) => Cancelled.Add(id);
    }
}

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyRtsWarDoctrine
    {
        public static bool IsAbstractDecisive = true;
    }
    internal enum ArmyRtsProposalKind { Attack, Defend, Retreat }
    internal sealed class ArmyRtsMission
    {
        public long ArmyId;
        public long WarId;
        public long TargetCityId;
        public ArmyRtsProposalKind ProposalKind;
    }
    internal enum ArmyRtsObjectiveState { OpenAttack, Complete }
    internal static class ArmyRtsObjectiveService
    {
        public static ArmyRtsObjectiveState Classify(War war, Kingdom kingdom, City target) =>
            kingdom != target.kingdom ? ArmyRtsObjectiveState.OpenAttack : ArmyRtsObjectiveState.Complete;
    }
    internal static class ArmyRtsControllerService
    {
        public static readonly List<ArmyRtsMission> Missions = new();
        public static readonly HashSet<long> Invalidated = new();
        public static IReadOnlyList<ArmyRtsMission> SnapshotMissions() => Missions.ToArray();
        public static void OnTargetCompleted(City city) =>
            Missions.RemoveAll(m => m.TargetCityId == city.id);
        public static void Invalidate(long id)
        {
            Invalidated.Add(id);
            Missions.RemoveAll(m => m.ArmyId == id);
        }
    }
    internal static class ArmyFieldIndexService
    {
        public static bool TryGetCityArmy(City city, out Army army)
        {
            army = city.Army;
            return army != null;
        }
    }
    internal static class AWArmyService
    {
        public static Kingdom GetIntendedKingdom(Army army) => army?.Kingdom;
        public static long GetAnchorCityId(Army army) => army.Anchor?.id ?? -1;
        public static List<Army> GetRoleArmies(Kingdom kingdom, string role) =>
            World.world.armies.Where(a => a.Kingdom == kingdom && a.Role == role).ToList();
    }
    internal static class AWArmyMarchService { public static void ClearArmy(long id) { } }
    internal static class ArmyRtsTransportService { public static void ReleaseArmy(Army army) { } }
    internal static class ArmyFormationService { public static void RemoveArmy(long id) { } }
    internal static class ArmyRtsWarLifecycleService
    {
        public static readonly HashSet<long> Cleared = new();
        public static void ClearArmy(Army army) => Cleared.Add(army.id);
    }
    internal static class GeneralService { public static bool IsActiveGeneralFast(Actor actor) => false; }
    internal static class HeirService { public static bool IsCurrentHeir(Kingdom kingdom, Actor actor) => false; }
    internal sealed class ArmyCaptainDisposalScope : IDisposable
    {
        public static ArmyCaptainDisposalScope Open(Army army) => new();
        public void Dispose() { }
    }
    internal static class SyntheticLevyService
    {
        public static bool IsSynthetic(Actor actor) => actor.Synthetic;
        public static void RemoveWithoutPersonalHistory(Actor actor)
        {
            actor.SyntheticRemoved = true;
            actor.Alive = false;
            actor.removeFromArmy();
        }
    }
    internal static class LineageKeys
    {
        public const string AW_RTS_ABSTRACT_BATTLE_OPERATION = nameof(AW_RTS_ABSTRACT_BATTLE_OPERATION);
        public const string AW_RTS_ABSTRACT_BATTLE_PHASE = nameof(AW_RTS_ABSTRACT_BATTLE_PHASE);
        public const string AW_RTS_ABSTRACT_BATTLE_SEQUENCE = nameof(AW_RTS_ABSTRACT_BATTLE_SEQUENCE);
        public const string AW_RTS_ABSTRACT_BATTLE_PARTICIPANT_HASH = nameof(AW_RTS_ABSTRACT_BATTLE_PARTICIPANT_HASH);
        public const string AW_RTS_ABSTRACT_BATTLE_TRANSACTION = nameof(AW_RTS_ABSTRACT_BATTLE_TRANSACTION);
        public const string IS_HEIR = nameof(IS_HEIR);
        public const string COURT_OFFICE_ID = nameof(COURT_OFFICE_ID);
    }
}

internal static class ModClass
{
    public static void LogWarning(string message) { }
}

using AncientWarfare3.core.lineage;

// Only the engine and adjacent services are stubbed. The production recovery
// service owns queue ordering, cooldowns, candidate selection and retry state.
public sealed class Data
{
    public long id;
    private readonly Dictionary<string, object> values = new();
    public void set<T>(string key, T value) => values[key] = value;
    public void get<T>(string key, out T value, T fallback = default) =>
        value = values.TryGetValue(key, out object stored) && stored is T typed ? typed : fallback;
}
public class Entity { public Data data = new(); public long id => data.id; }
public sealed class Store<T> where T : Entity
{
    public readonly Dictionary<long, T> Items = new();
    public T get(long id) => Items.GetValueOrDefault(id);
    public void Add(T entity) => Items.Add(entity.id, entity);
}
public sealed class World
{
    public static World world = new();
    public Store<Army> armies = new();
    public Store<Kingdom> kingdoms = new();
}
public sealed class Kingdom : Entity
{
    public Actor king;
    public City capital;
    public List<City> cities = new();
    public bool isRekt() => false;
}
public sealed class City : Entity { }
public sealed class Army : Entity
{
    public Kingdom Kingdom;
    public Actor Captain;
    public List<Actor> Members = new();
    public int CaptainChecks;
    public int countUnits() => Members.Count;
    public Actor getCaptain() => Captain;
    public void setCaptain(Actor actor) => Captain = actor;
    public IEnumerable<Actor> getUnits() => Members;
    public void checkCaptainExistence() => CaptainChecks++;
}
public sealed class Actor : Entity
{
    public Kingdom kingdom;
    public Army army;
    public bool Adult = true;
    public bool Alive = true;
    public bool is_profession_warrior = true;
    public bool Synthetic;
    public bool General;
    public Dictionary<string, float> stats = new();
    public bool isAlive() => Alive;
    public bool isRekt() => !Alive;
    public bool isAdult() => Adult;
    public bool isKing() => false;
    public bool isCityLeader() => false;
}
internal static class ModClass { public static void LogError(string message) { } }
namespace AncientWarfare3.content.schools { internal static class NamespaceStub { } }
namespace AncientWarfare3.core.schools { internal static class NamespaceStub { } }
namespace AncientWarfare3.core.performance
{
    internal static class AWPerformanceSettings { public static bool ArmyRtsDiagnosticsEnabled => false; }
}
namespace AncientWarfare3.core.lineage
{
    public enum ArmyRtsMode { Off, Shadow, On }
    internal static class ArmyRtsRuntimeMode { public static bool ShouldCommit = true; }
    internal enum ArmyRtsProposalKind { Attack, Retreat }
    internal enum ArmyRtsWithdrawalOrigin { MinimumForce }
    internal sealed class ArmyRtsMission
    {
        public long ArmyId;
        public long KingdomId;
        public long WarId = 1;
        public ArmyRtsProposalKind ProposalKind { get; set; }
    }
    internal static class ArmyRtsControllerService
    {
        public static readonly List<ArmyRtsMission> Missions = new();
        public static readonly List<long> Rehydrated = new();
        public static IReadOnlyList<ArmyRtsMission> SnapshotMissions() => Missions.ToArray();
        public static bool HasActiveMission(long id) => Missions.Any(m => m.ArmyId == id);
        public static void RehydrateAfterAuthorityChange(Army army) => Rehydrated.Add(army.id);
    }
    internal static class ArmyRtsAssignmentReconciliationService
    {
        public static readonly List<long> Enqueued = new();
        public static void Enqueue(Army army) => Enqueued.Add(army.id);
    }
    internal sealed class ArmyStrategicIdCursor { public int Remaining; }
    internal static class ArmyStrategicIndexService
    {
        public static ArmyStrategicIdCursor CreateSnapshotCursor(Kingdom kingdom) =>
            new() { Remaining = World.world.armies.Items.Values.Count(a => a.Kingdom == kingdom) };
        public static void CopyArmyIdsAfter(Kingdom kingdom, long after, int maximum,
            List<long> destination, out bool complete)
        {
            var ids = World.world.armies.Items.Values.Where(a => a.Kingdom == kingdom && a.id > after)
                .Select(a => a.id).Order().ToArray();
            destination.AddRange(ids.Take(maximum));
            complete = ids.Length <= maximum;
        }
        public static Army ResolveIndexedArmy(long id, long kingdomId) => World.world.armies.get(id);
    }
    internal static class KingdomWarDirectorService
    {
        public static readonly List<long> Changed = new();
        public static void Schedule(Kingdom kingdom) { }
        public static void QueueArmyChanged(Kingdom kingdom) => Changed.Add(kingdom.id);
    }
    internal static class AWArmyService
    {
        public static Kingdom GetIntendedKingdom(Army army) => army?.Kingdom;
        public static bool IsCaptainLeaseEligible(Army army, Actor actor, bool requireMembership) =>
            actor?.data != null && actor.Adult && actor.is_profession_warrior &&
            (!requireMembership || actor.army == army);
        public static void AddToArmy(Actor actor, Army army) { army.Members.Add(actor); actor.army = army; }
        public static void SetCaptainIfChanged(Army army, Actor actor) => army.Captain = actor;
        public static void SetCaptainAfterSuccession(Army army, Actor actor) => army.Captain = actor;
    }
    internal sealed class ArmyCaptainDisposalScope : IDisposable
    {
        public static ArmyCaptainDisposalScope Open(Army army) => new();
        public void Dispose() { }
    }
    internal static class MilitaryEmergencyService { public static bool HasAny(Kingdom kingdom) => kingdom != null; }
    internal static class ArmyLogisticsRules { public const int MinimumOperationalForce = 6; }
    internal static class ArmyRetreatService
    {
        public static void AssignArmyRetreat(Army army, long id, ArmyRtsWithdrawalOrigin origin) { }
    }
    internal static class ArmyFieldIndexService
    {
        public static bool TryGetCityArmy(City city, out Army army) { army = null; return false; }
    }
    internal static class TemporaryLevyService
    {
        public static readonly List<long> Requests = new();
        public static bool ThrowRequest;
        public static bool AcceptRequest = true;
        public static bool RequestOffensiveRecovery(Kingdom kingdom, City city, int count, bool pForceEstablishment)
        {
            if (ThrowRequest) throw new InvalidOperationException("injected temporary recruitment failure");
            if (!AcceptRequest) return false;
            Requests.Add(kingdom.id);
            return true;
        }
        public static bool TryPromoteExistingLevyCaptain(Army army) => false;
        public static void PromoteToPermanentMilitary(Actor actor) { }
    }
    internal sealed class GeneralReadModelEntry { public Actor Actor { get; set; } }
    internal static class GeneralService
    {
        public static List<GeneralReadModelEntry> GetActiveGeneralsForReadModel(Kingdom kingdom,
            bool pAllowUnitFallback, int pLimit) => new();
        public static bool IsGeneral(Actor actor) => actor.General;
        public static void PromoteToGeneral(Actor actor) => actor.General = true;
    }
    internal static class SyntheticLevyService
    {
        public static bool IsSynthetic(Actor actor) => actor?.Synthetic == true;
        public static void PromoteToPermanentCommand(Actor actor) { if (actor != null) actor.Synthetic = false; }
    }
    internal static class SlaveService { public static bool IsSlave(Actor actor) => false; }
    internal static class RoyalGuardService { public static bool IsRoyalGuard(Actor actor) => false; }
    internal static class WartimeGarrisonService { public static bool IsActive(Actor actor) => false; }
    internal static class TemporarySlaveVanguardService { public static bool IsMember(Actor actor) => false; }
    internal static class LineageKeys { public const string TEMPORARY_LEVY = nameof(TEMPORARY_LEVY); }
}

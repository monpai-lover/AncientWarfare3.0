// Minimal game boundary doubles. The appointment selection, reservation session,
// SQL occupancy query and native leader hook are compiled from production source.
using System.Data.SQLite;
using AncientWarfare3.core.court;
public class Data
{
    public long id;
    public readonly Dictionary<string, object> Values = new();
    public void get<T>(string key, out T value, T fallback) => value = Values.TryGetValue(key, out object found) ? (T)found : fallback;
}
public class Actor
{
    public Data data = new();
    public Kingdom kingdom;
    public City city;
    public bool Alive = true, Leader, Strict, Eligible = true;
    public int Rank;
    public Actor clan;
    public Dictionary<string, float> stats = new() { ["intelligence"] = 0, ["stewardship"] = 0, ["warfare"] = 0, ["diplomacy"] = 0 };
    public bool isSexMale() => true;
    public bool hasTrait(string name) => false;
    public bool isCityLeader() => Leader;
    public bool isAlive() => Alive;
    public bool isRekt() => false;
    public bool isAdult() => true;
    public bool isKing() => false;
    public bool hasClan() => false;
}
public class Kingdom { public Data data = new(); public long id => data.id; public City capital; public List<Actor> Units = new(); public IEnumerable<Actor> getUnits() => Units; public bool Destroyed; public bool isRekt() => Destroyed; public bool isNeutral() => false; public IEnumerable<City> getCities() => Array.Empty<City>(); }
public class City
{
    public Data data = new(); public Kingdom kingdom; public Actor leader; public bool Captured;
    public bool isRekt() => false;
    public bool hasLeader() => leader != null;
    public bool isGettingCaptured() => Captured;
    public void removeLeader() => leader = null;
    public int getPopulationPeople() => 100;
    public int countZones() => 25;
}
public class ActorBook { public Actor get(long id) => null; }
public class EntityBook<T> : IEnumerable<T> where T : class
{
    public readonly Dictionary<long, T> Items = new();
    public T get(long id) => Items.GetValueOrDefault(id);
    public IEnumerator<T> GetEnumerator() => Items.Values.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
public class World
{
    public static World world = new(); public ActorBook units = new();
    public EntityBook<Kingdom> kingdoms = new(); public EntityBook<City> cities = new();
}
public static class ModClass { public static void LogError(string message) {} }
namespace UnityEngine { public static class Time { public static int frameCount; } }

public static class Date { public static int Year = 1; public static int getCurrentYear() => Year; }
namespace ai { public class Placeholder {} }
namespace ai.behaviours { public class CityBehCheckLeader {} }
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class HarmonyPatch : Attribute { public HarmonyPatch() {} public HarmonyPatch(Type type, string name) {} }
    public class HarmonyPrefix : Attribute {}
}
namespace AncientWarfare3.core.db { public static class CourtOfficerTableItem { public static string GetTableName() => "Officers"; } }
namespace AncientWarfare3.core.lineage
{
    public class LineageArchiveManager { public static LineageArchiveManager Instance = new(); public SQLiteConnection OperatingDB; }
    public static class LineageKeys { public const string COURT_OFFICE_ID = "office", OFFICER_MERIT = "merit", OFFICER_NATIVE_CITY_ID = "native", SHI_ID = "shi"; }
}
namespace AncientWarfare3.core.county
{
    public class CountyRecord { public bool Active = true; public long CountyId; }
    public static class CountyAdministrationService { public static IEnumerable<CountyRecord> CountiesForCity(long id) => new[] { new CountyRecord { CountyId = 20 } }; }
}
namespace AncientWarfare3.core.schools
{
    internal static class SchoolGuestOfficeService
    {
        internal class VacancyCandidateSession {}
        internal static VacancyCandidateSession CreateVacancyCandidateSession(Kingdom kingdom) => new();
    }
}
namespace AncientWarfare3.core.court
{
    internal static class CourtService
    {
        internal class CandidateSelectionCache {}
        internal static HashSet<string> BuildActiveOfficeSet(Kingdom kingdom, List<Actor> roster) => new();
        internal static IEnumerable<Actor> LoadLocalFormalCandidates(Kingdom kingdom) => kingdom.Units.Where(actor => actor?.data != null && actor.Strict);
        internal static HashSet<long> Occupied = new();
        internal static bool Commit = true, ThrowOnAppointment;
        internal static int AppointmentAttempts;
        internal static Actor Appointed;
        internal static bool Promotion;
        internal static HashSet<long> BuildActiveOfficerActorSetForKingdom(Kingdom kingdom) => new(Occupied);
        internal static bool IsExplicitConcurrentOffice(Actor actor, CourtVacancyKey vacancy) => false;
        internal static string ResolveCityOffice(Kingdom kingdom, City city) => "chief";
        internal static bool HasNineRankSystem(Kingdom kingdom) => true;
        private static bool Appoint(Actor actor, bool promotion)
        {
            AppointmentAttempts++;
            if (ThrowOnAppointment) throw new InvalidOperationException("database temporarily unavailable");
            Appointed = actor; Promotion = promotion; return Commit;
        }
        internal static IReadOnlyList<CourtOfficerView> GetActiveOfficers(Kingdom kingdom, int limit) => Array.Empty<CourtOfficerView>();
        internal static string[] CentralOfficeIdsForCurrentProfile(Kingdom kingdom) => Array.Empty<string>();
        internal static string[] MilitaryOfficeIdsForCurrentProfile(Kingdom kingdom) => Array.Empty<string>();
        internal static CourtVacancyOutcome TryFillRegisteredCentralVacancy(Kingdom kingdom, CourtVacancyKey key, CourtCandidateSession session) => CourtVacancyOutcome.NoCandidate;
        internal static bool TryAssignCountyMagistrate(Actor actor, Kingdom kingdom, City city, long county, bool promotion) => Appoint(actor, promotion);
        internal static bool TryAssignLocalOfficerRecord(Actor actor, Kingdom kingdom, City city, string office, bool promotion) => Appoint(actor, promotion);
        internal static bool TryAssignLocalOfficer(Actor actor, Kingdom kingdom, City city, string office, bool promotion) => Appoint(actor, promotion);
    }
    internal static class ManualLocalChiefAppointmentService { internal static bool TryAppoint(Kingdom kingdom, City city, Actor candidate, Func<bool> persist) => persist(); }
    internal class CivilServiceQualificationRecord { internal string Qualification = "jinshi"; }
    internal static class CivilServiceQualificationService
    {
        internal static bool CanReceiveFormalCivilAppointment(Actor actor, Kingdom kingdom, string layer, string pOfficeId, bool pAllowVacancyPromotion, CivilServiceQualificationRecord pQualification = null, bool pQualificationsCaptured = false, bool pAllowLocalLowerQualification = false, City pCity = null, CourtCandidateSession pServiceHistorySession = null, CourtAppointmentContext pContext = default) => actor.Eligible && (actor.Strict || pAllowVacancyPromotion);
        internal static bool HasExaminationSystem(Kingdom kingdom) => true;
        internal static CourtServiceHistory LoadServiceHistory(Actor actor, Kingdom kingdom) => new(false, false);
        internal static CivilServiceQualificationRecord LoadOrRepair(Actor actor, Kingdom kingdom) => actor.Strict ? new() : null;
    }
    internal static class OfficialCareerStateService
    {
        internal static int OfficeGradeForOffice(Kingdom kingdom, string layer, string office, City city) => 20;
        internal static bool IsRegionalGovernorSeat(Kingdom kingdom, string layer, string office, City city) => false;
        internal static int ReadRankFast(Actor actor) => actor?.data != null ? actor.Rank : 0;
    }
    internal static class OfficialCareerRankRules
    {
        internal const int Unranked = 0;
        internal static int RequiredRankForLocalOfficeGrade(int grade) => 1;
        internal static int ResolveLocalVacancyPromotionRank(int rank, int grade, bool nine, bool qualified, bool vacancyPromotion, bool regionalGovernor) => rank;
    }
    internal static class LocalChiefOfficeResolver { internal static IReadOnlyList<string> ResolveOrderedSeats(Kingdom kingdom, City city, int capacity) => new[] { "chief", "local" }; }
    internal static class CourtRules { internal static int CityOfficeSlots(int population, int zones, bool capital) => 2; }
    internal static class CourtAffiliationResolver { internal static bool CanServe(Actor actor, Kingdom kingdom, string layer) => actor.kingdom == kingdom; }
    internal static class RoyalGuardOfficeRules { internal static bool CanAppearInOfficeCandidateList(bool guard) => !guard; }
    internal static class RoyalGuardService { internal static bool IsRoyalGuard(Actor actor) => false; }
    internal static class RoyalAsylumRules { internal static bool CanPerformProtectedRole(bool asylum) => !asylum; }
    internal static class RoyalAsylumService { internal static bool IsActive(Actor actor) => false; }
    internal static class SlaveService { internal static bool IsSlave(Actor actor) => false; }
    internal static class HeirService { internal static Actor PeekRegisteredHeir(Kingdom kingdom) => null; }
    internal class OfficialCareerPrior
    {
        internal long KingdomId = -1, CityId = -1, CountyId = -1;
        internal string OfficeId = "", Layer = "";
    }
    internal class CourtOfficerView { internal string layer = "", office_id = ""; }
    internal static class CityGovernorProjectionRepairService
    {
        internal static int Repairs;
        internal static void OnLeaderAssigned(City city, Actor actor, bool pNewAssignment) { if (pNewAssignment) Repairs++; }
    }
}

namespace AncientWarfare3.core.lineage
{
    internal enum DeferredWorkClass { Runtime }
    internal static class DeferredRuntimeWorkService
    {
        internal static readonly Dictionary<string, Action> Pending = new();
        internal static void EnqueueCoalesced(string key, DeferredWorkClass workClass, Action action) => Pending[key] = action;
        internal static void RunPending()
        {
            Action[] work = Pending.Values.ToArray();
            Pending.Clear();
            foreach (Action action in work) action();
        }
    }
}

namespace AncientWarfare3.core.court
{
    internal readonly struct CourtServiceHistory { internal CourtServiceHistory(bool city, bool county) { } }
    internal static class CourtManualAppointmentRules
    { internal static bool CanUseLayerCandidate(string layer, bool cityLeader) => layer != CourtOfficeLayer.County || !cityLeader; }
}
namespace AncientWarfare3.core.lineage
{
    internal static class HistoricalFigureCardIdentityService { internal static bool IsMinisterCardActor(Actor actor) => false; }
    internal static class HistoricalFigureCardRoleRules { internal const int MinisterCandidateBonus = 0; }
}
namespace AncientWarfare3.core.performance { internal static class AWDiagnosticsGate { internal static bool Enabled => false; } }
namespace AncientWarfare3.core.lineage
{
    internal static class SuccessionPoolService { internal static void ClearRuntime() {} }
    internal static class InheritanceCandidateService { internal static void ClearRuntime() {} }
    internal static class HeirService { internal static void ClearSuccessionPoolRepairs() {} }
    internal static class GeneralService { internal static void ClearCandidatePools() {} }
}

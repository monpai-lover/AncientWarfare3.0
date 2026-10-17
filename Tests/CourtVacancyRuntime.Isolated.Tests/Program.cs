using System.Data.SQLite;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.patch;

using var db = new SQLiteConnection("Data Source=:memory:");
db.Open();
using (var command = new SQLiteCommand(db))
{
    command.CommandText = "CREATE TABLE Officers (ACTOR_ID INTEGER, OFFICE_ID TEXT, KINGDOM_ID INTEGER, CITY_ID INTEGER, COUNTY_ID INTEGER, LAYER TEXT, ACTIVE INTEGER, APPOINTED_TIME REAL, OFFICER_ID INTEGER)";
    command.ExecuteNonQuery();
}
LineageArchiveManager.Instance.OperatingDB = db;
var kingdom = new Kingdom { data = new Data { id = 1 } };
var city = new City { data = new Data { id = 10 }, kingdom = kingdom };
Actor Make(long id, bool strict, float score) => new Actor { data = new Data { id = id }, kingdom = kingdom, city = city, Strict = strict, stats = new() { ["intelligence"] = score, ["stewardship"] = 0, ["warfare"] = 0, ["diplomacy"] = 0 } };
void Check(bool value, string message) { if (!value) throw new Exception(message); }
foreach (string layer in new[] { CourtOfficeLayer.City, CourtOfficeLayer.County })
foreach (bool chief in new[] { false, true })
{
    if (layer == CourtOfficeLayer.County && chief) continue;
    var vacancy = new CourtVacancyKey(1, 10, layer == CourtOfficeLayer.County ? 20 : -1, layer, layer == CourtOfficeLayer.County ? CourtOfficeId.CountyMagistrate : chief ? "chief" : "local", chief);
    Actor fallback = Make(1, false, 100), strict = Make(2, true, 1);
    kingdom.Units = new() { fallback, strict };
    OfficerCandidateCatalog.Invalidate(kingdom);
    CourtService.Commit = true;
    CourtService.Occupied.Clear();
    var session = new CourtCandidateSession(kingdom);
    Check(LocalCourtAppointmentService.TryFillRegisteredLocalVacancy(kingdom, city, vacancy, session) == CourtVacancyOutcome.Filled, "strict appointment fills vacancy");
    Check(CourtService.Appointed == strict && !CourtService.Promotion, "strict candidate wins over stronger promotion-only candidate");
    Check(session.ReservedActorIds.Contains(strict.data.id), "committed appointment reserves actor in the shared session");
    Check(LocalCourtAppointmentService.TryFillRegisteredLocalVacancy(kingdom, city, vacancy, session) == CourtVacancyOutcome.Filled, "fallback fills next seat");
    Check(CourtService.Appointed == fallback && CourtService.Promotion, "reserved strict candidate is excluded and promotion flag persists");
    Check(LocalCourtAppointmentService.TryFillRegisteredLocalVacancy(kingdom, city, vacancy, session) == CourtVacancyOutcome.NoCandidate, "a session cannot reuse either appointed actor");
    CourtService.Commit = false;
    OfficerCandidateCatalog.Invalidate(kingdom);
    session = new CourtCandidateSession(kingdom);
    Check(LocalCourtAppointmentService.TryFillRegisteredLocalVacancy(kingdom, city, vacancy, session) == CourtVacancyOutcome.TechnicalFailure, "failed persistence reports retryable failure");
    Check(session.ReservedActorIds.Count == 0, "failed appointment cannot reserve a candidate");
    CourtService.Occupied.Add(strict.data.id);
    CourtService.Commit = true;
    OfficerCandidateCatalog.Invalidate(kingdom);
    session = new CourtCandidateSession(kingdom);
    LocalCourtAppointmentService.TryFillRegisteredLocalVacancy(kingdom, city, vacancy, session);
    Check(CourtService.Appointed == fallback, "pre-existing durable reservations exclude actors with stale runtime office projection");
}
Check(!AW_CityLeaderPatch.CheckFindLeader_Prefix(city) && CourtVacancyRegistry.Snapshot(kingdom.id).Count == 0, "empty city AI must defer to event-driven reconciliation without scanning");
city.Captured = true;
city.leader = Make(3, true, 3);
city.leader.Alive = false;
AW_CityLeaderPatch.CheckFindLeader_Prefix(city);
Check(city.leader != null, "captured cities preserve native leadership until capture completes");
city.Captured = false;
AW_CityLeaderPatch.CheckFindLeader_Prefix(city);
Check(city.leader == null, "stale dead leader is removed through the native event hook");
city.leader = Make(4, true, 3);
city.leader.Leader = true;
AW_CityLeaderPatch.CheckFindLeader_Prefix(city);
Check(city.leader != null && CourtVacancyRegistry.Snapshot(kingdom.id).Count == 0, "valid leaders must not trigger periodic vacancy scans");
Console.WriteLine("Court vacancy runtime regression tests passed (city, chief, county, reservations, rollback and native leader hooks).");

// Clean appointment failures follow the branch's persisted per-seat year
// cooldown. Thrown failures get one deferred retry. Neither may loop per frame.
World.world.kingdoms.Items[kingdom.id] = kingdom;
World.world.cities.Items[city.data.id] = city;
city.leader = null;
CourtService.Occupied.Clear();
kingdom.Units = new() { Make(10, true, 10) };
OfficerCandidateCatalog.Invalidate(kingdom);
var localVacancy = new CourtVacancyKey(1, 10, -1, CourtOfficeLayer.City, "local");
foreach (bool throws in new[] { false, true })
{
    Date.Year = 1;
    OfficerCandidateCatalog.Invalidate(kingdom);
    CourtVacancyReconciliationService.ClearRuntime();
    CourtService.Commit = false;
    CourtService.ThrowOnAppointment = throws;
    CourtService.AppointmentAttempts = 0;
    UnityEngine.Time.frameCount = 0;
    CourtVacancyReconciliationService.RegisterVacancy(localVacancy);
    DeferredRuntimeWorkService.RunPending();
    Check(CourtService.AppointmentAttempts == 1, "initial failure attempts appointment once");
    CourtVacancyReconciliationService.DrainDueRetryTickets();
    Check(DeferredRuntimeWorkService.Pending.Count == 0, "failed work cannot retry in the same frame");
    UnityEngine.Time.frameCount++;
    CourtVacancyReconciliationService.DrainDueRetryTickets();
    DeferredRuntimeWorkService.RunPending();
    int expectedAttempts = throws ? 2 : 1;
    Check(CourtService.AppointmentAttempts == expectedAttempts, "clean failures cool down immediately while thrown failures get one deferred retry");
    for (int frame = 2; frame < 10; frame++)
    {
        UnityEngine.Time.frameCount = frame;
        CourtVacancyReconciliationService.DrainDueRetryTickets();
        DeferredRuntimeWorkService.RunPending();
    }
    Check(CourtService.AppointmentAttempts == expectedAttempts, "persistent technical failure must stop generating appointment attempts");
    Check(CourtVacancyRegistry.Contains(localVacancy), "exhausted retry keeps vacancy for a later relevant event");
    CourtService.Commit = true;
    CourtService.ThrowOnAppointment = false;
    CourtVacancyReconciliationService.CandidatePoolChanged(kingdom);
    DeferredRuntimeWorkService.RunPending();
    if (!throws)
    {
        Check(CourtService.AppointmentAttempts == expectedAttempts, "candidate events preserve the clean-failure seat cooldown");
        Date.Year += CourtAppointmentFailureBackoffRules.RetryCooldownYears;
        CourtVacancyReconciliationService.Request(kingdom);
        DeferredRuntimeWorkService.RunPending();
    }
    Check(CourtService.AppointmentAttempts == expectedAttempts + 1 && !CourtVacancyRegistry.Contains(localVacancy), "eligible work resumes after the appropriate failure cooldown");
}
CourtVacancyReconciliationService.RegisterVacancy(localVacancy);
kingdom.Destroyed = true;
int beforeDestroyed = CourtService.AppointmentAttempts;
DeferredRuntimeWorkService.RunPending();
Check(CourtService.AppointmentAttempts == beforeDestroyed, "queued work ignores a kingdom destroyed before execution");
kingdom.Destroyed = false;
Console.WriteLine("Court reconciliation retry and destroyed-kingdom regression tests passed.");

// Event-maintained catalog references can survive native destruction clearing
// actor data after the roster was built. Every consumption must revalidate it.
Actor releasedActor = Make(100, true, 1), survivingActor = Make(101, true, 1);
kingdom.Units = new() { releasedActor, survivingActor };
OfficerCandidateCatalog.Invalidate(kingdom);
List<Actor> cachedRoster = OfficerCandidateCatalog.GetOrBuild(kingdom);
releasedActor.data = null;
cachedRoster.Add(null);
Check(ReferenceEquals(cachedRoster, OfficerCandidateCatalog.GetOrBuild(kingdom)), "the regression exercises a reused event-maintained roster");
Check(OfficerCandidateCatalog.EnumerateWithData(cachedRoster).SequenceEqual(new[] { survivingActor }), "released actors and null roster entries cannot reach court data readers");
survivingActor.Alive = false;
Check(OfficerCandidateCatalog.EnumerateWithData(cachedRoster).Single() == survivingActor, "dead actors with intact data remain available for durable office cleanup");
Check(!OfficerCandidateCatalog.EnumerateWithData(null).Any(), "an absent native roster is empty");
string courtSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Code/core/court/CourtService.cs"));
Check(System.Text.RegularExpressions.Regex.IsMatch(courtSource, @"(?s)private static IEnumerable<Actor> RosterOrSafeUnits\([^{}]*\)\s*\{\s*return OfficerCandidateCatalog\.EnumerateWithData\("), "every shared court roster consumer must use the tested data validation boundary");
Console.WriteLine("Court cached actor destruction regression tests passed.");

// Insertions must tolerate an actor disposed before its removal event. The old
// list may still be enumerated by an in-flight candidate session.
Actor highRank = Make(200, true, 1), disposed = Make(202, false, 1), last = Make(204, false, 1), inserted = Make(203, false, 1);
highRank.Rank = 5;
kingdom.Units = new() { highRank, disposed, last };
OfficerCandidateCatalog.Invalidate(kingdom);
List<Actor> priorSnapshot = OfficerCandidateCatalog.GetOrBuild(kingdom);
disposed.data = null;
OfficerCandidateCatalog.EnsurePresent(kingdom, inserted);
List<Actor> nextSnapshot = OfficerCandidateCatalog.GetOrBuild(kingdom);
Check(nextSnapshot.SequenceEqual(new[] { highRank, inserted, last }), "inserting alongside a disposed actor compacts the cache and preserves rank/ID ordering");
Check(priorSnapshot.SequenceEqual(new[] { highRank, disposed, last }), "insertion must not mutate snapshots already held by reconciliation");
OfficerCandidateCatalog.EnsurePresent(kingdom, inserted);
Check(ReferenceEquals(nextSnapshot, OfficerCandidateCatalog.GetOrBuild(kingdom)), "duplicate candidate events leave the published snapshot alone");
inserted.Rank = 10;
OfficerCandidateCatalog.Reposition(kingdom, inserted);
Check(OfficerCandidateCatalog.GetOrBuild(kingdom).SequenceEqual(new[] { inserted, highRank, last }), "rank changes preserve sorted event-maintained membership");
Console.WriteLine("Court event-maintained catalog disposal and insertion regression tests passed.");

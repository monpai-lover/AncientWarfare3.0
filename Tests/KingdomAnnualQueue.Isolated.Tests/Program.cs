using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

var cases = new (string Name, Action Test)[]
{
    ("a later annual event revives work abandoned after two stage failures", ExhaustedWorkRecovers),
    ("transient failures use the existing bounded deferred retry", TransientFailure),
    ("new-year schedules coalesce with work still draining", PendingYearCoalesces),
    ("one failed kingdom does not stall another kingdom", IndependentKingdoms),
    ("a year event raised during a stage preserves both year requests", ReentrantYearEvent),
    ("destroyed kingdoms drop pending work", DestroyedKingdom)
};
int failures = 0;
foreach (var item in cases)
{
    Reset();
    try { item.Test(); Console.WriteLine("PASS " + item.Name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL " + item.Name + ": " + error.Message); }
}
return failures == 0 ? 0 : 1;

static void ExhaustedWorkRecovers()
{
    Kingdom kingdom = Realm(1);
    Boundary.FaultService = "OfficialCareerStateService.OnKingdomYear";
    Boundary.FaultKingdom = kingdom.id;
    Boundary.FailuresLeft = 2;
    KingdomAnnualWorkService.Schedule(kingdom);
    Drain();
    Equal(2, Boundary.Calls(1, Boundary.FaultService), "deferred retry reaches its terminal failure");
    Equal(0, Boundary.Calls(1, "MinisterialPowerService.OnKingdomYear"), "later stages did not run before recovery");
    Equal(1, Boundary.Calls(1, "HeirService.OnKingdomYear"), "earlier stage was already completed");
    Equal(1, ModClass.Warnings.Count, "terminal deferred failure remains observable");
    Date.Year = 101;
    KingdomAnnualWorkService.Schedule(kingdom);
    Equal(1, DeferredRuntimeWorkService.PendingCount, "new annual event must restore the missing queue entry");
    Step();
    Equal(3, Boundary.Calls(1, Boundary.FaultService), "recovery resumes the failed stage");
    Equal(1, Boundary.Calls(1, "HeirService.OnKingdomYear"), "recovery does not replay completed stages");
    Drain();
    Equal(2, Boundary.Calls(1, "HeirService.OnKingdomYear"), "the newly requested year runs after the recovered year");
    Equal(2, Boundary.Calls(1, "MinisterialPowerService.OnKingdomYear"), "both years reach the later stages");
    KingdomAnnualWorkService.Schedule(kingdom);
    Equal(0, DeferredRuntimeWorkService.PendingCount, "completed latest year is not scheduled twice");
}

static void TransientFailure()
{
    Kingdom kingdom = Realm(1);
    Boundary.FaultService = "OfficialCareerStateService.OnKingdomYear";
    Boundary.FaultKingdom = 1;
    Boundary.FailuresLeft = 1;
    KingdomAnnualWorkService.Schedule(kingdom);
    Drain();
    Equal(2, Boundary.Calls(1, Boundary.FaultService), "one transient failure is retried");
    Equal(1, Boundary.Calls(1, "MinisterialPowerService.OnKingdomYear"), "the annual cycle still completes");
    Equal(0, ModClass.Warnings.Count, "successful retry does not exhaust its budget");
    KingdomAnnualWorkService.Schedule(kingdom);
    Equal(0, DeferredRuntimeWorkService.PendingCount, "completed cycle remains idempotent");
}

static void PendingYearCoalesces()
{
    Kingdom kingdom = Realm(1);
    KingdomAnnualWorkService.Schedule(kingdom);
    Step();
    Date.Year = 101;
    KingdomAnnualWorkService.Schedule(kingdom);
    KingdomAnnualWorkService.Schedule(kingdom);
    Equal(1, DeferredRuntimeWorkService.PendingCount, "pending years share one coalesced work item");
    Drain();
    Equal(2, Boundary.Calls(1, "HeirService.OnKingdomYear"), "both years run once");
    Equal(2, Boundary.Calls(1, "KingdomWarDirectorService.Schedule"), "both years reach the final stage");
}

static void IndependentKingdoms()
{
    Kingdom broken = Realm(1);
    Kingdom healthy = Realm(2);
    Boundary.FaultService = "OfficialCareerStateService.OnKingdomYear";
    Boundary.FaultKingdom = broken.id;
    Boundary.FailuresLeft = 2;
    KingdomAnnualWorkService.Schedule(broken);
    KingdomAnnualWorkService.Schedule(healthy);
    Drain();
    Equal(1, Boundary.Calls(healthy.id, "KingdomWarDirectorService.Schedule"), "healthy realm completes despite another failure");
    Date.Year = 101;
    KingdomAnnualWorkService.Schedule(broken);
    Drain();
    Equal(2, Boundary.Calls(broken.id, "KingdomWarDirectorService.Schedule"), "failed realm can recover on a later year event");
}

static void ReentrantYearEvent()
{
    Kingdom kingdom = Realm(1);
    Boundary.DuringCall = (service, realm) =>
    {
        if (service != "HeirService.OnKingdomYear") return;
        Boundary.DuringCall = null;
        Date.Year = 101;
        KingdomAnnualWorkService.Schedule(realm);
    };
    KingdomAnnualWorkService.Schedule(kingdom);
    Drain();
    Equal(2, Boundary.Calls(1, "HeirService.OnKingdomYear"), "reentrant request runs once after the active year");
    Equal(2, Boundary.Calls(1, "KingdomWarDirectorService.Schedule"), "neither year loses its last stage");
}

static void DestroyedKingdom()
{
    Kingdom kingdom = Realm(1);
    KingdomAnnualWorkService.Schedule(kingdom);
    kingdom.data = null;
    Drain();
    Equal(0, Boundary.Calls(1, "HeirService.OnKingdomYear"), "destroyed realm is ignored at consumption");
}

static Kingdom Realm(long id)
{
    var kingdom = new Kingdom { Id = id };
    World.world.kingdoms.Values[id] = kingdom;
    return kingdom;
}
static void Step()
{
    UnityEngine.Time.frameCount++;
    DeferredRuntimeWorkService.DrainFrame(1000, 1);
}
static void Drain()
{
    for (int step = 0; step < 128 && DeferredRuntimeWorkService.PendingCount > 0; step++) Step();
    Equal(0, DeferredRuntimeWorkService.PendingCount, "work must converge within its finite stage budget");
}
static void Reset()
{
    KingdomAnnualWorkService.ClearRuntimeState();
    DeferredRuntimeWorkService.ClearRuntimeState();
    World.world = new MapBox();
    Date.Year = 100;
    Boundary.Counts.Clear(); Boundary.FaultService = ""; Boundary.FaultKingdom = -1; Boundary.FailuresLeft = 0; Boundary.DuringCall = null;
    ModClass.Warnings.Clear();
}
static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual);
}

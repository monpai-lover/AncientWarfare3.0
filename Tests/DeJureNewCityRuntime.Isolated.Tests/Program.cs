using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static (City city, Kingdom kingdom) Reset()
{
    DeferredRuntimeWorkService.ClearRuntimeState();
    DeJureNewCityAssignmentService.ClearRuntime();
    ModClass.Warnings.Clear();
    World.world = new MapBox();
    UnityEngine.Time.frameCount = 0;
    DeJureRegionStore.Attempts = DeJureRegionStore.Assigned = DeJureRegionStore.CapitalRepairs = 0;
    DeJureRegionStore.FailAssignment = DeJureRegionStore.ExplicitRemoval = DeJureRegionStore.FailCreate = false;
    var kingdom = new Kingdom { data = new Data { id = 1 } };
    var seat = new City { data = new Data { id = 1 }, kingdom = kingdom };
    var city = new City { data = new Data { id = 2 }, kingdom = kingdom };
    kingdom.capital = seat;
    city.neighbours_cities.Add(seat);
    World.world.cities.AddRange(new[] { seat, city });
    DeJureRegionStore.Regions = new() { new DeJureRegion { RegionId = 10, SeatCityId = 1, MemberCityIds = new() { 1 } } };
    return (city, kingdom);
}
static void Drain(int frames)
{
    for (int i = 0; i < frames; i++)
    {
        UnityEngine.Time.frameCount++;
        DeferredRuntimeWorkService.DrainFrame(100, 1);
    }
}

foreach (string failure in new[] { "missing_kingdom", "disposed_kingdom", "neighbours", "write", "isolated_create" })
{
    (City city, Kingdom kingdom) = Reset();
    if (failure == "missing_kingdom") city.kingdom = null;
    if (failure == "disposed_kingdom") city.kingdom = new Kingdom { data = null };
    if (failure == "neighbours") city.NeighboursReady = false;
    if (failure == "write") DeJureRegionStore.FailAssignment = true;
    if (failure == "isolated_create") { DeJureRegionStore.Regions.Clear(); DeJureRegionStore.FailCreate = true; }
    DeJureNewCityAssignmentService.OnCityFounded(city);
    Check(DeferredRuntimeWorkService.PendingCount == 1, failure + ": first failure schedules one retry");
    DeJureNewCityAssignmentService.OnCityFounded(city);
    Check(DeferredRuntimeWorkService.PendingCount == 1, failure + ": duplicate events coalesce");
    Drain(620);
    Check(DeferredRuntimeWorkService.PendingCount == 0, failure + ": permanently invalid city must stop re-enqueuing runtime work");
    Check(ModClass.Warnings.Count == 1 && ModClass.Warnings[0].Contains("city=2"), failure + ": exhausted chain warns once with its city identity");
    int attempts = DeJureRegionStore.Attempts, checks = city.NeighbourChecks;
    Drain(20);
    Check(DeJureRegionStore.Attempts == attempts && city.NeighbourChecks == checks, failure + ": retry exhaustion leaves no background activity");
    city.kingdom = kingdom;
    city.NeighboursReady = true;
    DeJureRegionStore.FailAssignment = DeJureRegionStore.FailCreate = false;
    DeJureNewCityAssignmentService.OnCityFounded(city);
    Check(DeJureRegionStore.Assigned == 1 && DeferredRuntimeWorkService.PendingCount == 0, failure + ": a later valid event can still assign an exhausted city");
    Console.WriteLine("PASS bounded retry and recovery: " + failure);
}

{
    (City city, Kingdom kingdom) = Reset();
    city.kingdom = null;
    DeJureNewCityAssignmentService.OnCityFounded(city);
    Drain(3);
    city.kingdom = kingdom;
    Drain(1);
    Check(DeJureRegionStore.Assigned == 1 && DeferredRuntimeWorkService.PendingCount == 0, "normal delayed native ownership still recovers");
    Check(ModClass.Warnings.Count == 0, "normal initialization must not log a warning");
}
foreach (bool clearData in new[] { false, true })
{
    (City city, _) = Reset();
    city.NeighboursReady = false;
    DeJureNewCityAssignmentService.OnCityFounded(city);
    if (clearData) city.data = null;
    else city.Destroyed = true;
    Drain(5);
    Check(DeferredRuntimeWorkService.PendingCount == 0 && DeJureRegionStore.Attempts == 0, "a destroyed queued city terminates without assignment");
}
{
    (City city, _) = Reset();
    city.NeighboursReady = false;
    DeJureNewCityAssignmentService.OnCityFounded(city);
    bool otherWorkRan = false;
    DeferredRuntimeWorkService.EnqueueOrdered(DeferredWorkClass.Runtime, () => otherWorkRan = true);
    Drain(2);
    Check(otherWorkRan, "one invalid city does not prevent following runtime work");
}
Console.WriteLine("De jure new-city runtime regressions passed.");

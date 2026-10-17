using AncientWarfare3.core.performance;
using AncientWarfare3.core.lineage;

void Check(bool value, string message) { if (!value) throw new Exception(message); }
Actor Person(long id) => new() { data = new() { id = id } };
void Run(MapBox world, Action<string> beforeStep = null,
    AWCooperativeWorldMaintenanceRunner existingRunner = null)
{
    World.world = world;
    UnityEngine.Time.frameCount++;
    int checksBefore = world.Checks, dirtyBefore = world.DirtyChecks,
        changedBefore = world.AnythingChanged;
    var runner = existingRunner ?? new AWCooperativeWorldMaintenanceRunner();
    runner.Start(world);
    int steps = 0;
    while (runner.Active)
    {
        string phase = runner.GetNextPhaseName();
        beforeStep?.Invoke(phase);
        bool complete = runner.Step();
        if (++steps > 200) throw new Exception("maintenance cycle did not complete");
        if (complete) Check(!runner.Active, "completion must abort and release snapshots");
    }
    Check(world.Checks - checksBefore == 2 && world.DirtyChecks - dirtyBefore == 2 &&
        world.AnythingChanged - changedBefore == 1, "bad entries must not prevent later maintenance phases");
}

// Full-cycle classification preserves dead actor and invalid-kingdom quarantine
// semantics while ignoring already disposed actors from native snapshots.
{
    var world = new MapBox();
    var missingData = Person(1); missingData.data = null;
    var missingAsset = Person(2); missingAsset.asset = null;
    var dead = Person(3); dead.Alive = false;
    var wild = Person(4); wild.kingdom.wild = true;
    var orphan = Person(5); orphan.kingdom = null;
    var healthy = Person(6);
    world.units.Source.AddRange(new[] { null, missingData, missingAsset, dead, wild, orphan, healthy });
    var dirty = new BaseSystemManager { Dirty = true };
    var clean = new BaseSystemManager();
    world._list_meta_main_managers.AddRange(new[] { dirty, clean });
    Run(world);
    Check(world.units.units_only_dying.SequenceEqual(new[] { dead, orphan }) && world.units.have_dying_units, "intact dead actors and orphan actors retain native dying/quarantine classification");
    Check(world.units.units_only_alive.SequenceEqual(new[] { wild, healthy }), "disposed actors cannot enter live batches");
    Check(world.units.units_only_wild.Single() == wild && world.units.units_only_civ.Single() == healthy, "healthy classification remains unchanged");
    Check(ActorKingdomSafetyService.Repairs.Contains(orphan) && !ActorKingdomSafetyService.Repairs.Contains(dead), "only live orphan actors need kingdom repair");
    Check(missingData.Checks == 0 && missingAsset.RelationshipChecks == 0 && healthy.Checks == 1 && dead.Checks == 1, "every actor batch skips disposed state without skipping intact dead actors");
    Check(dirty.Runs == 1 && clean.Runs == 0, "dirty managers still execute once");
}

// Buildings can be disposed after HousesStart has captured occupied_buildings.
// Invalid homes are detached before native helpers dereference them, and normal
// home/docks accounting is still completed for actors following bad entries.
{
    var world = new MapBox();
    var disposedHome = new Building();
    var missingDataHome = new Building { data = null };
    var missingAssetHome = new Building { asset = null };
    var disposedInside = new Building(); disposedInside.Dispose();
    var docks = new Building { asset = new() { docks = true }, component_docks = new() };
    var unfinishedDocks = new Building { asset = new() { docks = true } };
    var normalHome = new Building(); normalHome.residents.Add(999);
    var abandoned = new Building { Abandoned = true };
    world.buildings.occupied_buildings.AddRange(new[] { null, disposedHome, missingDataHome, missingAssetHome, docks, unfinishedDocks, normalHome, abandoned });
    var badHomeActor = Person(10); badHomeActor.home_building = disposedHome;
    var badDataActor = Person(11); badDataActor.home_building = missingDataHome;
    var badAssetActor = Person(12); badAssetActor.home_building = missingAssetHome;
    var insideActor = Person(13); insideActor.inside_building = disposedInside; insideActor.is_inside_building = true;
    var boat = Person(14); boat.home_building = docks;
    var waitingBoat = Person(15); waitingBoat.home_building = unfinishedDocks;
    var resident = Person(16); resident.home_building = normalHome;
    var deadResident = Person(17); deadResident.home_building = normalHome; deadResident.Alive = false;
    var abandonedActor = Person(18); abandonedActor.home_building = abandoned;
    world.units.Source.AddRange(new[] { badHomeActor, badDataActor, badAssetActor, insideActor, boat, waitingBoat, resident, deadResident, abandonedActor });
    bool disposedAfterSnapshot = false;
    Run(world, phase =>
    {
        if (!disposedAfterSnapshot && phase == "vanilla.maintenance.houses_buildings")
        {
            disposedHome.Dispose(); disposedAfterSnapshot = true;
        }
    });
    foreach (Actor actor in new[] { badHomeActor, badDataActor, badAssetActor })
        Check(actor.home_building == null && actor.Clears == 1 && actor.HappinessChanges == 1 && actor.Checks == 0, "disposed home uses safe detachment and lost-house semantics without calling the unsafe native helper");
    Check(insideActor.inside_building == null && !insideActor.is_inside_building && insideActor.Exits == 1 && insideActor.Cancellations == 1, "disposed inside building releases the actor and cancels its building behavior");
    Check(docks.component_docks.Resets == 1 && docks.component_docks.Boats.Single() == boat, "normal docks counters reset and rebuild");
    Check(waitingBoat.home_building == unfinishedDocks && waitingBoat.HappinessChanges == 0, "temporarily absent dock components must not evict otherwise valid homes");
    Check(normalHome.residents.SetEquals(new[] { 16L, 17L }), "normal house counters rebuild and preserve intact dead-resident semantics");
    Check(abandonedActor.home_building == null && abandonedActor.Checks == 1 && abandonedActor.HappinessChanges == 1, "native abandoned-home validation still runs");
}

// Disposal between cooperative steps must be checked at each consumption point,
// including snapshots refreshed for unit destruction and houses.
foreach (string targetPhase in new[] { "prepare_actors", "unit_destroy", "building_destroy", "houses_actors" })
{
    var world = new MapBox();
    var released = Person(20); var survivor = Person(21);
    world.units.Source.AddRange(new[] { released, survivor });
    bool disposed = false;
    Run(world, phase =>
    {
        if (!disposed && phase == "vanilla.maintenance." + targetPhase)
        {
            released.Dispose(); disposed = true;
        }
    });
    Check(disposed && survivor.Checks == 1, "a released actor in " + targetPhase + " cannot stop the following valid actor");
}

// b reuses metadata arrays and object indices between passes. Verify the
// incremental path removes disposed entries without breaking ordering, and
// accepts the same object again after valid state has been restored.
{
    var world = new MapBox();
    var first = Person(30); var changing = Person(31); var last = Person(32);
    world.units.Source.AddRange(new[] { first, changing, last });
    world._list_meta_main_managers.AddRange(new[] {
        new BaseSystemManager { Dirty = true }, new BaseSystemManager { Dirty = true },
        new BaseSystemManager { Dirty = true } });
    var runner = new AWCooperativeWorldMaintenanceRunner();
    Run(world, existingRunner: runner);
    int parallelDispatches = AWSimulationWorkerPool.Instance.Dispatches;
    var phases = new List<string>();

    world.units.version++;
    AWActorMetaPartitionVersion.MarkAliveCall(changing, true, false);
    changing.Alive = false;
    changing.Dispose();
    Run(world, phases.Add, runner);
    Check(phases.Contains("vanilla.maintenance.prepare_actors_incremental"), "life changes retain b's incremental path");
    Check(!world.units.units_only_dying.Contains(changing) &&
        world.units.units_only_alive.SequenceEqual(new[] { first, last }),
        "disposed incremental entry is removed from every partition");

    changing.data = new() { id = 31 }; changing.asset = new();
    changing.Alive = true;
    AWActorMetaPartitionVersion.MarkAliveCall(changing, false, true);
    Run(world, existingRunner: runner);
    Check(world.units.units_only_alive.SequenceEqual(new[] { first, changing, last }),
        "restored incremental entry returns at the native container rank");

    var wildKingdom = new Kingdom { wild = true };
    AWActorMetaPartitionVersion.MarkKingdomChange(changing, wildKingdom);
    changing.kingdom = wildKingdom;
    Run(world, existingRunner: runner);
    Check(world.units.units_only_wild.SequenceEqual(new[] { changing }) &&
        world.units.units_only_civ.SequenceEqual(new[] { first, last }),
        "incremental affiliation changes preserve partition ordering");
    Check(parallelDispatches > 0 && AWSimulationWorkerPool.Instance.Dispatches > parallelDispatches,
        "parallel classification and dirty manager execution remain active");
}
Console.WriteLine("Cooperative world maintenance lifecycle regressions passed (complete cycles, parallel and incremental partitions, actor disposal, stale buildings, docks, houses, quarantine and dead actors).");

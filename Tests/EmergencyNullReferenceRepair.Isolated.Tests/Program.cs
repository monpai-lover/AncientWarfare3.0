using AncientWarfare3.core.performance;

var tests = new (string Name, Action Run)[]
{
    ("healthy targets, jobs, orders and movement survive", HealthyState),
    ("disposed attack pointer is cleared even with flag false", StaleAttackPointer),
    ("null attack pointer with flag true is repaired without batch", NullAttackTarget),
    ("interrupted combat with no target finishes safely", InterruptedCombat),
    ("dead attacker reference is cleared", DeadAttacker),
    ("invalid behaviour targets finish only the current task", BehaviourTargets),
    ("only invalid native paths reset movement", BadPaths),
    ("consumed invalid path prefix is harmless", ConsumedPath),
    ("healthy boat passenger retains task and route", PassengerState),
    ("dirty spawn remains for native tile recalculation", DirtySpawn),
    ("target without tile is released while dirty spawn survives", TargetWithoutTile),
    ("valid dying actors remain in death jobs", DyingActors),
    ("disposed batch entries and cached views are pruned", BatchResidue),
    ("stale cached views are rebuilt from valid membership", CachedResidue),
    ("pending additions and removals preserve native semantics", PendingBatchChanges),
    ("free batch residue is cleaned without clearing new ownership", FreeBatchResidue),
    ("core damage blocks recovery and reports completed repairs", CoreDamage),
    ("moving actor without current tile is reported", MissingCurrentTile),
    ("stationary, dead and boat actors need a current tile", MissingStationaryTile),
    ("dead actor core damage is reported without removing death jobs", DeadCoreDamage),
    ("active movement without a batch remains blocked", MissingBatch),
    ("identity mismatch is reported without rewriting entity", IdentityMismatch),
    ("second repair is idempotent", Idempotent),
    ("foreign world is rejected before mutation", ForeignWorld)
};
foreach (var test in tests)
{
    test.Run();
    Console.WriteLine("PASS " + test.Name);
}
Console.WriteLine($"Emergency reference repair runtime: {tests.Length}/{tests.Length} passed.");

static MapBox Fixture()
{
    var world = new MapBox { tiles_list = Enumerable.Range(0, 4).Select(i => new WorldTile(i)).ToArray() };
    World.world = world;
    return world;
}

static Actor Add(MapBox world, long id)
{
    var batch = new BatchActors();
    var actor = new Actor(id) { current_tile = world.tiles_list[0], batch = batch };
    world.units.dict.Add(id, actor);
    world.units._job_manager._batches_active.Add(batch);
    batch.main.Add(actor);
    batch.main.checkAddRemove();
    return actor;
}

static void StartPath(MapBox world, Actor actor)
{
    actor.current_path.Add(world.tiles_list[1]);
    actor.current_path.Add(world.tiles_list[2]);
    actor.current_path_global = new() { new MapRegion() };
    actor.tile_target = world.tiles_list[2];
    actor.tile_target._targeted_by = actor;
    actor._next_step_tile = world.tiles_list[1];
    actor.scheduled_tile_target = world.tiles_list[3];
    actor._is_moving = true;
    actor.batch.c_update_movement.Add(actor);
    actor.batch.c_update_movement.checkAddRemove();
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static InvalidOperationException Fails(MapBox world)
{
    try { AWEmergencyNullReferenceRepairService.Repair(world); }
    catch (InvalidOperationException error) { return error; }
    throw new Exception("Expected unresolved damage to prevent recovery");
}

static void HealthyState()
{
    var world = Fixture();
    var actor = Add(world, 1);
    var target = Add(world, 2);
    var building = new Building { current_tile = world.tiles_list[0] };
    var book = new Book();
    actor.attack_target = actor.beh_actor_target = actor.attackedBy = target;
    actor.has_attack_target = true;
    actor.beh_building_target = building;
    actor.beh_book_target = book;
    actor.beh_tile_target = world.tiles_list[1];
    actor.batch.c_check_attack_target.Add(actor);
    StartPath(world, actor);
    var job = actor.ai.job;
    var task = actor.ai.task;
    var army = actor.army;
    var insideBuilding = actor.inside_building;
    string summary = AWEmergencyNullReferenceRepairService.Repair(world);
    Check(summary.Contains("references=0, paths=0, tasks=0, batch_entries=0, unresolved=0"), summary);
    Check(ReferenceEquals(actor.attack_target, target) && actor.has_attack_target &&
        ReferenceEquals(actor.beh_actor_target, target) && ReferenceEquals(actor.attackedBy, target), "healthy targets lost");
    Check(ReferenceEquals(actor.beh_building_target, building) && ReferenceEquals(actor.beh_book_target, book), "healthy objects lost");
    Check(ReferenceEquals(actor.ai.job, job) && ReferenceEquals(actor.ai.task, task) &&
        ReferenceEquals(actor.army, army) && ReferenceEquals(actor.inside_building, insideBuilding), "valid task/ownership mutated");
    Check(actor._is_moving && actor.current_path.Count == 2 && actor.tile_target._targeted_by == actor, "valid movement lost");
}

static void StaleAttackPointer()
{
    var world = Fixture(); var actor = Add(world, 1);
    var disposed = new Actor(2) { data = null };
    // The real id getter must throw; repair must never call it on disposed targets.
    try { _ = disposed.id; throw new Exception("stub is too permissive"); } catch (NullReferenceException) { }
    actor.attack_target = disposed;
    actor.batch.c_check_attack_target.Add(actor);
    var task = actor.ai.task;
    AWEmergencyNullReferenceRepairService.Repair(world);
    Check(actor.attack_target == null && !actor.has_attack_target, "stale false-flag pointer retained");
    Check(!actor.batch.c_check_attack_target._hashSet.Contains(actor), "combat batch retained actor");
    Check(ReferenceEquals(actor.ai.task, task), "unrelated task reset");
}

static void NullAttackTarget()
{
    var world = Fixture(); var actor = Add(world, 1);
    actor.has_attack_target = true; actor.batch = null;
    AWEmergencyNullReferenceRepairService.Repair(world);
    Check(!actor.has_attack_target && actor.attack_target == null, "inconsistent flag retained");
}

static void DeadAttacker()
{
    var world = Fixture(); var actor = Add(world, 1);
    actor.attackedBy = new Actor(2) { alive = false };
    AWEmergencyNullReferenceRepairService.Repair(world);
    Check(actor.attackedBy == null && actor.ai.Finished == 0, "attacker cleanup changed current task");
}

static void InterruptedCombat()
{
    var world = Fixture(); var actor = Add(world, 1);
    actor.has_attack_target = true;
    actor.ai.task.in_combat = true;
    actor.ai.action_index = 1;
    var job = actor.ai.job;
    AWEmergencyNullReferenceRepairService.Repair(world);
    Check(!actor.has_attack_target && actor.ai.task == null && actor.ai.action == null &&
        ReferenceEquals(actor.ai.job, job), "interrupted combat can resume its target action");
}

static void BehaviourTargets()
{
    var world = Fixture(); var actor = Add(world, 1);
    actor.beh_actor_target = new Actor(2) { data = null };
    actor.beh_building_target = new Building { data = null };
    actor.beh_book_target = new Book { data = null };
    actor.beh_tile_target = new WorldTile(0); // Same id, different world's tile.
    actor.timer_action = 50;
    var job = actor.ai.job; var army = actor.army; var building = actor.inside_building;
    AWEmergencyNullReferenceRepairService.Repair(world);
    Check(actor.beh_actor_target == null && actor.beh_building_target == null &&
        actor.beh_book_target == null && actor.beh_tile_target == null, "invalid behaviour reference remains");
    Check(actor.ai.task == null && actor.ai.action == null && actor.ai.action_index == -1 && actor.ai.Finished == 1, "invalid task remains runnable");
    Check(actor.timer_action == 0 && actor.ai.task_index == 2 && ReferenceEquals(actor.ai.job, job) &&
        ReferenceEquals(actor.army, army) && ReferenceEquals(actor.inside_building, building), "unrelated job/ownership reset");
}

static void BadPaths()
{
    Action<Actor>[] damage =
    {
        actor => actor.current_path[0] = null,
        actor => actor.current_path[0] = new WorldTile(1) { data = null },
        actor => actor.current_path[0] = new WorldTile(1),
        actor => actor.current_path_index = -1,
        actor => actor.current_path_global.Add(null),
        actor => actor.tile_target = new WorldTile(2) { Type = null },
        actor => actor.scheduled_tile_target = new WorldTile(3),
        actor => actor._next_step_tile = new WorldTile(1)
    };
    foreach (var corrupt in damage)
    {
        var world = Fixture(); var actor = Add(world, 1); StartPath(world, actor);
        corrupt(actor);
        var job = actor.ai.job;
        string summary = AWEmergencyNullReferenceRepairService.Repair(world);
        Check(summary.Contains("paths=1"), summary);
        Check(!actor._is_moving && actor.tile_target == null && actor._next_step_tile == null &&
            actor.scheduled_tile_target == null && actor.current_path.Count == 0 && actor.current_path_global == null, "broken path remains");
        Check(!actor.batch.c_update_movement._hashSet.Contains(actor), "movement batch remains active");
        Check(actor.ai.task == null && ReferenceEquals(actor.ai.job, job), "job was replaced");
    }
}

static void ConsumedPath()
{
    var world = Fixture(); var actor = Add(world, 1); StartPath(world, actor);
    actor.current_path[0] = null; actor.current_path_index = 1;
    AWEmergencyNullReferenceRepairService.Repair(world);
    Check(actor.current_path.Count == 2 && actor.current_path_index == 1 && actor._is_moving && actor.ai.Finished == 0, "consumed prefix treated as active corruption");
}

static void PassengerState()
{
    var world = Fixture(); var actor = Add(world, 1); StartPath(world, actor);
    actor.is_inside_boat = true; actor.inside_boat = new object();
    var boat = actor.inside_boat; var task = actor.ai.task; var army = actor.army;
    AWEmergencyNullReferenceRepairService.Repair(world);
    Check(actor.is_inside_boat && ReferenceEquals(actor.inside_boat, boat) && ReferenceEquals(actor.army, army) &&
        ReferenceEquals(actor.ai.task, task) && actor.current_path.Count == 2, "passenger voyage mutated");
}

static void DyingActors()
{
    var world = Fixture(); var actor = Add(world, 1);
    actor.alive = false; actor.ai = null;
    AWEmergencyNullReferenceRepairService.Repair(world);
    Check(world.units.dict[1] == actor && actor.batch.main._hashSet.Contains(actor), "valid death job removed");
}

static void BatchResidue()
{
    var world = Fixture(); var actor = Add(world, 1); var batch = actor.batch;
    var disposed = new Actor(2) { data = null };
    foreach (var container in batch.containers)
    {
        container.Add(null); container.Add(disposed); container.Add(actor); container.checkAddRemove();
        _ = container.getSimpleArray(); _ = container.getSimpleList();
    }
    batch.l_parallel_update_sprites.AddRange(new[] { null, disposed, actor });
    AWEmergencyNullReferenceRepairService.Repair(world);
    foreach (var container in batch.containers)
        Check(container.getSimpleArray().SequenceEqual(new[] { actor }) && container.getSimpleList().SequenceEqual(new[] { actor }), "native cache retained disposed actor");
    Check(batch.l_parallel_update_sprites.SequenceEqual(new[] { actor }), "sprite helper retained disposed actor");
    Check(actor.batch == batch && world.units.dict.Count == 1, "ownership mutated while pruning residue");
}

static void DirtySpawn()
{
    var world = Fixture(); var actor = Add(world, 1);
    actor.current_tile = null; actor.dirty_current_tile = true;
    var task = actor.ai.task;
    AWEmergencyNullReferenceRepairService.Repair(world);
    Check(actor.current_tile == null && actor.dirty_current_tile && ReferenceEquals(actor.ai.task, task), "native spawn transition was fabricated/cancelled");
}

static void DeadCoreDamage()
{
    var world = Fixture(); var actor = Add(world, 1);
    actor.alive = false; actor.asset = null;
    var error = Fails(world);
    Check(error.Message.Contains("no asset") && actor.batch.main._hashSet.Contains(actor) && world.units.dict[1] == actor,
        "dead actor core corruption was silently accepted or deleted");
}

static void TargetWithoutTile()
{
    foreach (bool registeredSpawn in new[] { false, true })
    {
        var world = Fixture(); var actor = Add(world, 1);
        var target = registeredSpawn ? Add(world, 2) : new Actor(2);
        target.current_tile = null;
        target.dirty_current_tile = registeredSpawn;
        actor.attack_target = actor.beh_actor_target = target;
        actor.has_attack_target = true;
        actor.ai.task.in_combat = true;
        var targetTask = target.ai.task;
        var actorJob = actor.ai.job;
        AWEmergencyNullReferenceRepairService.Repair(world);
        Check(actor.attack_target == null && !actor.has_attack_target &&
            actor.beh_actor_target == null && actor.ai.task == null &&
            ReferenceEquals(actor.ai.job, actorJob), "unpositioned target remains actionable");
        Check(target.data != null && target.current_tile == null &&
            ReferenceEquals(target.ai.task, targetTask) &&
            (!registeredSpawn || world.units.dict[2] == target && target.dirty_current_tile),
            "target entity or pending native spawn was changed");
    }
}

static void MissingStationaryTile()
{
    for (int kind = 0; kind < 3; kind++)
    {
        var world = Fixture(); var actor = Add(world, 1);
        actor.current_tile = null;
        if (kind == 1) actor.alive = false;
        if (kind == 2) actor.is_inside_boat = true;
        Check(Fails(world).Message.Contains("no valid current tile") && actor.current_tile == null && actor.batch.main._hashSet.Contains(actor),
            "unguarded timer/visibility access can resume");
    }
}

static void PendingBatchChanges()
{
    var world = Fixture(); var actor = Add(world, 1); var other = Add(world, 2); var container = actor.batch.main;
    container.Add(new Actor(3) { data = null }); container.Add(null); container.Add(other); container.Remove(actor);
    AWEmergencyNullReferenceRepairService.Repair(world);
    Check(container._hashSet.SetEquals(new[] { other }) && container._to_add.Count == 0 && container._to_remove.Count == 0, "pending mutations lost/replayed incorrectly");
}

static void CachedResidue()
{
    var world = Fixture(); var actor = Add(world, 1); var container = actor.batch.main;
    container.getSimpleList(); container.getSimpleArray();
    var disposed = new Actor(2) { data = null };
    container._simple_list.Add(disposed);
    container._array = new[] { actor, disposed, null }; container._array_count = 3;
    var summary = AWEmergencyNullReferenceRepairService.Repair(world);
    Check(container.getSimpleList().SequenceEqual(new[] { actor }) && container.getSimpleArray().SequenceEqual(new[] { actor }), "cached-only stale reference survived");
    Check(container._hashSet.SetEquals(new[] { actor }) && summary.Contains("cache_views=1"), summary);
    Check(AWEmergencyNullReferenceRepairService.Repair(world).Contains("cache_views=0"), "rebuild not idempotent");
}

static void FreeBatchResidue()
{
    var world = Fixture(); var actor = Add(world, 1); var oldBatch = new BatchActors();
    var disposed = new Actor(2) { data = null, batch = actor.batch };
    oldBatch.main.Add(disposed);
    world.units._job_manager._batches_free.Push(oldBatch);
    world.units._job_manager._batches_free.Push(actor.batch); // Active batches can also have free slots.
    AWEmergencyNullReferenceRepairService.Repair(world);
    Check(oldBatch.main._hashSet.Count == 0 && disposed.batch == actor.batch && actor.batch.main._hashSet.Contains(actor), "old-batch cleanup changed actor's later ownership");
}

static void CoreDamage()
{
    var world = Fixture(); var actor = Add(world, 1); var damaged = Add(world, 2);
    actor.has_attack_target = true; damaged.asset = null;
    var error = Fails(world);
    Check(error.Message.Contains("references=1") && error.Message.Contains("unresolved=1") && error.Message.Contains("actor 2"), error.Message);
    Check(!actor.has_attack_target && world.units.dict[2] == damaged && damaged.data != null, "core damage was guessed/deleted");
    damaged.asset = new(); damaged.data = null;
    error = Fails(world);
    Check(error.Message.Contains("no live data") && world.units.dict[2] == damaged, "missing data was silently discarded");
}

static void MissingCurrentTile()
{
    var world = Fixture(); var actor = Add(world, 1);
    actor.current_tile = null; actor._is_moving = true;
    var error = Fails(world);
    Check(error.Message.Contains("no valid current tile") && actor.current_tile == null, "current tile fabricated");
}

static void IdentityMismatch()
{
    var world = Fixture(); var actor = Add(world, 1); actor.data.id = 2;
    var error = Fails(world);
    Check(error.Message.Contains("mismatched identity") && actor.data.id == 2 && world.units.dict.ContainsKey(1), "identity was rewritten");
}

static void MissingBatch()
{
    var world = Fixture(); var actor = Add(world, 1); StartPath(world, actor);
    actor.batch = null;
    Check(Fails(world).Message.Contains("no movement/combat batch") && actor.current_path.Count == 2, "live route silently removed to hide missing core batch");
}

static void Idempotent()
{
    var world = Fixture(); var actor = Add(world, 1);
    actor.beh_actor_target = new Actor(2) { data = null };
    actor.batch.main.Add(new Actor(3) { data = null });
    AWEmergencyNullReferenceRepairService.Repair(world);
    string second = AWEmergencyNullReferenceRepairService.Repair(world);
    Check(second.Contains("references=0, paths=0, tasks=0, batch_entries=0, unresolved=0") && actor.ai.Finished == 1, second);
}

static void ForeignWorld()
{
    var world = Fixture(); var actor = Add(world, 1); actor.has_attack_target = true;
    World.world = new MapBox();
    Check(Fails(world).Message.Contains("Current world") && actor.has_attack_target, "old world was mutated");
}

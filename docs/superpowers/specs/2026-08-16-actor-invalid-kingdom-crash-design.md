# Invalid Actor Kingdom Crash Repair Design

## Goal

Prevent WorldBox background zone recalculation from crashing when a living actor still references a Kingdom whose runtime object remains allocated but whose `data` has already been removed. Also prevent bandit stronghold creation rollback from producing this invalid reference.

## Root Cause

`SimObjectsZones.checkUnits()` sends each living actor to `current_tile.chunk.objects.addActor(actor)`. The vanilla `ChunkObjectContainer.addActor()` implementation reads `actor.kingdom.id`. `CoreSystemObject.getID()` dereferences `kingdom.data`, so a Kingdom with `asset != null` and `data == null` throws `NullReferenceException`.

The existing actor Kingdom safety layer only treats a missing `asset` as invalid. Its `SimObjectsZones.addUnit` prefix is also too early and too narrow: returning `false` there does not stop `checkUnits()` from calling `ChunkObjectContainer.addActor()` on the following line.

Bandit stronghold creation creates a temporary Kingdom and removes it during rollback. If any transaction actor still references that Kingdom at removal time, the actor becomes one possible producer of the invalid state. Other vanilla or mod lifecycle paths may produce the same state, so the repair cannot be limited to bandits.

## Design

### Runtime Validity Invariant

An Actor Kingdom is safe for vanilla zone, enemy, and display processing only when all three conditions hold:

- the Kingdom reference exists;
- `kingdom.data` exists;
- `kingdom.asset` exists.

The pure safety rules receive both the data and asset validity flags so this invariant is testable without WorldBox runtime objects.

### Crash Boundary Guard

Add a Harmony prefix for `ChunkObjectContainer.addActor(Actor)`. When the actor is absent or its Kingdom fails the invariant, the prefix queues that actor for repair and prevents the vanilla method from reading `kingdom.id`. Valid actors continue through the original method unchanged.

This guard is the final containment boundary. It prevents the crash even when an invalid reference originates outside the bandit feature.

### Actor Repair

Upgrade `ActorKingdomSafetyService` so an existing Kingdom counts as repaired only when both `data` and `asset` exist. Before joining a replacement Kingdom, clear an invalid current reference so vanilla join logic cannot inspect the disposed Kingdom.

Repair order remains conservative:

1. use the actor's current city's valid Kingdom;
2. otherwise use the actor asset's valid wild Kingdom;
3. otherwise leave the actor detached and keep vanilla unsafe processing blocked.

The repair queue remains the only deferred recovery mechanism; no actor is destroyed as part of this fix.

### Bandit Rollback Cleanup

Before removing a temporary bandit Kingdom, inspect the transaction actors and the temporary Kingdom's unit collection. Any actor still referencing the temporary Kingdom must be restored to its snapshot city or origin Kingdom when valid, or detached when no valid target exists. Only after that cleanup may `World.world.kingdoms.removeObject()` run.

This cleanup is idempotent so it can be called from all failure and rollback exits without changing successful stronghold creation.

## Error Handling

Invalid references are contained and queued rather than allowed into vanilla code that expects a live `KingdomData`. Repair failures do not resume unsafe processing. Diagnostic logging identifies the actor and failed recovery source without reading the invalid Kingdom ID.

## Testing

Add regression coverage before implementation:

- `data == false` and `asset == true` cannot enter vanilla zone or enemy processing;
- the same state selects repair and requires detaching the current reference;
- source-level integration checks require the `ChunkObjectContainer.addActor` prefix and a `kingdom.data` guard;
- source-level integration checks require actor-reference cleanup before temporary bandit Kingdom removal.

After the tests pass, build the net481 mod, deploy it to the WorldBox Mods directory, start WorldBox visibly, and inspect the new `Player.log` for the original `SimObjectsZones.checkUnits -> ChunkObjectContainer.addActor -> CoreSystemObject.getID` exception chain.

## Scope

This repair does not change bandit gameplay, stronghold layout, raids, food settlement, or Kingdom destruction policy. It only enforces safe Kingdom references and rollback cleanup.

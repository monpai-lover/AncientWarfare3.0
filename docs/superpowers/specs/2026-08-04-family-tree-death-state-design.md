# Family Tree Death State Root Fix Design

## Goal

Make a dead actor appear dead in the family tree immediately and remain dead
after save/load, including saves whose archive row still says `IS_ALIVE=1`.
The behavior must be identical in Native and Large scheduling modes.

## Confirmed Semantics

Once the current world is fully loaded, living actors are resident in
`World.world.units`. Therefore a family-tree actor ID that resolves to `null`
is dead. This inference must not run while a world is loading, clearing, or
while applying a result captured for an older world generation.

Resolution order:

1. Reject data from another world generation or an obsolete projection
   revision.
2. If the current world is stable and the runtime actor is `null`, resolve
   dead.
3. If the runtime actor exists but `isAlive()` is false or `isRekt()` is true,
   resolve dead.
4. Otherwise overlay a captured or accepted pending archive snapshot over the
   database snapshot.
5. Use the database state only when runtime authority is not yet available.

## Root Cause

`ActorDeathArchiveService.EnqueueLineage` currently keeps a death snapshot in
its private queue. The family-tree read path can only see
`ActorArchivePendingStore`, so the death is invisible until the writer accepts
it. With async database writes disabled by default, acceptance may never occur
before save. In addition, an async family-tree query can read `alive=true`
before death and then be stamped with the newer revision when it completes.

The backlog tuning in `b5e63a9` improves throughput but does not close either
correctness gap.

## Design

### Immediate Authority Overlay

Publish each successfully captured death to a dedicated in-memory captured
death store before enqueue returns. Publishing advances the family-tree
projection revision. The overlay is keyed by world generation, actor ID, and
death revision so an old completion cannot clear a newer death.

`FamilyTreeSnapshotOverlayService` reads this store before accepted pending
archives and database rows. The stable-world `actor == null` rule is an
additional authority signal and repairs stale old saves without guessing while
the world is loading.

### Durable Write Path

The authority cycle chooses one of two bounded paths:

- If the historical writer is ready and accepts the item, transfer ownership
  to the accepted pending store and then clear the captured overlay.
- If the writer is unavailable or rejects the item, write a small bounded
  number synchronously from the authority cycle. Clear the captured overlay
  only after the commit succeeds.

No SQLite write runs inside `Actor.die`. Save flush remains the final durability
barrier and must preserve snapshots on timeout or failure.

### Async Materialization

Family-tree query tickets retain their request world generation and projection
revision. Completion is accepted only if both still equal current authority.
An obsolete result is discarded and a fresh materialization is requested; it
must never be relabeled with the current revision.

## Failure Handling

- Queue full or synchronous write failure retains the captured overlay and
  retries with existing backoff.
- World reset clears all captured deaths from the old generation.
- Loading and replica application suppress `actor == null` inference.
- A database row is never rewritten from absence alone while runtime authority
  is unavailable.

## Tests

- DB alive plus stable-world `actor == null` resolves dead.
- The same snapshot remains unchanged during world load.
- Captured death is visible before async queue acceptance.
- Async DB disabled selects bounded synchronous fallback.
- Failed fallback retains the snapshot; successful commit clears it.
- A pre-death async UI result is rejected after revision changes.
- Save/reload keeps the actor dead.
- Matrix: Native/Large, async DB on/off, async UI on/off.

## Non-Goals

- Inferring a death cause when none was captured.
- Blocking the death callback on SQLite.
- Treating arbitrary missing actors as dead before world authority is ready.

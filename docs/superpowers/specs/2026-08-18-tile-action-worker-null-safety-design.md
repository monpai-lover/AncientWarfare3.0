# Tile Action Worker Null-Safety Design

**Status:** Approved direction, pending implementation review

## Problem

`AWCooperativeActorPostRunner.TileActionBatchWork.RunParallel()` currently
calls `CanSkipSafeGroundTileAction` from a worker thread. That helper reads
mutable Unity/WorldBox state (`Actor`, `current_tile`, tile type, actor asset,
building asset, and the fire array). A tile or actor can become invalid between
batch preparation and worker execution, producing a `NullReferenceException`
inside `u5_curTileAction` classification. The frame boundary catches the
exception and pauses the game, which is why the log reports a paused game even
though the pause is a response rather than the trigger.

## Goals

- Remove mutable Unity object reads from the tile-action worker path.
- Preserve the existing `u5_curTileAction` behavior and bounded commit batches.
- Skip invalid/disposed actors safely for the current tick and let normal
  container cleanup remove them on a later pass.
- Emit fatal scheduler/presentation boundary failures through `LogError`.
- Keep pathfinding, RTS ownership, transport, enemy search, timer partitioning,
  and spatial-index parallelism unchanged.

## Non-Goals

- Do not increase or otherwise redesign the global worker/thread budget.
- Do not replace the actor post runner or scheduler architecture.
- Do not turn ordinary recoverable fallbacks into error-level logs.

## Design

### Main-thread classification

`PrepareTileActionWorkItems` will capture and classify each batch on the
authority thread. It may inspect the live actor/tile/asset objects while the
simulation owns the batch. Only actor references selected for the native tile
action are retained in `SerialActors`.

`RunParallel` will no longer call `CanSkipSafeGroundTileAction` or dereference
an actor. It will only mark the prepared work item as checked. The tile-action
stage will proceed directly to the existing commit path rather than dispatching
mutable actor reads to `AWSimulationWorkerPool`.

### Commit and invalid state handling

The existing `splitPostJobs` commit cadence remains responsible for bounded
main-thread work. Before adding an actor to `SerialActors`, classification will
reject null actors and missing `current_tile`, tile type, or actor asset. A
rejected actor receives no `u5_curTileAction` call for that tick; the normal
container reconciliation path handles its removal later. No per-actor error
log is emitted because transient disposal is expected during world updates.

### Error severity

The following paths pause simulation after an unhandled scheduler exception and
will use `ModClass.LogError`: MapBox frame takeover, native authority cycle,
background simulation/presentation boundary, and native Army RTS scheduling.
Recoverable fallback and diagnostic messages remain warnings.

## Data Flow

```text
authority thread: live actor/tile reads
        -> TileActionBatchWork.SerialActors
        -> bounded main-thread u5_curTileAction commits

worker pool: no tile-action Unity object reads
```

## Testing

Add a failing source/rules guard before production changes that proves:

1. `RunParallel` does not invoke `CanSkipSafeGroundTileAction` or access actor
   fields.
2. Main-thread classification rejects invalid actor/tile/asset state without
   scheduling a native tile action.
3. Tile-action scheduling no longer creates a worker ticket for mutable actor
   classification.
4. All forced-pause scheduler exception paths call `LogError`, not
   `LogWarning`.

Run the focused rules test in RED before implementation, then the same test in
GREEN, the full rules executable, production `net48` build, and the existing
RTS/transport regression slices. Deploy only after all checks pass.

## Rollback

If the focused tile-action test or runtime smoke test fails, restore the prior
validated post-runner behavior while retaining the diagnostic severity change;
no other performance stage is part of this change.

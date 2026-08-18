# Cultiway Advanced Performance Extraction Design

**Status:** Approved design

**Goal:** Port the useful performance capabilities from `origin/fix/cultiway-perf-large-scheduler-completion` into the current `master` implementation without replacing current RTS, path ownership, multiplayer, court, bandit, or lineage behavior.

## Context

The Cultiway branch contains fourteen commits after the shared baseline. It is not a merge-ready feature branch: several final files are older than the current `master` files and remove current RTS stages, ownership guards, and production APIs. Direct cherry-picking fails in the scheduler, pathfinding, project file, and test registration layers.

The migration must therefore be behavior-oriented. Each Cultiway behavior is compared with the current implementation, ported into the current API, and protected by a focused regression test before the next layer is enabled.

## Non-negotiable Compatibility Rules

1. `master` remains the source of truth for all production APIs.
2. The current `Aw3RtsLogicalPulse` stage remains present and ordered as it is today.
3. `AWSimulationWorkerDispatchGate`, generation checks, scheduled/completed item checks, and exception propagation remain active.
4. `AWPathMovementBridge.HasOwnership()` remains a pure ownership-state query. It must not call `Finder.Poll()`.
5. `Finder.Poll()` may be consumed only by the existing formal `IsUsing`/`Update` lifecycle.
6. Worker threads may calculate immutable snapshots or indexes only. Unity objects, actor mutation, RTS ledger writes, and multiplayer writes are committed on the authoritative simulation thread.
7. Every new optimization has a fallback to the current vanilla/full-rebuild path.

## Architecture

The migration is organized into six layers:

1. **Scheduler diagnostics and boundaries:** expose coordinator and stage timings; preserve frame and presentation barriers; add actor timer range partitioning.
2. **Worker execution:** adopt persistent workers only behind the existing dispatch gate and generation lifecycle.
3. **Actor post pipeline:** add bounded post-processing and enemy presence snapshots without moving gameplay mutation off the authority thread.
4. **Spatial indexes:** add dirty, chunk, zone, island, and nearby-target indexes with deterministic full-rebuild fallback.
5. **Path request integration:** add deferred request batching and teardown safety while preserving the current single-consumer path result lifecycle.
6. **RTS integration:** connect the indexes and scheduling budgets to armies, royal guards, synthetic levies, transport, siege, and peacetime release paths.

The branch commits are source references, not cherry-pick instructions. Documentation-only commits are not imported into production code. Existing equivalent behavior is left unchanged and covered by tests rather than duplicated.

## Migration Batches

### Batch 0: Baseline and migration guards

Record the current `master` build and rule-test results. Add source guards that assert the five compatibility rules above. Add a migration diagnostic switch using the existing performance settings pattern; default it off until runtime validation passes.

### Batch 1: Scheduler boundaries and diagnostics

Port the useful parts of `fc5800b0`, `dad171a1`, `ce020d7e`, and `ed33acf5` into current files rather than replacing them. This includes completed-background-work checks, presentation shutdown barriers, visibility/timer cadence, timer range partitioning, and coordinator diagnostics. Do not remove current RTS logical stages or current scheduler gates.

Required tests cover completed-vs-pending background work, presentation barriers, timer range coverage, and diagnostic output with no scheduler behavior change.

### Batch 2: Persistent worker execution

Use `3c1980b4` as a behavior reference. Keep the current dispatch gate and operation-generation contract. Port only persistent thread reuse, bounded wake-up, and exact completion accounting. Keep the current failure diagnostics and add tests for missing work, duplicate completion, generation mismatch, cancellation, and shutdown.

### Batch 3: Actor post processing and enemy cache

Use `06844204` as a behavior reference. Port the enemy presence cache and post-processing slices behind immutable snapshots. Preserve current actor task ownership, RTS target locks, and main-thread commit boundaries. Add tests for stale entries, actor disposal, kingdom changes, cache rebuild, and authority/replica separation.

### Batch 4: Spatial membership indexes

Use `07a2858e`, `5d3ea7d6`, and `813369e0` as references. Port dirty tracking, chunk membership, zone units, island membership, and nearby target indexing into the current types. Preserve current `AWIncrementalChunkActorMembership` cleanup and vanilla full rebuild fallback. Add consistency tests for add, remove, kingdom transfer, cross-chunk movement, city disposal, and repeated dirty records.

### Batch 5: Deferred path requests and teardown safety

Use `97d6137a` and `dc425517` only as references. Do not copy their final `AWPathMovementBridge` wholesale. Port deferred request batching, terrain validation, and teardown null guards around the current path API. Add a regression test proving that a completed path result is consumed once, that `HasOwnership()` does not poll, and that a missing finder releases movement safely.

### Batch 6: RTS integration

Port the scheduling/index integration for armies, royal guards, and synthetic levies only after Batches 2-5 pass. Preserve current ownership release, transport handoff, siege return, captain succession, peacetime native AI release, and multiplayer authority checks. Add focused tests for land march, embark, unload, siege continuation, leader death, peace return, and actor hunger recovery.

### Batch 7: Enablement and cleanup

Enable each validated layer through the migration switch, deploy to the local Mods directory, and run a controlled WorldBox smoke test. Promote a layer to its normal default only after the corresponding runtime logs and tests pass. Keep full-rebuild and vanilla path fallbacks permanently available; remove only obsolete duplicate code proven unused by source guards.

## Verification Gates

Every batch must pass:

```powershell
dotnet build AncientWarfare3.csproj -c Release --no-restore
dotnet build Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-build
```

Before enabling a batch in the game, deploy the resulting DLL and verify logs for scheduler ownership, worker completion, path consumption, actor movement, RTS transport, and authority/replica boundaries. Any crash, stuck actor, duplicate path consumption, incomplete worker operation, or RTS ownership leak blocks that batch and triggers rollback to the previous validated commit.

## Explicitly Deferred

The following are not imported as wholesale commits: the branch's final `AWCooperativeSimulationRunner`, `AWCooperativeActorPostRunner`, `AWIncrementalSimObjectZoneUnits`, `AWParallelSimObjectZoneUnits`, `AWPathMovementBridge`, and worker-pool replacements. They contain stale deletions or incompatible APIs relative to current `master`; only their isolated behaviors are ported through the batches above.

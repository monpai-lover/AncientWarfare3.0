# Cultiway Perf Large Scheduler Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete AW3's large-step scheduler against `F:\WorldBox New Mod\Cultiway-Reborn-perf`, removing the incomplete-port stalls and scale bottlenecks while preserving AW3 pathfinding, RTS, multiplayer, and save-boundary ownership.

**Architecture:** Keep AW3's cooperative frame governor and AW-specific authority stages, but update the vanilla simulation side to the current Cultiway perf contracts. Use persistent simulation workers for bounded indexed work, ordered main-thread commits for live WorldBox mutations, preparation-scoped caches for repeated reads, and incremental spatial membership with full-rebuild fallbacks.

**Tech Stack:** C# 11 targeting .NET Framework 4.8, Unity/WorldBox, Harmony, `ConcurrentDictionary`, dedicated simulation threads, existing AW3 rules executable and PowerShell source guards.

---

## Porting Boundaries

- Port scheduler infrastructure and vanilla simulation optimizations: worker pool, Actor timer/visibility behavior, Actor post stages, enemy presence cache, deferred Actor path submissions, spatial membership indexing, lifecycle barriers, diagnostics, and semantic tests.
- Preserve AW3's `AWPathFinder`, `AWPathMovementBridge`, RTS route provider, authority cycle, multiplayer replica behavior, presentation snapshots, and save/load boundary handling.
- Do not port Cultiway ECS systems, cultivation gameplay, `CultiwayLogicScheduler`, `CooperativeSystemRootRunner`, geo-region gameplay initialization, portal routing, trains, or other content-only behavior.
- Worker stages may only perform operations proven safe by the current Cultiway implementation or by the vanilla parallel-job contract. Ordered commits remain on the main thread when they mutate task, target, tile, path cursor, city, army, or Unity presentation state.

### Task 1: Lock Down Scheduler Completion and Mode Boundaries

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt`
- Create: `Tests/CultiwayPerfSchedulerCompletionSourceGuard.ps1`
- Modify: `AncientWarfare3.csproj`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`
- Modify: `Code/patch/AW_FramePrioritySchedulerPatch.cs`
- Modify: `Code/patch/AW_ActorBoatLifecyclePatch.cs`

- [ ] Add failing regression assertions for completed post tickets, active-cycle ownership after the setting is disabled, and unconditional boat lifecycle notifications.
- [ ] Run the focused rules/source guard and verify the expected failures.
- [ ] Exclude already-completed post work from the join branch so `Step()` consumes and commits the ticket.
- [ ] Keep control until an admitted cycle reaches a boundary; apply setting changes only to future admissions.
- [ ] Keep the boat index current in both native and large modes and reset it on world teardown.
- [ ] Verify focused tests, full rules, production build, and commit.

### Task 2: Restore Actor Timer and Per-Frame Visibility Semantics

**Files:**
- Modify: `Code/core/performance/AWCooperativeActorParallelJobRunner.cs`
- Modify: `Code/patch/AW_FramePrioritySchedulerPatch.cs`
- Modify: `Tests/CultiwayPerfSchedulerCompletionSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt`

- [ ] Add failing checks for `_precalc_movement_speed_skips` and a once-per-render-frame visibility refresh before timer workers run.
- [ ] Verify RED.
- [ ] Port current Cultiway timer skip behavior without Cultiway flying-content branches.
- [ ] Port `RefreshFrameVisibility()` and invoke it after Actor read-boundary enforcement at frame start.
- [ ] Verify focused tests, full rules, production build, and commit.

### Task 3: Replace Nested TPL Scheduling With Persistent Simulation Workers

**Files:**
- Create: `Code/core/performance/AWSimulationWorkerPool.cs`
- Modify: `Code/core/performance/AWSimulationCoordinatorThread.cs`
- Modify: `Code/core/performance/AWCooperativeBatchRunner.cs`
- Modify: `Code/core/performance/AWCooperativeActorParallelJobRunner.cs`
- Modify: `Code/core/performance/AWCooperativeActorPostRunner.cs`
- Modify: `Code/core/performance/AWCooperativeWorldMaintenanceRunner.cs`
- Modify: `Code/core/performance/AWPerformanceSettings.cs`
- Modify: `Tests/CultiwayPerfSchedulerCompletionSourceGuard.ps1`

- [ ] Add failing tests/guards for persistent worker creation, indexed dynamic work claiming, main/coordinator assistance, exception propagation, and one active operation at a time.
- [ ] Verify RED.
- [ ] Port `SimulationWorkerPool` using AW settings and diagnostics, preserving a separate coordinator only for presentation overlap.
- [ ] Replace scheduler-owned `Parallel.For` calls with indexed pool work and remove competing TPL allocation from these paths.
- [ ] Add bounded wait/abort diagnostics; teardown must wait for live access to stop without silently losing the original operation error.
- [ ] Verify stress rules, full rules, production build, and commit.

### Task 4: Complete the Actor Post Pipeline and Enemy Presence Cache

**Files:**
- Replace: `Code/core/performance/AWCooperativeActorPostRunner.cs`
- Create: `Code/core/performance/AWEnemyPresenceCache.cs`
- Create: `Code/patch/AW_EnemyFinderCachePatch.cs`
- Modify: `Code/core/performance/AWCooperativeBatchRunner.cs`
- Modify: `Code/core/performance/AWSimulationTickBenchmark.cs`
- Modify: `Code/core/policy/RuntimePerformanceDiagnostic.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt`

- [ ] Add failing ordered-stage tests covering dead, tile action, frozen, update eligibility, current target, enemy preparation/search/commit, task verifier, path movement, smooth movement, and finish.
- [ ] Add failing cache lifecycle/concurrency tests covering no-war kingdoms, per-cycle invalidation, and selective negative-key clearing.
- [ ] Verify RED.
- [ ] Port the current Cultiway state machine and preparation-scoped cache, removing content-specific flying/ECS branches.
- [ ] Preserve exact vanilla random advancement, job skip counters, container ordering, and ordered main-thread commits.
- [ ] Add worker/commit/enemy-cache diagnostics and verify full rules plus production build.
- [ ] Compare the resulting stage list and cache lifecycle symbol-for-symbol with the perf reference, then commit.

### Task 5: Batch Actor Path Requests and Worker Wakeups

**Files:**
- Create: `Code/core/performance/AWDeferredPathRequestBatch.cs`
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`
- Modify: `Code/core/pathfinding/AWPathFinder.cs`
- Modify: `Code/core/performance/AWCooperativeActorPostRunner.cs`
- Modify: `Code/patch/AW_FramePrioritySchedulerPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt`

- [ ] Add failing tests for per-cycle capture, stable request order, bounded admission, cancellation, fallback to immediate submission, and main-thread wakeup application.
- [ ] Verify RED.
- [ ] Adapt `DeferredPathRequestBatch` to AW3 request/session types and keep all Actor/Army mutation in existing AW bridge commits.
- [ ] Add a hard capacity/backpressure policy so distinct Actor sessions cannot grow without bound.
- [ ] Apply worker wakeups at frame start after simulation read barriers.
- [ ] Verify pathfinding rules, scheduler rules, full rules, production build, and commit.

### Task 6: Port Incremental and Parallel Spatial Membership

**Files:**
- Create: `Code/core/performance/AWActorZoneMembershipDirtyIndex.cs`
- Create: `Code/core/performance/AWIncrementalChunkActorMembership.cs`
- Create: `Code/core/performance/AWParallelIslandActorMembership.cs`
- Create: `Code/core/performance/AWParallelSimObjectZoneUnits.cs`
- Create: `Code/core/performance/AWIncrementalSimObjectZoneUnits.cs`
- Create: `Code/patch/AW_SimObjectsZonesPatch.cs`
- Modify: Actor lifecycle/path movement patches that change `current_tile`, alive state, or boat membership.
- Modify: `Code/core/performance/AWCooperativeWorldMaintenanceRunner.cs`
- Modify: `Tests/CultiwayPerfSchedulerCompletionSourceGuard.ps1`

- [ ] Add failing pure rules and source guards for dirty membership capture, generation identity, deterministic chunk order, threshold fallback, and full-clear invalidation.
- [ ] Verify RED.
- [ ] Port vanilla-facing membership logic while excluding Cultiway geo-region and status-index consumers not present in AW3.
- [ ] Preserve vanilla city conquest/danger-zone ordering and full rebuild fallback whenever a dirty/version invariant is uncertain.
- [ ] Replace eligible tile/chunk/island full scans with persistent-worker indexed operations.
- [ ] Verify full rules, production build, world create/load/clear lifecycle tests, and commit.

### Task 7: Correct Remaining Semantic Drift and Guard Coverage

**Files:**
- Modify: `Code/core/performance/AWStatusSimulationScheduler.cs`
- Modify: `Code/core/performance/ArmyRtsSchedulingMode.cs`
- Modify: `Code/core/performance/ArmyRtsSchedulingService.cs`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`
- Replace/update: scheduler source guards under `Tests/`

- [ ] Add failing tests for original single-precision status timer decrement semantics and a cycle-frozen RTS owner.
- [ ] Verify RED.
- [ ] Match current Cultiway status timing semantics.
- [ ] Snapshot RTS scheduler ownership at admission and use it for both native army and AW authority stages.
- [ ] Replace obsolete exact-text guards with semantic source checks for all required stages, barriers, pool ownership, path batching, and spatial invalidation.
- [ ] Verify all guards, full rules, production build, and commit.

### Task 8: Static Diff, Runtime Validation, and Integration

**Files:**
- Modify: `docs/superpowers/plans/2026-08-08-cultiway-perf-large-scheduler-completion.md` with measured results.
- Modify: release notes only after runtime acceptance succeeds.

- [ ] Run a filename/symbol/stage audit against `Cultiway-Reborn-perf` and classify every remaining difference as AW-specific, Cultiway-content-only, or unresolved.
- [ ] Run full rules, all scheduler guards, `git diff --check`, and Release production build.
- [ ] Deploy source and DLL to the installed AW3 mod only after static verification passes; compare SHA-256 hashes.
- [ ] Test native and large modes across small/no-war, large/no-war/few kingdoms, large/no-war/many kingdoms, active war, save/load, world clear, and hot setting changes.
- [ ] Record frame, Actor, post worker/commit, enemy-cache, sim-zone, path queue, worker utilization, heap, and stale/cancel metrics.
- [ ] Require no logical-stage stall, no growing queue/heap trend, no repeated multi-frame commit spike, and materially reduced Actor/main-thread cost before merging.
- [ ] Request final spec and quality reviews, then use the branch-finishing workflow.

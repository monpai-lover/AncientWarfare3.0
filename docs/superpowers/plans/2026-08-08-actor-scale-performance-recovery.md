# Actor Scale Performance Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the main-thread Actor bottleneck that grows with map chunks and kingdom count, including the no-war case, while preserving vanilla Actor job ordering and behavior.

**Architecture:** Extend the existing AW cooperative scheduler with Cultiway's specialized Actor post-job pipeline. Keep mutation-sensitive vanilla operations on the main thread, but prepare enemy-search inputs and run pure candidate searches on the worker pool; commit targets in the original job order. Replace the global-lock enemy cache with a preparation-scoped cache whose lifecycle is tied to the Actor post pass.

**Tech Stack:** C#/.NET Framework 4.8, Unity/WorldBox, Harmony, existing `AWCooperative*` scheduler, `AWSimulationCoordinatorThread`, rule-test executable.

---

### Task 1: Add a no-war scale regression harness

**Files:**
- Create: `Code/core/performance/ActorScalePerformanceRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/ActorScalePerformanceRulesTests.cs.txt`

- [ ] **Step 1: Write failing rules for the observed workload.**

```csharp
public static void Run()
{
    Equal(false, ActorScalePerformanceRules.ShouldUseWarOnlyExplanation(
        liveActors: 84, kingdomCount: 24, mapTileCount: 512 * 512,
        activeWars: 0), "no-war large-map sample is still a valid regression");
    Equal(true, ActorScalePerformanceRules.ShouldPrepareEnemySearchOffThread(
        activeActorStage: true, enemySearchJobPresent: true),
        "enemy search must use the actor post pipeline");
    Equal(false, ActorScalePerformanceRules.ShouldHoldGlobalEnemyCacheLock(
        preparationWorkerCount: 8), "worker enemy preparation cannot use a global lock");
}
```

- [ ] **Step 2: Run the focused test and verify it fails because the rule does not exist.**

Run:
`dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --actor-scale-performance`

Expected: compile failure naming the missing `ActorScalePerformanceRules` methods.

- [ ] **Step 3: Add the smallest pure rule implementation.**

Create `Code/core/performance/ActorScalePerformanceRules.cs` with the three predicates above; the no-war predicate must return `false` whenever `activeWars == 0`, so diagnostics cannot incorrectly attribute this workload to RTS warfare.

- [ ] **Step 4: Register the test source and switch, then verify it passes.**

Add `<Compile Include="ActorScalePerformanceRulesTests.cs.txt" />` to the test project and add `--actor-scale-performance` to `Program.cs.txt`; run the command again and require `AW3 actor scale performance rules passed.`

- [ ] **Step 5: Commit the isolated regression harness.**

```text
git add Code/core/performance/ActorScalePerformanceRules.cs Tests/AncientWarfare3.Rules.Tests/ActorScalePerformanceRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "test: capture no-war actor scale regression"
```

### Task 2: Introduce specialized Actor runner interfaces

**Files:**
- Create: `Code/core/performance/IAWCooperativeBatchPostRunner.cs`
- Create: `Code/core/performance/IAWCooperativeBatchParallelJobRunner.cs`
- Modify: `Code/core/performance/AWCooperativeBatchRunner.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt`

- [ ] **Step 1: Add failing delegation tests.**

The test double must record `Start`, `Step`, `WaitingForBackgroundWork`, and `Abort` calls; assert that an Actor runner uses the injected post/parallel delegates while a Building runner keeps the existing generic path.

- [ ] **Step 2: Define the exact interfaces.**

```csharp
internal interface IAWCooperativeBatchPostRunner<TBatch, TObject>
    where TBatch : Batch<TObject>, new()
{
    void Start(List<TBatch> pBatches, float pElapsed);
    string GetNextPhaseName();
    bool Step();
    bool WaitingForBackgroundWork { get; }
    bool IsBackgroundWorkCompleted { get; }
    bool BeginParallelPresentationWork();
    void WaitForBackgroundWork();
    void Abort();
}
```

Add the parallel-job interface with `TrySkipAllBatches(...)` and `TryRunGroup(...)`, matching the signatures in Cultiway's `CooperativeActorParallelJobRunner`.

- [ ] **Step 3: Make `AWCooperativeBatchRunner` delegate only the Actor-specific stages.**

Preserve the current generic pre/parallel/apply flow for buildings. For Actor post work, call the injected post runner instead of `RunMainThreadJobsWithoutBenchmark` over every `jobs_post` entry. Do not change job order or `applyParallelResults` ownership.

- [ ] **Step 4: Run focused scheduler tests and commit.**

```text
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --actor-runtime
git add Code/core/performance/IAWCooperativeBatchPostRunner.cs Code/core/performance/IAWCooperativeBatchParallelJobRunner.cs Code/core/performance/AWCooperativeBatchRunner.cs Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt
git commit -m "perf: add specialized actor scheduler stages"
```

### Task 3: Port the Cultiway Actor parallel jobs

**Files:**
- Create: `Code/core/performance/AWCooperativeActorParallelJobRunner.cs`
- Modify: `Code/core/performance/AWCooperativeBatchRunner.cs`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`
- Modify: `Code/core/policy/RuntimePerformanceDiagnostic.cs`

- [ ] **Step 1: Port the timer/visibility implementation from `Cultiway-Reborn-perf/Source/Core/Performance/CooperativeActorParallelJobRunner.cs`.**

Rename namespaces and settings to AW equivalents. Keep `prepare` skipped and `update_visibility` skipped when frame-priority presentation already owns visibility. Retain the 128-actor timer range and the existing `AWSimulationCoordinatorThread` ticket path.

- [ ] **Step 2: Add worker/main-thread counters.**

Expose `actor_post_worker_ms`, `actor_post_commit_ms`, `enemy_search_calls`, `enemy_search_candidates`, and `enemy_search_empty` in the existing `[AW3 PERF]` line. The counters must distinguish worker execution time from main-thread commit time.

- [ ] **Step 3: Run actor runtime and scheduler tests.**

```text
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --actor-runtime
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --runtime-diagnostic-rules
```

- [ ] **Step 4: Commit the parallel-job port.**

```text
git add Code/core/performance/AWCooperativeActorParallelJobRunner.cs Code/core/performance/AWCooperativeBatchRunner.cs Code/core/performance/AWCooperativeSimulationRunner.cs Code/core/policy/RuntimePerformanceDiagnostic.cs
git commit -m "perf: move actor timer and visibility work off main thread"
```

### Task 4: Port the Actor post pipeline with ordered commits

**Files:**
- Create: `Code/core/performance/AWCooperativeActorPostRunner.cs`
- Modify: `Code/core/performance/AWCooperativeBatchRunner.cs`
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`

- [ ] **Step 1: Add a failing ordering test.**

Use a fake batch whose post jobs are named `u4_deadCheck`, `u5_curTileAction`, `u6_checkFrozen`, `u8_checkUpdateTimers`, `b1_checkUnderForce`, `b2_checkCurrentEnemyTarget`, `b3_findEnemyTarget`, `b4_checkTaskVerifier`, `b5_checkPathMovement`, and `u10_checkSmoothMovement`; assert that the runner emits commits in exactly that order and never commits a target before the corresponding search ticket completes.

- [ ] **Step 2: Port the safe phases from Cultiway.**

Implement these phases in `AWCooperativeActorPostRunner`: dead/tile/frozen preparation, enemy preparation, enemy worker search, ordered enemy commit, task verification, path movement, smooth movement, and finish. Keep `Actor` mutation calls (`setAttackTarget`, tile changes, task execution, path cursor consumption) on the main thread; only immutable candidate collection and scoring run on workers.

- [ ] **Step 3: Preserve AW path ownership and recovery.**

Worker code must call only read-only `AWPathFinder`/snapshot APIs. Main-thread commit must continue through `AWPathMovementBridge.Update`, `HandlePoll`, and existing army recovery callbacks; no new direct mutation of `Actor`, `WorldTile`, `TaxiManager`, or `Army` is allowed in worker code.

- [ ] **Step 4: Run focused tests, then build the production project.**

```text
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --actor-runtime
dotnet build AncientWarfare3.0\AncientWarfare3.0.csproj --no-restore
```

Expected: zero production errors; existing unrelated warnings may remain.

- [ ] **Step 5: Commit the ordered post pipeline.**

```text
git add Code/core/performance/AWCooperativeActorPostRunner.cs Code/core/performance/AWCooperativeBatchRunner.cs Code/core/pathfinding/AWPathMovementBridge.cs Code/core/performance/AWCooperativeSimulationRunner.cs Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt
git commit -m "perf: parallelize actor enemy search and post stages"
```

### Task 5: Replace the locked enemy cache with preparation-scoped caching

**Files:**
- Modify: `Code/core/performance/AWEnemyPresenceCache.cs`
- Modify: `Code/patch/AW_EnemyFinderCachePatch.cs`
- Modify: `Code/core/performance/AWCooperativeActorPostRunner.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt`

- [ ] **Step 1: Add concurrency regression tests.**

Assert that `BeginPreparation` clears the per-cycle kingdom cache, concurrent preparation reads do not enter a global monitor, `EndPreparation` disables the shortcut, and `ClearNegativeKeys` invalidates only the affected kingdom.

- [ ] **Step 2: Implement the Cultiway lifecycle.**

Add `BeginPreparation()`, `EndPreparation()`, `IsPreparationActive`, `HasPopulatedEnemy(...)`, and lock-free `ConcurrentDictionary` access for `Cache` and `NegativeKeys`. `AWCooperativeActorPostRunner.Start` calls `EndPreparation`; the enemy-preparation phase calls `BeginPreparation`; the runner always calls `EndPreparation` in `Finish` and `Abort`.

- [ ] **Step 3: Keep vanilla semantics.**

When preparation is inactive or a kingdom has a populated hostile candidate, return control to vanilla `EnemyFinderContainer.getData`; preserve the original chunk key and random advancement behavior for non-empty searches.

- [ ] **Step 4: Run the full rules suite and commit.**

```text
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
git add Code/core/performance/AWEnemyPresenceCache.cs Code/patch/AW_EnemyFinderCachePatch.cs Code/core/performance/AWCooperativeActorPostRunner.cs Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt
git commit -m "perf: remove global enemy cache lock"
```

### Task 6: Integrate diagnostics and verify scale behavior in-game

**Files:**
- Modify: `Code/core/policy/RuntimePerformanceDiagnostic.cs`
- Modify: `Code/core/performance/AWSimulationTickBenchmark.cs`
- Modify: `docs/superpowers/plans/2026-08-08-actor-scale-performance-recovery.md` (record results)

- [ ] **Step 1: Add a scale-test protocol.**

Use the same save/settings for four runs: small map/no war, large map/no war, large map/few kingdoms/no war, and large map/many kingdoms/no war. Keep live population near 84 and record ten `[AW3 PERF]` windows per run.

- [ ] **Step 2: Compare the required fields.**

Record `frame_ms`, `actor_ms`, `actor_other_ms`, `actor_parallel_stage_wall_ms`, `actor_post_worker_ms`, `actor_post_commit_ms`, `enemy_search_calls`, `enemy_search_candidates`, `async_active`, `managed_heap_delta_kb`, and `aw3_async_commit`.

- [ ] **Step 3: Enforce acceptance thresholds.**

The no-war large-map run must show enemy-search worker time separated from the main-thread Actor wall, no recurring multi-frame commit spike, no global-cache lock contention, and no unbounded growth in `async_traversal_stale` or managed heap. If `actor_post_commit_ms` grows with map size, stop and inspect the commit phase before deployment.

- [ ] **Step 4: Build, deploy, and compare source hashes.**

Build `AncientWarfare3.0.dll`, copy only the changed source/DLL files to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`, then SHA-256 compare each deployed file against the workspace. Do not modify RTS war logic.

- [ ] **Step 5: Commit the verified implementation and update the release notes.**

```text
git add Code/core/performance Code/core/pathfinding/AWPathMovementBridge.cs Code/patch/AW_EnemyFinderCachePatch.cs Code/core/policy/RuntimePerformanceDiagnostic.cs docs/superpowers/plans/2026-08-08-actor-scale-performance-recovery.md
git commit -m "fix: recover actor performance on large multi-kingdom maps"
```

## Scope boundaries

- No changes to Zhulu/total-war rules, RTS target selection, or war settlement behavior.
- No reduction of path worker count; this plan addresses the Actor post pipeline first.
- No worker-thread mutation of live Unity/WorldBox objects.
- The no-war, 84-actor, large-map case is a required acceptance case, not an optional benchmark.

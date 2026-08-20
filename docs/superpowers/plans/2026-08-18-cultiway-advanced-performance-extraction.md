# Cultiway Advanced Performance Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the Cultiway scheduler, worker, actor-post, spatial-index, path-batching, and RTS performance migration on current `master` without replacing current AW3 behavior or reintroducing path-result consumption and RTS ownership regressions.

**Architecture:** Treat `origin/fix/cultiway-perf-large-scheduler-completion` as a behavior reference only. Port isolated behavior into current types, preserve `Aw3RtsLogicalPulse`, `AWSimulationWorkerDispatchGate`, authoritative main-thread commits, and pure path ownership queries, and require a green verification gate after every batch.

**Tech Stack:** C# 11, .NET Framework 4.8, Unity/WorldBox, Harmony, SQLite, PowerShell source guards, the `AncientWarfare3.Rules.Tests` executable, and the existing RTS adversarial simulations.

---

## Current Status (Updated 2026-08-18)

- Tasks 1-7 are implemented on `master` in commits `995b0765` through
  `f8aa8419`.
- Automated extraction verification is recorded by `bfb8b9cf` and that
  baseline is present on `origin/master`.
- The extracted scheduler remains opt-in. Interactive WorldBox validation,
  installed-mod hash verification, default enablement, and deletion of
  `origin/fix/cultiway-perf-large-scheduler-completion` remain pending.
- The reference branch must stay available until the interactive matrix proves
  the current `master` implementation under real save/load, land-war, and
  amphibious-war workloads.

## File Map

- `Code/core/performance/AWSchedulerStageDiagnostics.cs`: scheduler-stage timing buckets and immutable snapshots.
- `Code/core/performance/AWCooperativeSimulationRunner.cs`: stage admission, timing boundaries, RTS logical pulse, and authoritative stage ordering.
- `Code/core/performance/AWCooperativeActorParallelJobRunner.cs`: timer partitioning and once-per-frame visibility updates.
- `Code/core/performance/AWSimulationWorkerPool.cs`: persistent workers, dispatch gate, exact item accounting, and teardown.
- `Code/core/performance/AWCooperativeActorPostRunner.cs`: worker-safe preparation and ordered main-thread actor commits.
- `Code/core/performance/AWEnemyPresenceCache.cs`: preparation-scoped enemy lookup cache.
- `Code/core/performance/AWIncrementalChunkActorMembership.cs`: deterministic chunk membership repair.
- `Code/core/performance/AWIncrementalSimObjectZoneUnits.cs`: incremental zone membership with full-rebuild fallback.
- `Code/core/performance/AWParallelSimObjectZoneUnits.cs`: bounded parallel rebuild.
- `Code/core/performance/AWNearbyStatusTargetIndex.cs`: nearby target cache.
- `Code/core/performance/AWDeferredPathRequestBatch.cs`: bounded, ordered main-thread path submission.
- `Code/core/pathfinding/AWPathFinder.cs`: path session state and new read-only diagnostics.
- `Code/core/pathfinding/AWPathMovementBridge.cs`: the sole movement result consumer.
- `Code/core/policy/RuntimePerformanceDiagnostic.cs`: combined scheduler/worker/cache/path metrics.
- `Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1`: non-negotiable compatibility boundaries.
- `Tests/CultiwayPerfSchedulerCompletionSourceGuard.ps1`: scheduler and worker completion contracts.
- `Tests/AncientWarfare3.Rules.Tests/SimulationWorkerPoolTests.cs.txt`: persistent worker concurrency tests.
- `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt`: scheduler, path, cache, and spatial regression rules.

### Task 1: Lock Current Compatibility Boundaries

**Files:**
- Create: `Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/CultiwayPerfSchedulerNonRegressionSourceGuard.ps1`

- [x] **Step 1: Add a focused source guard**

Create a method-block reader and assert these exact contracts:

```powershell
$runner = Read-Source 'Code\core\performance\AWCooperativeSimulationRunner.cs'
$pathBridge = Read-Source 'Code\core\pathfinding\AWPathMovementBridge.cs'
$workerPool = Read-Source 'Code\core\performance\AWSimulationWorkerPool.cs'

Require-Contains $runner 'Aw3RtsLogicalPulse' `
    'Cultiway extraction must retain the AW3 RTS logical pulse.'
$hasOwnership = Get-MethodBlock $pathBridge `
    'internal static bool HasOwnership(Actor pActor)'
Forbid-Contains $hasOwnership '.Poll(' `
    'HasOwnership must not consume path results.'
Require-Contains $workerPool 'AWSimulationWorkerDispatchGate' `
    'Persistent workers must retain generation-gated dispatch.'
Require-Contains $workerPool 'result.ExecutedItems != result.ScheduledItems' `
    'Persistent workers must reject incomplete operations.'
```

- [x] **Step 2: Register both Cultiway guards**

Add `Exec` entries after the existing non-regression guard:

```xml
<Exec Command="powershell -NoProfile -ExecutionPolicy Bypass -File &quot;$(MSBuildProjectDirectory)\..\CultiwayPerfSchedulerCompletionSourceGuard.ps1&quot;" />
<Exec Command="powershell -NoProfile -ExecutionPolicy Bypass -File &quot;$(MSBuildProjectDirectory)\..\CultiwayAdvancedPerformanceExtractionSourceGuard.ps1&quot;" />
```

- [x] **Step 3: Run the focused guards**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\CultiwayPerfSchedulerCompletionSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\CultiwayAdvancedPerformanceExtractionSourceGuard.ps1
```

Expected: both report `passed`.

- [x] **Step 4: Commit the compatibility boundary**

```powershell
git add -- Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1 Tests/CultiwayPerfSchedulerNonRegressionSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "test: lock Cultiway extraction compatibility boundaries"
```

### Task 2: Add Stage Diagnostics and Timer Partitioning

**Files:**
- Create: `Code/core/performance/AWSchedulerStageDiagnostics.cs`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`
- Modify: `Code/core/performance/AWCooperativeActorParallelJobRunner.cs`
- Modify: `Code/core/performance/AWSimulationCoordinatorThread.cs`
- Modify: `Code/core/policy/RuntimePerformanceDiagnostic.cs`
- Modify: `AncientWarfare3.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt`

- [x] **Step 1: Write failing stage snapshot tests**

Add assertions for bucket accumulation, frame totals, and formatting:

```csharp
AWSchedulerStageDiagnostics.BeginFrame(pSampling: true);
long started = AWSchedulerStageDiagnostics.Begin(
    AWSchedulerStageBucket.Actors);
AWSchedulerStageDiagnostics.End(AWSchedulerStageBucket.Actors, started);
AWSchedulerStageDiagnosticSnapshot snapshot =
    AWSchedulerStageDiagnostics.TakeSnapshot();
True(snapshot.Calls[(int)AWSchedulerStageBucket.Actors] == 1,
    "actor stage diagnostics record one call");
True(snapshot.FormatMilliseconds().Contains("actors:"),
    "actor stage diagnostics expose a stable label");
```

- [x] **Step 2: Run the rules project and verify RED**

Run:

```powershell
dotnet build Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore
```

Expected: compilation fails because `AWSchedulerStageDiagnostics` is not registered.

- [x] **Step 3: Port the stage diagnostic type**

Port `AWSchedulerStageDiagnostics.cs` from reference commit `97d6137a`, retaining the exact buckets `Maintenance`, `World`, `Map`, `Cities`, `Actors`, `Buildings`, `Armies`, `Kingdoms`, `Statuses`, `OtherVanilla`, and `Aw3Authority`.

Register the production file in `AncientWarfare3.csproj` and link the same source file into `AncientWarfare3.Rules.Tests.csproj` so the RED/GREEN test cycle compiles against the real implementation.

- [x] **Step 4: Instrument the current runner without changing stage order**

Wrap the current stage switch, leaving `Aw3RtsLogicalPulse` untouched:

```csharp
AWSchedulerStageBucket bucket = GetDiagnosticBucket(_stage);
long started = AWSchedulerStageDiagnostics.Begin(bucket);
try
{
    ExecuteCurrentStageCoreUnmeasured();
}
finally
{
    AWSchedulerStageDiagnostics.End(bucket, started);
}
```

- [x] **Step 5: Add coordinator totals**

In `AWSimulationCoordinatorThread.Complete`, accumulate `WallTicks` and `WaitTicks`, then expose:

```csharp
internal string GetDiagnostics()
{
    return string.Format(CultureInfo.InvariantCulture,
        "ops={0} wall={1:0.0}ms wait={2:0.0}ms active={3} name={4}",
        Interlocked.Read(ref _completedOperations),
        TicksToMilliseconds(Interlocked.Read(ref _completedWallTicks)),
        TicksToMilliseconds(Interlocked.Read(ref _completedWaitTicks)),
        active, activeName ?? "none");
}
```

- [x] **Step 6: Partition actor timer work into fixed ranges**

Keep current timer behavior and split batches into 128-actor ranges:

```csharp
private const int TimerRangeSize = 128;

for (int start = 0; start < count; start += TimerRangeSize)
    _timerRanges[rangeCount++] = new TimerRange(
        actors, start, Math.Min(count, start + TimerRangeSize));
```

- [x] **Step 7: Verify and commit**

Run the three standard build/test commands from the spec, then:

```powershell
git add -- AncientWarfare3.csproj Code/core/performance/AWSchedulerStageDiagnostics.cs Code/core/performance/AWCooperativeSimulationRunner.cs Code/core/performance/AWCooperativeActorParallelJobRunner.cs Code/core/performance/AWSimulationCoordinatorThread.cs Code/core/policy/RuntimePerformanceDiagnostic.cs Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt
git commit -m "perf: add scheduler stage diagnostics and timer partitioning"
```

### Task 3: Harden the Existing Persistent Worker Pool

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/SimulationWorkerPoolTests.cs.txt`
- Modify: `Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1`
- Review only: `Code/core/performance/AWSimulationWorkerPool.cs`
- Review only: `Code/core/performance/AWCooperativeBatchRunner.cs`

- [x] **Step 1: Add adversarial worker tests**

Extend `SimulationWorkerPoolTests.Run()` with tests that alternate one-item and 4096-item operations, inject an exception, and immediately run another operation. Assert every index is visited once and `ScheduledItems == ExecutedItems` after each successful operation.

```csharp
for (int pass = 0; pass < 256; pass++)
{
    int count = (pass & 1) == 0 ? 1 : 4096;
    int[] visits = new int[count];
    AWSimulationWorkerPool.WorkResult result =
        AWSimulationWorkerPool.Instance.RunIndexed(0, count,
            index => Interlocked.Increment(ref visits[index]));
    Equal(count, result.ScheduledItems, "scheduled count remains exact");
    Equal(count, result.ExecutedItems, "executed count remains exact");
    for (int index = 0; index < count; index++)
        Equal(1, visits[index], "each index executes once");
}
```

- [x] **Step 2: Verify the current stronger implementation**

Run the rules executable. Expected: PASS without removing `_dispatchGate`. If a test fails, repair the current pool; do not replace it with the branch version.

- [x] **Step 3: Extend the guard against stale worker replacement**

Require `_dispatchGate.Assign`, `_dispatchGate.Consume`, `_activeGeneration`, and incomplete-operation failure text. Forbid a worker loop that runs solely after `_workerSignals[i].Set()` without consuming a generation token.

- [x] **Step 4: Commit test hardening**

```powershell
git add -- Tests/AncientWarfare3.Rules.Tests/SimulationWorkerPoolTests.cs.txt Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1
git commit -m "test: harden persistent simulation worker contracts"
```

### Task 4: Complete Actor Post and Enemy Cache Parity

**Files:**
- Modify: `Code/core/performance/AWCooperativeActorPostRunner.cs`
- Modify: `Code/core/performance/AWEnemyPresenceCache.cs`
- Modify: `Code/patch/AW_EnemyFinderCachePatch.cs`
- Modify: `Code/core/performance/AWSimulationTickBenchmark.cs`
- Modify: `Code/core/policy/RuntimePerformanceDiagnostic.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt`

- [x] **Step 1: Write failing cache lifecycle tests**

Add source-level assertions that `BeginPreparation()` precedes enemy-search worker admission, `EndPreparation()` runs in a `finally`, disposed kingdoms clear negative keys, and actor mutation remains in ordered commit methods.

- [x] **Step 2: Compare stage lists with reference commit `06844204`**

Classify every reference stage as already present, AW3-specific replacement, or missing. Port only missing preparation/cache behavior. Do not replace the current file and do not remove RTS target locks or task ownership checks.

- [x] **Step 3: Keep worker/main-thread boundaries explicit**

Worker stages may fill immutable arrays and cache decisions. Calls that change `Actor.ai`, `current_tile`, path cursors, armies, tasks, targets, or Unity presentation must remain in current ordered commit methods.

- [x] **Step 4: Add diagnostics and verify**

Append `AWEnemyPresenceCache.GetDiagnostics()` and actor-post worker/commit timings to the existing runtime performance record. Run focused rules, full rules, and production build.

- [x] **Step 5: Commit**

```powershell
git add -- Code/core/performance/AWCooperativeActorPostRunner.cs Code/core/performance/AWEnemyPresenceCache.cs Code/patch/AW_EnemyFinderCachePatch.cs Code/core/performance/AWSimulationTickBenchmark.cs Code/core/policy/RuntimePerformanceDiagnostic.cs Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt
git commit -m "perf: complete actor post and enemy cache migration"
```

### Task 5: Validate and Complete Spatial Index Migration

**Files:**
- Modify: `Code/core/performance/AWIncrementalChunkActorMembership.cs`
- Modify: `Code/core/performance/AWIncrementalSimObjectZoneUnits.cs`
- Modify: `Code/core/performance/AWParallelSimObjectZoneUnits.cs`
- Modify: `Code/core/performance/AWNearbyStatusTargetIndex.cs`
- Modify: `Code/patch/AW_ActorSpatialMembershipPatch.cs`
- Modify: `Code/patch/AW_SimObjectsZonesPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt`
- Modify: `Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1`

- [x] **Step 1: Add consistency tests before changing production code**

Cover repeated remove, kingdom transfer, cross-chunk move, actor disposal, full clear, and stale dirty records. Assert `_total_units == units_all.Count` and that an actor appears in at most one kingdom list.

- [x] **Step 2: Verify RED against any missing invariant**

Run the rules executable. Record the first failing invariant; do not change multiple spatial components at once.

- [x] **Step 3: Port only missing behavior from `07a2858e`, `5d3ea7d6`, `813369e0`, and `3acdf3cb`**

Preserve current deterministic ordering and the stronger removal loop that clears stale references from every kingdom list. Keep full rebuild admission whenever generation, count, or rank validation fails.

- [x] **Step 4: Add a source guard for permanent fallbacks**

Require `Validate`, full-clear invalidation, `container.units_all.Count`, and the vanilla rebuild call. Forbid a code path that catches membership corruption and silently continues without validation or fallback.

- [x] **Step 5: Verify and commit**

Run focused rules, full rules, and production build, then:

```powershell
git add -- Code/core/performance/AWIncrementalChunkActorMembership.cs Code/core/performance/AWIncrementalSimObjectZoneUnits.cs Code/core/performance/AWParallelSimObjectZoneUnits.cs Code/core/performance/AWNearbyStatusTargetIndex.cs Code/patch/AW_ActorSpatialMembershipPatch.cs Code/patch/AW_SimObjectsZonesPatch.cs Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1
git commit -m "perf: complete incremental spatial membership migration"
```

### Task 6: Make Path Diagnostics Read-only and Preserve Single Consumption

**Files:**
- Modify: `Code/core/pathfinding/AWPathFinder.cs`
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`
- Modify: `Code/core/performance/AWDeferredPathRequestBatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportP0RulesTests.cs.txt`
- Modify: `Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1`

- [x] **Step 1: Write failing source guards**

Assert `HasOwnership()` and `DescribeRuntimeState()` contain no `.Poll(` or `OpenReadyCursor(`. Assert only `IsUsing`, `Update`, or their private lifecycle helpers may call those APIs.

- [x] **Step 2: Verify RED**

Expected: the guard fails because current `DescribeRuntimeState()` calls `finder.Poll(actorId)`.

- [x] **Step 3: Add a read-only path session snapshot**

Add an immutable state query to `AWPathFinder` that does not call the stream:

```csharp
public AWPathSessionState ReadState(long pActorId)
{
    if (!_sessions.TryGetValue(pActorId, out PathSessionRecord record))
        return AWPathSessionState.None;
    return new AWPathSessionState(
        hasQueued: record.Queued != null,
        hasRunning: record.Running != null,
        isLatestQueued: ReferenceEquals(record.Latest, record.Queued),
        isLatestRunning: ReferenceEquals(record.Latest, record.Running));
}
```

Use this snapshot in `DescribeRuntimeState()` and leave formal `Poll()` consumption unchanged.

- [x] **Step 4: Lock deferred request behavior**

Test stable actor order, latest-request replacement, capacity rejection, main-thread frame-start flush, and `Clear()` on world teardown. Preserve the current immediate submission fallback when capture is unavailable.

- [x] **Step 5: Add teardown safety without copying branch movement code**

At finder teardown, return a non-consuming `NoRequest`/stop decision. Do not copy the branch's parallel smooth-movement implementation or move actor tile mutation to a worker thread.

- [x] **Step 6: Verify and commit**

Run path rules, transport rules, full rules, and production build, then:

```powershell
git add -- Code/core/pathfinding/AWPathFinder.cs Code/core/pathfinding/AWPathMovementBridge.cs Code/core/performance/AWDeferredPathRequestBatch.cs Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportP0RulesTests.cs.txt Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1
git commit -m "fix: keep Cultiway path diagnostics non-consuming"
```

### Task 7: Validate RTS Ownership Across the Upgraded Pipeline

**Files:**
- Modify: `Code/core/performance/ArmyRtsSchedulingService.cs`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/core/lineage/ArmyRtsTransportService.cs`
- Modify: `Tests/ArmyRtsAdversarialSimulation/ContinuityAcceptanceSuite.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarriorDecisionAndCombatRegressionTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportP0RulesTests.cs.txt`

- [x] **Step 1: Add adversarial scenarios**

Cover land march, no-port embark, unload, next-city siege continuation, captain death, war end, peacetime native release, and hunger-task recovery. Every scenario must assert progress within a bounded number of logical pulses.

- [x] **Step 2: Freeze RTS ownership at cycle admission**

Snapshot the current scheduling mode once per admitted cycle and use the same value for army and AW authority stages. Do not let a setting toggle split ownership inside a cycle.

- [x] **Step 3: Preserve current handoffs**

Keep transport P0, siege task repair, return-to-vanilla release, and captain succession logic. Port only cache/index lookups that avoid repeated scans; do not replace the controller state machine.

- [x] **Step 4: Run RTS-specific verification**

Run:

```powershell
dotnet run --project Tests\ArmyRtsAdversarialSimulation\ArmyRtsAdversarialSimulation.csproj -c Release
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-build
```

Expected: all scenarios and rules pass with no stalled ownership state.

- [x] **Step 5: Commit**

```powershell
git add -- Code/core/performance/ArmyRtsSchedulingService.cs Code/core/performance/AWCooperativeSimulationRunner.cs Code/core/lineage/ArmyRtsControllerService.cs Code/core/lineage/ArmyRtsTransportService.cs Tests/ArmyRtsAdversarialSimulation/ContinuityAcceptanceSuite.cs Tests/AncientWarfare3.Rules.Tests/WarriorDecisionAndCombatRegressionTests.cs.txt Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportP0RulesTests.cs.txt
git commit -m "perf: integrate upgraded scheduler with current RTS ownership"
```

### Task 8: Interactive Validation, Default Enablement, and Branch Cleanup

**Files:**
- Modify: `Code/core/performance/AWPerformanceSettings.cs`
- Modify: `Code/core/policy/RuntimePerformanceDiagnostic.cs`
- Modify: `Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1`
- Modify: `docs/superpowers/plans/2026-08-18-cultiway-advanced-performance-extraction.md`

- [x] **Step 1: Run all static verification**

Run `git diff --check`, the production build, the rules build, the full rules executable, both Cultiway source guards, and RTS adversarial simulation. All must exit zero.

- [ ] **Step 2: Deploy to the installed mod**

Use the repository deployment script and verify the deployed DLL SHA-256 matches `bin\Release\net48\AncientWarfare3.dll`.

- [ ] **Step 3: Run the WorldBox smoke matrix**

Test native and upgraded modes with small peace, large peace, active land war, cross-water war, save/load, world clear, and live setting toggles. Inspect logs for worker completion mismatches, duplicate path consumption, stalled actors, RTS ownership leaks, and unbounded queue/index growth.

- [ ] **Step 4: Promote the validated pipeline**

Only after the smoke matrix passes, bind the advanced pipeline to `EnableFramePriorityScheduler`. Keep vanilla/full rebuild and immediate path submission fallbacks available.

- [ ] **Step 5: Record measurements and commit**

Record actor time, post worker/commit time, scheduler bucket time, path queue size, spatial fallback count, worker utilization, and RTS progress observations in this plan. Then:

```powershell
git add -- Code/core/performance/AWPerformanceSettings.cs Code/core/policy/RuntimePerformanceDiagnostic.cs Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1 docs/superpowers/plans/2026-08-18-cultiway-advanced-performance-extraction.md
git commit -m "perf: enable validated Cultiway advanced pipeline"
```

- [x] **Step 6: Record and push the automated extraction baseline**

Commits `995b0765` through `f8aa8419` implement the extraction, and
`bfb8b9cf` records the automated verification. This baseline is present on
`origin/master`.

- [ ] **Step 7: Complete final review and remove the reference branch**

After Steps 2-5 pass, request final spec-compliance and code-quality reviews,
rerun the full verification commands on the reviewed HEAD, and push any
default-enablement or measurement commit. Delete
`origin/fix/cultiway-perf-large-scheduler-completion` only after confirming
that every reference behavior is ported, superseded by a stronger current
implementation, or intentionally excluded in the committed audit.

## Implementation Record

Implemented on `master` from `995b0765` through `f8aa8419`.

- Added scheduler stage buckets, coordinator totals, and bounded 128-actor timer ranges without changing simulation stage order.
- Retained the generation-gated persistent worker pool and added alternating 1/4096 item adversarial coverage.
- Audited actor-post and enemy-search stages against `06844204`; current RTS P0, task ownership, ordered commit, and path preparation behavior supersede the reference implementation. Added enemy-presence cache diagnostics.
- Audited chunk, zone, and nearby-target indexes against `07a2858e`, `5d3ea7d6`, `813369e0`, and `3acdf3cb`; current deterministic membership repair supersedes them. Added optimization-fault invalidation and vanilla rebuild fallback.
- Replaced consuming path diagnostics with `AWPathFinder.ReadState()`. `HasOwnership()` and `DescribeRuntimeState()` are now read-only, while formal movement update helpers remain the only result consumers.
- Restored the P0 temporary transport-boat no-batch movement boundary that was lost in `55f3252a`.
- Captured the scheduler mode at cooperative cycle admission and passed it through the RTS logical pulse.

Automated verification on 2026-08-18:

- Production `net48` build: passed with zero warnings and zero errors.
- Rules `net9.0` build and full executable: passed.
- Cultiway completion, non-regression, and advanced extraction source guards: passed.
- RTS war lifecycle, member combat, transport P0, and peacetime ownership release slices: passed.
- RTS adversarial simulation: 10 continuity scenarios passed; 80 large-mode armies advanced; no duplicate assignments.

The advanced scheduler remains opt-in (`AW3_ENABLE_FRAME_PRIORITY_SCHEDULER=false`) until the interactive WorldBox smoke matrix is completed. Native/full-rebuild and immediate path submission fallbacks remain available. The reference remote branch is retained until that smoke matrix confirms final cleanup is safe.

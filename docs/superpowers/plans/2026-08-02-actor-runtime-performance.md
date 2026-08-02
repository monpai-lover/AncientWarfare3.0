# Actor Runtime Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce AW3 Actor hot-path and batch-death frame cost without changing WorldBox Actor scheduling or degrading RTS behavior.

**Architecture:** Add allocation-free diagnostic gates and state signatures on the main thread, route immutable death snapshots through the existing `HistoricalWriteService`, replace presentation full scans with dirty IDs plus a bounded repair cursor, and coalesce equivalent requests inside the existing per-Actor path work slot. Live WorldBox and Unity objects never leave the main thread; every optimization has an existing synchronous fallback.

**Tech Stack:** C# source mod, Harmony, WorldBox/Unity APIs, System.Data.SQLite, existing AW3 async writer and rules-test executable, PowerShell source guards.

---

### Task 1: Freeze The Baseline And Add Test Registration

**Files:**
- Create: `docs/performance/2026-08-02-actor-runtime-baseline.md`
- Create: `Tests/AncientWarfare3.Rules.Tests/ActorDiagnosticSamplingRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/ActorAgeWorkRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/ActorDeathArchiveRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/ActorPresentationDirtyRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/AWPathRequestReuseRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Record the accepted baseline**

Write the measured 427-sample baseline into the performance document: Actor average `2.31 ms`, Actor maximum `43.218 ms`, `parallel_checks` maximum `9.806 ms`, Actor AI maximum `3.949 ms`, smoothing maximum `1.342 ms`, and the `39.591 ms` `updateDeathCheck` spike. Record that `update_age_ms` was zero in this sample and therefore is a static-risk optimization, not a measured root-cause claim.

- [ ] **Step 2: Add empty test entry points**

Create each test class with a `Run()` method and register the `.cs.txt` file in the rules test project. Call the five `Run()` methods immediately before `Console.WriteLine("Rule tests passed.")`.

```csharp
internal static class ActorDiagnosticSamplingRulesTests
{
    public static void Run() { }
}
```

- [ ] **Step 3: Run the unchanged suite**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: `Rule tests passed.` This establishes that registration itself has not changed behavior.

- [ ] **Step 4: Commit the baseline and test shells**

```powershell
git add -f docs/performance/2026-08-02-actor-runtime-baseline.md
git add Tests/AncientWarfare3.Rules.Tests
git commit -m "test: establish actor runtime performance baseline"
```

### Task 2: Define Bounded Actor Diagnostic Rules

**Files:**
- Create: `Code/core/policy/ActorDiagnosticSamplingRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ActorDiagnosticSamplingRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write failing diagnostic budget tests**

```csharp
Equal(false, ActorDiagnosticSamplingRules.ShouldCollect(
    diagnosticsEnabled: false, benchmarkEnabled: false,
    used: 0, budget: 16), "disabled diagnostics do no Actor detail work");
Equal(true, ActorDiagnosticSamplingRules.ShouldCollect(
    diagnosticsEnabled: true, benchmarkEnabled: false,
    used: 0, budget: 16), "first diagnostic sample is accepted");
Equal(false, ActorDiagnosticSamplingRules.ShouldCollect(
    diagnosticsEnabled: true, benchmarkEnabled: false,
    used: 16, budget: 16), "detail budget is bounded");
Equal(64, ActorDiagnosticSamplingRules.ClampBudget(1000),
    "detail budget has a hard upper bound");
```

- [ ] **Step 2: Run the tests and observe the missing-type failure**

Run the rules test executable. Expected: compile failure naming `ActorDiagnosticSamplingRules`.

- [ ] **Step 3: Implement the pure rules**

```csharp
namespace AncientWarfare3.core.policy
{
    public static class ActorDiagnosticSamplingRules
    {
        public const int MaximumDetailSamplesPerFrame = 64;

        public static int ClampBudget(int pBudget)
        {
            return System.Math.Max(0, System.Math.Min(
                MaximumDetailSamplesPerFrame, pBudget));
        }

        public static bool ShouldCollect(bool diagnosticsEnabled,
            bool benchmarkEnabled, int used, int budget)
        {
            return (diagnosticsEnabled || benchmarkEnabled) && used >= 0 &&
                   used < ClampBudget(budget);
        }
    }
}
```

- [ ] **Step 4: Run tests and commit**

Expected: `Rule tests passed.`

```powershell
git add Code/core/policy/ActorDiagnosticSamplingRules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "test: define bounded actor diagnostic sampling"
```

### Task 3: Remove Disabled Diagnostic Hot-Path Work

**Files:**
- Modify: `Code/core/policy/RuntimePerformanceDiagnostic.cs`
- Modify: `Code/patch/AW_ActorAiBenchmarkPatch.cs`
- Modify: `Code/patch/AW_ActorBatchBenchmarkPatch.cs`
- Modify: `Code/patch/AW_ActorRacePerformancePatch.cs`
- Create: `Tests/ActorRuntimePerformanceSourceGuardTests.ps1`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Write a failing source guard**

The guard must require `RuntimePerformanceDiagnostic.ShouldCollectActorDetail`, `TryConsumeActorDetailSample`, and a reset of the frame counter in `BeginFrame`. It must reject any `task.id` access in `UpdateAi_Prefix` before the first `ShouldCollectActorDetail()` branch.

```powershell
$ai = Get-Content -Raw (Join-Path $root 'Code/patch/AW_ActorAiBenchmarkPatch.cs')
$gate = $ai.IndexOf('ShouldCollectActorDetail()')
$task = $ai.IndexOf('.ai.task.id')
if ($gate -lt 0 -or $task -lt 0 -or $task -lt $gate) {
    throw 'Actor task lookup must occur after the disabled diagnostic gate.'
}
```

Register the guard in `Tests/SourceGuardTests.ps1`.

- [ ] **Step 2: Run the source guard and observe failure**

```powershell
pwsh -NoProfile -File Tests/ActorRuntimePerformanceSourceGuardTests.ps1
```

Expected: failure because the fast-path APIs do not exist and task lookup precedes the gate.

- [ ] **Step 3: Add the shared frame budget**

Add `_actorDetailSamples` and `ActorDetailBudgetPerFrame = 64` to `RuntimePerformanceDiagnostic`. Reset the counter in `BeginFrame` even on non-sampling frames. Implement:

```csharp
public static bool ShouldCollectActorDetail()
{
    return _sampling || Bench.bench_enabled;
}

public static bool TryConsumeActorDetailSample()
{
    if (!ShouldCollectActorDetail()) return false;
    int used = System.Threading.Interlocked.Increment(
        ref _actorDetailSamples) - 1;
    return ActorDiagnosticSamplingRules.ShouldCollect(
        _sampling, Bench.bench_enabled, used, ActorDetailBudgetPerFrame);
}
```

- [ ] **Step 4: Short-circuit all three Harmony patches**

`AW_ActorAiBenchmarkPatch.UpdateAi_Prefix` writes `default` state and returns before task lookup when detail collection is disabled or the budget is exhausted. `AW_ActorBatchBenchmarkPatch.Prefix` returns a zero-start state unless sampling is active. `AW_ActorRacePerformancePatch` uses the same detail budget for `updateAge` and `calculateMainSprite`. Every postfix returns immediately for a zero/default state.

- [ ] **Step 5: Verify and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
pwsh -NoProfile -File Tests/ActorRuntimePerformanceSourceGuardTests.ps1
git add Code/core/policy Code/patch/AW_ActorAiBenchmarkPatch.cs Code/patch/AW_ActorBatchBenchmarkPatch.cs Code/patch/AW_ActorRacePerformancePatch.cs Tests
git commit -m "perf: eliminate disabled actor diagnostic overhead"
```

Expected: rules and guard pass; no simulation method is skipped.

### Task 4: Define Actor Age-Work Signatures

**Files:**
- Create: `Code/core/lineage/ActorAgeWorkRules.cs`
- Create: `Code/core/lineage/ActorAgeWorkState.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ActorAgeWorkRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write failing signature tests**

Test that unchanged signatures run no work; becoming adult triggers dynastic work; profession or military membership changes trigger job/release work; war-state change triggers military work but not dynastic work; and `force=true` runs all stages.

```csharp
var same = new ActorAgeWorkState(true, 2, true, true, false);
Equal(ActorAgeWorkStage.None,
    ActorAgeWorkRules.Resolve(same, same, force: false),
    "unchanged Actor state performs no AW3 age work");
var adult = new ActorAgeWorkState(true, 2, true, true, false);
var child = new ActorAgeWorkState(false, 2, true, true, false);
True((ActorAgeWorkRules.Resolve(child, adult, false) &
      ActorAgeWorkStage.DynasticTitle) != 0,
    "adulthood triggers dynastic-title work");
```

- [ ] **Step 2: Run tests and observe failure**

Expected: missing `ActorAgeWorkState`, `ActorAgeWorkStage`, and `ActorAgeWorkRules`.

- [ ] **Step 3: Implement immutable state and bit-mask rules**

`ActorAgeWorkState` contains `IsAdult`, `Profession`, `InPermanentArmy`, `AtWar`, and `DynasticEligible`. `ActorAgeWorkStage` is a `[Flags]` enum with `DynasticTitle`, `StandingArmyJob`, and `MilitaryRoleRelease`. `Resolve` compares only inputs consumed by each service; it must not contain or reference an `Actor`.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
git add Code/core/lineage/ActorAgeWorkRules.cs Code/core/lineage/ActorAgeWorkState.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "test: define state-triggered actor age work"
```

### Task 5: Integrate State-Triggered Age Work

**Files:**
- Create: `Code/core/lineage/ActorAgeWorkService.cs`
- Modify: `Code/patch/AW_AgePatch.cs`
- Modify: `Code/core/lineage/StandingArmyPeacetimeService.cs`
- Modify: `Code/core/lineage/DynasticReproductionService.cs`
- Modify: `Code/core/lineage/DynasticTitleService.cs`
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Tests/ActorRuntimePerformanceSourceGuardTests.ps1`

- [ ] **Step 1: Extend the source guard**

Require exactly one `ActorAgeWorkService.Process(__instance)` call in `AW_AgePatch.UpdateAge_Postfix` and reject direct calls there to the three existing services.

- [ ] **Step 2: Run the guard and observe failure**

Expected: direct service calls are still present.

- [ ] **Step 3: Implement the main-thread state service**

Use Actor data keys for the compact previous signature and one force-refresh bit. `Process(Actor)` captures the current signature, calls `ActorAgeWorkRules.Resolve`, runs only selected services, and persists the signature only after successful processing. `MarkDirty(Actor)` sets the force-refresh bit. `Remove(long actorId)` clears any auxiliary in-memory state.

Do not scan all Actors. Add `MarkDirty` at the existing entry points inside the three services when profession, military membership, or dynastic eligibility is actually changed. Death calls `Remove`.

- [ ] **Step 4: Preserve Xia old-head behavior**

Keep the existing Xia old-head threshold logic in `AW_AgePatch` after `ActorAgeWorkService.Process`. It remains independently state-checked by `XIA_OLD_HEAD_ACTIVE` and must not be merged into the military signature.

- [ ] **Step 5: Verify and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
pwsh -NoProfile -File Tests/ActorRuntimePerformanceSourceGuardTests.ps1
git add Code/core/lineage Code/patch/AW_AgePatch.cs Code/patch/AW_ActorDeathPatch.cs Tests
git commit -m "perf: trigger actor age services on state changes"
```

### Task 6: Define Immutable Death Archive Work

**Files:**
- Create: `Code/core/lineage/ActorDeathArchiveModels.cs`
- Create: `Code/core/lineage/ActorDeathArchiveRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ActorDeathArchiveRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write failing idempotency and lifecycle tests**

Cover stable keys, duplicate rejection, stale-generation rejection, retry limits, exponential backoff capped at 256 frames, queue saturation choosing synchronous fallback, and save readiness requiring zero captured, running, retry, and completion items.

```csharp
Equal("death:7:42:3:lineage_archive",
    ActorDeathArchiveRules.Key(7, 42, 3, "lineage_archive"),
    "death archive keys are stable and stage-specific");
True(ActorDeathArchiveRules.AcceptCompletion(7, 7, 3, 3),
    "matching world and death revisions commit");
Equal(false, ActorDeathArchiveRules.AcceptCompletion(7, 8, 3, 3),
    "old-world completion is stale");
True(ActorDeathArchiveRules.ReadyForSave(0, 0, 0, 0),
    "save requires every death queue to be empty");
```

- [ ] **Step 2: Run tests and observe failure**

Expected: missing death archive types.

- [ ] **Step 3: Implement primitive-only models and rules**

`ActorDeathSnapshot` contains copied primitives and strings required by the Actor archive, person history, and school membership close:

- world generation, Actor ID, death revision, death time, death cause, and attack type;
- given/display/family/clan names, lineage ID, shi ID, lineage status, noble distance, and noble-origin fields;
- asset/subspecies IDs and names, sex, age, profession, king/leader/warrior flags, and historical title;
- kingdom/city IDs, names, colors, social title, and social-title color;
- clan ID/color/banner fields, parent IDs, generation, and birth time;
- head, skin, skin set, age overgrowth, phenotype index/shade, and founded branch ID;
- active school membership ID, school ID, role, join time, and the copied localized death-event text.

It exposes no `Actor`, `City`, `Kingdom`, `Clan`, `WorldTile`, Unity object, delegate, or mutable collection. `ActorDeathArchiveResult` contains only the key, stamp, success flag, retry disposition, and committed revisions.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
git add Code/core/lineage/ActorDeathArchiveModels.cs Code/core/lineage/ActorDeathArchiveRules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "test: define immutable actor death archive work"
```

### Task 7: Move Death Persistence Off The Death Call Stack

**Files:**
- Create: `Code/core/lineage/ActorDeathArchiveService.cs`
- Modify: `Code/core/lineage/LineageArchiveWriter.cs`
- Modify: `Code/core/schools/SchoolMembershipService.cs`
- Modify: `Code/core/lineage/HistoryWriter.cs`
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Code/core/asyncwork/AWAsyncWorldLifecycle.cs`
- Modify: `Code/core/policy/RuntimePerformanceDiagnostic.cs`
- Create: `Tests/ActorDeathArchiveSourceGuardTests.ps1`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Write the failing source guard**

Require `AW_ActorDeathPatch` to capture one `ActorDeathSnapshot`, require lineage/history/school persistence calls to accept the snapshot, and reject `Actor`, `City`, `Kingdom`, `Clan`, `WorldTile`, and `UnityEngine` fields in `ActorDeathArchiveModels.cs`.

- [ ] **Step 2: Run the guard and observe failure**

Expected: the death patch still invokes archive services with a live Actor.

- [ ] **Step 3: Capture once while the Actor is valid**

In the death prefix, retain immediate live-world cleanup: military indexes, succession, office/title transitions, guard cleanup, path cancellation, and presentation dirty marking. After those fields are finalized, call `ActorDeathArchiveService.Capture(__instance, pType)` once. Copy every string and scalar needed by the three persistence consumers.

- [ ] **Step 4: Add snapshot overloads without a second worker system**

Add `LineageArchiveWriter.TryUpsertDeathSnapshot`, `HistoryWriter.RecordDeferredDeath(ActorDeathSnapshot)`, and `SchoolMembershipService.QueueDeathSnapshot`. Build `HistoricalWriteEnvelope` values on the main thread and enqueue through the existing `HistoricalWriteService`; do not add an `AWAsyncLane` or another SQLite connection.

For Actor archive SQL, use a single upsert envelope keyed by `actor-archive:{actorId}`. Preserve historical-only values with SQL `CASE`/`COALESCE` expressions rather than synchronously calling `LineageArchiveReader.ReadRow` from the death stack.

- [ ] **Step 5: Make retries ID-based and bounded**

Replace school death retry state that retains `PendingSchoolDeath.Actor` with the immutable snapshot plus actor ID. Any remaining live cleanup resolves the Actor by ID on the main thread and tolerates it already being destroyed. Queue-full or writer-unavailable outcomes enter the existing deferred main-thread queue with a maximum of 64 items or 1 ms per authority cycle.

- [ ] **Step 6: Extend save and world lifecycle barriers**

`AW_SavePatch.TryPrepareForSave` calls `ActorDeathArchiveService.FlushForSave(TimeSpan.FromSeconds(5), out error)` before `HistoricalWriteService.FlushForSave`. `AWAsyncWorldLifecycle` advances generation before clearing death queues and rejects stale completions. A timeout returns `false` and the existing save prefix throws `AWSaveBoundaryException`; it must never report success with pending death work.

- [ ] **Step 7: Verify and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
pwsh -NoProfile -File Tests/ActorDeathArchiveSourceGuardTests.ps1
pwsh -NoProfile -File Tests/SourceGuardTests.ps1
git add Code/core/lineage Code/core/schools/SchoolMembershipService.cs Code/core/asyncwork/AWAsyncWorldLifecycle.cs Code/core/policy/RuntimePerformanceDiagnostic.cs Code/patch/AW_ActorDeathPatch.cs Code/patch/AW_SavePatch.cs Tests
git commit -m "perf: defer immutable actor death persistence"
```

### Task 8: Define Dirty Presentation Scheduling

**Files:**
- Create: `Code/core/performance/AWActorPresentationDirtyRules.cs`
- Create: `Code/core/performance/AWActorPresentationDirtyIndex.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ActorPresentationDirtyRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write failing dirty-index tests**

Test ID coalescing, remove-over-update precedence, generation reset, a capture item cap, a time-budget stop, bounded repair cursor wraparound, and the requirement that the first snapshot remains incomplete until the cursor has visited all Actor IDs.

- [ ] **Step 2: Run tests and observe failure**

Expected: missing dirty rules and index.

- [ ] **Step 3: Implement allocation-bounded structures**

The index owns `HashSet<long> dirty`, `HashSet<long> removed`, a repair cursor, and an integer world generation. `MarkDirty`/`MarkRemoved` are main-thread only. `Take(maxItems)` reuses a caller-provided list, removes accepted IDs from the sets, and never enumerates the WorldBox unit manager.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
git add Code/core/performance/AWActorPresentationDirtyRules.cs Code/core/performance/AWActorPresentationDirtyIndex.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "test: define dirty actor presentation scheduling"
```

### Task 9: Replace Periodic Presentation Full Scans

**Files:**
- Modify: `Code/core/performance/AWActorPresentationSnapshot.cs`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`
- Create: `Code/patch/AW_ActorPresentationDirtyPatch.cs`
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Tests/CultiwayLargeSchedulerPresentationSourceGuard.ps1`
- Modify: `Tests/CultiwayLargeSchedulerAw3IntegrationSourceGuard.ps1`
- Modify: `Tests/ActorRuntimePerformanceSourceGuardTests.ps1`

- [ ] **Step 1: Add failing source guards**

Require `CaptureIfRequested` to consume dirty IDs and reject `FullCaptureRealIntervalSeconds`, `FullCaptureSimulationIntervalSeconds`, and interval-triggered full capture as steady-state mechanisms. Preserve triple-buffer ownership and `AcquireLatest()`.

- [ ] **Step 2: Run presentation guards and observe failure**

```powershell
pwsh -NoProfile -File Tests/CultiwayLargeSchedulerPresentationSourceGuard.ps1
pwsh -NoProfile -File Tests/ActorRuntimePerformanceSourceGuardTests.ps1
```

Expected: the current one-second full capture policy violates the new guard.

- [ ] **Step 3: Build snapshots from dirty IDs**

Keep the current writer/ready/render triple buffer. Start a writer from the latest complete snapshot, apply removed IDs, then recapture dirty Actor IDs under `64 items` and `0.75 ms` per call. Call `calculateMainSprite` only when the captured sprite revision differs from the previous sample. Publish only complete coherent snapshot generations.

- [ ] **Step 4: Add lifecycle dirty hooks and bounded repair**

Patch existing Actor birth/add, death/destroy, `clearGraphicsFully`, equipment dirty, status add/remove, and kingdom/city ownership change points to mark the Actor ID. Add a repair cursor that samples at most 32 live IDs per second to recover missed hooks. Initial world load uses the same cursor and must not perform one full Actor traversal in a frame.

- [ ] **Step 5: Preserve scheduler gates**

`AWCooperativeSimulationRunner` requests and captures presentation data only when `EnableFramePriorityScheduler`, game-loaded, non-loading, and non-replica gates pass. With the scheduler disabled, the dirty index can collect IDs but must not scan or capture.

- [ ] **Step 6: Verify and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
pwsh -NoProfile -File Tests/CultiwayLargeSchedulerPresentationSourceGuard.ps1
pwsh -NoProfile -File Tests/CultiwayLargeSchedulerAw3IntegrationSourceGuard.ps1
pwsh -NoProfile -File Tests/ActorRuntimePerformanceSourceGuardTests.ps1
git add Code/core/performance Code/patch/AW_ActorPresentationDirtyPatch.cs Code/patch/AW_ActorDeathPatch.cs Tests
git commit -m "perf: update actor presentation from dirty snapshots"
```

### Task 10: Define Equivalent Path Request Reuse

**Files:**
- Modify: `Code/core/pathfinding/AWPathLifecycleRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AWPathRequestReuseRulesTests.cs.txt`

- [ ] **Step 1: Write failing reuse tests**

Test that the same Actor, target tile, movement flags, bounded-water settings, start region, and terrain revision reuse the active request. Different Actor, target, start region, terrain revision, boarding state, or movement flags must not reuse it. Test active age and completed-cache bounds.

```csharp
var key = new AWPathReuseKey(42, 10, 20, false, false, false,
    32, false, 0, 7);
var changedTerrain = new AWPathReuseKey(42, 10, 20, false, false,
    false, 32, false, 0, 8);
Equal(true, AWPathRequestReuseRules.CanReuse(key, key, ageTicks: 2,
    maximumAgeTicks: 8), "equivalent live request is reused");
Equal(false, AWPathRequestReuseRules.CanReuse(key, changedTerrain, 2, 8),
    "terrain changes invalidate reuse");
```

- [ ] **Step 2: Run tests and observe failure**

Expected: missing `AWPathReuseKey` and `AWPathRequestReuseRules`.

- [ ] **Step 3: Add the immutable reuse key and rules**

Extend the existing request-key model rather than replacing it. Include Actor ID, quantized start region, target tile, movement flags, water bound, and terrain revision. Use value equality and a stable hash; do not include Actor references.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
git add Code/core/pathfinding/AWPathLifecycleRules.cs Tests/AncientWarfare3.Rules.Tests/AWPathRequestReuseRulesTests.cs.txt
git commit -m "test: define equivalent actor path reuse"
```

### Task 11: Coalesce Requests In The Existing Path Work Slot

**Files:**
- Modify: `Code/core/pathfinding/AWPathFinder.cs`
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`
- Modify: `Code/core/pathfinding/AWPathDiagnostics.cs`
- Modify: `Code/patch/AW_GlobalPathfindingPatch.cs`
- Modify: `Tests/PathfindingModeSourceGuardTests.ps1`
- Modify: `Tests/ActorRuntimePerformanceSourceGuardTests.ps1`

- [ ] **Step 1: Add failing source guards**

Require `AWPathFinder` to compare the incoming reuse key with `ActorWorkSlot.RunningTask` and `PendingTask` before cancellation or allocation. Require counters for reused-running, replaced-pending, expired, and active requests. Reject any cross-Actor completed path sharing.

- [ ] **Step 2: Run guards and observe failure**

Expected: current submission replaces/cancels equivalent work without a reuse gate.

- [ ] **Step 3: Reuse running and pending requests**

When the same Actor submits an equivalent key, retain the current stream and update only its waiter/callback metadata. A different key keeps the existing latest-pending behavior. Cap one running and one pending task per Actor; preserve current work-class priority.

- [ ] **Step 4: Add bounded completed reuse**

Keep at most 2,048 immutable completed entries for at most two simulation ticks. Apply only when Actor ID, current start region, target, flags, terrain revision, and world generation still match. Death, boarding, disembarking, target cancellation, terrain dirtying, load, and reset remove the Actor entry.

- [ ] **Step 5: Preserve live movement semantics**

`AWPathMovementBridge` continues polling movement every simulation pass. Cache reuse must not alter RTS target selection, split long paths into short steps, delay fallback to original `goTo`, or let original and AW3 movement own the same Actor simultaneously.

- [ ] **Step 6: Verify and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
pwsh -NoProfile -File Tests/PathfindingModeSourceGuardTests.ps1
pwsh -NoProfile -File Tests/ActorRuntimePerformanceSourceGuardTests.ps1
git add Code/core/pathfinding Code/patch/AW_GlobalPathfindingPatch.cs Tests
git commit -m "perf: coalesce equivalent actor path requests"
```

### Task 12: Full Verification, Runtime Measurement, And Source Deployment

**Files:**
- Modify: `docs/performance/2026-08-02-actor-runtime-baseline.md`
- Deploy changed source files to: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run every rules project**

```powershell
Get-ChildItem Tests -Filter *.csproj -Recurse | ForEach-Object {
    dotnet run --project $_.FullName -c Release
    if ($LASTEXITCODE -ne 0) { throw "Failed: $($_.FullName)" }
}
```

Expected: every test project exits zero.

- [ ] **Step 2: Run every PowerShell source guard**

```powershell
Get-ChildItem Tests -Filter *.ps1 | ForEach-Object {
    & pwsh -NoProfile -File $_.FullName
    if ($LASTEXITCODE -ne 0) { throw "Failed: $($_.Name)" }
}
```

Expected: every guard reports PASS and exits zero.

- [ ] **Step 3: Compile both configurations**

```powershell
dotnet build AncientWarfare3.csproj -c Debug --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
git diff --check
```

Expected: both builds report `0 Error(s)` and `git diff --check` produces no output. Building is validation only; do not deploy the DLL.

- [ ] **Step 4: Run the 20,000-Actor baseline comparison**

Use the same save and camera position. Run diagnostics off at 1x and 2x for 30 minutes each, then run one bounded diagnostic capture. Record P50/P95/P99/max for Actor, `parallel_checks`, Actor AI, death check, death stages, presentation capture, path submissions, active paths, queue depth, and managed heap.

Pass thresholds:

- Actor P95 improves at least 35 percent;
- Actor P99 is below 8 ms;
- `parallel_checks` P95 is below 4 ms;
- batch-death main-thread frames remain below 16 ms;
- death work normally drains within two seconds;
- active paths and managed heap do not trend upward over 30 minutes.

- [ ] **Step 5: Run the RTS regression matrix**

Use ten cities and twenty dispersed armies. Verify 10-versus-10 assembly, marching, combat, replenishment, immediate re-engagement, sequential city occupation, five-tile swimming, ship transport, pause/resume, save/load, reset, and replica gates. Any new idle army, mission loss, leader churn, failed reinforcement, or failed transport is a blocking failure.

- [ ] **Step 6: Repeat failures before changing code**

For every failed gate, preserve the log, save ID, configuration, exact frame/time, and minimal reproduction. Add a failing rule or source guard, implement the smallest correction, and rerun Tasks 12.1 through 12.5. Do not waive a functional failure for a performance improvement.

- [ ] **Step 7: Deploy source files only**

Stop WorldBox. Copy only changed tracked source/config/localization files required by this plan into the installed `Mods/AncientWarfare3.0` tree. Exclude `bin`, `obj`, `Tests`, `docs`, logs, temporary files, and every DLL. Compare each deployed file with `Get-FileHash -Algorithm SHA256`.

- [ ] **Step 8: Commit verification evidence**

Append exact measurements, game scenarios, pass/fail results, and deployed source hashes to the performance document.

```powershell
git add -f docs/performance/2026-08-02-actor-runtime-baseline.md
git commit -m "docs: record actor runtime performance verification"
```

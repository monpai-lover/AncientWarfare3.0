# AW3 Cultiway Large Scheduler Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace AW3's current opt-in large scheduler with a semantic mirror of the latest `Cultiway-Reborn-perf` large scheduler, including background actor/building execution and immutable presentation snapshots, while preserving AW3 authority, multiplayer, native fallback, and worker ownership.

**Architecture:** Keep Cultiway's current stage graph, burst logic, simulation clock, batch boundaries, and snapshot pipeline structurally recognizable under AW3-prefixed types. Connect AW3-only behavior through narrow adapters at admission, worker allocation, authority-cycle, lifecycle, and presentation fallback boundaries. The scheduler switch remains binary: enabled owns the complete large scheduler and snapshot presentation; disabled restores native WorldBox execution.

**Tech Stack:** C# 11/net48 source, Harmony patches, WorldBox publicized API, NeoModLoader configuration, PowerShell static source guards, Git worktrees. Do not compile or deploy DLLs.

---

### Task 1: Add authoritative simulation time and per-domain frame budgeting

**Files:**
- Create: `Tests/CultiwayLargeSchedulerSourceGuardCommon.ps1`
- Create: `Tests/CultiwayLargeSchedulerClockGovernorSourceGuard.ps1`
- Create: `Code/core/performance/AWSimulationTime.cs`
- Modify: `Code/core/performance/AWFramePriorityGovernor.cs`
- Modify: `Code/core/performance/AWPerformanceSettings.cs`
- Modify: `Code/core/performance/AWSimulationStepContext.cs`
- Modify: `default_config.json`

- [ ] **Step 1: Write the failing clock/governor source guard**

Create the shared PowerShell guard helpers:

```powershell
$ErrorActionPreference = 'Stop'
$script:GuardRoot = Split-Path -Parent $PSScriptRoot
$script:GuardFailures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$path) {
    $full = Join-Path $script:GuardRoot $path
    if (-not [IO.File]::Exists($full)) {
        $script:GuardFailures.Add("missing source file: $path")
        return ''
    }
    [IO.File]::ReadAllText($full)
}

function Require([string]$name, [string]$text, [string]$needle) {
    if (-not $text.Contains($needle)) {
        $script:GuardFailures.Add("${name}: missing '$needle'")
    }
}

function Require-Count([string]$name, [string]$text,
    [string]$needle, [int]$expected) {
    $count = ([regex]::Matches($text, [regex]::Escape($needle))).Count
    if ($count -ne $expected) {
        $script:GuardFailures.Add(
            "${name}: expected $expected occurrences of '$needle', found $count")
    }
}

function Require-Before([string]$name, [string]$text,
    [string]$first, [string]$second) {
    $firstIndex = $text.IndexOf($first, [StringComparison]::Ordinal)
    $secondIndex = $text.IndexOf($second, [StringComparison]::Ordinal)
    if ($firstIndex -lt 0 -or $secondIndex -lt 0 -or
        $firstIndex -ge $secondIndex) {
        $script:GuardFailures.Add(
            "${name}: '$first' must occur before '$second'")
    }
}

function Forbid([string]$name, [string]$text, [string]$needle) {
    if ($text.Contains($needle)) {
        $script:GuardFailures.Add("${name}: forbidden '$needle'")
    }
}

function Complete-Guard([string]$name, [string]$successMessage) {
    if ($script:GuardFailures.Count -gt 0) {
        throw "${name} failures:`n - " +
            ($script:GuardFailures -join "`n - ")
    }
    $successMessage
}
```

Create the clock/governor guard that imports the helper and checks the four production files:

```powershell
. (Join-Path $PSScriptRoot 'CultiwayLargeSchedulerSourceGuardCommon.ps1')

$time = Read-Source 'Code/core/performance/AWSimulationTime.cs'
$governor = Read-Source 'Code/core/performance/AWFramePriorityGovernor.cs'
$settings = Read-Source 'Code/core/performance/AWPerformanceSettings.cs'
$context = Read-Source 'Code/core/performance/AWSimulationStepContext.cs'
$config = Read-Source 'default_config.json'

@(
    @('time bind', $time, 'BindWorld(MapBox pWorld)'),
    @('time begin', $time, 'BeginTick(MapBox pWorld, float pDeltaTime)'),
    @('time complete', $time, 'CompleteTick(MapBox pWorld)'),
    @('time cancel', $time, 'CancelTick()'),
    @('time unbind', $time, 'UnbindWorld()'),
    @('domain enum', $governor, 'AWSimulationDomain'),
    @('remaining budget', $governor,
        'GetRemainingSimulationBudgetMilliseconds()'),
    @('domain starvation method', $governor,
        'CanUseStarvationSlice('),
    @('domain starvation parameter', $governor,
        'AWSimulationDomain pDomain'),
    @('every-frame starvation', $settings,
        'public const int StarvationFrameInterval = 1;'),
    @('background join budget', $settings,
        'public const float BackgroundJoinMilliseconds = 0.2f;'),
    @('vanilla batch size', $settings,
        'public const int SimulationBatchSize = 256;'),
    @('eight millisecond code default', $settings,
        'MaxSimulationMillisecondsPerFrame { get; private set; } = 8f;'),
    @('presentation smoothing code default', $settings,
        'EnablePresentationSmoothing { get; private set; } = true;'),
    @('thread-local step depth', $context, '[ThreadStatic]'),
    @('active step marker', $context,
        'internal static bool IsActive => _depth > 0;')
) | ForEach-Object { Require $_[0] $_[1] $_[2] }

$configObject = $config | ConvertFrom-Json
$budgetSetting = $configObject.AWPerformanceSettings |
    Where-Object Id -eq 'AW3_MAX_SIMULATION_MS_PER_FRAME'
$smoothingSetting = $configObject.AWPerformanceSettings |
    Where-Object Id -eq 'AW3_ENABLE_PRESENTATION_SMOOTHING'
if ($null -eq $budgetSetting -or
    [double]$budgetSetting.FloatVal -ne 8d) {
    $script:GuardFailures.Add(
        'default config simulation budget must be 8ms')
}
if ($null -eq $smoothingSetting -or
    -not [bool]$smoothingSetting.BoolVal) {
    $script:GuardFailures.Add(
        'default config presentation smoothing must be enabled')
}

Complete-Guard 'clock/governor guard' `
    'Cultiway large scheduler clock/governor guard passed.'
```

- [ ] **Step 2: Run the guard and verify RED**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/CultiwayLargeSchedulerClockGovernorSourceGuard.ps1
```

Expected: FAIL because `AWSimulationTime.cs`, domain accounting, remaining-budget API, and updated constants do not exist.

- [ ] **Step 3: Port the clock and governor behavior**

Port `Cultiway.Core.Performance.SimulationTime` to `AWSimulationTime` with AW3 parameter naming. Preserve its world identity checks, pending tick time, commit, cancel, synchronization, and generation counter. Update the governor around this domain contract:

```csharp
internal enum AWSimulationDomain
{
    Vanilla,
    Aw3Authority
}

internal static double GetRemainingSimulationBudgetMilliseconds()
{
    BeginFrame();
    double targetMilliseconds =
        1000d / AWPerformanceSettings.TargetRenderFps;
    double deadlineRemaining =
        targetMilliseconds -
        AWPerformanceSettings.RenderReserveMilliseconds -
        ElapsedMilliseconds(_frameStartedAt);
    double budgetRemaining =
        _frameBudgetMilliseconds - _simulationCpuMilliseconds;
    return Math.Max(0d,
        Math.Min(deadlineRemaining, budgetRemaining));
}

private static bool CanUseStarvationSlice(AWSimulationDomain pDomain)
{
    int lastRun = pDomain == AWSimulationDomain.Vanilla
        ? _lastVanillaRunFrame
        : _lastAw3RunFrame;
    if (lastRun != _frameId &&
        _frameId - lastRun <
            AWPerformanceSettings.StarvationFrameInterval)
        return false;

    double domainSpent = pDomain == AWSimulationDomain.Vanilla
        ? _vanillaCpuMilliseconds
        : _aw3CpuMilliseconds;
    double starvationBudget = Math.Min(
        AWPerformanceSettings.StarvationSliceMilliseconds,
        AWPerformanceSettings.MaxSimulationMillisecondsPerFrame);
    return domainSpent < starvationBudget &&
           _simulationCpuMilliseconds <
           _frameBudgetMilliseconds + starvationBudget;
}
```

Set the default maximum simulation budget to `8f`, presentation smoothing to enabled, `StarvationSliceMilliseconds` to `2f`, `StarvationFrameInterval` to `1`, `BackgroundJoinMilliseconds` to `0.2f`, and `SimulationBatchSize` to `256`. Update matching values in `default_config.json`. Keep AW3 target FPS, user-configured overrides, and worker allocation as the authorities. Update `AWSimulationStepContext` with Cultiway's thread-local nesting marker and restore native values in `finally`.

- [ ] **Step 4: Run the guard and verify GREEN**

Expected output:

```text
Cultiway large scheduler clock/governor guard passed.
```

- [ ] **Step 5: Commit the clock/governor slice**

```powershell
git add Tests/CultiwayLargeSchedulerSourceGuardCommon.ps1 Tests/CultiwayLargeSchedulerClockGovernorSourceGuard.ps1 Code/core/performance/AWSimulationTime.cs Code/core/performance/AWFramePriorityGovernor.cs Code/core/performance/AWPerformanceSettings.cs Code/core/performance/AWSimulationStepContext.cs default_config.json
git commit -m "perf: update scheduler clock and frame governor"
```

### Task 2: Upgrade actor and building cooperative batch execution

**Files:**
- Create: `Tests/CultiwayLargeSchedulerBatchSourceGuard.ps1`
- Create: `Code/core/performance/AWSimulationCoordinatorThread.cs`
- Modify: `Code/core/performance/AWCooperativeBatchRunner.cs`
- Modify: `Code/core/performance/AWSchedulerResourceOwnership.cs`

- [ ] **Step 1: Write the failing batch source guard**

Require these exact symbols and forbid an independent Cultiway pool:

```powershell
. (Join-Path $PSScriptRoot 'CultiwayLargeSchedulerSourceGuardCommon.ps1')

$batch = Read-Source 'Code/core/performance/AWCooperativeBatchRunner.cs'
$coordinator = Read-Source 'Code/core/performance/AWSimulationCoordinatorThread.cs'
$ownership = Read-Source 'Code/core/performance/AWSchedulerResourceOwnership.cs'

Require 'waiting dispatch' $batch 'WaitingForPresentationDispatch'
Require 'begin presentation work' $batch 'BeginParallelPresentationWork()'
Require 'complete presentation work' $batch 'CompleteParallelPresentationWork()'
Require 'synchronous deferred work' $batch 'RunDeferredParallelWorkSynchronously()'
Require 'wait and discard on abort' $batch 'WaitAndDiscard('
Require 'coordinator begin' $coordinator 'WorkTicket Begin('
Require 'coordinator wait' $coordinator 'void Wait(WorkTicket pTicket)'
Require 'coordinator complete' $coordinator 'WorkResult Complete('
Require 'single background coordinator' $coordinator 'new Thread(CoordinatorLoop)'
Require 'coordinator background flag' $coordinator 'IsBackground = true'
Require 'scheduler parallel ownership' $ownership 'schedulerParallelism'

foreach ($source in @($batch, $ownership)) {
    if ($source.Contains('new SimulationWorkerPool(') -or
        $source.Contains('new Thread(') -or
        $source.Contains('Environment.ProcessorCount - 2')) {
        $script:GuardFailures.Add(
            'scheduler created a competing worker allocation')
    }
}

Complete-Guard 'batch guard' `
    'Cultiway large scheduler batch guard passed.'
```

- [ ] **Step 2: Run the batch guard and verify RED**

Expected: FAIL because the current batch runner has no presentation-dispatch boundary or AW3 actor post/parallel adapters.

- [ ] **Step 3: Port Cultiway batch boundaries with an AW3 coordinator**

Mirror the current Cultiway `SimulationCoordinatorThread` and `CooperativeBatchRunner` presentation-dispatch behavior. Do not port Cultiway's custom actor post/pathfinding runner: AW3 already owns actor pathfinding and RTS routing, and the design excludes Cultiway gameplay-specific stages. The AW3 batch runner must expose:

```csharp
internal bool WaitingForPresentationDispatch { get; }
internal bool HasParallelPresentationWorkInFlight { get; }
internal bool BeginParallelPresentationWork();
internal bool RunDeferredParallelWorkSynchronously();
internal void CompleteParallelPresentationWork();
internal string GetNextPhaseName();
internal void Abort();
```

The single coordinator thread runs the already-existing `Parallel.For` batch stage off the render thread. Its `ParallelOptions.MaxDegreeOfParallelism` is the AW3 foreground allocation after actor pathfinding and RTS route reservations. Do not construct Cultiway's `SimulationWorkerPool` or another fixed-size pool. Keep Unity and WorldBox collection snapshotting on the main thread. Completion and post-processing return to the main thread before mutation is exposed.

- [ ] **Step 4: Run the batch guard and verify GREEN**

Expected output:

```text
Cultiway large scheduler batch guard passed.
```

- [ ] **Step 5: Commit the batch slice**

```powershell
git add Tests/CultiwayLargeSchedulerBatchSourceGuard.ps1 Code/core/performance/AWSimulationCoordinatorThread.cs Code/core/performance/AWCooperativeBatchRunner.cs Code/core/performance/AWSchedulerResourceOwnership.cs
git commit -m "perf: add scheduler actor and building completion boundaries"
```

### Task 3: Add immutable actor and world-object presentation snapshots

**Files:**
- Create: `Tests/CultiwayLargeSchedulerPresentationSourceGuard.ps1`
- Create: `Code/core/performance/AWActorPresentationSnapshot.cs`
- Create: `Code/core/performance/AWActorPresentationRenderer.cs`
- Create: `Code/core/performance/AWWorldObjectPresentationRenderer.cs`
- Create: `Code/core/performance/AWActorPresentationOverlays.cs`
- Create: `Code/core/performance/AWActorTransientPresentationFrame.cs`
- Create: `Code/core/performance/AWPresentationCommandQueue.cs`
- Create: `Code/core/performance/AWPresentationVisibility.cs`
- Create: `Code/core/performance/AWStatusPresentationAnimationClock.cs`
- Create: `Code/core/performance/AWInsideBoatActorIndex.cs`
- Create: `Code/core/performance/AWWorldTimeRateTracker.cs`
- Modify: `Code/core/performance/AWPresentationInterpolator.cs`

- [ ] **Step 1: Write the failing presentation source guard**

Require immutable publication and all read-side boundaries:

```powershell
. (Join-Path $PSScriptRoot 'CultiwayLargeSchedulerSourceGuardCommon.ps1')

$snapshot = Read-Source 'Code/core/performance/AWActorPresentationSnapshot.cs'
$actors = Read-Source 'Code/core/performance/AWActorPresentationRenderer.cs'
$worldObjects = Read-Source 'Code/core/performance/AWWorldObjectPresentationRenderer.cs'
$overlays = Read-Source 'Code/core/performance/AWActorPresentationOverlays.cs'
$transient = Read-Source 'Code/core/performance/AWActorTransientPresentationFrame.cs'
$commands = Read-Source 'Code/core/performance/AWPresentationCommandQueue.cs'
$clock = Read-Source 'Code/core/performance/AWStatusPresentationAnimationClock.cs'
$interpolator = Read-Source 'Code/core/performance/AWPresentationInterpolator.cs'

Require 'double buffered snapshot' $snapshot 'Interlocked.Exchange('
Require 'published snapshot' $snapshot 'HasPublishedSnapshot'
Require 'capture request' $snapshot 'RequestCapture()'
Require 'actor prepared snapshot' $actors 'PreparedSnapshot'
Require 'building snapshot render' $worldObjects 'TryPrepareBuildings('
Require 'overlay snapshot render' $overlays 'TryDrawStatuses('
Require 'transient snapshot render' $transient 'TryDrawDamage('
Require 'main thread command drain' $commands 'DrainMainThread()'
Require 'snapshot animation mode' $clock 'SetSnapshotMode('
Require 'paused authoritative snap' $interpolator 'IsWorldPaused()'

Complete-Guard 'presentation guard' `
    'Cultiway large scheduler presentation guard passed.'
```

- [ ] **Step 2: Run the presentation guard and verify RED**

Expected: FAIL because AW3 currently has interpolation only and no immutable actor/building snapshot pipeline.

- [ ] **Step 3: Port the Cultiway snapshot data model**

Mirror the current Cultiway presentation sample structs, handles, flags, double-buffer publication, generation validation, and capture request lifecycle under AW3-prefixed types. Snapshot capture occurs only after a complete simulation tick and includes actor identity/generation, transform, visibility, sprite/status data, buildings, stockpiles, light windows, fires, projectiles, throws, and the transient collections required by the patched draw methods.

The publication contract is:

```csharp
internal static bool HasPublishedSnapshot { get; }
internal static void RequestCapture();
internal static AWActorPresentationSnapshot AcquireLatest();
internal static void CaptureIfRequested(MapBox pMap, int pGeneration);
internal static void Reset();
```

The captured buffers become immutable after `Interlocked.Exchange`. Writers never mutate the published buffer. Readers reject a snapshot whose world seed or generation differs from `AWSimulationTime`.

- [ ] **Step 4: Port the Cultiway snapshot renderers and command queue**

Adapt actor, building, overlay, transient, status-animation, inside-boat, and command-queue code. Remove references to Cultiway-only plots, ECS systems, powers, or assets; retain all WorldBox-native draw paths present in the source snapshot. Keep AW3's paused snap and cursor/minimap fixes.

Every renderer follows this result contract:

```csharp
if (!AWPerformanceSettings.EnableFramePriorityScheduler ||
    snapshot == null || !snapshot.MatchesCurrentWorld)
    return false;

// Draw only from snapshot samples.
return true;
```

Returning `false` never authorizes an unsafe native read by itself; Task 5 establishes the required actor/building completion boundary before native fallback.

- [ ] **Step 5: Run the presentation guard and verify GREEN**

Expected output:

```text
Cultiway large scheduler presentation guard passed.
```

- [ ] **Step 6: Commit the presentation slice**

```powershell
git add Tests/CultiwayLargeSchedulerPresentationSourceGuard.ps1 Code/core/performance/AWActorPresentationSnapshot.cs Code/core/performance/AWActorPresentationRenderer.cs Code/core/performance/AWWorldObjectPresentationRenderer.cs Code/core/performance/AWActorPresentationOverlays.cs Code/core/performance/AWActorTransientPresentationFrame.cs Code/core/performance/AWPresentationCommandQueue.cs Code/core/performance/AWPresentationVisibility.cs Code/core/performance/AWStatusPresentationAnimationClock.cs Code/core/performance/AWInsideBoatActorIndex.cs Code/core/performance/AWWorldTimeRateTracker.cs Code/core/performance/AWPresentationInterpolator.cs
git commit -m "perf: add immutable scheduler presentation snapshots"
```

### Task 4: Port the latest Cultiway stage burst runner

**Files:**
- Create: `Tests/CultiwayLargeSchedulerRunnerSourceGuard.ps1`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`
- Modify: `Code/core/performance/AWCooperativeWorldMaintenanceRunner.cs`

- [ ] **Step 1: Write the failing runner source guard**

Require the current Cultiway large-runner contracts:

```powershell
. (Join-Path $PSScriptRoot 'CultiwayLargeSchedulerSourceGuardCommon.ps1')

$runner = Read-Source 'Code/core/performance/AWCooperativeSimulationRunner.cs'

@(
    @('animation stage', 'AnimationTime'),
    @('burst entry', 'ExecuteCurrentStageBurst()'),
    @('burst core', 'ExecuteVanillaStageBurstCore()'),
    @('burst limit', 'MaximumStagesPerBurst'),
    @('remaining budget',
        'GetRemainingSimulationBudgetMilliseconds()'),
    @('eager deferred work', 'TryBeginDeferredParallelWorkEagerly()'),
    @('actor overlap', 'TryBeginActorPresentationOverlap()'),
    @('building overlap', 'TryBeginBuildingPresentationOverlap()'),
    @('actor read boundary', 'EnsureActorReadBoundary(string pReason)'),
    @('building read boundary',
        'EnsureBuildingReadBoundary(string pReason)'),
    @('tick begin', 'AWSimulationTime.BeginTick('),
    @('tick complete', 'AWSimulationTime.CompleteTick('),
    @('tick cancel', 'AWSimulationTime.CancelTick()'),
    @('snapshot publish',
        'AWActorPresentationSnapshots.CaptureIfRequested('),
    @('AW authority domain',
        'AWAuthorityCycleService.ProcessCooperativeCycle(')
) | ForEach-Object { Require $_[0] $runner $_[1] }

Complete-Guard 'runner guard' `
    'Cultiway large scheduler runner guard passed.'
```

- [ ] **Step 2: Run the runner guard and verify RED**

Expected: FAIL on `AnimationTime`, stage burst, eager deferred work, read boundaries, simulation time, and snapshot publication.

- [ ] **Step 3: Mirror Cultiway's runner stage graph**

Port the current `CooperativeSimulationRunner` stage enum and switch in the same order, excluding only Cultiway ECS/root-system stages. Insert `Aw3Authority` after the last vanilla maintenance stage and before tick completion. Preserve every WorldBox manager stage already present in AW3.

Construct both vanilla batch runners with deferred presentation work enabled; building parallelism is no longer forced off:

```csharp
private readonly AWCooperativeBatchRunner<BatchActors, Actor> _actorRunner =
    new AWCooperativeBatchRunner<BatchActors, Actor>(
        "vanilla.actors",
        pAllowWorkerParallelism: true,
        pDeferParallelToPresentation: true);
private readonly AWCooperativeBatchRunner<BatchBuildings, Building>
    _buildingRunner =
        new AWCooperativeBatchRunner<BatchBuildings, Building>(
            "vanilla.buildings",
            pAllowWorkerParallelism: true,
            pDeferParallelToPresentation: true);
```

Use this burst-stop model exactly:

```csharp
private enum StageBurstStopReason
{
    None,
    Completed,
    AsyncBoundary,
    DomainBoundary,
    Deadline,
    StageLimit
}

private const int MaximumStagesPerBurst = 256;
private const double MinimumBurstMilliseconds = 0.25d;
private const double MaximumBurstMilliseconds = 2d;
private const double TargetFrameBurstRatio = 0.01d;
```

The burst loops while the current domain is unchanged, no actor/building dispatch boundary is pending, the stage count is below the limit, and the deadline has not elapsed. The deadline derives from the governor's remaining budget and Cultiway's target-frame burst ratio.

- [ ] **Step 4: Wire eager deferred work and snapshot completion**

Before advancing a waiting actor/building batch, choose synchronous execution only when the remaining frame budget meets `BackgroundJoinMilliseconds`; otherwise launch presentation overlap. At every actor/building read boundary, force completion and record wall/wait diagnostics. At tick completion, run AW3 authority once, commit `AWSimulationTime`, capture the requested snapshot, and clear the active cycle.

Replica transition, abort, fault, clear, and load call both batch abort paths, `AWSimulationTime.CancelTick()`, snapshot reset, and command-queue clear.

- [ ] **Step 5: Run the runner guard and verify GREEN**

Expected output:

```text
Cultiway large scheduler runner guard passed.
```

- [ ] **Step 6: Commit the runner slice**

```powershell
git add Tests/CultiwayLargeSchedulerRunnerSourceGuard.ps1 Code/core/performance/AWCooperativeSimulationRunner.cs Code/core/performance/AWCooperativeWorldMaintenanceRunner.cs
git commit -m "perf: port Cultiway large scheduler stage bursts"
```

### Task 5: Replace the presentation and lifecycle patch surface

**Files:**
- Create: `Tests/CultiwayLargeSchedulerLifecycleSourceGuard.ps1`
- Modify: `Code/patch/AW_FramePrioritySchedulerPatch.cs`
- Modify: `Code/core/performance/AWCursorPresentationLifecycle.cs`
- Modify: `Code/patch/AW_CursorPresentationLifecyclePatch.cs`

- [ ] **Step 1: Write the failing lifecycle source guard**

Require lifecycle and safe-fallback hooks:

```powershell
. (Join-Path $PSScriptRoot 'CultiwayLargeSchedulerSourceGuardCommon.ps1')

$patch = Read-Source 'Code/patch/AW_FramePrioritySchedulerPatch.cs'

@(
    @('frame actor boundary', 'EnsureActorReadBoundary("mapbox.frame_begin")'),
    @('frame building boundary',
        'EnsureBuildingReadBoundary("mapbox.frame_begin")'),
    @('command drain', 'AWPresentationCommandQueue.DrainMainThread()'),
    @('capture request', 'AWActorPresentationSnapshots.RequestCapture()'),
    @('presentation finish', 'FinishPresentationFrame()'),
    @('actor prepare', 'AWActorPresentationRenderer.TryPrepare('),
    @('building prepare',
        'AWWorldObjectPresentationRenderer.TryPrepareBuildings('),
    @('save boundary', 'CompleteBeforeSave('),
    @('clear clock', 'AWSimulationTime.UnbindWorld()'),
    @('replica gate', 'AW3MultiplayerReplicaScope.IsReplicaSession'),
    @('authority native callback',
        'AWAuthorityCycleService.ProcessNativeCycle()'),
    @('flash presentation', 'pMap.flash_effects.update(0f);'),
    @('cursor reset', 'AWCursorPresentationLifecycle.Reset()')
) | ForEach-Object { Require $_[0] $patch $_[1] }

Complete-Guard 'lifecycle guard' `
    'Cultiway large scheduler lifecycle guard passed.'
```

Also require explicit safe-fallback boundaries before native draw calls for actor lights, building lights, debug rendering, transient actor effects, and any unsupported overlay.

- [ ] **Step 2: Run the lifecycle guard and verify RED**

Expected: FAIL because the current patch has no snapshot render surface or actor/building read boundaries.

- [ ] **Step 3: Port Cultiway's frame and lifecycle hooks**

Mirror the current `PatchFramePriorityScheduler` behavior for frame begin/end, visible actor/building preparation, stockpiles, lights, fires, projectiles, throws, actor overlays, transient effects, status animations, position/rotation, autosave deferral, save completion, clear, and world creation. Rename all services to AW3-prefixed equivalents.

Preserve these AW3-specific branches:

```csharp
if (AW3MultiplayerReplicaScope.IsReplicaSession)
{
    AWCooperativeSimulationRunner.Instance.AbortForReplica();
    ResetPresentationState();
    return true;
}

if (!AWPerformanceSettings.EnableFramePriorityScheduler)
{
    ResetPresentationState();
    return true; // native WorldBox path
}
```

Keep the existing `flash_effects.update(0f)`, `flash_effects.draw(0f)`, paused interpolation snap, minimap visibility, and cursor lifecycle behavior. For every snapshot renderer returning `false`, call the correct `EnsureActorReadBoundary` or `EnsureBuildingReadBoundary` before permitting the original method.

- [ ] **Step 4: Verify save/load/clear cleanup order**

The patch cleanup helper must run in this order: complete or abort actor/building work, clear presentation commands, reset actor/world renderers and overlays, reset interpolation/cursor state, cancel or unbind simulation time, reset authority state, then reset governor/fault state. Original exceptions remain primary if cleanup also throws.

- [ ] **Step 5: Run the lifecycle guard and verify GREEN**

Expected output:

```text
Cultiway large scheduler lifecycle guard passed.
```

- [ ] **Step 6: Commit the patch slice**

```powershell
git add Tests/CultiwayLargeSchedulerLifecycleSourceGuard.ps1 Code/patch/AW_FramePrioritySchedulerPatch.cs Code/core/performance/AWCursorPresentationLifecycle.cs Code/patch/AW_CursorPresentationLifecyclePatch.cs
git commit -m "perf: wire scheduler snapshots and lifecycle boundaries"
```

### Task 6: Prove native fallback, replica freeze, authority integrity, and worker ownership

**Files:**
- Create: `Tests/CultiwayLargeSchedulerAw3IntegrationSourceGuard.ps1`
- Modify: `Code/core/performance/AWFrameSchedulerRules.cs`
- Modify: `Code/core/performance/AWPerformanceSettings.cs`
- Modify: `Code/core/performance/AWSchedulerResourceOwnership.cs`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`
- Modify: `Code/patch/AW_FramePrioritySchedulerPatch.cs`

- [ ] **Step 1: Write the failing AW3 integration guard**

The guard must count and order these contracts rather than only search for names:

```powershell
. (Join-Path $PSScriptRoot 'CultiwayLargeSchedulerSourceGuardCommon.ps1')

$runner = Read-Source 'Code/core/performance/AWCooperativeSimulationRunner.cs'
$patch = Read-Source 'Code/patch/AW_FramePrioritySchedulerPatch.cs'
$ownership = Read-Source 'Code/core/performance/AWSchedulerResourceOwnership.cs'
$schedulerSources = @(
    $runner,
    $patch,
    $ownership,
    (Read-Source 'Code/core/performance/AWCooperativeBatchRunner.cs'),
    (Read-Source 'Code/core/performance/AWSimulationCoordinatorThread.cs'),
    (Read-Source 'Code/core/performance/AWActorPresentationSnapshot.cs')
) -join "`n"

Require-Count 'cooperative authority exactly once' $runner `
    'AWAuthorityCycleService.ProcessCooperativeCycle(' 1
Require-Count 'native authority exactly once' $patch `
    'AWAuthorityCycleService.ProcessNativeCycle()' 1
Require-Before 'replica checked before admission' $runner `
    'AW3MultiplayerReplicaScope.IsReplicaSession' 'TryAdmitCycle('
Require-Before 'native mode checked before takeover' $patch `
    'AWPerformanceSettings.EnableFramePriorityScheduler' 'RunFrame('
Require 'shared actor path workers' $ownership 'ActorPathWorkers'
Require 'shared RTS route workers' $ownership 'ArmyRouteWorkers'
Require 'foreground workers' $ownership 'ForegroundParallelism'
Forbid 'no Task.Run' $schedulerSources 'Task.Run('
Forbid 'no Cultiway namespace' $schedulerSources 'Cultiway.'
Require-Count 'one owned coordinator thread' $schedulerSources `
    'new Thread(CoordinatorLoop)' 1

Complete-Guard 'AW3 integration guard' `
    'Cultiway large scheduler AW3 integration guard passed.'
```

`Require-Before`, `Require-Count`, and `Forbid` report all failures before throwing. The scheduler source set includes every changed file under `Code/core/performance` plus `AW_FramePrioritySchedulerPatch.cs`.

- [ ] **Step 2: Run the integration guard and verify RED**

Expected: FAIL until ordering, count, native reset, and shared-worker assertions all match the completed implementation.

- [ ] **Step 3: Correct the AW3 integration boundaries**

Ensure mode resolution remains binary and environment overrides still resolve to native or large. The scheduler patch returns control to the original method before snapshot preparation in native mode. Replica checks occur before admission and before continuing an active local cycle. AW3 authority is not placed inside a stage burst loop that can repeat it.

Route actor batch parallelism through the foreground allocation left after `ActorPathWorkers` and `ArmyRouteWorkers` reservations. Do not change the RTS/native scheduler toggle or the actor pathfinding ownership contract.

- [ ] **Step 4: Run all six scheduler guards**

Run each `Tests/CultiwayLargeScheduler*SourceGuard.ps1` directly with PowerShell. Expected: all six print their `passed` message with exit code 0.

- [ ] **Step 5: Commit the AW3 integration slice**

```powershell
git add Tests/CultiwayLargeSchedulerAw3IntegrationSourceGuard.ps1 Code/core/performance/AWFrameSchedulerRules.cs Code/core/performance/AWPerformanceSettings.cs Code/core/performance/AWSchedulerResourceOwnership.cs Code/core/performance/AWCooperativeSimulationRunner.cs Code/patch/AW_FramePrioritySchedulerPatch.cs
git commit -m "perf: preserve AW3 scheduler ownership boundaries"
```

### Task 7: Perform the source-only completion audit

**Files:**
- Modify only files that fail the audit.

- [ ] **Step 1: Compare required symbols with the live Cultiway snapshot**

Use `Select-String`/`rg` to compare stage enum values, stage order, burst constants, governor APIs, batch boundary APIs, simulation-time lifecycle, patch hook targets, and presentation renderer entry points between `Cultiway-Reborn-perf` and the AW3 worktree. Record every intentional exclusion as Cultiway-only ECS/gameplay behavior; unexplained omissions are failures.

- [ ] **Step 2: Run every scheduler source guard**

```powershell
Get-ChildItem Tests -Filter 'CultiwayLargeScheduler*SourceGuard.ps1' |
    Sort-Object Name |
    ForEach-Object {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $_.FullName
        if ($LASTEXITCODE -ne 0) {
            throw "scheduler guard failed: $($_.Name)"
        }
    }
```

Expected: clock/governor, batch, presentation, runner, lifecycle, and AW3 integration guards all pass.

- [ ] **Step 3: Check formatting and scope**

```powershell
git diff --check master...HEAD
git status --short
git diff --stat master...HEAD
```

Expected: no whitespace errors; only scheduler source, scheduler guards, and plan/spec files are changed.

- [ ] **Step 4: Review prohibited actions**

Confirm shell history and repository output contain no `dotnet build`, `dotnet run`, generated DLL, copied DLL, or `bin/obj` change. Confirm no source file from the user's dirty master worktree was restored or deleted.

- [ ] **Step 5: Commit audit corrections**

If the audit required corrections, stage only the corrected scheduler files and commit:

```powershell
git commit -m "fix: complete Cultiway scheduler source parity"
```

If no corrections were needed, do not create an empty commit.

### Task 8: Reconcile the feature branch into dirty master and deploy sources

**Files:**
- Merge all verified scheduler source and guard files.
- Reconcile: `Code/patch/AW_FramePrioritySchedulerPatch.cs`
- Reconcile: `Code/core/performance/AWPresentationInterpolator.cs`
- Deploy only changed `Code/`, configuration, and localization files.

- [ ] **Step 1: Capture dirty-master overlap before merge**

In the main worktree, save the current diffs for the two overlapping files to review-only patch files outside the repository. Do not stash, reset, or checkout user changes.

- [ ] **Step 2: Merge the feature branch without auto-committing unrelated index state**

Use a path-aware merge or cherry-pick sequence that does not include the main worktree's already-staged test deletions. Resolve the two overlaps by preserving the new snapshot pipeline plus the user's flash refresh and paused interpolation behavior.

- [ ] **Step 3: Re-run all scheduler guards in master**

Run only `CultiwayLargeScheduler*SourceGuard.ps1`; do not run the aggregate `SourceGuardTests.ps1` because it invokes `dotnet run`. Then run `git diff --check` scoped to the merged scheduler files.

- [ ] **Step 4: Deploy source files only**

Resolve the configured game AW3 source directory from the repository's existing deployment configuration or script. Copy only the verified changed source/config/localization files. Do not copy tests, docs, DLLs, `bin`, or `obj`.

- [ ] **Step 5: Verify deployment hashes**

For every deployed file, compare `Get-FileHash -Algorithm SHA256` between the repository and game installation. Any mismatch is a deployment failure. Report the exact deployed paths and explicitly state that no DLL was compiled or copied.

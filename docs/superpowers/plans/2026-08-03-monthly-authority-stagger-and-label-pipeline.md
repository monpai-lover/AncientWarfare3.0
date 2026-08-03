# Monthly Authority Stagger and Label Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Spread monthly kingdom settlement across authority cycles and allow two bounded hierarchical-label geometry workers so month rollover and initial label preparation no longer create avoidable stalls.

**Architecture:** Add a pure generic FIFO that snapshots `(monthKey, item)` work and drains a small batch without synchronous catch-up. Migrate ruler-household, kingdom-policy, and preparation-recruitment monthly loops to independent queues. Keep live WorldBox capture on the main thread while refactoring the label runtime from one serial build job to one bounded collector plus at most two immutable geometry workers.

**Tech Stack:** C#, Harmony, SQLite, Unity `TextMesh`, .NET `Task`, console rules tests, NUnit, PowerShell source guards.

---

### Task 1: Pure Monthly Authority Work Queue

**Files:**
- Create: `Code/core/performance/MonthlyAuthorityWorkQueue.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/MonthlyAuthorityWorkQueueTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing queue behavior tests**

Add `MonthlyAuthorityWorkQueueTests.Run()` with real `int` items. Require one
month to enqueue each item once, a repeated month to enqueue nothing, a drain
of two to leave later items pending, a second month to append behind the first,
`ResetScheduleGate()` to permit an explicit same-month reschedule, and
`Clear()` to empty work and permit the same month to be scheduled again.

```csharp
var queue = new MonthlyAuthorityWorkQueue<int>();
True(queue.ScheduleMonth(1201, new[] { 1, 2, 3 }),
    "first observation schedules the month");
Equal(false, queue.ScheduleMonth(1201, new[] { 4 }),
    "same month is not duplicated");
var processed = new List<string>();
Equal(2, queue.Drain(2, (month, value) =>
    processed.Add(month + ":" + value)),
    "drain respects the item budget");
True(queue.ScheduleMonth(1202, new[] { 4 }),
    "new month appends while old work remains");
queue.Drain(8, (month, value) =>
    processed.Add(month + ":" + value));
Equal("1201:1,1201:2,1201:3,1202:4", string.Join(",", processed),
    "FIFO order survives a month boundary");
```

- [ ] **Step 2: Run the focused rules suite and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --monthly-authority-work
```

Expected: compilation fails because `MonthlyAuthorityWorkQueue<T>` does not
exist.

- [ ] **Step 3: Implement the minimal generic queue**

Create a dependency-free helper under `AncientWarfare3.core.performance`:

```csharp
internal readonly struct MonthlyAuthorityWorkItem<T>
{
    internal readonly int MonthKey;
    internal readonly T Value;

    internal MonthlyAuthorityWorkItem(int pMonthKey, T pValue)
    {
        MonthKey = pMonthKey;
        Value = pValue;
    }
}

internal sealed class MonthlyAuthorityWorkQueue<T>
{
    private readonly Queue<MonthlyAuthorityWorkItem<T>> _pending =
        new Queue<MonthlyAuthorityWorkItem<T>>();
    private int _lastScheduledMonthKey = int.MinValue;

    internal int PendingCount => _pending.Count;

    internal bool ScheduleMonth(int pMonthKey, IEnumerable<T> pValues)
    {
        if (pMonthKey == _lastScheduledMonthKey) return false;
        _lastScheduledMonthKey = pMonthKey;
        if (pValues != null)
            foreach (T value in pValues)
                _pending.Enqueue(new MonthlyAuthorityWorkItem<T>(pMonthKey,
                    value));
        return true;
    }

    internal int Drain(int pMaximumItems, Action<int, T> pProcess)
    {
        if (pMaximumItems <= 0 || pProcess == null) return 0;
        int processed = 0;
        while (processed < pMaximumItems && _pending.Count > 0)
        {
            MonthlyAuthorityWorkItem<T> item = _pending.Dequeue();
            processed++;
            pProcess(item.MonthKey, item.Value);
        }
        return processed;
    }

    internal void Clear()
    {
        _pending.Clear();
        _lastScheduledMonthKey = int.MinValue;
    }

    internal void ResetScheduleGate()
    {
        _lastScheduledMonthKey = int.MinValue;
    }
}
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the command from Step 2. Expected: the monthly queue test reports PASS.

- [ ] **Step 5: Commit only the queue and focused tests**

```powershell
git add Code/core/performance/MonthlyAuthorityWorkQueue.cs `
  Tests/AncientWarfare3.Rules.Tests/MonthlyAuthorityWorkQueueTests.cs.txt `
  Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj `
  Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "perf: add bounded monthly authority work queue"
```

### Task 2: Migrate the Three Monthly Kingdom Loops

**Files:**
- Modify: `Code/core/lineage/RulerHouseholdPregnancyService.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Modify: `Code/core/policy/KingdomDecisionMonthlyService.cs`
- Create: `Tests/AW3MonthlyAuthorityStaggerSourceGuard.ps1`

- [ ] **Step 1: Write a failing source guard**

Require each service to own `MonthlyAuthorityWorkQueue<Kingdom>`, clear it from
`Reset()`, call `ScheduleMonth(monthKey, World.world.kingdoms)`, and drain only
its explicit batch. Reject the old month-change bodies containing a direct
`foreach (Kingdom kingdom in World.world.kingdoms)` settlement loop.

```powershell
$requirements = @{
  'Code/core/lineage/RulerHouseholdPregnancyService.cs' =
    'MonthlyAuthorityWorkQueue<Kingdom>'
  'Code/core/lineage/TemporaryLevyService.cs' =
    'MonthlyAuthorityWorkQueue<Kingdom>'
  'Code/core/policy/KingdomDecisionMonthlyService.cs' =
    'MonthlyAuthorityWorkQueue<Kingdom>'
}
```

- [ ] **Step 2: Run the guard and verify RED**

```powershell
& Tests\AW3MonthlyAuthorityStaggerSourceGuard.ps1
```

Expected: all three services fail because they still settle every kingdom in
the month-change frame.

- [ ] **Step 3: Migrate ruler-household processing**

Use a one-item drain budget because each item performs a synchronous SQLite
query. Keep the existing per-kingdom `try/catch`:

```csharp
private const int KingdomsPerAuthorityCycle = 1;
private static readonly MonthlyAuthorityWorkQueue<Kingdom> MonthlyWork = new();

public static void ProcessAuthorityCycle()
{
    if (!IsAuthority() || !Ready || World.world?.kingdoms == null) return;
    int monthKey = RulerHouseholdPregnancyRules.ToMonthKey(
        Date.getCurrentYear(), Date.getCurrentMonth());
    MonthlyWork.ScheduleMonth(monthKey, World.world.kingdoms);
    MonthlyWork.Drain(KingdomsPerAuthorityCycle, (queuedMonth, kingdom) =>
    {
        try { ProcessKingdomMonth(kingdom, queuedMonth); }
        catch { }
    });
}
```

`Reset()` calls `MonthlyWork.Clear()` and no longer needs a separate last-month
field.

- [ ] **Step 4: Migrate kingdom-policy processing**

Use one kingdom per authority cycle so one frame performs at most one policy
snapshot upsert. Preserve `OnKingdomDecisionMonth(kingdom, queuedMonth)` and its
stored per-kingdom idempotency key.

- [ ] **Step 5: Migrate preparation-recruitment processing**

Use two kingdoms per authority cycle because most non-emergency kingdoms take
the cheap cancellation/return path. Preserve the queued month key when calling
the existing private `ProcessPreparationMonth(kingdom, queuedMonth)`. Keep the
existing `LastPreparationMonthKey` invalidation used by emergency transitions;
when that gate is reset, call a queue schedule-gate reset method before
scheduling the current month again. Do not synchronously drain the queue from
`OnEmergencyChanged`.

- [ ] **Step 6: Verify the source guard and queue suite GREEN**

```powershell
& Tests\AW3MonthlyAuthorityStaggerSourceGuard.ps1
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --monthly-authority-work
```

- [ ] **Step 7: Commit the service migration**

```powershell
git add Code/core/lineage/RulerHouseholdPregnancyService.cs `
  Code/core/lineage/TemporaryLevyService.cs `
  Code/core/policy/KingdomDecisionMonthlyService.cs `
  Tests/AW3MonthlyAuthorityStaggerSourceGuard.ps1
git commit -m "perf: stagger monthly kingdom settlement"
```

### Task 3: Add Monthly Runtime Observability

**Files:**
- Modify: `Code/core/policy/RecentFeatureBenchmarkRules.cs`
- Modify: `Code/core/lineage/RulerHouseholdPregnancyService.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Modify: `Code/core/policy/KingdomDecisionMonthlyService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/AW3MonthlyAuthorityStaggerSourceGuard.ps1`

- [ ] **Step 1: Make benchmark mapping and source assertions RED**

Require stable recent IDs:

```text
aw3_month_ruler_household
aw3_month_kingdom_policy
aw3_month_preparation_levy
```

Require each service to expose `PendingMonthlyWorkForDiagnostics` and measure
only an actually drained kingdom item, so recent call count equals processed
kingdom work rather than fast per-frame month checks.

- [ ] **Step 2: Run RED verification**

```powershell
& Tests\AW3MonthlyAuthorityStaggerSourceGuard.ps1
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --recent-benchmark-rules
```

- [ ] **Step 3: Add benchmark indices and per-item scopes**

Append three indices and IDs after the current final indexed entry. Wrap each
service's item callback with `RecentFeatureBenchmark.Begin()` and
`RecentFeatureBenchmark.End(index, token)` in `finally`. Do not wrap the empty
queue check. Add:

```csharp
internal static int PendingMonthlyWorkForDiagnostics =>
    MonthlyWork.PendingCount;
```

- [ ] **Step 4: Run GREEN verification**

Run both Step 2 commands and confirm the three mappings and source assertions
pass.

- [ ] **Step 5: Commit observability**

```powershell
git add Code/core/policy/RecentFeatureBenchmarkRules.cs `
  Code/core/lineage/RulerHouseholdPregnancyService.cs `
  Code/core/lineage/TemporaryLevyService.cs `
  Code/core/policy/KingdomDecisionMonthlyService.cs `
  Tests/AncientWarfare3.Rules.Tests/Program.cs.txt `
  Tests/AW3MonthlyAuthorityStaggerSourceGuard.ps1
git commit -m "perf: expose monthly authority drain costs"
```

### Task 4: Two-Slot Hierarchical Label Geometry Pipeline

**Files:**
- Create: `Code/core/policy/HierarchicalVassalLabelPipelineRules.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeSchedulingRules.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapLabelRuntime.cs`
- Modify: `Tests/HierarchicalVassalMapModeRules.Tests/HierarchicalVassalMapModeRulesTests.csproj`
- Create: `Tests/HierarchicalVassalMapModeRules.Tests/HierarchicalVassalLabelPipelineRulesTests.cs`
- Modify: `Tests/HierarchicalVassalMapModeRules.Tests/HierarchicalVassalLabelDiscoveryJobTests.cs`

- [ ] **Step 1: Write failing pipeline-limit tests**

Add pure rules assertions that zero or one worker permits submission, two does
not, pending workers prevent batch completion, and all sources submitted plus
zero workers permits completion:

```csharp
Assert.That(HierarchicalVassalLabelPipelineRules.CanSubmit(0), Is.True);
Assert.That(HierarchicalVassalLabelPipelineRules.CanSubmit(1), Is.True);
Assert.That(HierarchicalVassalLabelPipelineRules.CanSubmit(2), Is.False);
Assert.That(HierarchicalVassalLabelPipelineRules.CanFinish(
    allSourcesSubmitted: true, collecting: false, inFlightCount: 1), Is.False);
Assert.That(HierarchicalVassalLabelPipelineRules.CanFinish(
    allSourcesSubmitted: true, collecting: false, inFlightCount: 0), Is.True);
```

Extend discovery tests with null and empty kingdom containers and assert that
`Advance(... cityBudget: 1 ...)` remains incomplete after inspecting only one
container.

- [ ] **Step 2: Run the hierarchical project and verify RED**

```powershell
dotnet test Tests\HierarchicalVassalMapModeRules.Tests\HierarchicalVassalMapModeRulesTests.csproj --no-restore --filter "HierarchicalVassalLabelPipelineRulesTests|HierarchicalVassalLabelDiscoveryJobTests"
```

Expected: pipeline rules are missing and the new budget cases expose any
unbounded empty-container traversal.

- [ ] **Step 3: Add the bounded pipeline rule**

Create a pure helper with `MaximumInFlightWorkers = 2`, `CanSubmit(count)`, and
`CanFinish(allSourcesSubmitted, collecting, inFlightCount)`. Reference the same
constant from `HierarchicalVassalMapModeSchedulingRules`.

- [ ] **Step 4: Refactor runtime state without changing cache semantics**

Keep `_currentSource` and `_currentJob` as the sole main-thread collector. Add
a private `InFlightBuild` value containing the immutable source and its build
job, plus a list capped at two.

At the start of `ProcessFrame`, poll completed in-flight workers and pass their
results through the existing generation checks, `Accept`, dirty clearing, and
`PublishOrShow` path. While a worker slot is free, advance the collector using
the existing tile budget. When its phase becomes `ComputePureGeometry`, move it
to the in-flight list, clear the collector, increment `_sourceIndex`, and allow
the next source to start with remaining budget.

Do not call `FinishCurrentBatch()` until:

```csharp
HierarchicalVassalLabelPipelineRules.CanFinish(
    _sourceIndex >= _sources.Count,
    _currentJob != null,
    InFlightBuilds.Count)
```

`CancelCurrentBatch` and `Reset` cancel and clear the collector and every
in-flight worker. Existing generation checks reject stale completions.

- [ ] **Step 5: Add runtime assertions for overlap and cancellation**

Use the existing WorldBox test stubs to start at least three sources. Verify
diagnostic in-flight count never exceeds two, a running first job does not stop
the second source from entering collection/submission, and reset leaves zero
workers and no publishable result.

- [ ] **Step 6: Run full hierarchical verification GREEN**

```powershell
dotnet test Tests\HierarchicalVassalMapModeRules.Tests\HierarchicalVassalMapModeRulesTests.csproj --no-restore
Get-ChildItem Tests -Filter '*HierarchicalVassal*.ps1' | ForEach-Object { & $_.FullName }
```

- [ ] **Step 7: Commit the bounded pipeline**

```powershell
git add Code/core/policy/HierarchicalVassalLabelPipelineRules.cs `
  Code/core/policy/HierarchicalVassalMapModeSchedulingRules.cs `
  Code/core/policy/HierarchicalVassalMapLabelRuntime.cs `
  Tests/HierarchicalVassalMapModeRules.Tests/HierarchicalVassalMapModeRulesTests.csproj `
  Tests/HierarchicalVassalMapModeRules.Tests/HierarchicalVassalLabelPipelineRulesTests.cs `
  Tests/HierarchicalVassalMapModeRules.Tests/HierarchicalVassalLabelDiscoveryJobTests.cs
git commit -m "perf: pipeline hierarchical label geometry"
```

### Task 5: Aggregate Verification and Source Deployment

**Files:**
- Verify all files changed in Tasks 1-4
- Deploy changed runtime `.cs` files to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`

- [ ] **Step 1: Run focused and aggregate tests without building the main DLL**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --monthly-authority-work
dotnet test Tests\HierarchicalVassalMapModeRules.Tests\HierarchicalVassalMapModeRulesTests.csproj --no-restore
& Tests\AW3MonthlyAuthorityStaggerSourceGuard.ps1
& Tests\AW3MapBoxRecentBenchmarkCoverageSourceGuard.ps1
Get-ChildItem Tests -Filter '*HierarchicalVassal*.ps1' | ForEach-Object { & $_.FullName }
git diff --check
```

Expected: every command exits zero; no main `AncientWarfare3.dll` is built.

- [ ] **Step 2: Deploy source files only**

Copy the changed production `.cs` files while preserving their repository
relative paths. Do not copy test files, `bin`, `obj`, or any DLL.

- [ ] **Step 3: Hash-verify every deployed source**

For each deployed file, compare workspace and installed SHA256 hashes and fail
if any differ.

- [ ] **Step 4: Inspect startup log after the user's next restart**

Confirm the native simulation scheduler loads, all hierarchical label patches
register, and there are no label worker, SQLite, monthly queue, or
`MapBox.Update` exceptions.

- [ ] **Step 5: Manual large-world acceptance**

Observe multiple month boundaries and confirm the former periodic red spike is
gone or materially reduced. Confirm policy progress, pregnancies, and wartime
preparation continue to settle gradually. Confirm country/city names appear
progressively in the background, opening the map reuses cached labels, visuals
remain unchanged, and the recent panel reports the three monthly categories.

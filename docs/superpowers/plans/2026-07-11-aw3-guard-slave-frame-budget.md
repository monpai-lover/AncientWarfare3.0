# AW3 Guard And Slave Frame-Budget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep guard and slave gameplay state immediate while moving persistence, chronicles, archival, graphics work, and 80-tile target enumeration into bounded main-thread work spread across frames.

**Architecture:** A reusable main-thread queue drains at most one expensive side effect per frame and flushes persistent work before save. A separate resumable catcher scanner stores both chunk and unit cursors and shares results by kingdom/island/origin chunk. Guard and slave services capture immutable snapshots before enqueueing work.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, WorldBox map chunks, SQLite lineage archive, existing `Bench` profiler.

---

## File Map

- Create `Verification/AW3FocusedRuleTests/AW3FocusedRuleTests.csproj`: isolated executable rule-test project.
- Create `Verification/AW3FocusedRuleTests/Program.cs`: performance decision regressions.
- Create `Code/core/lineage/DeferredRuntimeWorkRules.cs`: pure queue budget, retry, and coalescing rules.
- Create `Code/core/lineage/DeferredRuntimeWorkService.cs`: main-thread ordered/coalesced work queue.
- Create `Code/core/lineage/SlaveCaptureScanRules.cs`: pure cursor, key, cache, and budget rules.
- Create `Code/core/lineage/SlaveCaptureScanService.cs`: shared resumable target scans.
- Create `Code/core/lineage/SlaveArmyFillSideEffectRules.cs`: fill-context suppression decisions.
- Create `Code/patch/AW_DeferredRuntimeWorkPatch.cs`: frame drain hook.
- Modify `Code/patch/AW_SavePatch.cs`: pre-save flush and world-state clearing.
- Modify `Code/core/lineage/RoyalGuardService.cs`: immediate guard state plus deferred side effects.
- Modify `Code/core/lineage/SlaveService.cs`: fill batching and shared target scan integration.
- Modify `Code/patch/AW_SlaveryPatch.cs`: skip repeated naming during a fill batch.
- Modify `Code/ai/behaviours/actor/BehFindSlaveCaptureTarget.cs`: distinguish pending scans from completed misses.
- Modify `Code/core/policy/CityMaintenanceBenchmarkRules.cs`: separate queue and scan profiler stages.

### Task 1: Create the focused verification project and RED performance tests

**Files:**
- Create: `Verification/AW3FocusedRuleTests/AW3FocusedRuleTests.csproj`
- Create: `Verification/AW3FocusedRuleTests/Program.cs`

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>11</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\AncientWarfare3.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the initial failing test program**

```csharp
using System;
using AncientWarfare3.core.lineage;

namespace AW3FocusedRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            ExpectDeferredWorkRules();
            ExpectCaptureScanRules();
            ExpectFillSideEffectRules();
            Console.WriteLine("AW3 focused rule tests passed.");
            return 0;
        }

        private static void ExpectDeferredWorkRules()
        {
            if (!DeferredRuntimeWorkRules.ShouldStopDrain(1, 1, 0, 10))
                throw new Exception("The item budget must stop a drain.");
            if (!DeferredRuntimeWorkRules.ShouldStopDrain(0, 1, 11, 10))
                throw new Exception("The elapsed budget must stop a drain.");
            if (DeferredRuntimeWorkRules.ShouldStopDrain(0, 1, 9, 10))
                throw new Exception("Available budget must allow work.");
            if (!DeferredRuntimeWorkRules.ShouldRetry(1, 2) ||
                DeferredRuntimeWorkRules.ShouldRetry(2, 2))
                throw new Exception("Retry count must be bounded.");
            if (DeferredRuntimeWorkRules.CoalescingKey("guard_state", 42) != "guard_state:42")
                throw new Exception("Coalescing keys must be stable.");
        }

        private static void ExpectCaptureScanRules()
        {
            if (SlaveCaptureScanRules.ChunkRadius(80, 16) != 5 ||
                SlaveCaptureScanRules.ChunkCount(5) != 121)
                throw new Exception("An 80-tile search must cover 121 candidate chunks.");
            SlaveCaptureScanRules.OffsetForIndex(0, 5, out int x0, out int y0);
            SlaveCaptureScanRules.OffsetForIndex(120, 5, out int x1, out int y1);
            if (x0 != -5 || y0 != -5 || x1 != 5 || y1 != 5)
                throw new Exception("Chunk cursor endpoints are incorrect.");
            if (!SlaveCaptureScanRules.ShouldReuseResult(true, true, true, true, 5, 10) ||
                SlaveCaptureScanRules.ShouldReuseResult(true, false, true, true, 5, 10))
                throw new Exception("Cached targets must be fully revalidated.");
            if (!SlaveCaptureScanRules.ShouldPause(128, 128, 0, 10) ||
                !SlaveCaptureScanRules.ShouldPause(0, 128, 11, 10))
                throw new Exception("Unit and elapsed budgets must both stop scanning.");
        }

        private static void ExpectFillSideEffectRules()
        {
            if (!SlaveArmyFillSideEffectRules.ShouldDeferPerActorSideEffects(true, true) ||
                SlaveArmyFillSideEffectRules.ShouldDeferPerActorSideEffects(false, true))
                throw new Exception("Only slave promotions inside a fill batch are deferred.");
            if (!SlaveArmyFillSideEffectRules.ShouldRefreshArmyOnce(2) ||
                SlaveArmyFillSideEffectRules.ShouldRefreshArmyOnce(0))
                throw new Exception("A changed fill batch refreshes its army exactly once.");
        }
    }
}
```

- [ ] **Step 3: Run RED**

Run: `dotnet run --project Verification/AW3FocusedRuleTests/AW3FocusedRuleTests.csproj`

Expected: compilation fails because the three rule classes do not exist.

- [ ] **Step 4: Commit the failing tests**

```powershell
git add Verification/AW3FocusedRuleTests
git commit -m "test: 覆盖运行时削峰规则"
```

### Task 2: Implement pure frame-budget and scan rules

**Files:**
- Create: `Code/core/lineage/DeferredRuntimeWorkRules.cs`
- Create: `Code/core/lineage/SlaveCaptureScanRules.cs`
- Create: `Code/core/lineage/SlaveArmyFillSideEffectRules.cs`

- [ ] **Step 1: Implement queue rules**

```csharp
namespace AncientWarfare3.core.lineage
{
    public static class DeferredRuntimeWorkRules
    {
        public static bool ShouldStopDrain(int pProcessed, int pMaxItems,
            long pElapsedTicks, long pBudgetTicks)
        {
            return pProcessed >= System.Math.Max(1, pMaxItems) ||
                   pElapsedTicks >= System.Math.Max(1L, pBudgetTicks);
        }

        public static bool ShouldRetry(int pAttempts, int pMaxAttempts)
        {
            return pAttempts < System.Math.Max(1, pMaxAttempts);
        }

        public static string CoalescingKey(string pKind, long pObjectId)
        {
            return (pKind ?? "") + ":" + pObjectId;
        }
    }
}
```

- [ ] **Step 2: Implement scan rules**

```csharp
namespace AncientWarfare3.core.lineage
{
    public static class SlaveCaptureScanRules
    {
        public static int ChunkRadius(int pTileRadius, int pChunkSize)
        {
            return pTileRadius <= 0 ? 0 :
                (pTileRadius + System.Math.Max(1, pChunkSize) - 1) /
                System.Math.Max(1, pChunkSize);
        }

        public static int ChunkCount(int pRadius)
        {
            int side = System.Math.Max(0, pRadius) * 2 + 1;
            return side * side;
        }

        public static void OffsetForIndex(int pIndex, int pRadius, out int pX, out int pY)
        {
            int radius = System.Math.Max(0, pRadius);
            int side = radius * 2 + 1;
            int index = System.Math.Max(0, System.Math.Min(pIndex, side * side - 1));
            pX = index % side - radius;
            pY = index / side - radius;
        }

        public static bool ShouldReuseResult(bool pHasEntry, bool pTargetAlive,
            bool pStillHostile, bool pSameIslandAndRadius, double pNow, double pExpiresAt)
        {
            return pHasEntry && pTargetAlive && pStillHostile &&
                   pSameIslandAndRadius && pNow < pExpiresAt;
        }

        public static bool ShouldPause(int pCheckedUnits, int pUnitBudget,
            long pElapsedTicks, long pTickBudget)
        {
            return pCheckedUnits >= System.Math.Max(1, pUnitBudget) ||
                   pElapsedTicks >= System.Math.Max(1L, pTickBudget);
        }
    }
}
```

- [ ] **Step 3: Implement fill-side-effect rules**

```csharp
namespace AncientWarfare3.core.lineage
{
    public static class SlaveArmyFillSideEffectRules
    {
        public static bool ShouldDeferPerActorSideEffects(bool pInsideFill, bool pIsSlave)
        {
            return pInsideFill && pIsSlave;
        }

        public static bool ShouldRefreshArmyOnce(int pChangedActors)
        {
            return pChangedActors > 0;
        }
    }
}
```

- [ ] **Step 4: Run GREEN and build**

Run: `dotnet run --project Verification/AW3FocusedRuleTests/AW3FocusedRuleTests.csproj`

Expected: `AW3 focused rule tests passed.`

Run: `dotnet build AncientWarfare3.csproj`

Expected: build succeeds with zero errors.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/DeferredRuntimeWorkRules.cs Code/core/lineage/SlaveCaptureScanRules.cs Code/core/lineage/SlaveArmyFillSideEffectRules.cs
git commit -m "perf: 添加跨帧预算规则"
```

### Task 3: Add the main-thread deferred-work service and lifecycle hooks

**Files:**
- Create: `Code/core/lineage/DeferredRuntimeWorkService.cs`
- Create: `Code/patch/AW_DeferredRuntimeWorkPatch.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Code/core/policy/CityMaintenanceBenchmarkRules.cs`

- [ ] **Step 1: Implement the queue API**

Create `DeferredRuntimeWorkService` with these exact public operations:

```csharp
internal enum DeferredWorkClass { Persistent, Runtime }

internal static class DeferredRuntimeWorkService
{
    internal static void EnqueueCoalesced(string pKey, DeferredWorkClass pClass, Action pAction);
    internal static void EnqueueOrdered(DeferredWorkClass pClass, Action pAction);
    internal static void DrainFrame(double pMilliseconds = 1.5, int pMaxItems = 1);
    internal static void FlushPersistent();
    internal static void ClearRuntimeState();
    internal static int PendingCount { get; }
}
```

Use one `LinkedList<WorkItem>` to preserve order and a `Dictionary<string, LinkedListNode<WorkItem>>` for coalesced replacement. A `WorkItem` contains `key`, `work_class`, `action`, and `attempts`. `DrainFrame` removes before invoking, catches exceptions, and requeues only while `ShouldRetry(attempts, 2)` is true. `FlushPersistent` executes persistent items in queue order and leaves runtime-only graphics items queued. `ClearRuntimeState` clears both containers.

- [ ] **Step 2: Add the per-frame hook**

```csharp
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_DeferredRuntimeWorkPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), "Update")]
        private static void MapBoxUpdate_Postfix()
        {
            if (!Config.game_loaded || SmoothLoader.isLoading()) return;
            DeferredRuntimeWorkService.DrainFrame();
        }
    }
}
```

- [ ] **Step 3: Flush before save and clear on archive switches**

Add a Prefix for `SaveManager.saveWorldToDirectory` that calls `FlushPersistent()` before the existing Postfix copies the archive. Add `DeferredRuntimeWorkService.ClearRuntimeState()` to `ResetHistoryWindowsAfterArchiveSwitch()`. Task 6 adds the scan-service lifecycle calls after that service exists.

- [ ] **Step 4: Add profiler IDs**

Add and register: `aw3_deferred_enqueue`, `aw3_deferred_flush`, `aw3_guard_immediate`, `aw3_slave_fill_immediate`, `aw3_capture_scan_submit`, `aw3_capture_scan_step`, and `aw3_capture_cache_hit`.

- [ ] **Step 5: Build and commit**

Run: `dotnet build AncientWarfare3.csproj`

Expected: zero errors.

```powershell
git add Code/core/lineage/DeferredRuntimeWorkService.cs Code/patch/AW_DeferredRuntimeWorkPatch.cs Code/patch/AW_SavePatch.cs Code/core/policy/CityMaintenanceBenchmarkRules.cs
git commit -m "perf: 添加主线程预算队列"
```

### Task 4: Move guard persistence, chronicles, archival, and graphics out of maintenance passes

**Files:**
- Modify: `Code/core/lineage/RoyalGuardService.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Code/core/lineage/HistoryText.cs`

- [ ] **Step 1: Add immutable guard snapshots**

Inside `RoyalGuardService`, add snapshot records containing actor ID/name/color, kingdom ID/name/color, city ID/name, guard name, active, captain, noble, appointed/dismissed time, and reason. Capture them before enqueueing.

- [ ] **Step 2: Add snapshot text construction**

Add `HistoryText.Reference(string text, string color, string targetType, long targetId)` so deferred chronicles preserve actor/city links and event-time colors without retaining live Unity objects.

- [ ] **Step 3: Split immediate and deferred work**

In `RefreshGuardIdentity`, keep actor-data, trait, profession, job, roster, and army changes synchronous. Replace direct `UpsertGuardState`, `ChronicleEvents.OnRoyalGuardAppointed`, dirty graphics processing, and archival with:

```csharp
DeferredRuntimeWorkService.EnqueueCoalesced(
    DeferredRuntimeWorkRules.CoalescingKey("guard_state", snapshot.actor_id),
    DeferredWorkClass.Persistent,
    () => UpsertGuardState(snapshot));
DeferredRuntimeWorkService.EnqueueOrdered(
    DeferredWorkClass.Persistent,
    () => ChronicleEvents.OnRoyalGuardAppointed(snapshot));
```

In `DismissGuard`, keep identity/trait/job/army removal synchronous, then enqueue state, chronicle, archive-by-ID, and graphics-by-ID as four separate items. Coalesce state, archive, and graphics; do not coalesce chronicles.

- [ ] **Step 4: Verify focused tests and build**

Run the focused project and `dotnet build AncientWarfare3.csproj`; expect both to pass.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/RoyalGuardService.cs Code/core/lineage/ChronicleEvents.cs Code/core/lineage/HistoryText.cs
git commit -m "perf: 延迟禁卫军昂贵副作用"
```

### Task 5: Collapse slave-army promotion side effects

**Files:**
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/patch/AW_SlaveryPatch.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`

- [ ] **Step 1: Track promoted actors in the existing fill context**

Add a batch list cleared immediately before `_formingSlaveArmy = true`. When `OnMadeWarrior` sees a slave during that context, immediately set `SLAVE_SOLDIER` and enable the feature, append the actor snapshot once, then return before persistence, formation recording, recursive ensure, and chronicle work.

- [ ] **Step 2: Queue one actor state and chronicle per promoted actor**

After `FillSlaveArmy` finishes, enqueue coalesced slave-state upserts and ordered enlistment chronicles from snapshots. Keep the existing single `RecordSlaveArmyFormation` and `RenameArmyIfSlaveArmy` at the end of the batch.

- [ ] **Step 3: Suppress repeated Postfix naming**

Expose an internal `SlaveService.IsFillingSlaveArmy` getter. In `AW_SlaveryPatch.MakeWarrior_Postfix`, skip slave-army and fief name refresh while it is true; the end-of-batch path performs one refresh.

- [ ] **Step 4: Verify and commit**

Run focused tests and build; expect success.

```powershell
git add Code/core/lineage/SlaveService.cs Code/patch/AW_SlaveryPatch.cs Code/core/lineage/ChronicleEvents.cs
git commit -m "perf: 合并奴隶军晋升副作用"
```

### Task 6: Replace whole-radius catcher enumeration with shared resumable scans

**Files:**
- Create: `Code/core/lineage/SlaveCaptureScanService.cs`
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/ai/behaviours/actor/BehFindSlaveCaptureTarget.cs`

- [ ] **Step 1: Implement scan state**

Each state stores kingdom ID, island ID, origin tile coordinates, origin chunk coordinates, `chunk_index`, `unit_index`, best actor ID/distance, completion state, expiry, and waiting city IDs. Build the key as `kingdomId:islandId:chunkX:chunkY`.

- [ ] **Step 2: Implement bounded advancement**

`DrainFrame` uses a 1.0 ms and 128-unit budget. Resolve each chunk through `World.world.map_chunk_manager.get`; resume at `chunk.objects.units_all[unit_index]`; validate exact 80-tile distance before target eligibility; advance `unit_index` and then `chunk_index`. Publish a miss only after all 121 chunk positions are exhausted.

- [ ] **Step 3: Integrate city assignment**

Replace `HasCaptureTargetForCity` with a shared-result lookup/request. A pending scan registers the city ID. When a hit publishes, resolve waiting cities and add one catcher job if the city still belongs to the kingdom and lacks a catcher.

- [ ] **Step 4: Integrate actor AI**

Add `CaptureTargetSearchState { Pending, Hit, Miss }`. `FindSlaveCaptureTarget` returns that state through an `out Actor`. The behavior waits 0.25–0.75 seconds for `Pending`, retains the current 3–8 second wait for `Miss`, and continues immediately for `Hit`.

- [ ] **Step 5: Attach scan lifecycle hooks**

Add `SlaveCaptureScanService.DrainFrame()` immediately after `DeferredRuntimeWorkService.DrainFrame()` in `AW_DeferredRuntimeWorkPatch`. Add `SlaveCaptureScanService.Clear()` to `AW_SavePatch.ResetHistoryWindowsAfterArchiveSwitch()`.

- [ ] **Step 6: Verify and commit**

Run focused tests and build; expect success.

```powershell
git add Code/core/lineage/SlaveCaptureScanService.cs Code/core/lineage/SlaveService.cs Code/ai/behaviours/actor/BehFindSlaveCaptureTarget.cs Code/patch/AW_DeferredRuntimeWorkPatch.cs Code/patch/AW_SavePatch.cs
git commit -m "perf: 分片共享捕奴目标扫描"
```

### Task 7: Final performance verification

**Files:**
- Modify only if verification exposes a defect in the files already listed.

- [ ] **Step 1: Run focused verification**

Run: `dotnet run --project Verification/AW3FocusedRuleTests/AW3FocusedRuleTests.csproj`

Expected: `AW3 focused rule tests passed.`

- [ ] **Step 2: Build the mod**

Run: `dotnet build AncientWarfare3.csproj`

Expected: zero errors.

- [ ] **Step 3: Inspect staging boundaries**

Run: `git status --short`

Expected: user-deleted `Tests/*` remain unstaged; only intended performance files are committed.

- [ ] **Step 4: In-game profiler acceptance**

Create a Xia kingdom, form and dissolve a guard, enable slavery, fill a slave army, and run catchers in a dense war. Confirm `guard_refresh`, `guard_dismiss`, `slave_army_fill`, and `capture_scan_step` rolling averages remain below 100% of the 60 FPS effective budget and deferred records appear within one to three seconds.

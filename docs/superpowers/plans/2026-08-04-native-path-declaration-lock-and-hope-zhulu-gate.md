# Native Path, Declaration Lock, and Hope Zhulu Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove AW3's duplicate per-Actor smooth movement cost in Native mode, make issued war declarations execute from their persisted snapshot on time, and prevent new Zhulu declarations during the Hope Age.

**Architecture:** Keep AW3 path ownership independent from the frame-level movement executor: Native uses vanilla `Actor.updateMovement`, while Large keeps AW3 smooth movement. Treat the existing declaration ledger record as the immutable execution snapshot and process it directly from the kingdom's yearly callback instead of the deferred annual pipeline. Put the Hope Age restriction in a pure Zhulu declaration gate and validate it both while building choices and at the final issue boundary.

**Tech Stack:** C# 10, Harmony patches, Newtonsoft.Json save payloads, .NET 9 rule-test executable, WorldBox source-mod deployment.

---

## File Map

- Modify `Code/core/pathfinding/AWPathLifecycleRules.cs`: pure executor-selection rule.
- Modify `Code/core/pathfinding/AWPathMovementBridge.cs`: include scheduler mode when selecting custom smooth movement.
- Modify `Code/patch/AW_GlobalPathfindingPatch.cs`: keep direct and patched movement paths on the same executor rule.
- Create `Tests/AncientWarfare3.Rules.Tests/PathMovementExecutorRulesTests.cs.txt`: Native/Large executor regression tests.
- Modify `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`: compile the new tests.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`: run new tests and update old signatures.
- Modify `Code/core/lineage/DiplomaticWarDeclarationLedgerRules.cs`: pure deadline and target fallback rules.
- Modify `Code/core/lineage/DiplomaticWarDeclarationService.cs`: execute from the ledger snapshot.
- Modify `Code/patch/AW_KingdomPolicyPatch.cs`: process deadlines in the authoritative yearly callback.
- Modify `Code/core/policy/KingdomAnnualWorkService.cs`: remove duplicate deferred declaration work.
- Create `Tests/AncientWarfare3.Rules.Tests/DiplomaticWarDeclarationLockRulesTests.cs.txt`: lock and wiring tests.
- Modify `Code/core/lineage/ZhuluWarRules.cs`: pure Hope Age declaration gate.
- Modify `Code/core/lineage/ZhuluWarService.cs`: enforce ordinary eligibility gate.
- Modify `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`: Hope Age coverage.

### Task 1: Select the movement executor by scheduler mode

**Files:**
- Modify: `Code/core/pathfinding/AWPathLifecycleRules.cs`
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`
- Modify: `Code/patch/AW_GlobalPathfindingPatch.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/PathMovementExecutorRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing Native/Large executor tests**

Create the test file with:

```csharp
using AncientWarfare3.core.pathfinding;

internal static class PathMovementExecutorRulesTests
{
    public static void Run()
    {
        False(AWPathLifecycleRules.ShouldUseCustomSmoothMovement(
            largeSchedulerEnabled: false, hasCustomPathState: true,
            hasVanillaLocalPath: false, hasVanillaGlobalPath: false),
            "Native reuses vanilla frame movement for an AW3 path");
        True(AWPathLifecycleRules.ShouldUseCustomSmoothMovement(
            largeSchedulerEnabled: true, hasCustomPathState: true,
            hasVanillaLocalPath: false, hasVanillaGlobalPath: false),
            "Large retains AW3 smooth streamed movement");
        False(AWPathLifecycleRules.ShouldUseCustomSmoothMovement(
            largeSchedulerEnabled: true, hasCustomPathState: true,
            hasVanillaLocalPath: true, hasVanillaGlobalPath: false),
            "a vanilla local path wins over stale AW3 ownership");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void False(bool value, string message)
    {
        if (value) throw new InvalidOperationException(message);
    }
}
```

Add the file to the test project and call `PathMovementExecutorRulesTests.Run()` from `Program.cs.txt`. Update the existing calls in `Program.cs.txt` to pass `largeSchedulerEnabled: true`, preserving their old intent.

- [ ] **Step 2: Run the rule suite and confirm the new signature fails**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: compilation fails because the scheduler-aware signature does not exist.

- [ ] **Step 3: Implement scheduler-aware executor selection**

Change the pure rule to:

```csharp
public static bool ShouldUseCustomSmoothMovement(
    bool largeSchedulerEnabled, bool hasCustomPathState,
    bool hasVanillaLocalPath, bool hasVanillaGlobalPath)
{
    return largeSchedulerEnabled && hasCustomPathState &&
           !hasVanillaLocalPath && !hasVanillaGlobalPath;
}
```

In `AWPathMovementBridge.ShouldUseCustomSmoothMovement`, pass `AWPerformanceSettings.EnableFramePriorityScheduler` first. Keep `UpdateMovement_Prefix` and `UpdateMovementDirect` calling only this bridge method so one path cannot bypass the choice.

- [ ] **Step 4: Run focused and full checks**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
git diff --check
```

Expected: tests exit 0 and diff check is empty.

- [ ] **Step 5: Commit the movement fix**

```powershell
git add Code/core/pathfinding/AWPathLifecycleRules.cs Code/core/pathfinding/AWPathMovementBridge.cs Code/patch/AW_GlobalPathfindingPatch.cs Tests/AncientWarfare3.Rules.Tests/PathMovementExecutorRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "perf: reuse native actor movement executor"
```

### Task 2: Execute issued declarations from immutable snapshots on time

**Files:**
- Modify: `Code/core/lineage/DiplomaticWarDeclarationLedgerRules.cs`
- Modify: `Code/core/lineage/DiplomaticWarDeclarationService.cs`
- Modify: `Code/patch/AW_KingdomPolicyPatch.cs`
- Modify: `Code/core/policy/KingdomAnnualWorkService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/DiplomaticWarDeclarationLockRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing deadline and target-lock tests**

Create a rule test with:

```csharp
using AncientWarfare3.core.lineage;

internal static class DiplomaticWarDeclarationLockRulesTests
{
    public static void Run()
    {
        False(DiplomaticWarDeclarationLedgerRules.ShouldExecute(
            136, 137, 139, noticeReady: false),
            "a declaration cannot start before its earliest year");
        True(DiplomaticWarDeclarationLedgerRules.ShouldExecute(
            137, 137, 139, noticeReady: true),
            "a ready declaration starts from its earliest year");
        True(DiplomaticWarDeclarationLedgerRules.ShouldExecute(
            139, 137, 139, noticeReady: false),
            "the forced year overrides an unready notice projection");
        Equal(12L, DiplomaticWarDeclarationLedgerRules.ResolveTargetCityId(
            true, 12L, 21L, 22L), "a valid locked target remains stable");
        Equal(21L, DiplomaticWarDeclarationLedgerRules.ResolveTargetCityId(
            false, 12L, 21L, 22L), "invalid target falls back to capital");
        Equal(22L, DiplomaticWarDeclarationLedgerRules.ResolveTargetCityId(
            false, 12L, -1L, 22L), "missing capital uses first city");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void False(bool value, string message)
    {
        if (value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"{message}: expected {expected}, got {actual}");
    }
}
```

Add a source-wiring assertion that `AW_KingdomPolicyPatch.cs` calls `DiplomaticWarDeclarationService.OnKingdomYear(__instance)`, while `KingdomAnnualWorkService.cs` no longer contains that call.

- [ ] **Step 2: Verify the new tests fail**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: compilation fails because `ShouldExecute` and `ResolveTargetCityId` are missing.

- [ ] **Step 3: Implement the pure ledger rules**

```csharp
public static bool ShouldExecute(int currentYear, int earliestWarYear,
    int forcedWarYear, bool noticeReady)
{
    if (earliestWarYear >= 0 && currentYear < earliestWarYear)
        return false;
    return noticeReady || forcedWarYear >= 0 && currentYear >= forcedWarYear;
}

public static long ResolveTargetCityId(bool storedCityValid,
    long storedCityId, long capitalCityId, long firstCityId)
{
    if (storedCityValid && storedCityId >= 0L) return storedCityId;
    return capitalCityId >= 0L ? capitalCityId : firstCityId;
}
```

Keep terminal lifecycle checks in `IsPending`; do not create another declaration collection.

- [ ] **Step 4: Execute directly from the ledger record**

Change `ProcessPendingRecord` to reject only destroyed participants, treat an already active pair as started, use `ShouldExecute`, and call `Execute(attacker, defender, record)`.

Change execution-plan building to read `GoalType`, `WarType`, `ReasonKey`, target and claim IDs from `pRecord`, not mutable kingdom keys. Remove `CanQueueCurrentGoal` and goal-specific current eligibility checks from this locked path.

Use this target fallback:

```csharp
City stored = FindCity(pRecord.TargetCityId);
bool storedValid = stored?.data != null && stored.kingdom == pDefender;
City capital = pDefender.capital;
City first = WarTerritoryService.FindFirstTargetCity(pDefender);
long targetId = DiplomaticWarDeclarationLedgerRules.ResolveTargetCityId(
    storedValid, pRecord.TargetCityId,
    capital?.id ?? -1L, first?.id ?? -1L);
City target = FindCity(targetId);
```

Resolve persisted claimant/claim/core IDs directly. If a city-requiring goal has no fallback, return `missing_target_city` once. Preserve the special reunification adapter and normal goal persistence.

- [ ] **Step 5: Move deadline processing out of deferred annual work**

In `AW_KingdomPolicyPatch.UpdateAge_Postfix`, after its validity gate and before scheduling, call:

```csharp
DiplomaticWarDeclarationService.OnKingdomYear(__instance);
```

Remove the declaration call from `KingdomAnnualWorkService.RunWarMobilization`. Ledger lifecycle plus the yearly kingdom callback provides idempotency; `OnWarStarted` remains a duplicate safety net.

- [ ] **Step 6: Run tests**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
git diff --check
```

Expected: all rules pass and the source-wiring test shows one deadline owner.

- [ ] **Step 7: Commit declaration locking**

```powershell
git add Code/core/lineage/DiplomaticWarDeclarationLedgerRules.cs Code/core/lineage/DiplomaticWarDeclarationService.cs Code/patch/AW_KingdomPolicyPatch.cs Code/core/policy/KingdomAnnualWorkService.cs Tests/AncientWarfare3.Rules.Tests/DiplomaticWarDeclarationLockRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "fix: lock and timely execute war declarations"
```

### Task 3: Block only new Zhulu declarations during Hope Age

**Files:**
- Modify: `Code/core/lineage/ZhuluWarRules.cs`
- Modify: `Code/core/lineage/ZhuluWarService.cs`
- Modify: `Code/core/lineage/DiplomaticWarDeclarationService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`

- [ ] **Step 1: Add failing Hope Age tests**

```csharp
False(ZhuluWarRules.CanCreateDeclaration("age_hope"),
    "Hope blocks new Zhulu declarations");
True(ZhuluWarRules.CanCreateDeclaration("age_zhulu"),
    "the Zhulu age permits new declarations");
True(ZhuluWarRules.CanCreateDeclaration("age_wonders"),
    "other ages retain intrinsic eligibility");
```

Update source-wiring assertions to require the final `IssueZhulu` boundary to call the gate, and remove the old assertion that preparation completion revalidates through `ZhuluWarService.CanDeclare`.

- [ ] **Step 2: Verify the tests fail**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: compilation fails because `CanCreateDeclaration` is missing.

- [ ] **Step 3: Implement and wire the gate**

```csharp
public const string HopeAgeId = "age_hope";
public const string HopeAgeBlockedReason = "zhulu_blocked_in_hope_age";

public static bool CanCreateDeclaration(string currentAgeId)
{
    return !string.Equals(currentAgeId, HopeAgeId,
        StringComparison.Ordinal);
}
```

At the start of ordinary `ZhuluWarService.CanDeclare`, reject Hope Age with `HopeAgeBlockedReason`. At the start of `DiplomaticWarDeclarationService.IssueZhulu`, repeat the live-age gate before `TryIssue` to protect stale UI and async plans.

Do not call the gate from locked pending execution, `IsZhuluWar`, settlement, occupation, or existing-war restoration.

- [ ] **Step 4: Verify all Zhulu paths**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
rg -n "IssueZhulu|CanDeclare\(|TryDeclare\(" Code/core/lineage Code/ui -g "*.cs"
git diff --check
```

Expected: tests pass; every new declaration reaches `IssueZhulu` or `CanDeclare`; existing-war paths do not use `CanCreateDeclaration`.

- [ ] **Step 5: Commit the gate**

```powershell
git add Code/core/lineage/ZhuluWarRules.cs Code/core/lineage/ZhuluWarService.cs Code/core/lineage/DiplomaticWarDeclarationService.cs Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt
git commit -m "fix: block new zhulu declarations in hope age"
```

### Task 4: Regression verification and source deployment

**Files:**
- Verify: `Code/core/pathfinding/AWPathLifecycleRules.cs`
- Verify: `Code/core/lineage/DiplomaticWarDeclarationService.cs`
- Verify: `Code/core/lineage/ZhuluWarRules.cs`
- Deploy: repository source mod folder only

- [ ] **Step 1: Run the full suite twice**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: both runs exit 0 without order-dependent failures.

- [ ] **Step 2: Inspect call ownership**

```powershell
rg -n "ShouldUseCustomSmoothMovement|UpdateSmoothMovement" Code/core/pathfinding Code/patch -g "*.cs"
rg -n "DiplomaticWarDeclarationService.OnKingdomYear" Code -g "*.cs"
rg -n "CanCreateDeclaration|IssueZhulu" Code -g "*.cs"
```

Expected: one scheduler-aware movement bridge, one timely yearly deadline owner, and gated new Zhulu entries.

- [ ] **Step 3: Deploy source only**

Use the repository's existing source deployment script or command. Confirm no DLL is built or copied. Compare deployed and repository hashes for `AWPathMovementBridge.cs`, `DiplomaticWarDeclarationService.cs`, and `ZhuluWarRules.cs`.

- [ ] **Step 4: Perform in-game checks**

With `AW3_ENABLE_FRAME_PRIORITY_SCHEDULER=false`, load the same approximately 4399-population save. Record FPS, frame time, `aw3_actor_path_movement`, path submissions, active paths, and stuck Actors for at least one game year. Enable Large once and verify streamed Actors still move.

Issue a legal declaration, mutate its former eligibility during preparation, and verify war begins by the forced year. Enter Hope Age and verify no new Zhulu reason appears while an already pending declaration and active Zhulu war continue.

- [ ] **Step 5: Review the final tree**

```powershell
git status --short
git diff --check
git log -4 --oneline
```

Expected: only intentional source/tests exist. Do not commit logs, screenshots, DLLs, saves, or test binaries.

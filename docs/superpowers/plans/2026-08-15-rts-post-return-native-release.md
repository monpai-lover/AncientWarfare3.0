# RTS Post-Return Native Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** End all RTS ownership after a wartime army reaches a friendly safe city, hand permanent survivors to native WorldBox AI, and keep synthetic soldiers protected until that arrival boundary.

**Architecture:** Split successful arrival from invalid/disposed return cleanup inside `WarArmyReturnService`. Add one narrow `ArmyRtsControllerService.ReleaseAfterReturn` API that invalidates controller state without starting another return or installing mod peacetime jobs, then releases actor tasks to native jobs; retain `Cancel` unchanged for a valid replacement wartime mission.

**Tech Stack:** C# 11, .NET Framework 4.8, WorldBox actor AI/jobs, source-guard executable rules tests.

---

## File Map

- Modify `Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt`: encode arrival/discard/cancellation ordering and native-handoff source guards.
- Modify `Code/core/lineage/WarArmyReturnService.cs`: distinguish successful arrival from invalid cleanup and invoke post-return release only on arrival.
- Modify `Code/core/lineage/ArmyRtsControllerService.cs`: add idempotent controller teardown plus permanent-actor native job handoff without peacetime mod job installation.
- Verify `Code/core/lineage/SyntheticMobilizationLedgerService.cs`: retain its existing `WarArmyReturnService.IsActive` demobilization gate; no production edit is expected.

### Task 1: Lock The Arrival Ownership Boundary With RED Tests

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt`

- [ ] **Step 1: Replace the obsolete peacetime-handoff assertion**

Change the `ReleaseReturnActor` source guard so it rejects direct mod peacetime ownership:

```csharp
False(releaseActor.Contains(
        "StandingArmyPeacetimeService.RefreshJob(pActor)",
        StringComparison.Ordinal),
    "successful return completion never installs a mod peacetime job");
```

- [ ] **Step 2: Add successful-arrival and cancellation source guards**

Assert the successful path has this ordering and that cancellation remains non-destructive:

```csharp
string completion = Section(service,
    "private static void CompleteArrival(long pArmyId, Army pArmy)",
    "private static void Discard(long pArmyId, Army pArmy)");
int queueComplete = completion.IndexOf("Queue.Complete(pArmyId)",
    StringComparison.Ordinal);
int clearReturn = completion.IndexOf("ClearPersisted(pArmy)",
    StringComparison.Ordinal);
int nativeRelease = completion.IndexOf(
    "ArmyRtsControllerService.ReleaseAfterReturn(pArmy)",
    StringComparison.Ordinal);
int completedLog = completion.IndexOf("stage=completed",
    StringComparison.Ordinal);
True(queueComplete >= 0 && clearReturn > queueComplete &&
     nativeRelease > clearReturn && completedLog > nativeRelease,
    "arrival clears return ownership before native handoff and logging");
True(!cancellation.Contains("ReleaseAfterReturn(",
        StringComparison.Ordinal),
    "a valid replacement mission cancels return without releasing RTS");
```

Also assert that only the safe-city branches call `CompleteArrival`, while invalid army, invalid target, restore rejection, and disposed cleanup call `Discard`.

- [ ] **Step 3: Add controller-release and synthetic-ordering source guards**

Read `ArmyRtsControllerService.cs` and require a public API whose body uses `Invalidate(pArmy.id, pReleaseActorJobs: false)`, never calls `WarArmyReturnService.TryBegin`, never calls `RefreshReleasedArmyPeacetimeJobs`, unregisters military P0 ownership, clears return/RTS targets, skips native job assignment for `SyntheticLevyService.IsSynthetic(actor)`, and calls `actor.ai.setJob(actor.getNextJob())` for permanent survivors. Retain these existing ledger guards:

```csharp
True(syntheticSource.Contains(
        "WarArmyReturnService.IsActive(actor.army)",
        StringComparison.Ordinal),
    "synthetic demobilization remains deferred until return ownership clears");
True(nativeRelease > clearReturn,
    "synthetic actors become demobilization-eligible only at arrival handoff");
```

- [ ] **Step 4: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --rts-exclusive-retreat-return
```

Expected: FAIL because `CompleteArrival`, `Discard`, and `ReleaseAfterReturn` do not exist and completion still calls `StandingArmyPeacetimeService.RefreshJob`.

- [ ] **Step 5: Commit the RED regression**

```powershell
git add Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt
git commit -m "test: require native AI handoff after army return"
```

### Task 2: Implement Idempotent Post-Return Controller Release

**Files:**
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`

- [ ] **Step 1: Add the narrow release API**

Add the API beside `Invalidate` and reuse the full army-state invalidation path without its existing actor/peacetime release:

```csharp
public static void ReleaseAfterReturn(Army pArmy)
{
    if (pArmy?.data == null) return;
    long armyId = pArmy.id;
    Invalidate(armyId, pReleaseActorJobs: false);
    ReleaseAfterReturnActors(pArmy);
}
```

This clears controller and mission persistence/index, transport, logistics, formation, routes, runtime state, and pending replenishment without recursively beginning another return.

- [ ] **Step 2: Add best-effort actor release helpers**

Traverse `pArmy.units` and the captain defensively. For every live actor, unregister `ArmyMilitaryMovementPriorityIndex`, cancel `AWPathMovementBridge` ownership, clear attack/tile targets, and cancel RTS/return behaviors. For synthetic actors stop after cleanup so the bounded demobilization ledger owns their destruction; for permanent actors request native ownership:

```csharp
private static void ReleaseAfterReturnActor(Actor pActor)
{
    if (pActor?.data == null || pActor.ai == null) return;
    ArmyMilitaryMovementPriorityIndex.Unregister(pActor.data.id);
    try
    {
        if (AWPathMovementBridge.HasOwnership(pActor))
            AWPathMovementBridge.Cancel(pActor,
                AWPathFailureReason.CancelledByNewRequest);
        pActor.cancelAllBeh();
        pActor.stopMovement();
        pActor.clearOldPath();
        pActor.clearTileTarget();
        pActor.clearAttackTarget();
        pActor.beh_tile_target = null;
        pActor.beh_actor_target = null;
        if (!SyntheticLevyService.IsSynthetic(pActor))
            pActor.ai.setJob(pActor.getNextJob());
        else
            pActor.ai.clearJob();
    }
    catch { }
}
```

Use per-actor `try/catch` so one disposed actor cannot prevent release of the remaining roster. Ensure captain de-duplication is harmless, making repeated calls idempotent.

- [ ] **Step 3: Run the focused test**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --rts-exclusive-retreat-return
```

Expected: still FAIL only on `WarArmyReturnService` arrival/discard routing; controller release guards pass.

- [ ] **Step 4: Commit controller teardown**

```powershell
git add Code/core/lineage/ArmyRtsControllerService.cs
git commit -m "fix: release returned armies from RTS ownership"
```

### Task 3: Separate Successful Arrival From Invalid Return Cleanup

**Files:**
- Modify: `Code/core/lineage/WarArmyReturnService.cs`

- [ ] **Step 1: Introduce explicit terminal methods**

Replace the ambiguous `Finish` with two methods:

```csharp
private static void CompleteArrival(long pArmyId, Army pArmy)
{
    Queue.Complete(pArmyId);
    ClearPersisted(pArmy);
    ArmyRtsControllerService.ReleaseAfterReturn(pArmy);
    ModClass.LogInfo("[AW3 RTS return] stage=completed" +
                     " army=" + pArmyId +
                     " rts_active=" +
                     ArmyRtsControllerService.HasValidMission(pArmy));
}

private static void Discard(long pArmyId, Army pArmy)
{
    Queue.Complete(pArmyId);
    ClearPersisted(pArmy);
    ReleaseReturnJobs(pArmy);
}
```

The completion diagnostic must be emitted after release and show whether RTS ownership unexpectedly remains.

- [ ] **Step 2: Route only proven safe-city arrival to native release**

Use `CompleteArrival` in `TryBegin` when `IsInsideFriendlySafeCity` is true and in `ProcessFrame` only when `decision == WarArmyReturnOrderDecision.Complete` for a live valid army. Keep `Cancel` for `CancelForMission`. Route invalid/disposed army, unavailable replacement target, restore-invalid, and queue-rejected paths to `Discard`.

In `TryRestore`, distinguish a live safe-city completion from invalid facts before selecting `CompleteArrival`; never infer successful arrival merely from `HasArrived` returning true for a dead army.

- [ ] **Step 3: Remove mod peacetime refresh from arrival release**

Delete `StandingArmyPeacetimeService.RefreshJob(pActor)` from `ReleaseReturnActor`. `Discard` may clear stale return jobs, but it must not install new mod peacetime work. Permanent actors on a successful arrival already receive native `getNextJob()` through `ReleaseAfterReturn`.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --rts-exclusive-retreat-return
```

Expected: PASS.

- [ ] **Step 5: Commit arrival routing**

```powershell
git add Code/core/lineage/WarArmyReturnService.cs
git commit -m "fix: hand returned armies to native AI"
```

### Task 4: Verify Lifecycle Compatibility

**Files:**
- Verify: `Code/core/lineage/WarArmyReturnService.cs`
- Verify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Verify: `Code/core/lineage/SyntheticMobilizationLedgerService.cs`
- Verify: `Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt`

- [ ] **Step 1: Run return and war lifecycle slices**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --rts-exclusive-retreat-return
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --rts-war-lifecycle
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --rts-wartime-lifecycle-slice
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --army-mobilization-slice
```

Expected: all four commands exit 0. The mobilization slice proves synthetic soldiers are protected while return is active and become removable after arrival clears it.

- [ ] **Step 2: Run the full rules suite**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected: exit 0 with all rules tests passing.

- [ ] **Step 3: Build the mod**

```powershell
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: zero errors.

- [ ] **Step 4: Review the branch diff and commit any verification-only corrections**

```powershell
git diff --check
git status --short
git log --oneline master..HEAD
```

Expected: no whitespace errors; only the design, plan, focused tests, and two RTS service files differ from `master`.

# RTS Siege and Return Handoffs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure active target-city combat continuously owns the correct siege tasks and completed return-home orders release every ordinary soldier to vanilla peacetime AI.

**Architecture:** Keep both fixes at their existing ownership boundaries. The siege controller will use the existing job cursor to repair the captain and bounded member batches while `SiegeCombatActive` remains true; return completion will clear each return job before asking the vanilla static job selector for the next job.

**Tech Stack:** C# 8, .NET Framework 4.8 mod assembly, source-guard and pure-rule console tests in `AncientWarfare3.Rules.Tests`.

---

### Task 1: Guard Active Siege Task Repair

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsCaptainCombatRulesTests.cs.txt:182`
- Test: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write the failing source-guard test**

Replace the existing one-shot `RegisterTargetCitySiegeMembers` assertion with guards that require active siege to repair ownership before returning:

```csharp
int siegeEntryStart = controller.IndexOf(
    "private static bool EnterTargetCitySiegeCombat(",
    StringComparison.Ordinal);
int siegeEntryEnd = controller.IndexOf(
    "private static void EnsureTargetCitySiegeTasks(",
    Math.Max(0, siegeEntryStart), StringComparison.Ordinal);
string siegeEntryBody = siegeEntryStart >= 0 &&
    siegeEntryEnd > siegeEntryStart
    ? controller.Substring(siegeEntryStart, siegeEntryEnd - siegeEntryStart)
    : string.Empty;
int refreshTarget = siegeEntryBody.IndexOf(
    "pRuntime.SiegeTargetActorId = target.data.id;",
    StringComparison.Ordinal);
int ensureTasks = siegeEntryBody.IndexOf(
    "EnsureTargetCitySiegeTasks(pArmy, pRuntime);",
    StringComparison.Ordinal);
int activeReturn = siegeEntryBody.IndexOf(
    "if (!enteringSiege) return true;", StringComparison.Ordinal);
Assert(refreshTarget >= 0 && ensureTasks > refreshTarget &&
       (activeReturn < 0 || ensureTasks < activeReturn),
    "every active siege tick repairs tactical task ownership before returning");

int siegeEnsureStart = controller.IndexOf(
    "private static void EnsureTargetCitySiegeTasks(",
    StringComparison.Ordinal);
int siegeEnsureEnd = controller.IndexOf(
    "private static void ExitTargetCitySiegeCombat(",
    Math.Max(0, siegeEnsureStart), StringComparison.Ordinal);
string siegeEnsureBody = siegeEnsureStart >= 0 &&
    siegeEnsureEnd > siegeEnsureStart
    ? controller.Substring(siegeEnsureStart,
        siegeEnsureEnd - siegeEnsureStart)
    : string.Empty;
Assert(siegeEnsureBody.Contains("SetCaptainTacticalTask(captain)",
           StringComparison.Ordinal) &&
       siegeEnsureBody.Contains("TryReopenJobOwnershipRepair(pRuntime)",
           StringComparison.Ordinal) &&
       siegeEnsureBody.Contains("MaximumJobMutationsPerController",
           StringComparison.Ordinal) &&
       siegeEnsureBody.Contains("SetNativeMemberMissionTask(actor, pArmy)",
           StringComparison.Ordinal),
    "active siege repairs the captain immediately and members in bounded batches");
```

- [ ] **Step 2: Run the rules suite and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: FAIL with `every active siege tick repairs tactical task ownership before returning` because the current implementation returns immediately when siege is already active.

- [ ] **Step 3: Commit the failing test**

```powershell
git add Tests/AncientWarfare3.Rules.Tests/ArmyRtsCaptainCombatRulesTests.cs.txt
git commit -m "test: guard active RTS siege task repair"
```

### Task 2: Repair Siege Tasks While Siege Is Active

**Files:**
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs:4662`
- Test: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsCaptainCombatRulesTests.cs.txt`

- [ ] **Step 1: Replace the one-shot member registration with bounded ownership repair**

In `EnterTargetCitySiegeCombat`, keep target validation and first-entry route cleanup unchanged. Set `SiegeCombatActive`, reopen the job cursor only on first entry, then call task repair on every active tick:

```csharp
bool enteringSiege = !pRuntime.SiegeCombatActive;
pRuntime.SiegeTargetActorId = target.data.id;
if (enteringSiege)
{
    pRuntime.NativeRoute.MarkMovementInterrupted();
    ArmyRouteProviderService.Cancel(pArmy.id,
        ArmyRouteCancelReason.TargetReplaced);
    AWArmyMarchService.ClearArmy(pArmy.id);
    ClearArmyAttackTargets(pArmy);
    ResetStrategicMovementRuntime(pRuntime);
    pRuntime.SiegeCombatActive = true;
    pRuntime.JobCursor.Reopen();
}
EnsureTargetCitySiegeTasks(pArmy, pRuntime);
if (!enteringSiege) return true;
```

Replace `RegisterTargetCitySiegeMembers` with a bounded repair helper:

```csharp
private static void EnsureTargetCitySiegeTasks(Army pArmy,
    RuntimeState pRuntime)
{
    if (pArmy?.data == null || pRuntime == null ||
        !pRuntime.SiegeCombatActive) return;
    Actor captain = SafeCaptain(pArmy);
    SetCaptainTacticalTask(captain);
    TryReopenJobOwnershipRepair(pRuntime);
    int count;
    try { count = pArmy.units.Count; }
    catch { count = 0; }
    bool jobsWereInitialized = pRuntime.JobCursor.JobsInitialized;
    int end = Math.Min(count, pRuntime.JobCursor.MemberCursor +
                              MaximumJobMutationsPerController);
    for (int i = pRuntime.JobCursor.MemberCursor; i < end; i++)
    {
        Actor actor;
        try { actor = pArmy.units[i]; }
        catch { continue; }
        if (actor == captain || !IsLiveWarriorActor(actor)) continue;
        SetNativeMemberMissionTask(actor, pArmy);
        ArmyMilitaryMovementPriorityIndex.Register(actor.data.id,
            ArmyMilitaryMovementPriorityKind.RtsMember);
    }
    pRuntime.JobCursor.Advance(end, count);
    if (!jobsWereInitialized && pRuntime.JobCursor.JobsInitialized)
        pRuntime.NextJobOwnershipRepairWorldTime = CurrentWorldTime() +
            ArmyRtsRules.JobOwnershipRepairIntervalSeconds;
}
```

Keep the existing `SetNativeMemberMissionTask` city-zone gate so only soldiers in the target city core/border receive `SiegeCombatTaskId`; outside soldiers retain vanilla follow.

- [ ] **Step 2: Run the rules suite and verify GREEN**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: `Rule tests passed.`

- [ ] **Step 3: Commit the siege fix**

```powershell
git add Code/core/lineage/ArmyRtsControllerService.cs
git commit -m "fix: retain RTS siege task ownership"
```

### Task 3: Guard Clean Return-to-Vanilla Release

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt:248`
- Test: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Change the return-release source guard to require a clean job boundary**

Update the `releaseAfterReturn` assertion so the ordinary actor branch must clear the return job before invoking the vanilla static selector:

```csharp
int releaseActorStart = releaseAfterReturn.IndexOf(
    "private static void ReleaseAfterReturnActor(Actor pActor)",
    StringComparison.Ordinal);
string releaseActorBody = releaseActorStart >= 0
    ? releaseAfterReturn.Substring(releaseActorStart)
    : string.Empty;
int clearReturnJob = releaseActorBody.IndexOf(
    "pActor.ai.clearJob();", StringComparison.Ordinal);
int selectVanillaJob = releaseActorBody.IndexOf(
    "pActor.ai.setJob(Actor.nextJobActor(pActor));",
    StringComparison.Ordinal);
True(clearReturnJob >= 0 && selectVanillaJob > clearReturnJob &&
     !releaseActorBody.Contains("pActor.getNextJob()",
         StringComparison.Ordinal),
    "completed return clears RTS return ownership before vanilla job selection");
```

Retain the existing assertions that return completion invalidates the RTS mission without starting another return and unregisters military P0.

- [ ] **Step 2: Run the rules suite and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: FAIL with `completed return clears RTS return ownership before vanilla job selection` because production still calls `pActor.getNextJob()` while the return job is current.

- [ ] **Step 3: Commit the failing test**

```powershell
git add Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt
git commit -m "test: guard return-to-vanilla job release"
```

### Task 4: Release Every Returned Soldier to Vanilla AI

**Files:**
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs:3856`
- Test: `Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt`

- [ ] **Step 1: Clear the return job before vanilla selection**

Keep synthetic levy cleanup unchanged. Replace the ordinary branch in `ReleaseAfterReturnActor` with:

```csharp
else
{
    pActor.ai.clearJob();
    pActor.ai.setJob(Actor.nextJobActor(pActor));
}
```

Keep the existing catch fallback that clears the job. Because `ReleaseAfterReturnActors` already iterates `pArmy.units` and then the captain, this applies to every surviving ordinary member and the captain.

- [ ] **Step 2: Run the rules suite and verify GREEN**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: `Rule tests passed.`

- [ ] **Step 3: Commit the return fix**

```powershell
git add Code/core/lineage/ArmyRtsControllerService.cs
git commit -m "fix: release returned soldiers to vanilla AI"
```

### Task 5: Full Verification

**Files:**
- Verify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Verify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsCaptainCombatRulesTests.cs.txt`
- Verify: `Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt`

- [ ] **Step 1: Run the full rules suite**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: `Rule tests passed.`

- [ ] **Step 2: Build the Release mod assembly**

```powershell
dotnet build AncientWarfare3.csproj -c Release
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Check patch integrity and scope**

```powershell
git diff master...HEAD --check
git status --short --branch
git log --oneline master..HEAD
```

Expected: no whitespace errors; only the design/plan, two focused tests, and `ArmyRtsControllerService.cs` are changed; the worktree is clean after commits.

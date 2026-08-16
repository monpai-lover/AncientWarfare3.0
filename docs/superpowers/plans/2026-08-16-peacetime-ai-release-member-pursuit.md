# Peacetime AI Release and RTS Member Pursuit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove AW3 peacetime patrol ownership and make ordinary RTS soldiers retain detected hostile targets long enough to approach and attack them.

**Architecture:** Legacy peacetime patrol assets stay registered only so old saves can resolve their IDs; runtime job selection never assigns them and performs one-way cleanup when encountered. Member combat gains a validator and bounded target finder separate from captain combat, so the captain's ten-tile envelope cannot clear a soldier's chase target before the existing `BehGoToActorTarget` movement step.

**Tech Stack:** C# 7-compatible WorldBox mod code, Harmony patches, .NET 9 rules-test executables, .NET Framework 4.8 release build.

---

### Task 1: Release Peacetime Warriors to Vanilla AI

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt:4366-4478`
- Modify: `Code/core/lineage/StandingArmyRules.cs:27-47`
- Modify: `Code/core/lineage/StandingArmyPeacetimeService.cs:36-50,132-174,233-240`
- Modify: `Code/patch/AW_EnlistPatch.cs:247-272`

- [ ] **Step 1: Write the failing rule and source-boundary tests**

Change the peaceful career-soldier expectation and add legacy-state cleanup coverage:

```csharp
Equal(false, StandingArmyRules.ShouldUsePeacetimePatrol(
        isCareerStandingSoldier: true,
        militaryEmergency: false,
        inCombat: false,
        cityAttackOrder: false),
    "complete peace leaves career soldiers under vanilla AI");
Equal(true, StandingArmyRules.ShouldReleaseLegacyPeacetimePatrol(
        "aw_standing_army_peacetime_job", ""),
    "a legacy patrol job is released");
Equal(true, StandingArmyRules.ShouldReleaseLegacyPeacetimePatrol(
        "", "aw_standing_army_peacetime_patrol"),
    "a legacy patrol task is released");
Equal(false, StandingArmyRules.ShouldReleaseLegacyPeacetimePatrol(
        "unit", "sexual_reproduction_civ_go"),
    "unrelated vanilla work is not cleared");
```

Update source assertions so `AW_EnlistPatch.GetNextJob_Asylum_Prefix` calls `ReleaseLegacyPatrolForJobSelection` but no longer calls `StandingArmyPeacetimeService.GetJob`, and `StandingArmyPeacetimeService.RefreshJob` contains no assignment to `StandingArmyPeacetimeContent.JobId` or `PatrolTaskId`.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected: FAIL because complete peace still returns `true` for patrol selection and `ShouldReleaseLegacyPeacetimePatrol` does not exist.

- [ ] **Step 3: Implement the minimum peacetime release rule**

In `StandingArmyRules`, make patrol assignment permanently false and add the compatibility predicate:

```csharp
public static bool ShouldUsePeacetimePatrol(
    bool isCareerStandingSoldier, bool militaryEmergency,
    bool inCombat, bool cityAttackOrder)
{
    return false;
}

public static bool ShouldReleaseLegacyPeacetimePatrol(
    string pJobId, string pTaskId)
{
    return pJobId == "aw_standing_army_peacetime_job" ||
           pTaskId == "aw_standing_army_peacetime_patrol";
}
```

In `StandingArmyPeacetimeService`, replace assignment behavior with these cleanup paths:

```csharp
public static void ReleaseLegacyPatrolForJobSelection(Actor pActor)
{
    ReleaseLegacyPeacetimePatrol(pActor, pRestoreImmediately: false);
}

public static void RefreshJob(Actor pActor)
{
    if (pActor?.data == null || pActor.ai == null) return;
    if (WarArmyReturnService.IsActive(pActor.army))
    {
        WarArmyReturnService.TryPrepareMilitaryP0Actor(pActor);
        return;
    }
    ReleaseLegacyPeacetimePatrol(pActor, pRestoreImmediately: true);
}

private static void ReleaseLegacyPeacetimePatrol(Actor pActor,
    bool pRestoreImmediately)
{
    if (pActor?.data == null || pActor.ai == null) return;
    string jobId = pActor.ai.job?.id ?? "";
    string taskId = pActor.ai.task?.id ?? "";
    if (!StandingArmyRules.ShouldReleaseLegacyPeacetimePatrol(
            jobId, taskId)) return;
    pActor.cancelAllBeh();
    pActor.data.set(PatrolCursorKey, 0);
    if (!pRestoreImmediately)
    {
        pActor.ai.clearJob();
        return;
    }
    try { pActor.ai.setJob(Actor.nextJobActor(pActor)); }
    catch { pActor.ai.clearJob(); }
}
```

`CanYieldToReproduction` must return `true` only for the legacy patrol job/task so the compatibility patch cannot alter unrelated vanilla tasks after patrol ownership is removed. Existing legacy content registration remains unchanged.

Replace the standing-army `GetJob` interception block in `AW_EnlistPatch.GetNextJob_Asylum_Prefix` with `StandingArmyPeacetimeService.ReleaseLegacyPatrolForJobSelection(__instance);`. This cleanup never supplies `__result` or returns `false`, so the original `Actor.getNextJob()` decides peaceful warrior work.

- [ ] **Step 4: Run focused and shared rule tests and verify GREEN**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected: both exit 0; no assertion still requires peaceful soldiers to receive the AW3 patrol job.

- [ ] **Step 5: Commit the peacetime fix**

```powershell
git add -- Tests/AncientWarfare3.Rules.Tests/Program.cs.txt Code/core/lineage/StandingArmyRules.cs Code/core/lineage/StandingArmyPeacetimeService.cs Code/patch/AW_EnlistPatch.cs
git commit -m "fix: release peacetime armies to vanilla AI"
```

### Task 2: Preserve Member Targets Through Approach Movement

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsCaptainCombatRulesTests.cs.txt:9-24,100-190`
- Modify: `Code/core/lineage/ArmyRtsCaptainCombatRules.cs:5-9`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs:1837-1903,2034-2059,2186-2268`
- Modify: `Code/ai/behaviours/actor/BehArmyRtsCaptainCombat.cs:77-108`

- [ ] **Step 1: Write failing member target-retention tests**

Add rule assertions without changing captain expectations:

```csharp
Assert(ArmyRtsCaptainCombatRules.ShouldRetainMemberTarget(
        targetAlive: true, targetHostile: true,
        sameIsland: true, combatOwned: true),
    "an RTS member retains a hostile target outside the captain envelope");
Assert(!ArmyRtsCaptainCombatRules.ShouldRetainMemberTarget(
        targetAlive: false, targetHostile: true,
        sameIsland: true, combatOwned: true),
    "a dead member target is rejected");
Assert(!ArmyRtsCaptainCombatRules.ShouldRetainMemberTarget(
        targetAlive: true, targetHostile: false,
        sameIsland: true, combatOwned: true),
    "a friendly member target is rejected");
Assert(!ArmyRtsCaptainCombatRules.ShouldRetainMemberTarget(
        targetAlive: true, targetHostile: true,
        sameIsland: false, combatOwned: true),
    "a cross-island member target is rejected");
Assert(!ArmyRtsCaptainCombatRules.ShouldRetainMemberTarget(
        targetAlive: true, targetHostile: true,
        sameIsland: true, combatOwned: false),
    "a target is released after RTS combat ownership ends");
```

Replace the source guard that requires member combat to call captain validation with assertions that the member behavior calls `IsValidMemberCombatTarget` and `FindMemberCombatTarget`, while captain behavior still calls `IsValidCaptainCombatTarget`.

- [ ] **Step 2: Run the focused rules suite and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected: FAIL to compile because `ShouldRetainMemberTarget` does not exist, proving the new contract is not implemented.

- [ ] **Step 3: Add the member-specific rule and runtime validator**

Add to `ArmyRtsCaptainCombatRules`:

```csharp
public static bool ShouldRetainMemberTarget(bool targetAlive,
    bool targetHostile, bool sameIsland, bool combatOwned)
{
    return targetAlive && targetHostile && sameIsland && combatOwned;
}
```

Add `IsValidMemberCombatTarget(Actor, Actor)` to `ArmyRtsControllerService`. It must validate live actors and tiles, same-island reachability, `HasMemberCombatMission(pActor)`, hostility, and the new rule, without calling `IsWithinCaptainCombatEnvelope`.

Add `FindMemberCombatTarget(Actor)` using the existing bounded `Finder.getUnitsFromChunk(pActor.current_tile, 2, 10)` scan and nearest-target comparison, but filter with `IsValidMemberCombatTarget`. Do not scan `World.world.units`.

- [ ] **Step 4: Route every member-combat boundary through the member validator**

Change `BehArmyRtsMemberCombat` to validate and search through the member methods. Change `HasValidMemberCombatTarget` to use the member validator. In `IsValidAssignedCombatTarget`, preserve siege validation first, then use member validation when the actor is on `MemberCombatTaskId`, and otherwise use captain validation.

The behavior must keep this successful path so the next registered action performs approach movement:

```csharp
pActor.beh_actor_target = target;
return BehResult.Continue;
```

No change is made to the task sequence `BehArmyRtsMemberCombat -> BehGoToActorTarget -> BehArmyRtsCaptainAttack -> BehRestartTask`.

- [ ] **Step 5: Run focused and adversarial tests and verify GREEN**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
dotnet run --project Tests\ArmyRtsAdversarialSimulation\ArmyRtsAdversarialSimulation.csproj --no-restore
```

Expected: both exit 0; captain envelope assertions remain unchanged and member source guards confirm the independent chase path.

- [ ] **Step 6: Commit the member pursuit fix**

```powershell
git add -- Tests/AncientWarfare3.Rules.Tests/ArmyRtsCaptainCombatRulesTests.cs.txt Code/core/lineage/ArmyRtsCaptainCombatRules.cs Code/core/lineage/ArmyRtsControllerService.cs Code/ai/behaviours/actor/BehArmyRtsCaptainCombat.cs
git commit -m "fix: keep RTS members pursuing detected enemies"
```

### Task 3: Build and Deploy the Runtime Sources

**Files:**
- Verify: `AncientWarfare3.csproj`
- Deploy to: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run the full required verification set**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
dotnet run --project Tests\ArmyRtsAdversarialSimulation\ArmyRtsAdversarialSimulation.csproj --no-restore
$aw3Ref='C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\.NETFramework\v4.8'
$aw3Root='C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\'
dotnet build AncientWarfare3.csproj -c Release --no-restore -p:FrameworkPathOverride="$aw3Ref" -p:TargetFrameworkRootPath="$aw3Root"
```

Expected: all commands exit 0 with no build errors.

- [ ] **Step 2: Deploy only the changed runtime source files**

```powershell
$aw3Deploy='D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
Copy-Item -Force 'Code\core\lineage\StandingArmyRules.cs' "$aw3Deploy\Code\core\lineage\StandingArmyRules.cs"
Copy-Item -Force 'Code\core\lineage\StandingArmyPeacetimeService.cs' "$aw3Deploy\Code\core\lineage\StandingArmyPeacetimeService.cs"
Copy-Item -Force 'Code\patch\AW_EnlistPatch.cs' "$aw3Deploy\Code\patch\AW_EnlistPatch.cs"
Copy-Item -Force 'Code\core\lineage\ArmyRtsCaptainCombatRules.cs' "$aw3Deploy\Code\core\lineage\ArmyRtsCaptainCombatRules.cs"
Copy-Item -Force 'Code\core\lineage\ArmyRtsControllerService.cs' "$aw3Deploy\Code\core\lineage\ArmyRtsControllerService.cs"
Copy-Item -Force 'Code\ai\behaviours\actor\BehArmyRtsCaptainCombat.cs' "$aw3Deploy\Code\ai\behaviours\actor\BehArmyRtsCaptainCombat.cs"
```

- [ ] **Step 3: Verify deployed source hashes**

For each deployed file, compare `(Get-FileHash <source>).Hash` with the matching deployed path. Expected: all six pairs match.

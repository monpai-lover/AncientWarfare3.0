# Warrior Movement and RTS Member Combat Implementation Plan

> **Superseded on 2026-08-16:** Do not execute the peacetime patrol restoration in this document. The current root-cause plan is `docs/superpowers/plans/2026-08-16-peacetime-warrior-movement-root-cause.md`; peaceful warriors must return to vanilla AI without reintroducing an AncientWarfare3 patrol job.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore peaceful warriors' native decision and movement lifecycle, and restore RTS ordinary soldiers to the working captain-combat lifecycle.

**Architecture:** The peacetime standing-army job will again compose native decision-making with bounded border patrol, exactly as the working v1.1.2 release did. RTS field combat will keep all army members inside the dedicated member-combat task while field combat is active and will reuse captain target validation/search semantics; the controller alone decides when combat ends and follow resumes. Existing return-to-city-center behavior and post-return release logic are explicitly out of scope.

**Tech Stack:** C#/.NET Framework 4.8.1, WorldBox actor behavior jobs/tasks, source-guard rule tests, PowerShell deployment verification.

---

### Task 1: Restore Peaceful Warrior Native Decisions

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt`
- Modify: `Code/content/StandingArmyPeacetimeContent.cs`
- Modify: `Code/ai/behaviours/actor/BehStandingArmyPeacetimePatrol.cs`

- [ ] **Step 1: Write the failing source-guard test**

Add assertions that read the peacetime content and behavior sources and require the v1.1.2 lifecycle:

```csharp
string peacetimeContent = File.ReadAllText(Path.Combine(root, "Code",
    "content", "StandingArmyPeacetimeContent.cs"));
string peacetimeBehaviour = File.ReadAllText(Path.Combine(root, "Code",
    "ai", "behaviours", "actor",
    "BehStandingArmyPeacetimePatrol.cs"));
int nativeDecision = peacetimeContent.IndexOf(
    "job.addTask(\"make_decision\")", StringComparison.Ordinal);
int patrolTask = peacetimeContent.IndexOf(
    "job.addTask(PatrolTaskId)", StringComparison.Ordinal);
Assert(nativeDecision >= 0 && patrolTask > nativeDecision,
    "peaceful warriors retain native hunger and movement decisions before patrol");
Assert(peacetimeContent.Contains("cancellable_by_socialize = true",
           StringComparison.Ordinal) &&
       peacetimeContent.Contains("new BehRandomWait(2f, 5f)",
           StringComparison.Ordinal),
    "peacetime patrol yields to native social work and pauses between patrols");
Assert(peacetimeBehaviour.Contains("return BehResult.Stop;",
           StringComparison.Ordinal) &&
       !peacetimeBehaviour.Contains("return BehResult.RepeatStep;",
           StringComparison.Ordinal),
    "an unavailable patrol target releases the current patrol cycle");
```

- [ ] **Step 2: Run the focused lifecycle tests and verify failure**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --rts-exclusive-retreat-return
```

Expected: FAIL because `make_decision`, `BehRandomWait`, and the stopping patrol behavior are absent.

- [ ] **Step 3: Restore the v1.1.2 peacetime job composition**

In `StandingArmyPeacetimeContent.Init`, use:

```csharp
job.addTask("make_decision");
job.addTask(PatrolTaskId);
```

Set:

```csharp
cancellable_by_socialize = true,
```

and append:

```csharp
patrol.addBeh(new BehRandomWait(2f, 5f));
```

In `BehStandingArmyPeacetimePatrol`, replace the retry loop with the proven release behavior:

```csharp
if (tile == null || tile == pActor?.current_tile)
{
    pActor?.makeWait(Randy.randomFloat(2f, 5f));
    return BehResult.Stop;
}
```

- [ ] **Step 4: Run the focused tests and verify success**

Run the command from Step 2.

Expected: PASS, including the new peaceful-warrior ownership assertions.

- [ ] **Step 5: Commit only the peaceful warrior repair**

```powershell
git add -- Code/content/StandingArmyPeacetimeContent.cs Code/ai/behaviours/actor/BehStandingArmyPeacetimePatrol.cs Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt
git commit -m "fix: restore peaceful warrior native decisions"
```

### Task 2: Restore Captain-Style RTS Member Combat

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsCaptainCombatRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsCombatHandoffRulesTests.cs.txt`
- Modify: `Code/core/lineage/ArmyRtsCaptainCombatRules.cs`
- Modify: `Code/ai/behaviours/actor/BehArmyRtsCaptainCombat.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`

- [ ] **Step 1: Replace the broken target-first expectations with field-combat ownership tests**

Require these rule outcomes:

```csharp
Assert(ArmyRtsCaptainCombatRules.ShouldUseMemberCombatTask(
        missionActive: true, actorIsCaptain: false,
        fieldCombatReleased: true, hasValidCombatTarget: false),
    "active field combat keeps a temporarily targetless soldier in member combat");
Assert(!ArmyRtsCaptainCombatRules.ShouldSuppressVanillaMemberFight(
        missionActive: true, actorIsCaptain: false,
        fieldCombatReleased: true, hasValidCombatTarget: false),
    "field combat routes targetless members into dedicated combat");
Assert(!ArmyRtsCaptainCombatRules.ShouldRestoreVanillaMemberFollow(
        suppressVanillaFight: true, isDedicatedMemberCombatTask: true),
    "transient target loss cannot replace dedicated member combat with follow");
```

Add source assertions requiring `runtime.FieldCombatReleased || HasValidMemberCombatTarget(pActor)`, captain target search inside `BehArmyRtsMemberCombat`, `makeWait(0.15f)`, and `BehResult.RepeatStep`. Remove assertions for the member-only 16-tile finder and `RestoreMemberFollowAfterCombatLull`.

- [ ] **Step 2: Run the RTS war-lifecycle test and verify failure**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --rts-war-lifecycle
```

Expected: FAIL because current code requires a personal target before assigning member combat and restores follow after a target miss.

- [ ] **Step 3: Restore field-combat task ownership rules**

In `ArmyRtsCaptainCombatRules`, restore:

```csharp
return missionActive && !actorIsCaptain &&
       (fieldCombatReleased || hasValidCombatTarget);
```

Restore vanilla-fight suppression and follow restoration to:

```csharp
return missionActive && !actorIsCaptain &&
       !fieldCombatReleased && !hasValidCombatTarget;
```

```csharp
return suppressVanillaFight && !isDedicatedMemberCombatTask;
```

Remove the member-only combat-envelope rule introduced by the broken change.

- [ ] **Step 4: Align member target acquisition with captain combat**

In `BehArmyRtsMemberCombat.execute`, use the captain lifecycle:

```csharp
Actor target = pActor.beh_actor_target?.a;
if (!ArmyRtsControllerService.IsValidCaptainCombatTarget(pActor, target))
    target = ArmyRtsControllerService.FindCaptainCombatTarget(pActor);

if (!ArmyRtsControllerService.IsValidCaptainCombatTarget(pActor, target))
{
    pActor.beh_actor_target = null;
    pActor.makeWait(0.15f);
    return BehResult.RepeatStep;
}

pActor.beh_actor_target = target;
return BehResult.Continue;
```

- [ ] **Step 5: Restore controller admission without reverting unrelated fixes**

Use captain validation for shared assigned targets and engagement counting. Remove `IsValidMemberCombatTarget`, `FindMemberCombatTarget`, and `RestoreMemberFollowAfterCombatLull`. In `SetNativeMemberMissionTask`, restore:

```csharp
else if (runtime.FieldCombatReleased ||
         HasValidMemberCombatTarget(pActor))
```

Keep the existing `pActor.getNextJob()` restoration and the post-AI `followerTaskAfterAi` P0 refresh unchanged. Do not modify return rules, return service, or city-center arrival behavior.

- [ ] **Step 6: Run focused RTS tests and verify success**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --rts-war-lifecycle
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --rts-exclusive-retreat-return
```

Expected: both commands PASS.

- [ ] **Step 7: Commit only member-combat changes**

```powershell
git add -- Code/core/lineage/ArmyRtsCaptainCombatRules.cs Code/ai/behaviours/actor/BehArmyRtsCaptainCombat.cs Code/core/lineage/ArmyRtsControllerService.cs Tests/AncientWarfare3.Rules.Tests/ArmyRtsCaptainCombatRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/ArmyRtsCombatHandoffRulesTests.cs.txt
git commit -m "fix: restore RTS soldier combat lifecycle"
```

### Task 3: Regression Verification and Deployment

**Files:**
- Verify only: `AncientWarfare3.csproj`
- Deploy with: `deploy-local.ps1`
- Verify with: `Tests/VerifySourceDeployment.ps1`

- [ ] **Step 1: Run the complete rules suite**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
```

Expected: PASS with no failed assertion.

- [ ] **Step 2: Build the mod**

```powershell
dotnet build AncientWarfare3.csproj -c Release -p:TargetFrameworkVersion=v4.8.1
```

Expected: build succeeds with zero errors.

- [ ] **Step 3: Deploy the verified source**

```powershell
.\deploy-local.ps1
```

Expected: deployment completes to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`.

- [ ] **Step 4: Verify deployment parity**

```powershell
.\Tests\VerifySourceDeployment.ps1 -SourceRoot 'F:\WorldBox New Mod\AncientWarfare3.0' -DestinationRoot 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
```

Expected: source and deployed mod files match.

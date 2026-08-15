# RTS Exclusive Retreat And Return Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent combat, stall recovery, socializing, and peacetime patrol from interrupting RTS retreat or post-war return movement.

**Architecture:** Add pure ownership rules at the existing P0, watchdog, and peacetime boundaries. Keep retreat as a fixed-city RTS task, and give post-war return its own persisted captain/follower jobs without creating a fake wartime mission.

**Tech Stack:** C# 11, .NET Framework 4.8, WorldBox actor behavior tasks, existing bounded military P0 scheduler, executable rules tests.

---

## File Map

- Modify `Code/core/lineage/ArmyRtsTaskOwnershipRules.cs`: pure retreat combat-preemption and return ownership decisions.
- Modify `Code/core/lineage/ArmyStallWatchdogRules.cs`: prevent alternate-endpoint escalation for fixed retreat movement.
- Modify `Code/core/lineage/ArmyStallWatchdogService.cs`: apply same-target retreat recovery.
- Modify `Code/core/lineage/ArmyRtsControllerService.cs`: expose retreat transit facts and suppress combat/member task takeover.
- Modify `Code/core/performance/AWCooperativeActorPostRunner.cs`: retain P0 ownership for retreat and active return actors.
- Modify `Code/core/performance/ArmyMilitaryMovementPriorityRules.cs`: pure P0 admission rules for return.
- Modify `Code/content/ArmyRtsContent.cs`: register the return captain/follower jobs and non-cancellable return task; raise retreat speed to 1.15.
- Create `Code/ai/behaviours/actor/BehWarArmyReturnTarget.cs`: resolve the persisted return target and hand cross-island actors to transport.
- Modify `Code/core/lineage/WarArmyReturnService.cs`: own tasks, expose targets, repair jobs, register P0, and release on arrival.
- Modify `Code/core/lineage/WarArmyReturnRules.cs`: pure peacetime/P0/task-claim rules.
- Modify `Code/core/lineage/StandingArmyPeacetimeService.cs`: reject returning soldiers.
- Modify `Code/core/lineage/ArmyRtsControllerService.cs`: skip peacetime refresh while return is active.
- Modify `Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt`: return ownership regressions and source guards.
- Modify `Tests/AncientWarfare3.Rules.Tests/ArmyRtsCombatHandoffRulesTests.cs.txt`: retreat combat-preemption and task-registration guards.
- Modify `Tests/AncientWarfare3.Rules.Tests/ArmyRtsRouteChoiceRulesTests.cs.txt`: retreat watchdog recovery regressions.

### Task 1: Lock Retreat Movement With TDD

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsCombatHandoffRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsRouteChoiceRulesTests.cs.txt`
- Modify: `Code/core/lineage/ArmyRtsTaskOwnershipRules.cs`
- Modify: `Code/core/lineage/ArmyStallWatchdogRules.cs`
- Modify: `Code/core/lineage/ArmyStallWatchdogService.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/core/performance/AWCooperativeActorPostRunner.cs`
- Modify: `Code/content/ArmyRtsContent.cs`

- [ ] **Step 1: Write failing retreat ownership tests**

Add assertions that a valid retreat transit suppresses combat preemption,
disallows alternate endpoints, reasserts only the same target, and registers a
1.15-speed non-social task.

- [ ] **Step 2: Run RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- ArmyRtsCombatHandoffRulesTests ArmyRtsRouteChoiceRulesTests
```

Expected: the new ownership members and source guards fail because retreat is
still combat-preemptible and the watchdog still chooses alternate endpoints.

- [ ] **Step 3: Implement minimal retreat ownership rules**

Add pure predicates for combat suppression and fixed-endpoint recovery, route
the P0 boundary through them, and clear stale attack targets without clearing
the native retreat path. Make watchdog recovery reassert the retreat task or
rebuild the same endpoint only. Change `RetreatTaskId` speed to `1.15f`.

- [ ] **Step 4: Run GREEN**

Run the same focused command. Expected: both slices pass.

### Task 2: Give Post-War Return A Dedicated Task With TDD

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt`
- Modify: `Code/core/lineage/WarArmyReturnRules.cs`
- Modify: `Code/content/ArmyRtsContent.cs`
- Create: `Code/ai/behaviours/actor/BehWarArmyReturnTarget.cs`
- Modify: `Code/core/lineage/WarArmyReturnService.cs`
- Modify: `Code/core/performance/ArmyMilitaryMovementPriorityRules.cs`
- Modify: `Code/core/performance/AWCooperativeActorPostRunner.cs`

- [ ] **Step 1: Write failing return ownership tests**

Cover active-return P0 admission, peacetime blocking, replacement of a moving
social task, captain/follower task registration, native follower use, and the
absence of the old `if (pCaptain.is_moving) return;` command gate.

- [ ] **Step 2: Run RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- WarArmyReturnRulesTests
```

Expected: new rule calls or source guards fail because return has no actor task
ownership.

- [ ] **Step 3: Implement return content and behavior**

Register `aw_army_return_home_captain`, `aw_army_return_home_follower`, and
`aw_army_return_home`. The task resolves its target from the persisted return
order and uses native tile movement. The service repairs task ownership,
registers every live member for P0, and preserves existing healthy paths.

- [ ] **Step 4: Run GREEN**

Run the same focused command. Expected: `WarArmyReturnRulesTests` passes.

### Task 3: Close The Peacetime Ownership Window With TDD

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt`
- Modify: `Code/core/lineage/StandingArmyPeacetimeService.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/core/lineage/WarArmyReturnService.cs`

- [ ] **Step 1: Add failing ordering and peacetime source guards**

Assert that active return rejects peacetime eligibility, return starts after
mission invalidation and before any released-job refresh can own the actors,
and arrival explicitly refreshes peacetime jobs only after clearing intent.

- [ ] **Step 2: Run RED**

Run the `WarArmyReturnRulesTests` slice. Expected: source guards fail on the
current unconditional peacetime refresh path.

- [ ] **Step 3: Implement the ownership gates**

Make both peacetime eligibility and released-job refresh return-aware. On
arrival, clear return state and request a bounded peacetime job refresh. Add
sampled return diagnostics at admission, repair, movement, transport, and
completion.

- [ ] **Step 4: Run GREEN**

Run the return slice and the standing-army peacetime slice. Expected: pass.

### Task 4: Verify, Build, And Deploy

**Files:**
- Verify all modified production and test files.

- [ ] **Step 1: Run focused RTS slices**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- ArmyRtsCombatHandoffRulesTests ArmyRtsRouteChoiceRulesTests WarArmyReturnRulesTests
```

Expected: all selected slices pass.

- [ ] **Step 2: Run the full rules suite**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: all rules tests pass.

- [ ] **Step 3: Build Release**

```powershell
dotnet build AncientWarfare3.csproj -c Release
```

Expected: zero errors and zero warnings.

- [ ] **Step 4: Deploy through the repository deployment script**

Run the existing deployment command used by the project, then verify deployed
source and DLL hashes against the repository build output. Preserve the nested
`.claude/worktrees/rts-army-overhaul` state.

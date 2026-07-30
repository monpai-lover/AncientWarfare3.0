# Wartime Army Command Reinforcement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure newly created and replenished wartime armies receive real actors immediately at the army and are replanned into military orders for either side of a war.

**Architecture:** Reuse `TemporaryLevyService` for candidate eligibility, actor conversion, population floors, occupied-city exclusion, and directed batch recruitment. Extend the existing replenishment-arrival bridge so a wartime army can receive and teleport recruits before its first RTS mission, then make every army registration/roster mutation enqueue the existing coalesced kingdom war-director refresh.

**Tech Stack:** C#, Harmony/WorldBox runtime APIs, existing AW3 deferred work queue, .NET 9 rules tests, PowerShell source guards.

---

### Task 1: Specify Missionless Wartime Reinforcement

**Files:**
- Modify: `Code/core/lineage/ArmyRtsReplenishmentArrivalRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsReplenishmentArrivalRulesTests.cs.txt`
- Modify: `Tests/ArmyRtsSchedulingSourceGuardTests.ps1`

- [ ] **Step 1: Write failing arrival-rule tests**

Add `ShouldTrackArrival` tests proving that a live non-captain warrior attached to the target army is tracked when either an RTS mission is active or its kingdom is in a military emergency. Reject peacetime missionless members and invalid membership.

Extend `ResolveAction` with separate `missionActive` and `wartimeEmergency` inputs. Require a missionless wartime recruit to return `Teleport`, while a missionless peacetime record returns `Discard`. Preserve combat and transport waits.

- [ ] **Step 2: Write a failing lifecycle source guard**

Require `ArmyStrategicIndexService.OnArmyRegistered`, `OnArmyKingdomChanged`, and `OnArmyRosterChanged` to call `KingdomWarDirectorService.QueueArmyChanged`. Require `ArmyRtsControllerService.TrackReplenishmentArrival` to use `ShouldTrackArrival` and a mission-independent reinforcement teleport helper.

- [ ] **Step 3: Run RED verification**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-replenishment-arrival-slice
powershell -ExecutionPolicy Bypass -File Tests/ArmyRtsSchedulingSourceGuardTests.ps1
```

Expected: FAIL because missionless wartime arrivals are currently discarded and strategic roster changes do not queue a director refresh.

### Task 2: Teleport Real Recruits Before First Mission

**Files:**
- Modify: `Code/core/lineage/ArmyRtsReplenishmentArrivalRules.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`

- [ ] **Step 1: Implement arrival eligibility**

Add:

```csharp
internal static bool ShouldTrackArrival(bool runtimeCommit,
    bool memberAttached, bool captain, bool liveWarrior,
    bool missionActive, bool wartimeEmergency)
```

Track only valid non-captain warrior members when runtime mutation is enabled and either the army already has a mission or its kingdom is at war.

- [ ] **Step 2: Add mission-independent reinforcement teleport**

Refactor the existing teleport path into `TryTeleportReinforcementMember`. With an active mission, retain the formation recovery target and job reassertion. Before the first mission, validate army membership, live captain, military emergency, combat/transport safety, then stop movement and spawn the actor on the captain's tile. Do not invent population or edit an army count.

- [ ] **Step 3: Track and process pre-mission arrivals**

Use `ShouldTrackArrival` in `TrackReplenishmentArrival`. Try immediate teleport first; otherwise queue the actor in `PendingReplenishmentArrivals`. In `ResolveReplenishmentArrivalAction`, treat membership as valid without requiring `ArmyFormationService.HasFollower` before the first mission, and pass mission/emergency state to the pure rule.

- [ ] **Step 4: Replan after successful arrival**

After a successful teleport, enqueue `KingdomWarDirectorService.QueueArmyChanged` for the army kingdom. Keep `ReleaseReplenishmentForDeparture` for active missions; it remains a no-op before a controller exists.

- [ ] **Step 5: Run GREEN arrival tests**

Run the focused arrival slice. Expected: PASS.

### Task 3: Replan Every Wartime Roster Mutation

**Files:**
- Modify: `Code/core/lineage/ArmyStrategicIndexService.cs`
- Modify: `Tests/ArmyRtsSchedulingSourceGuardTests.ps1`

- [ ] **Step 1: Defer new-army planning**

Change `OnArmyRegistered` from an immediate `OnArmyChanged` call to `QueueArmyChanged`, allowing the current `newArmy`/enlistment stack to finish before the director captures the roster.

- [ ] **Step 2: Queue after roster and ownership changes**

After refreshing the strategic index in `OnArmyRosterChanged` and `OnArmyKingdomChanged`, enqueue the coalesced kingdom refresh. The queue is keyed by kingdom, so many soldiers joining in one batch still create one director generation.

- [ ] **Step 3: Run source guard and RTS lifecycle tests**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Tests/ArmyRtsSchedulingSourceGuardTests.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-wartime-lifecycle-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-command-slice
```

Expected: PASS.

### Task 4: Verify, Integrate, And Deploy

**Files:**
- Verify: `AncientWarfare3.csproj`
- Deploy changed runtime files to `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run complete relevant verification**

Run the focused arrival, wartime lifecycle, RTS command, army map information, city army reinforcement, and full rules suites. Run the two source guards and rebuild the mod with zero compiler errors.

- [ ] **Step 2: Commit and review the isolated implementation**

Commit production and test changes with message `fix: reinforce and command new wartime armies`, then request a diff review before integration.

- [ ] **Step 3: Merge into master and rerun tests**

Preserve any concurrent city-capacity work and rerun focused tests on the merged tree.

- [ ] **Step 4: Deploy exact runtime files**

Copy only the modified `Code` files into the installed mod at matching paths and verify source/deployed SHA-256 hashes.

- [ ] **Step 5: In-game acceptance**

During an active war, observe both sides. A newly formed army must receive real eligible actors, those actors must appear at its captain, its flag must show `补员` only until operational strength is reached, and the next director cycle must replace that state with an actual defense/attack/reserve order. Confirm donor cities stay above the existing population floor and enemy-occupied cities provide no recruits.

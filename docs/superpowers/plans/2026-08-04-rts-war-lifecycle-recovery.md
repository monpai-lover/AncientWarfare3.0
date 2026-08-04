# RTS War Lifecycle Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent assigned RTS armies from freezing or waiting forever, hand tactical combat back to vanilla AI, and recover armies at fixed 20/80-percent wartime strength thresholds.

**Architecture:** Add small pure rule types for lifecycle, route truth, recruitment protection, idle reconciliation, and diagnostic sampling, then integrate them into the existing RTS controller and war hooks. Keep strategic mission state in AW3 while vanilla owns tactical combat; reuse existing director queues, movement reset APIs, and synthetic spawn path.

**Tech Stack:** C#/.NET 9 rules tests, Unity/WorldBox runtime services, Harmony war hooks.

---

### Task 1: Wartime lifecycle rules

**Files:**
- Create: `Code/core/lineage/ArmyRtsWarLifecycleRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsWarLifecycleRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] Write tests proving `ShouldWithdraw(living, baseline)` is true exactly at 20%, `ShouldResume` is true exactly at 80%, zero/invalid baselines do not transition, baseline capture is write-once, tactical handoff requires target-territory hostile contact, and generation is blocked during combat/transport/movement.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- ArmyRtsWarLifecycleRulesTests` and verify compile failure because `ArmyRtsWarLifecycleRules` is absent.
- [ ] Implement `ArmyRtsWarPhase`, `ArmyRtsWarLifecycleRules.ShouldWithdraw`, `ShouldResume`, `CaptureBaseline`, `ShouldReleaseToVanilla`, and `CanGenerateReplacements` with integer multiplication and the approved gates.
- [ ] Re-run the targeted test and verify PASS.
- [ ] Commit with `git commit -m "feat: add RTS wartime lifecycle rules"`.

### Task 2: Preparation recruitment protection

**Files:**
- Create: `Code/core/lineage/PreparationRecruitmentProtectionRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/PreparationRecruitmentProtectionRulesTests.cs.txt`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Modify: test project and runner registration files.

- [ ] Write table-driven tests proving any heir rank, king, city leader, official, captain, or existing protected identity is rejected and an ordinary resident is accepted.
- [ ] Run the targeted test and verify failure because the new rule does not exist.
- [ ] Implement `IsProtected(bool anyHeir, bool king, bool cityLeader, bool official, bool captain, bool existingProtection)` and feed facts from `TemporaryLevyService.IsProtectedIdentity`; resolve all heirs from the authoritative heir registry/service rather than current-heir-only checks.
- [ ] Run targeted and existing `TemporaryLevyRulesTests`; verify PASS.
- [ ] Commit with `git commit -m "fix: protect succession and office identities from levies"`.

### Task 3: Fixed war baseline state

**Files:**
- Create: `Code/core/lineage/ArmyRtsWarLifecycleService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsWarLifecycleStateTests.cs.txt`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: test project and runner registration files.

- [ ] Write state tests proving war-start capture is fixed, first mission captures a late army, captain replacement preserves state, destroyed-army removal clears it, and war end clears every record for that war.
- [ ] Run the targeted test and verify failure because the lifecycle store is absent.
- [ ] Implement keyed records containing `WarId`, `ArmyId`, fixed `BaselineStrength`, phase, prior offensive mission, wait reason/deadline, and replenishment city; expose capture, lookup, transfer, remove, and clear-war operations.
- [ ] Wire formal war start/end and mission assignment/invalidation to the service.
- [ ] Run targeted tests and the full rules suite; verify PASS.
- [ ] Commit with `git commit -m "feat: track fixed wartime army baselines"`.

### Task 4: Vanilla combat handoff and RTS reacquisition

**Files:**
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/core/lineage/ArmyRtsTaskOwnershipRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsCombatHandoffRulesTests.cs.txt`
- Modify: test project and runner registration files.

- [ ] Write tests for target-city hostile contact handoff, pass-through territory retaining RTS control, <=20% withdrawal overriding combat, defender removal reacquiring strategic control, and prior mission restoration.
- [ ] Run the targeted test and verify the new ownership decisions fail.
- [ ] During `VanillaCombat`, retain controller mission but clear AW movement/formation jobs; during withdrawal or completed contact, clear vanilla attack targets, restore RTS task ownership, and queue route/objective work.
- [ ] Run targeted tests and existing RTS task/transition tests; verify PASS.
- [ ] Commit with `git commit -m "feat: hand RTS tactical combat to vanilla AI"`.

### Task 5: Synthetic recovery at occupied or safe cities

**Files:**
- Modify: `Code/core/lineage/SyntheticLevyService.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsSyntheticRecoveryRulesTests.cs.txt`
- Modify: test project and runner registration files.

- [ ] Write tests proving a friendly occupied city without hostiles is eligible, unsafe current city selects a safe home city, generation is blocked in combat/transport/movement, batch size never exceeds the 80% baseline target, and residents are not consumed.
- [ ] Run the targeted test and verify failure for missing recovery decisions.
- [ ] Permit `SyntheticLevyService.CreateBatch` to attach generated soldiers to the specified target army when the approved replenishment city differs from its anchor; keep synthetic ledger accounting and population untouched.
- [ ] Drive bounded synthetic batches only in `Replenishing`, stop at 80%, then restore the prior open mission or queue the army director.
- [ ] Run targeted tests plus `SyntheticLevyRulesTests`; verify PASS.
- [ ] Commit with `git commit -m "feat: replenish withdrawn armies with synthetic soldiers"`.

### Task 6: Missionless and expired-wait reconciliation

**Files:**
- Create: `Code/core/lineage/ArmyRtsAssignmentReconciliationRules.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/core/lineage/KingdomWarDirectorService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsAssignmentReconciliationRulesTests.cs.txt`
- Modify: test project and runner registration files.

- [ ] Write tests proving eligible missionless armies queue assignment, controller mission with missing captain task repairs ownership, waits require non-empty reason plus finite deadline, and expired waits requeue while valid waits do not.
- [ ] Run the targeted test and verify failure because reconciliation rules are absent.
- [ ] Add bounded periodic reconciliation using existing army indexes and `QueueArmyChanged`; release stale objective claims and assign a reason/deadline whenever no legal objective exists.
- [ ] Run targeted tests and director/controller tests; verify PASS.
- [ ] Commit with `git commit -m "fix: reconcile idle wartime armies"`.

### Task 7: Empty shared-route recovery

**Files:**
- Modify: `Code/core/lineage/ArmySharedPathRules.cs`
- Modify: `Code/core/lineage/AWArmyMarchService.cs`
- Modify: `Code/core/lineage/ArmyStallWatchdogService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmySharedPathRecoveryRulesTests.cs.txt`
- Modify: test project and runner registration files.

- [ ] Write tests proving a matching historical revision is reusable only when following a non-empty path or already at endpoint, and empty/non-following installed status requests reset/reinstall while combat and transport block recovery.
- [ ] Run the targeted test and verify failure against the historical-status behavior.
- [ ] Make `GetSharedRouteInstallStatus` derive current truth, clear all actor route ownership/targets idempotently in `ResetActorSharedRoute`, and have watchdog recovery reset then requeue the preserved strategic mission.
- [ ] Run targeted tests and all route/watchdog tests; verify PASS.
- [ ] Commit with `git commit -m "fix: recover empty installed RTS routes"`.

### Task 8: Diagnostic throttling and final verification

**Files:**
- Modify: `Code/core/lineage/TemporaryLevyDiagnosticRules.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/TemporaryLevyDiagnosticSamplingTests.cs.txt`
- Modify: test project and runner registration files.

- [ ] Write tests proving first observation logs, identical signatures are suppressed until the sampling deadline, material changes log immediately, and disabling/resetting diagnostics clears sampling state.
- [ ] Run the targeted test and verify failure because stateful sampling is absent.
- [ ] Implement a bounded per-operation sampler and replace per-work-item recovery output with transition/periodic summaries.
- [ ] Run targeted diagnostics tests, the full rules suite, and the full mod build; verify zero failures and exit code 0.
- [ ] Inspect `git diff --check`, inspect the requirement checklist against the approved design, and commit with `git commit -m "fix: throttle RTS levy diagnostics"`.


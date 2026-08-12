# RTS Land March Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep RTS land armies moving as a unit by requiring completed formation observation and a 90% escort quorum, while recovering individual stalled followers without replacing the active mission.

**Architecture:** Extend the pure `ArmyRtsRules` and `ArmyFormationRules` decisions first, then thread those decisions through `ArmyRtsControllerService` and `ArmyStallWatchdogService`. Reuse the existing cursor-based formation scans, shared-route revisions and per-follower recovery state; never add a whole-army scan to a frame and never invalidate a strategic mission for tactical recovery.

**Tech Stack:** C#/.NET Framework 4.8, Unity/WorldBox Actor AI, existing rules-test console project, PowerShell source guards.

---

### Task 1: Add red tests for the 90% formation gate

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt` near the existing `CanCaptainAdvanceWithEscort` assertions around lines 928-947 and formation observations around lines 1030-1050.
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj` only if a rules file is not already linked.
- Test: `Code/core/lineage/ArmyRtsRules.cs` and `Code/core/lineage/ArmyFormationRules.cs` through the console test project.

- [ ] **Step 1: Write the failing assertions.** Add assertions for an incomplete observation, an 89% rallied quorum, a 90% rallied quorum, and a materially unsafe post-departure quorum. Use the existing rule APIs where possible; define the desired API explicitly if it does not exist:

```csharp
Equal(false, ArmyRtsRules.CanCaptainAdvanceWithEscort(
    requiresEscort: true, rosterLiving: 10, nearbyFollowers: 8,
    captainPresent: true, immediateCombat: false,
    transportOwnsMovement: false, observationComplete: true),
    "89 percent escort quorum blocks land departure");
True(ArmyRtsRules.CanCaptainAdvanceWithEscort(
    requiresEscort: true, rosterLiving: 10, nearbyFollowers: 9,
    captainPresent: true, immediateCombat: false,
    transportOwnsMovement: false, observationComplete: true),
    "90 percent escort quorum permits land departure");
Equal(false, ArmyRtsRules.CanCaptainAdvanceWithEscort(
    requiresEscort: true, rosterLiving: 10, nearbyFollowers: 10,
    captainPresent: true, immediateCombat: false,
    transportOwnsMovement: false, observationComplete: false),
    "incomplete formation observation never permits captain departure");
True(ArmyRtsRules.ShouldHoldAfterEscortLoss(
    departed: true, ralliedFollowers: 7, eligiblePopulation: 10,
    secondsBelowQuorum: 3d),
    "a sustained sub-90 percent escort loss pauses the captain");
```

- [ ] **Step 2: Run the focused rules project and verify RED.**

Run: `dotnet run --project Tests\\AncientWarfare3.Rules.Tests\\AncientWarfare3.Rules.Tests.csproj --no-restore`

Expected: compilation failure for the new observation/escort APIs, or assertion failure if an existing signature is reused incorrectly. Do not change production code before this red result.

- [ ] **Step 3: Commit the test-only red state only if the repository workflow requires checkpoints.** Keep the test changes unstaged until the implementation task is ready if the project does not permit red commits.

### Task 2: Implement pure escort and observation rules

**Files:**
- Modify: `Code/core/lineage/ArmyRtsRules.cs` around `CanCaptainAdvanceWithEscort`.
- Modify: `Code/core/lineage/ArmyFormationRules.cs` in the existing observation/quorum rule section.
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt` only to add edge cases after the first green run.

- [ ] **Step 1: Add explicit constants and pure decisions.** Add `LandEscortQuorumPercent = 90`, `LandEscortSafetyFloorPercent = 75`, and `EscortLossGraceSeconds = 2d` in the existing rules class. Add a helper that computes `ceil(eligiblePopulation * 0.90)` with zero/negative inputs clamped. Extend the captain gate with `observationComplete`; return false for incomplete observation before checking counts, while preserving the explicit transport and immediate-combat ownership exceptions.

- [ ] **Step 2: Add hysteresis decision.** Implement `ShouldHoldAfterEscortLoss(bool departed, int ralliedFollowers, int eligiblePopulation, double secondsBelowQuorum)` so it returns false before departure, returns true immediately below the 75% safety floor, and returns true after two continuous seconds below 90%.

- [ ] **Step 3: Run the focused rules project.**

Run: `dotnet run --project Tests\\AncientWarfare3.Rules.Tests\\AncientWarfare3.Rules.Tests.csproj --no-restore`

Expected: `Rule tests passed.`

- [ ] **Step 4: Add edge assertions and rerun.** Cover zero eligible population, transport ownership, immediate combat, negative elapsed time, and exactly 75%/90% boundaries. Keep all decisions deterministic and allocation-free.

### Task 3: Gate the RTS captain on completed observation and 90% quorum

**Files:**
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs` in the captain movement/escort gate around `CanCaptainAdvanceWithEscort` and `ShouldCaptainWaitForFormation`.
- Modify: `Code/core/lineage/ArmyFormationService.cs` only where observation progress/counters are read for the existing bounded cursor.
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt` source assertions near the existing controller source checks.

- [ ] **Step 1: Write a source-level failing guard.** Assert that the captain movement path passes `observation.Complete` into the escort decision and that an incomplete observation maps to a named hold reason rather than `formation_living=0`. Run the rules project to confirm the guard fails before implementation.

- [ ] **Step 2: Thread observation state into the captain gate.** In the existing captain movement path, read `ArmyFormationService.GetObservationProgress(army)` and `GetIncrementalFollowerCounters(army)`. Pass `observation.Complete` to the pure rule. When incomplete, keep the current mission/controller record, set the existing hold state, requeue bounded member installation, and return without advancing the captain.

- [ ] **Step 3: Preserve the bounded cursor.** Do not call `army.units` enumeration outside the existing cursor slice. On roster-version change, restart or rebase the formation observation generation and keep `record.Mission`, `WarId`, `TargetCityId`, `Role`, `FrontId`, and `DirectorGeneration` unchanged.

- [ ] **Step 4: Add the pending diagnostic.** Replace ambiguous zero counters in sampled diagnostics with `formation_observation_pending` when `Complete == false`; only report zero eligible population as an invariant issue after a completed observation.

- [ ] **Step 5: Run tests and build.**

Run: `dotnet run --project Tests\\AncientWarfare3.Rules.Tests\\AncientWarfare3.Rules.Tests.csproj --no-restore`

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Expected: both commands exit 0 with `Rule tests passed.` and zero build warnings/errors.

### Task 4: Add per-army escort-loss hysteresis without mission invalidation

**Files:**
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs` in the per-army `RuntimeState` definition and state update path.
- Modify: `Code/core/lineage/ArmyRtsControllerRules.cs` if the existing state transition helper belongs there; otherwise keep the pure decision in `ArmyRtsRules.cs`.
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt` with mission-continuity source assertions.

- [ ] **Step 1: Write the failing source guard.** Require a runtime timestamp/flag for continuous below-quorum time and require that escort hold does not call `Invalidate`, `OnArmyChanged`, `EnsureOffensiveContinuity`, or a director assignment method.

- [ ] **Step 2: Add runtime escort-loss state.** Store `EscortBelowQuorumSince` and `EscortHoldActive` in the existing per-army runtime state. Reset both on mission assignment, route progress, roster observation completion, or restored quorum. Use realtime/world time consistently with the controller's existing stall timing source.

- [ ] **Step 3: Apply the hysteresis decision.** After departure, sample the current counters once per controller item. Set the start timestamp on the first below-quorum sample; hold only when `ShouldHoldAfterEscortLoss` returns true. Resume only after the full 90% quorum is restored.

- [ ] **Step 4: Verify mission identity.** Add a unit/source assertion that the hold branch leaves the controller record and mission fields intact. Run the focused rules project and main build.

### Task 5: Make follower recovery escalate in bounded stages

**Files:**
- Modify: `Code/core/lineage/ArmyStallWatchdogService.cs` in follower sampling/escalation.
- Modify: `Code/core/lineage/AWArmyMarchService.cs` in `ResetActorSharedRoute`, shared-route install and independent correction handling.
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs` in `RecoverFormationMember` and `RecoverEmptySharedRoute` only where stage selection is required.
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmySharedPathRecoveryRulesTests.cs.txt` and `Program.cs.txt`.

- [ ] **Step 1: Add failing recovery-stage tests.** Prove the ordered outcomes: task reassertion first, route reset/reinstall at 5 seconds, alternate slot at 10 seconds, teleport at 20 seconds; combat and transport return `None` and do not escalate.

- [ ] **Step 2: Implement stage selection in pure rules.** Reuse `ArmyFollowerStallRecoveryRules` and `ArmyStallWatchdogRules` instead of introducing a second timer. Add named constants for 5/10/20 seconds and ensure each actor has independent timestamps and pending correction entries.

- [ ] **Step 3: Keep recovery member-scoped.** `RecoverFormationMember` may reset only the follower route/job and requeue the army controller. `RecoverEmptySharedRoute` may replan the same strategic endpoint only when the shared route or captain is stale. Neither path may clear the mission or call war-director reassignment.

- [ ] **Step 4: Run recovery tests.**

Run: `dotnet run --project Tests\\AncientWarfare3.Rules.Tests\\AncientWarfare3.Rules.Tests.csproj --no-restore`

Expected: `Rule tests passed.`

### Task 6: Keep tactical recovery responsive in large-step mode

**Files:**
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs` around `ProcessFrame` and controller queue admission.
- Modify: `Code/core/lineage/KingdomWarDirectorService.cs` only if a tactical requeue is incorrectly gated by strategic planning mode.
- Modify: `Code/core/lineage/ArmyRtsRuntimeModeRules.cs` or its actual linked rules file if the mode decision is pure.
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsContinuitySourceGuard.ps1` or add a focused source guard under `Tests/AncientWarfare3.Rules.Tests`.

- [ ] **Step 1: Add a failing mode-equivalence assertion.** Assert that one admitted logical pass invokes bounded controller work in both native and large-step modes, while only the director planning branch remains low frequency.

- [ ] **Step 2: Separate controller admission from director planning.** Keep `ArmyRtsRuntimeModeRules.ShouldPlan` around strategic planning, but allow the tactical controller queue and follower recovery budget to consume the current logical pass whenever authoritative RTS execution is enabled. Preserve paused, replica and load guards.

- [ ] **Step 3: Verify no duplicate pulses.** Use the existing logical-pass token/exact-once gate and add a source assertion that controller execution cannot run twice for one token.

- [ ] **Step 4: Run the mode and continuity guards.**

Run: `& .\\Tests\\AncientWarfare3.Rules.Tests\\ArmyRtsContinuitySourceGuard.ps1`

Run: `& .\\Tests\\ArmyRtsMissionTaskContinuitySourceGuard.ps1`

Expected: both source guards pass.

### Task 7: Add runtime diagnostics and run the full verification matrix

**Files:**
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs` diagnostic sampling near `LogMissionChanged` and movement diagnostics.
- Modify: `Code/core/lineage/ArmyStallWatchdogService.cs` diagnostic fields for follower stages.
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmySharedPathRecoveryRulesTests.cs.txt` and relevant source guards.

- [ ] **Step 1: Add sampled counters.** Record expected/eligible/observed/rallied counts, observation generation/completion, 90% quorum, shared-route revision, installed followers, local corrections, recovery stage, hold reason and hold duration. Respect `AWPerformanceSettings.ArmyRtsDiagnosticsEnabled` and existing sampling limits.

- [ ] **Step 2: Run the complete rules project.**

Run: `dotnet run --project Tests\\AncientWarfare3.Rules.Tests\\AncientWarfare3.Rules.Tests.csproj --no-restore`

Expected: `Rule tests passed.`

- [ ] **Step 3: Run relevant source guards.**

Run: `& .\\Tests\\RoyalGuardTaskPresentationSourceGuard.ps1`

Run: `& .\\Tests\\StandingArmyOwnershipSourceGuard.ps1`

Run: `& .\\Tests\\AncientWarfare3.Rules.Tests\\ArmyRtsContinuitySourceGuard.ps1`

Run: `& .\\Tests\\ArmyRtsMissionTaskContinuitySourceGuard.ps1`

Run: `& .\\Tests\\ArmyRtsStallAuditSourceGuardTests.ps1`

Expected: each guard passes. Existing unrelated legacy guard failures must be reported separately, not masked.

- [ ] **Step 4: Build the mod.**

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Expected: exit 0 with zero warnings and zero errors.

- [ ] **Step 5: Perform runtime verification.** Use a land-connected test world with one army below 128 members, one above 128, narrow terrain and deliberately blocked follower slots. Repeat in native and large-step modes. Confirm captain movement, 90% quorum, follower recovery within seconds, unchanged mission identity and no recurring full-army frame spike in `Player.log`.

### Checkpoint Commits

Commit after each green task using focused messages:

```text
test: cover RTS land escort quorum
fix: gate captain on completed formation observation
fix: preserve RTS mission during escort hold
fix: stage follower route recovery
fix: run RTS tactical recovery each logical pass
test: cover RTS land march diagnostics
```

### Self-Review

- Spec coverage: formation observation, 90% quorum, hysteresis, staged follower recovery, mission continuity, large-step tactical admission, diagnostics and bounded verification are covered by Tasks 1-7.
- Placeholder scan: no `TBD`, `TODO`, vague “handle edge cases” steps or undefined task references remain.
- Type consistency: all planned calls use existing `ArmyRtsRules`, `ArmyFormationService`, `AWArmyMarchService`, `ArmyStallWatchdogService` and `ArmyRtsControllerService` boundaries; any new pure method is introduced in Task 2 before consumption in later tasks.

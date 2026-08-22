# Succession Capital War Score Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Freeze both capitals at succession-war start, settle reunification by frozen-capital capture, and force authoritative `+100/-100` scores after total occupation.

**Architecture:** Keep succession-specific target resolution in `SuccessionDisputeService` and persistence, while keep total-occupation detection as a pure `WarScoreTotalOccupationRules` function consumed by `WarScoreService`. Missing legacy snapshots fall back to existing `WarWinner` behavior; all new writes are idempotent and transactional through existing persistence APIs.

**Tech Stack:** C# runtime services, SQLite migration/persistence, existing `WarScoreService` and `SuccessionDisputeService`, PowerShell source guards, tracked C# rules tests.

---

### Task 1: Pure capital-victory and total-occupation rules

**Files:**
- Create `Code/core/lineage/SuccessionCapitalVictoryRules.cs`
- Create `Code/core/lineage/WarScoreTotalOccupationRules.cs`
- Test `Tests/SuccessionCapitalWarScoreSourceGuard.ps1`
- Modify `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Add `Tests/AncientWarfare3.Rules.Tests/SuccessionCapitalWarScoreRulesTests.cs.txt`

- [ ] **Step 1: Write failing C# tests.** Add cases for original captures rival frozen capital, rival captures original frozen capital, neither capture falls back, invalid/destroyed capital does not count, and total occupation returns winner/loser sides only when all initial cities are controlled.
- [ ] **Step 2: Run the focused rules project/test entry and verify failure.** Expected: compile failure because both rule classes are absent.
- [ ] **Step 3: Implement `SuccessionCapitalVictoryRules`.** Expose a pure `ResolveWinner(long originalKingdomId, long rivalKingdomId, long originalCapitalId, long rivalCapitalId, Func<long,long> controllerByCity, WarWinner fallback)` that returns the kingdom ID whose frozen opposing capital is controlled, only accepting live positive controller IDs; return fallback winner when neither target is controlled.
- [ ] **Step 4: Implement `WarScoreTotalOccupationRules`.** Expose `TryResolveWinner(int attackerInitialCities, int defenderInitialCities, int attackerCurrentCities, int defenderCurrentCities, bool attackerControlsAllDefenderCities, bool defenderControlsAllAttackerCities, out WarScoreSide winner)`; require a positive initial city count and return exactly one side only for complete occupation.
- [ ] **Step 5: Run focused tests and source guard;** expected PASS.
- [ ] **Step 6: Commit** with `git add Code/core/lineage/SuccessionCapitalVictoryRules.cs Code/core/lineage/WarScoreTotalOccupationRules.cs Tests/SuccessionCapitalWarScoreSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests/SuccessionCapitalWarScoreRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj; git commit -m "test: define succession capital and total occupation rules"`.

### Task 2: Persist capitals at succession-war start

**Files:**
- Modify `Code/core/lineage/SuccessionDisputeService.cs`
- Modify `Code/core/lineage/SuccessionDisputePersistence.cs`
- Modify `Code/core/lineage/SuccessionDisputePersistenceService.cs`
- Modify `Code/core/db/SuccessionDisputeTableItem.cs`
- Modify `Code/core/lineage/LineageKeys.cs` only if a runtime key is needed
- Test `Tests/SuccessionCapitalWarStartSourceGuard.ps1`

- [ ] **Step 1: Write failing source guard.** Require snapshot fields `OriginalCapitalCityIdAtWarStart` and `RivalCapitalCityIdAtWarStart`, schema columns/default migration, and assignment in `StartWar` before the war binding is persisted.
- [ ] **Step 2: Run guard and verify failure.** Expected: missing fields and start-time capture.
- [ ] **Step 3: Add snapshot/persistence fields.** Use `-1L` defaults; add nullable-compatible `ORIGINAL_CAPITAL_CITY_ID` and `RIVAL_CAPITAL_CITY_ID` columns with idempotent `ALTER TABLE` migration and read/write indexes matching existing succession persistence patterns.
- [ ] **Step 4: Capture both capitals in `StartWar`.** Read only valid current capitals owned by the corresponding kingdom; write `-1` when unavailable. Persist the snapshot and war metadata atomically with the existing status transition.
- [ ] **Step 5: Add legacy repair.** When loading an active succession row with missing/negative capital fields, fill from the current valid capital once, persist it, and otherwise leave `-1` so end-war fallback remains available.
- [ ] **Step 6: Run source guard and database/rules tests;** expected PASS.
- [ ] **Step 7: Commit** with `git add Code/core/lineage/SuccessionDisputeService.cs Code/core/lineage/SuccessionDisputePersistence.cs Code/core/lineage/SuccessionDisputePersistenceService.cs Code/core/db/SuccessionDisputeTableItem.cs Tests/SuccessionCapitalWarStartSourceGuard.ps1; git commit -m "feat: freeze succession war capitals at war start"`.

### Task 3: Use frozen capitals for reunification settlement

**Files:**
- Modify `Code/core/lineage/SuccessionDisputeService.cs`
- Modify `Code/patch/AW_WarPatch.cs` only if the existing end-war call needs a stable winner handoff
- Test `Tests/SuccessionCapitalSettlementSourceGuard.ps1`

- [ ] **Step 1: Write failing source guard.** Require `OnWarEnded` to call frozen-capital resolution for permanent split wars, preserve `WarWinner` fallback, and avoid reading `kingdom.capital` as the primary target.
- [ ] **Step 2: Run guard and verify failure.** Expected: current code directly maps `WarWinner` to winner kingdom.
- [ ] **Step 3: Implement controller lookup.** Read authoritative frozen city control from `WarScoreService`/current city ownership; reject destroyed or invalid target cities. Resolve original or rival victory using `SuccessionCapitalVictoryRules`.
- [ ] **Step 4: Update `OnWarEnded`.** For `PermanentSplit`, choose the resolved kingdom ID first; when no frozen-capital result exists, use existing attacker/defender winner mapping. Call `SettleReunification` once with an idempotent reason (`original_capital_captured`, `rival_capital_captured`, or fallback reason).
- [ ] **Step 5: Add migration-safe logging and no-target fallback.** Log the dispute/war IDs and frozen target IDs at debug level; never block settlement if a legacy row has `-1` targets.
- [ ] **Step 6: Run source guard and focused tests;** expected PASS.
- [ ] **Step 7: Commit** with `git add Code/core/lineage/SuccessionDisputeService.cs Code/patch/AW_WarPatch.cs Tests/SuccessionCapitalSettlementSourceGuard.ps1; git commit -m "fix: settle reunification by frozen capital capture"`.

### Task 4: Force authoritative score on total occupation

**Files:**
- Modify `Code/core/lineage/WarScoreService.cs`
- Modify `Code/core/lineage/WarScorePersistence.cs`
- Modify `Code/core/lineage/WarScoreRuntimeBridge.cs` or existing occupation callback that has complete city facts
- Test `Tests/WarScoreTotalOccupationSourceGuard.ps1`

- [ ] **Step 1: Write failing source guard.** Require a total-occupation call, a persisted idempotent event key, and canonical score normalization to `100`/`-100`.
- [ ] **Step 2: Run guard and verify failure.** Expected: no total-occupation settlement path.
- [ ] **Step 3: Add a `WarScoreService.TrySettleTotalOccupation` method.** Inputs are war ID, winner side, initial/current city counts, all-cities-controlled flags, and world time. Under `_gate`, reject inactive/invalid/repeated cases; clone the snapshot, set decisive score and canonical score to `+100`, save a single total-occupation event/control transaction, update active state, and return true.
- [ ] **Step 4: Wire the call after city-control changes.** Use the authoritative city-control index and initial owner counts, call once after the new control is persisted, and ensure it runs before proposal/AI score reads. Do not alter ordinary city score budgets when the condition is false.
- [ ] **Step 5: Add history/read compatibility.** Load the total-occupation marker from the existing relief/event table or add a minimal migration column; repeated ticks must return false without changing revision or score.
- [ ] **Step 6: Run source guard and focused WarScore rules/tests;** expected PASS.
- [ ] **Step 7: Commit** with `git add Code/core/lineage/WarScoreService.cs Code/core/lineage/WarScorePersistence.cs Code/core/lineage/WarScoreRuntimeBridge.cs Tests/WarScoreTotalOccupationSourceGuard.ps1; git commit -m "fix: force war score extremes after total occupation"`.

### Task 5: End-to-end verification and handoff

- [ ] **Step 1: Run all new PowerShell guards.** Every new guard must exit 0.
- [ ] **Step 2: Run the tracked rules project.** Record the known repository post-build failure if `CultiwayPerfSchedulerCompletionSourceGuard.ps1` remains absent; distinguish it from compilation/test failures.
- [ ] **Step 3: Run the focused succession and war-score test entry directly** from the built DLL or isolated project and verify the new assertions pass.
- [ ] **Step 4: Review `git diff master...HEAD --check` and status.** Confirm no `SuccessionDisputeService.cs` edits from the main worktree are copied accidentally except this plan's intentional changes.
- [ ] **Step 5: Use `finishing-a-development-branch`** and present merge/push/keep/discard options; do not merge or push without explicit user choice.


# War Occupation Fail-Closed Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent every active hostile-war capture from permanently transferring a city before an explicit peace settlement.

**Architecture:** Separate the decision to block vanilla transfer from persistence success. Active hostile war always blocks `finishCapture`; successful recording creates frozen control, while transient recording failure leaves a bounded pending retry and still holds visual occupation at 100 percent.

**Tech Stack:** C#/.NET 4.8, Harmony City patches, AW3 war-score persistence, .NET 9 rule tests.

---

### Task 1: Define The Fail-Closed Capture Decision

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Code/core/lineage/CityOccupationAccelerationRules.cs`

- [ ] Add failing tests for active hostile war plus freeze success, freeze failure, no active war, rebellion, load, and peace execution.
- [ ] Run the rules project and verify active-war freeze failure currently lacks a blocking decision.
- [ ] Add a pure `ShouldBlockPermanentTransfer` decision that blocks every ordinary capture during active hostile war independently of freeze persistence.
- [ ] Re-run the focused occupation tests and expect them to pass.

### Task 2: Gate `finishCapture` And Retry Frozen Control

**Files:**
- Modify: `Code/patch/AW_CityOccupationAccelerationPatch.cs`
- Modify: `Code/core/lineage/WarScoreRuntimeBridge.cs`
- Create: `Code/core/lineage/PendingFrozenOccupationService.cs`

- [ ] Add a failing source guard proving `finishCapture` never falls through solely because `TryFreezeCityOccupation` returned false.
- [ ] Detect an active hostile war before attempting persistence.
- [ ] On success, keep the existing frozen-control path.
- [ ] On failure, set visual capture to 100, retain the proposed occupier, enqueue a deduplicated bounded retry, and return false from the Harmony prefix.
- [ ] Permit the original `finishCapture` transfer only when no active hostile war exists. Validated peace terms remain unaffected because they call `joinAnotherKingdom` with `pCaptured:false` outside `finishCapture`.
- [ ] Run the source guard and focused rules tests.

### Task 3: Verify Ownership Integrity

**Files:**
- Test: `Tests/WarScoreBudgetServiceTests.cs.txt`
- Test: `Tests/WarGoalSettlementRulesTests.cs.txt`

- [ ] Add a persistence test proving failed first write followed by retry produces one frozen-control record and no city owner mutation.
- [ ] Add a settlement test proving validated peace may transfer the frozen city exactly once.
- [ ] Run war-score, war-goal, civilian-protection, and RTS occupation slices.
- [ ] Build Debug and Release, deploy source, and verify the current save retains legal owner colors until peace while showing separate occupation control.

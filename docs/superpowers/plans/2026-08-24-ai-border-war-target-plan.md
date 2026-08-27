# AI Border War Target Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Restrict autonomous AI war selection to bordering targets whenever an eligible bordering target exists, while preserving remote fallback.

**Architecture:** Add a pure two-pass candidate filter in `WarStrategyCandidateRules`, expose neighbor facts to the legacy picker, and apply the filter in both asynchronous and legacy selection paths without changing war creation or player diplomacy.

**Tech Stack:** C#/.NET Framework 4.8, existing rule-test source guards, Harmony runtime patches.

---

### Task 1: Add pure border-preference rules and tests

**Files:**
- Modify: `Code/core/lineage/AsyncDiplomacyPlanModels.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/WarBorderTargetRulesTests.cs.txt`

- [x] Add `FilterByBorderPreference` that accepts evaluated candidates and a target-neighbor lookup, returns all neighboring candidates when at least one exists, otherwise all candidates.
- [x] Add tests for neighbor preference, remote fallback, special war candidates, and empty input.
- [x] Run the rules test harness/source guard and confirm the new cases pass. The full harness remains blocked by its pre-existing duplicate Compile entries.

### Task 2: Apply the filter to asynchronous planning

**Files:**
- Modify: `Code/core/lineage/AsyncDiplomacyPlanModels.cs`
- Modify: `Code/core/lineage/AsyncWarDecisionPlanner.cs`

- [x] Preserve target facts through ranking and apply the shared border filter before producing deterministic async plans.
- [x] Keep scores, salts, and tie-breaking unchanged after filtering.

### Task 3: Apply the filter to legacy AI selection

**Files:**
- Modify: `Code/core/lineage/WarDecisionAI.cs`

- [x] Build a target-id to `StrategyTargetFacts` map while constructing the existing legacy candidate list.
- [x] Rank through the shared rule and choose the first target from the filtered list.
- [x] Keep trace output aligned with the filtered candidate order.

### Task 4: Verify and commit

**Files:**
- Verify: `AncientWarfare3.csproj`

- [x] Run `dotnet build AncientWarfare3.csproj --no-restore` and require 0 warnings/errors.
- [x] Run the focused source/rule guards.
- [x] Review `git diff --check` and commit only the border-target feature files and tests.

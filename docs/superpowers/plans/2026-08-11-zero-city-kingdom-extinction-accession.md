# Zero-City Kingdom Extinction And Accession Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (or subagent-driven-development) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop zero-city kingdoms from entering an endless succession loop, repair a valid king-city capital without rejecting retained zero-population cities, and suppress only the known repetitive `invalid_capital` warning.

**Architecture:** Separate structural territory lifecycle from accession identity repair. A kingdom with no structurally owned cities is finalized for native `KingdomManager.checkDeadObjects()` removal; it never receives another managed king or deferred identity job. A kingdom with at least one owned city may use the current king's owned city as capital regardless of population, then fall back to another owned city. The existing `KingdomManager.removeObject` Harmony prefix remains the only destruction side-effect boundary.

**Tech Stack:** C# 9, Harmony, Unity/WorldBox APIs, existing console rule harness, PowerShell source guards.

---

## Root Cause Evidence

1. `Code/patch/AW_KingdomExtinctionPatch.cs` calls `KingdomExtinctionRules.ShouldForceImmediateRemoval(...)`, but when the rule is true it writes `__result = false` and returns `false`. The rule and integration behavior disagree: the test says a stable zero-city civilization must be removed, while the patch prevents the native removal check from returning ready.
2. `Code/core/lineage/KingdomExtinctionQueue.cs` waits for both `hasDirtyCities()==false` and `isUnitsDirty()==false`. The large-step scheduler can dirty unit indexes again while a kingdom is being processed, so a zero-city kingdom can be rescheduled indefinitely.
3. `Code/core/lineage/AccessionIdentityService.cs` retries a king installation even when the kingdom has no owned city. No candidate capital can exist in that state, so every retry ends with `reason=invalid_capital` and can produce repeated king-left/king-installed chronicle entries.
4. `TryRepairCapital` checks the successor's city and fallback cities through `IsValidCapitalCandidate`, which requires `City.isAlive()`. AW3 intentionally preserves zero-population cities; population/liveness must not be used as the structural capital gate.
5. The screenshots also show historical-school `EVENT_ID` uniqueness and clean-failure warnings. Those are a separate SQLite/async subsystem and are explicitly excluded from this succession/extinction change to keep verification causal.

## Implementation Tasks

### Task 1: Add failing pure-rule coverage

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/KingSuccessionPreparationRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Regression20260721Tests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ConsistencyRegressionRulesTests.cs.txt`
- Modify: `Code/core/lineage/AccessionIdentityRules.cs`
- Modify: `Code/core/lineage/KingdomExtinctionRules.cs`

- [ ] Add a capital rule that treats a non-rekt, owned city with zero population as structurally valid.
- [ ] Add an extinction rule assertion that a stable zero-city civilization is ready for native removal and a dirty index is not.
- [ ] Add a succession guard assertion that a managed installation cannot be queued when the kingdom has no owned city.
- [ ] Add a source assertion that `invalid_capital` is not emitted by the exhausted deferred-installation warning branch.
- [ ] Run the focused rules harness and verify the new integration assertions fail for the current `__result=false` and `isAlive()` behavior.

### Task 2: Make zero-city removal use the native destruction boundary

**Files:**
- Modify: `Code/patch/AW_KingdomExtinctionPatch.cs`
- Modify: `Code/core/lineage/KingdomExtinctionQueue.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] In `IsReadyForRemoval_Prefix`, only treat `countCities()` as authoritative after `hasDirtyCities()` is false.
- [ ] When a stable zero-city kingdom remains zero after `SuccessionDisputeService.OnZeroCityKingdom`, set `__result = true` and return `false`; do not call `makeSurvivorsToNomads` or `KingdomManager.removeObject` from the prefix.
- [ ] Preserve the existing `ShouldPreserveOriginalKingdom` branch only when the live city count is positive.
- [ ] Change `KingdomExtinctionQueue.Verify` to wait only for the city index, invoke the dispute settlement/recheck, and stop rescheduling on `isUnitsDirty()`. It must not create a second destruction path; the next native `checkDeadObjects()` pass owns removal.
- [ ] If the queue is no longer needed after the prefix change, remove its scheduling call and delete the file only after updating all source guards and project inclusion checks. Do not leave a dead queue that can retain kingdom ids.
- [ ] Add a source guard proving zero-city removal returns `__result = true`, preserves the destruction prefix, and never performs direct survivor conversion in the extinction patch/queue.

### Task 3: Prevent accession work for a cityless kingdom

**Files:**
- Modify: `Code/core/lineage/AccessionIdentityService.cs`
- Modify: `Code/patch/AW_HeirPatch.cs`
- Modify: `Code/core/lineage/KingdomExtinctionQueue.cs` if retained
- Modify: `Tests/AncientWarfare3.Rules.Tests/KingSuccessionPreparationRulesTests.cs.txt`

- [ ] Add a bounded structural-city predicate that reads the stable kingdom city index and returns false for a confirmed zero-city kingdom.
- [ ] At the start of `DeferInstalledKing` and `ProcessDeferredInstallations`, drop pending identity/completion state for a confirmed zero-city kingdom and do not log or retry it.
- [ ] In the managed `setKing` prefix, skip `Prepare`/defer for a confirmed zero-city kingdom; let the extinction check remove the realm instead of installing another king.
- [ ] Clear any pending accession state when `KingdomManager.removeObject` begins, so a removed kingdom cannot be referenced by a later deferred callback.
- [ ] Add a regression test for repeated `setKing` attempts on a zero-city kingdom producing no pending identity job and no chronicle retry token.

### Task 4: Repair capital from the current king's city

**Files:**
- Modify: `Code/core/lineage/AccessionIdentityService.cs`
- Modify: `Code/core/lineage/AccessionIdentityRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/KingSuccessionPreparationRulesTests.cs.txt`

- [ ] Change `IsValidCapitalCandidate` to require only city data, not rekt, and `city.kingdom == pKingdom`; do not require `City.isAlive()` or population.
- [ ] Keep candidate order: existing valid capital, current successor's owned city, then the highest-scoring owned city.
- [ ] Continue committing only through `pKingdom.setCapital(selected)` and revalidate ownership after the call.
- [ ] Add tests for a zero-population successor city, a foreign successor city, a destroyed city, and a valid fallback city.

### Task 5: Suppress the repetitive warning without hiding real failures

**Files:**
- Modify: `Code/core/lineage/AccessionIdentityService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/KingSuccessionPreparationRulesTests.cs.txt`

- [ ] Remove the exhausted retry warning only for `LastPrepareFailureReason == "invalid_capital"`; do not suppress `royal_guard_release`, `native_affiliation_transfer`, persistence, or completion-stage failures.
- [ ] Keep retry state bounded/coalesced so suppressing the message cannot create an unbounded queue.
- [ ] Add a source guard that the exact screenshot message is absent for `invalid_capital` and remains present for other failure reasons.

### Task 6: Verification and runtime acceptance

**Files:**
- No production files unless a guard requires an integration assertion.

- [ ] Run `dotnet run --project Tests\\AncientWarfare3.Rules.Tests\\AncientWarfare3.Rules.Tests.csproj -c Debug`; expect `Rule tests passed.`
- [ ] Run `powershell -ExecutionPolicy Bypass -File Tests\\SourceGuardTests.ps1`; expect all guards to pass.
- [ ] Run `dotnet build AncientWarfare3.csproj -c Debug` and `dotnet build AncientWarfare3.csproj -c Release`; expect zero warnings and errors.
- [ ] Run `git diff --check`.
- [ ] Deploy source files only, never a DLL, and start a fresh test world.
- [ ] Verify: a kingdom with one zero-population city keeps that city and promotes the current king using it as capital; a kingdom with no cities is removed once the city index is stable; no new king is installed into that cityless kingdom; no repeated `invalid_capital` warning appears; normal succession with a valid city still produces the native king transition and chronicle.
- [ ] Separately track the school `EVENT_ID`/clean-failure warnings for a later plan; do not use their disappearance as acceptance for this fix.

## Scope Boundary

This plan does not modify school SQLite event-id allocation, guest-office async retirement, localization assets, pathfinding, RTS scheduling, diplomacy, or historical-school warning policy. Those messages are visible in the supplied logs but have a different call chain and require independent reproduction/tests.


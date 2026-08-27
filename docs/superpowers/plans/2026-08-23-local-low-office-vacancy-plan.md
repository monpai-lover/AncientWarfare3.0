# Local Low-Office Vacancy Recruitment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill the lowest local offices promptly under examination governments by prioritizing qualified candidates, then eligible clan/shi members, then ordinary residents, while assigning a ninth-rank floor and preserving higher-office gates.

**Architecture:** Keep `LocalCourtAppointmentService` as the vacancy orchestrator. Add a small pure rules surface for candidate tier and rank-floor decisions, pass resolved office grade into the qualification gate, and retain the existing deferred queue as the retry transport. Candidate discovery remains bounded but becomes resumable within a reconcile request.

**Tech Stack:** C# source guards and isolated .NET rules tests; Unity/WorldBox runtime services; SQLite-backed civil-service and court persistence.

---

### Task 1: Add pure vacancy fallback rules and failing tests

**Files:**
- Create: `Code/core/court/LocalLowOfficeVacancyRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/LocalLowOfficeVacancyRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write failing tests** for: qualified tier precedence, clan-before-ordinary fallback, lowest-grade-only eligibility, and ninth-rank floor.
- [ ] **Step 2: Run the focused rules project** and verify the new tests fail because the rules type is absent.
- [ ] **Step 3: Implement the minimal pure rules API** (`IsLowestLocalGrade`, `CanUseUnqualifiedFallback`, `CandidateTier`, `ResolveEntryRank`) without Unity or database dependencies.
- [ ] **Step 4: Run the focused rules project** and verify the tests pass.
- [ ] **Step 5: Commit** the pure rules and tests.

### Task 2: Pass office grade through the qualification gate

**Files:**
- Modify: `Code/core/court/CivilServiceQualificationService.cs:77-151`
- Modify: `Code/core/court/OfficialCareerService.cs:83-103`
- Modify: `Code/core/court/OfficialCareerStateService.cs:1536-1642`
- Test: `Tests/CivilServiceCandidateSupplySourceGuard.ps1`

- [ ] **Step 1: Add a failing source/rules assertion** proving an unqualified candidate is accepted only for a vacant grade-30 city office with local fallback enabled.
- [ ] **Step 2: Run the assertion** and verify the current gate rejects the case.
- [ ] **Step 3: Compute the resolved office grade before qualification checks** and use `LocalLowOfficeVacancyRules.CanUseUnqualifiedFallback` to admit only the lowest local vacancy tier.
- [ ] **Step 4: Preserve formal qualification, legacy credential, central, military, feudatory, and non-vacancy behavior unchanged.**
- [ ] **Step 5: Run the source guard and focused rules tests** and verify they pass.
- [ ] **Step 6: Commit** the qualification-gate change.

### Task 3: Add clan/shi candidate tier and bounded resumable discovery

**Files:**
- Modify: `Code/core/court/LocalCourtAppointmentService.cs:13-305`
- Modify: `Code/core/lineage/DeferredRuntimeWorkService.cs` only if retry metadata needs persistence-free queue support
- Create: `Tests/AncientWarfare3.Rules.Tests/LocalCourtCandidateTierSourceGuardTests.cs.txt`

- [ ] **Step 1: Write failing source assertions** requiring waiting-pool candidates first, clan/shi detection before ordinary fallback, and no permanent `96`-actor cutoff.
- [ ] **Step 2: Run the source assertions** and verify they fail against the current implementation.
- [ ] **Step 3: Add a bounded scan cursor local to each reconcile state**, merge waiting-pool IDs first, then scan direct kingdom units across successive passes without duplicating actors.
- [ ] **Step 4: Add clan/shi detection** using the existing actor clan data and `LineageKeys.SHI_ID`; do not create new lineage state.
- [ ] **Step 5: Select candidates by tier before applying existing score and hometown tie-breakers.**
- [ ] **Step 6: Run source guards and all court rules tests** and verify they pass.
- [ ] **Step 7: Commit** candidate discovery and ranking changes.

### Task 4: Ensure ninth-rank assignment and persistent vacancy retry

**Files:**
- Modify: `Code/core/court/CityBureauAnnualWorkService.cs:47-75`
- Modify: `Code/core/court/LocalCourtAppointmentService.cs:86-115`
- Modify: `Code/core/court/OfficialCareerRankRules.cs:370-419` only if the existing local floor cannot represent grade 30
- Create: `Tests/AncientWarfare3.Rules.Tests/LocalCourtVacancyRetrySourceGuardTests.cs.txt`

- [ ] **Step 1: Write failing tests** showing an empty candidate page retains a coalesced retry and an unranked fallback appointment receives the minimum local rank.
- [ ] **Step 2: Run tests** and verify the current code clears the request after an empty but successful reconcile.
- [ ] **Step 3: Change `ProcessImmediate`/`ReconcileCity` outcome handling** so “no candidate” is distinguishable from “reconciled”; retain one coalesced persistent retry with bounded backoff.
- [ ] **Step 4: Route fallback appointments through the existing local vacancy-promotion rank resolver** and assert grade-30 candidates receive ninth rank without changing higher offices.
- [ ] **Step 5: Run focused rules, source guards, and compile checks**; verify no duplicate appointments are produced by retries.
- [ ] **Step 6: Commit** retry and rank-floor behavior.

### Task 5: Integration verification

**Files:**
- Test: `Tests/CivilServiceFocused.Tests/CivilServiceFocused.Tests.csproj`
- Test: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Run all local-court and civil-service source guards.**
- [ ] **Step 2: Run focused .NET rules tests.**
- [ ] **Step 3: Run the broader rules test project; document any pre-existing compile issue such as an omitted linked source file separately.**
- [ ] **Step 4: Inspect `git diff --check` and confirm only the planned files changed.**
- [ ] **Step 5: Commit the final verification changes if any.**

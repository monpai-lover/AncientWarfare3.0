# Western Court Candidate Eligibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure Western courts can appoint valid local adults when no educated or pre-existing noble candidates exist, while promoting only the successfully appointed actor to noble status.

**Architecture:** Add a pure profile-aware eligibility rule to the Western election rules and invoke it after the shared safety filters in `CourtService`. Preserve the existing post-persistence lineage admission path so promotion occurs only after appointment commit.

**Tech Stack:** C#, .NET rules test harness, PowerShell source guards, MSBuild

---

### Task 1: Candidate eligibility regression

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternCourtElectionRulesTests.cs.txt`
- Modify: `Code/core/court/WesternCourtElectionRules.cs`

- [ ] **Step 1: Write the failing rule assertions**

Add assertions proving a Western candidate who passed common validation does
not require historical-school education, while an Eastern candidate still
does and an otherwise-invalid Western candidate stays rejected.

- [ ] **Step 2: Run the focused rules project and verify RED**

Run the rules test project with the Western election test temporarily selected
and expect a compile failure because `CanUseLocalCandidate` does not exist.

- [ ] **Step 3: Implement the minimal pure rule**

Add `CanUseLocalCandidate(bool otherwiseEligible, bool westernProfile,
bool historicalSchoolEligible)` returning `otherwiseEligible &&
(westernProfile || historicalSchoolEligible)`.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the same rules command and expect all selected assertions to pass.

### Task 2: Runtime integration and post-commit guard

**Files:**
- Modify: `Code/core/court/CourtService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/WesternCourtCandidateEligibilitySourceGuard.ps1`

- [ ] **Step 1: Write a failing source guard**

Require `CourtService` to use `WesternCourtElectionRules.CanUseLocalCandidate`
at the shared candidate boundary and require the committed projection to keep
calling `LineageService.EnsureOfficialShiAndClan`.

- [ ] **Step 2: Run the guard and verify RED**

Run the new PowerShell guard and expect it to report the missing runtime rule
integration.

- [ ] **Step 3: Integrate the profile-aware rule**

After common candidate validation, detect `CourtProfileId.Western`; use the
pure rule to bypass `HistoricalSchoolEducationService.CanAppoint` only for that
profile. Do not alter `CivilServiceQualificationService` or other profiles.

- [ ] **Step 4: Run the guard and verify GREEN**

Run the new guard and expect its pass message.

### Task 3: Full verification and delivery

**Files:**
- Verify all files above

- [ ] **Step 1: Run Western court source guards**

Run all `WesternCourt*.ps1` and the new candidate guard; expect exit code 0.

- [ ] **Step 2: Run the complete rules suite**

Run the repository's complete rules test command; expect zero failures.

- [ ] **Step 3: Build Release**

Run the main Release build; expect zero errors.

- [ ] **Step 4: Review and commit scoped changes**

Inspect the diff, commit the Western candidate fix without overwriting existing
worktree changes, then push the current branch.

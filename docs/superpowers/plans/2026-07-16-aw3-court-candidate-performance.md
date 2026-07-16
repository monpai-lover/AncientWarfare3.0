# Court Candidate Performance and Civilianization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exclude minors, keep every eligible candidate selectable without a thousand-actor frame spike, and demobilize civil central officials only after appointment commit.

**Architecture:** A pure rules layer defines eligibility, paging, frame limits, and military-office classification. `CourtService` exposes an ID snapshot plus one-actor candidate projection; `CourtAppointmentWindow` owns a generation-scoped incremental scan and paged renderer. A focused military transition service performs actor-local cleanup after the durable career mutation commits.

**Tech Stack:** C# 10, .NET Framework 4.8, Unity UI, NeoModLoader list windows, Harmony-integrated WorldBox runtime, SQLite rule tests, PowerShell source guards.

---

### Task 1: Lock the behavior with failing tests

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add rule assertions for adulthood, office classification, and paging**

Add assertions equivalent to:

```csharp
False(CourtManualAppointmentRules.CanListCandidate(
    CourtFacts(adult: false)), "minor cannot be manually appointed");
True(CourtManualAppointmentRules.ShouldReleaseMilitaryIdentity(
    CourtOfficeLayer.Central, CourtOfficeId.Chancellor),
    "civil central office releases military identity");
False(CourtManualAppointmentRules.ShouldReleaseMilitaryIdentity(
    CourtOfficeLayer.Central, CourtOfficeId.Marshal),
    "marshal remains military");
Equal(21, CourtManualAppointmentRules.PageCount(1000),
    "one thousand candidates use bounded pages");
```

- [ ] **Step 2: Add source guards for incremental scanning and post-commit cleanup**

Require the window to use `CandidateScanPerFrame`, `CandidateRowsPerFrame`, and a
page-size bound, and reject the old synchronous
`GetManualAppointmentCandidates(...)` call. Assert that
`ReleaseMilitaryIdentityAfterCommit(...)` occurs only after
`careerResult.IsCommitted` has been checked.

- [ ] **Step 3: Run tests and verify RED**

Run:

```powershell
dotnet run --project .\Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -c Release
& .\Tests\SourceGuardTests.ps1
```

Expected: rule compilation or source-guard failure naming the missing adult,
paging, incremental-scan, and cleanup behavior.

### Task 2: Add pure appointment rules

**Files:**
- Modify: `Code/core/court/CourtManualAppointmentRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add adulthood and bounded-query rules**

Add `Adult` to `CourtManualCandidateFacts`; require it from `CanListCandidate`.
Define `CandidateScanPerFrame = 32`, `CandidateRowsPerFrame = 4`,
`CandidatePageSize = 48`, `CandidateFrameBudgetMilliseconds = 1`, and a safe
`PageCount(int)` implementation.

- [ ] **Step 2: Add military-office classification**

Implement:

```csharp
public static bool IsMilitaryCentralOffice(string officeId) =>
    officeId == CourtOfficeId.Marshal || officeId == CourtOfficeId.Bingbu;

public static bool ShouldReleaseMilitaryIdentity(string layer, string officeId) =>
    layer == CourtOfficeLayer.Central && !IsMilitaryCentralOffice(officeId);
```

- [ ] **Step 3: Run the rule tests and verify the pure rules are GREEN**

Run the Release rule-test command and expect `Rule tests passed.`

### Task 3: Replace synchronous candidate construction

**Files:**
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/ui/windows/CourtAppointmentWindow.cs`
- Modify: `Code/ui/items/CourtAppointmentCandidateListItem.cs`
- Modify: `Locales/aw3_court.csv`

- [ ] **Step 1: Split candidate snapshot and one-actor projection**

Replace the synchronous full-view method with a scan context containing the frozen
kingdom, office, incumbent, heir, preferred school, and actor-ID snapshot. Add a
method that projects one actor ID only after current eligibility checks.

- [ ] **Step 2: Add a generation-scoped frame scanner**

On refresh, show a localized loading row and reset the scan cursor. In `Update`,
process at most 32 actors and 1 ms, sort once at completion, and reset rendering to
page zero.

- [ ] **Step 3: Add bounded page rendering**

Render at most four rows per frame and at most 48 candidates per page. Add localized
previous/next rows and a page indicator. Page changes reuse the completed scan and
never rescan the kingdom.

- [ ] **Step 4: Run rule and source-guard tests**

Expect both commands to pass.

### Task 4: Demobilize committed civil officials

**Files:**
- Create: `Code/core/court/CourtOfficerMilitaryTransitionService.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/lineage/GeneralService.cs`
- Modify: `Code/core/lineage/MandateBorderDefenseService.cs`

- [ ] **Step 1: Expose actor-local role cleanup**

Add `GeneralService.RetireForCivilOffice(actor)` and
`MandateBorderDefenseService.ReleaseBorderGuard(actor)`. Neither method scans a
kingdom or world collection.

- [ ] **Step 2: Implement post-commit military transition**

For rules-approved civil appointments, dismiss guard/general state, invalidate the
temporary levy, clear border guard, call `stopBeingWarrior()`, and remove an empty
special army. Guard every operation against a dead or missing actor.

- [ ] **Step 3: Call transition from committed runtime projection**

Invoke the transition only inside `ApplyCommittedOfficerProjection` after checking
`careerResult.IsCommitted`, so failed persistence cannot demobilize a candidate.

- [ ] **Step 4: Run rule and source-guard tests**

Expect `Rule tests passed.` and `Source guards passed.`

### Task 5: Verify, commit, and push

**Files:**
- Verify all modified production, localization, test, and documentation files.

- [ ] **Step 1: Rebuild both configurations**

```powershell
dotnet build AncientWarfare3.csproj -c Debug --no-restore -t:Rebuild
dotnet build AncientWarfare3.csproj -c Release --no-restore -t:Rebuild
```

Expected: zero warnings and zero errors in both configurations.

- [ ] **Step 2: Check the final diff**

```powershell
git diff --check
git status --short --branch
```

Expected: no whitespace errors and only the intended work remains.

- [ ] **Step 3: Commit coherent slices and push**

Commit the remaining war/runtime work, manual appointment implementation, and test
coverage separately, then run the tests once more and push `master` to
`origin/master`.

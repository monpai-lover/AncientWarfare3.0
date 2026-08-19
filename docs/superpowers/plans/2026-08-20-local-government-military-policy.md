# Military Prefecture And Court Template Application Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add military-prefecture candidates for military governorate policy and transactional officer migration when applying local or central court templates.

**Architecture:** Add a runtime-only candidate rules/service layer around existing `CustomCourtRuntime` and `MilitaryGovernorateCreationService`. Add one bounded court-template migration transaction that snapshots active formal appointments, resolves target offices by ID/mapping/qualification, and rolls back on structural failure. Existing policy, court, history, and noble-identity boundaries remain authoritative.

**Tech Stack:** C#/.NET Framework 4.8.1, Unity UI, existing policy services, Newtonsoft.Json, focused console rules tests, PowerShell source guards.

---

### Task 1: Military-prefecture candidate rules

**Files:**
- Create: `Code/core/court/MilitaryPrefectureCandidateRules.cs`
- Create: `Code/core/court/MilitaryPrefectureCandidateService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/MilitaryPrefectureCandidateRulesTests.cs.txt`
- Test: `Tests/MilitaryPrefectureCandidateSourceGuard.ps1`

- [x] Write failing tests for military template selection, valid city ownership, deterministic city ordering, exclusion of civil/custom templates, and no-candidate behavior.
- [x] Run `dotnet run ... --military-prefecture-candidates` and verify the missing-rule failure.
- [x] Implement pure predicates and a kingdom-scoped runtime index with invalidation hooks; do not enumerate `World.world.cities`.
- [x] Add the focused program switch and source guard requiring reuse of `CustomCourtRuntime.TryGetLocalTemplate` and no full-world scan.
- [x] Re-run tests/guard and commit `feat: index military prefecture candidates`.

### Task 2: Wire candidates into military governorate policy

**Files:**
- Modify: `Code/core/lineage/MilitaryGovernorateCreationService.cs`
- Modify: `Code/content/GodPowerLibrary.cs`
- Modify: `Code/ui/windows/MilitaryGovernorateWindow.cs`
- Modify: existing kingdom policy definition/service files identified by `MilitaryGovernorateCreationService`
- Test: `Tests/AncientWarfare3.Rules.Tests/MilitaryGovernorateCandidateRulesTests.cs.txt`
- Test: `Tests/MilitaryGovernorateCandidateSourceGuard.ps1`

- [x] Add failing tests proving a military-prefecture city can be selected as a seat, a civil/custom city cannot, and successful creation consumes the candidate.
- [x] Route seat validation and candidate display through the new service while preserving existing general selection and creation persistence.
- [x] Add runtime-safe candidate resolution after local template/ownership changes; no stale global cache is retained.
- [x] Ensure errors use existing localized policy messages.
- [x] Run focused tests and guard.

### Task 3: Local template source list and migration rules

**Files:**
- Create: `Code/core/court/CourtTemplateOfficerMigrationRules.cs`
- Create: `Code/core/court/CourtTemplateOfficerMigrationService.cs`
- Modify: `Code/ui/windows/CourtWindow.cs`
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CourtTemplateOfficerMigrationRulesTests.cs.txt`
- Test: `Tests/CourtTemplateOfficerMigrationSourceGuard.ps1`

- [x] Add pure tests for office-ID match priority, compatible-office fallback, and duplicate prevention.
- [x] Add a bounded migration service that snapshots active formal appointments and rebinds eligible officers without changing window dimensions.
- [x] Reuse the existing local template source list, which already includes built-ins and imported/saved templates.
- [x] Preserve unmatched officials by leaving them in the existing candidate/history path.
- [x] Run focused rules verification.

### Task 4: Central template application migration

**Files:**
- Modify: `Code/core/court/CustomCourtRuntime.cs`
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`
- Modify: `Code/core/court/CourtService.cs`
- Reuse: `Code/core/court/CourtTemplateOfficerMigrationService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CourtTemplateOfficerMigrationRulesTests.cs.txt`
- Test: `Tests/CentralCourtTemplateMigrationSourceGuard.ps1`

- [x] Add pure tests for central office-ID match, compatible renamed-office mapping, and duplicate prevention.
- [x] Route central template apply through migration before publishing the new snapshot.
- [x] Keep regional layer and local-document separation intact and retain existing cache invalidation.
- [x] Preserve unmatched/under-qualified officers in the existing candidate path.
- [x] Run focused rules/guards.

### Task 5: Full verification and integration

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Locales/aw3_court.csv`
- Modify: `Locales/aw3_military_governorate.csv` if required

- [x] Run the new focused slices serially.
- [x] Run `git diff --check` and Release build with zero warnings/errors.
- [ ] Merge the feature branch into `master` without touching the unrelated dirty plan file.
- [ ] Deploy with `deploy-local.ps1`, verify backup and SHA-256 source deployment, launch WorldBox, and inspect process/runtime logs.
- [ ] Commit any verified fixes and report unautomated UI smoke-test limits explicitly.

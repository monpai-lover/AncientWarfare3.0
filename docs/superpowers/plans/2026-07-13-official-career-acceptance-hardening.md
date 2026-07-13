# Official Career Acceptance Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close every code-level official-career lifecycle blocker before WorldBox functional acceptance.

**Architecture:** Each multi-table lifecycle operation has one SQLite transaction owner and strict `Committed/CleanFailure/Unknown` readback. Durable state commits before live Actor projection; vanilla city leadership remains authoritative and its derived career projection converges through a bounded idempotent retry queue.

**Tech Stack:** C# 11, .NET Framework 4.8, System.Data.SQLite 1.0.99.0 / SQLite 3.9.2, Harmony, external focused rule harnesses under `F:/tmp`.

---

### Task 1: Guest appointment atomic start

**Files:**
- Create: `Code/core/schools/GuestOfficePersistence.cs`
- Create: `Code/core/schools/GuestOfficePersistenceRules.cs`
- Modify: `Code/core/court/OfficialCareerPersistence.cs`
- Modify: `Code/core/court/OfficialCareerService.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/schools/HistoricalAffiliationService.cs`
- Modify: `Code/core/schools/HistoricalSchoolStore.cs`
- Modify: `Code/core/schools/SchoolGuestOfficeService.cs`
- Modify: `Code/core/db/SchoolEventTableItem.cs`
- Modify: `Code/core/db/LineageArchiveIndexRules.cs`
- Test: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`
- Test: `F:/tmp/AW3HistoricalSchoolRuleTests/GuestSqliteIntegration/Program.cs`

- [ ] Add source-contract and pure-rule assertions for exact three-table outcomes, stable culture-invariant operation keys, no compensation on `Unknown`, single frozen time, and bounded recovery; run the Historical harness and confirm the expected RED assertions.
- [ ] Expose career `Capture`, `Stage`, `Readback`, and `ResultFor` seams that accept the caller's transaction while retaining the existing self-owned `Appoint` wrapper.
- [ ] Implement `GuestOfficePersistence.Start` as the sole transaction owner: 17-column affiliation CAS, staged career, keyed guest event, exact affected-row checks, commit, and strict exception readback.
- [ ] Replace `TryBeginService -> Appoint -> RecordSchoolEvent -> EndGuestOfficer` compensation with one prepared request and commit-only adoption in this order: affiliation snapshot, court projection, status, supplemental history.
- [ ] Make annual recovery adopt only a complete committed tuple, retry query/mixed failures, and end only proven clean absence.
- [ ] Build the net48/x64 SQLite harness against `Assemblies/System.Data.SQLite.dll`; verify success, event trigger rollback, central unique rollback, stale CAS, replay, and unrelated-row preservation.
- [ ] Run the Historical harness and Debug build; expect all guest contracts and compilation to pass, then commit the guest-start slice.

### Task 2: Guest appointment atomic end

**Files:**
- Create: `Code/core/schools/GuestOfficeEndPersistence.cs`
- Modify: `Code/core/schools/GuestOfficePersistenceRules.cs`
- Modify: `Code/core/court/OfficialCareerAppointmentResult.cs`
- Modify: `Code/core/court/OfficialCareerPersistence.cs`
- Modify: `Code/core/court/OfficialCareerService.cs`
- Modify: `Code/core/schools/HistoricalAffiliationService.cs`
- Modify: `Code/core/schools/SchoolGuestOfficeService.cs`
- Modify: `Code/core/court/CourtService.cs`
- Test: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`
- Test: `F:/tmp/AW3HistoricalSchoolRuleTests/GuestSqliteIntegration/Program.cs`

- [ ] Add failing cases for affiliation-close failure after career-close staging, career-close failure after affiliation staging, ambiguous commit, replay, dead/missing Actor, and host loss; confirm RED.
- [ ] Add reusable career-close `Capture`, `Stage`, `Readback`, and `ResultFor` seams with exact affected-row checks, while preserving a self-owned wrapper for callers that do not participate in a larger transaction.
- [ ] Implement one transaction that freezes the serving affiliation and exact active central career, stages both closures, commits, and performs strict two-table readback.
- [ ] Change renewal to close and reopen only through frozen operations; an uncertain close remains pending and cannot start a second term.
- [ ] Move `ClearOfficer`, guest status removal, history, and cache invalidation after `Committed`; missing live Actor closes by ID without live work.
- [ ] Add bounded recovery for committed durable close with stale live projection, run SQLite failure injection and the Historical harness, then commit the guest-end slice.

### Task 3: General career atomic lifecycle

**Files:**
- Create: `Code/core/lineage/GeneralCareerPersistence.cs`
- Create: `Code/core/lineage/GeneralCareerPersistenceRules.cs`
- Modify: `Code/core/lineage/GeneralService.cs`
- Modify: `Code/core/court/OfficialCareerPersistence.cs`
- Modify: `Code/core/db/LineageArchiveIndexRules.cs`
- Test: `F:/tmp/AW3CourtExpansionRuleTests/Program.cs`
- Test: `F:/tmp/AW3HistoricalSchoolRuleTests/GuestSqliteIntegration/Program.cs`

- [ ] Add failing start/end tests proving `GeneralState.ACTIVE` and military `CourtOfficer` cannot diverge, `Unknown` does not touch live flags, and replay creates one tenure.
- [ ] Implement a transaction owner that stages exact GeneralState and career mutations, validates affected rows, and resolves strict three-state readback.
- [ ] Change appointment to commit before `GENERAL_ACTIVE`, trait, official Shi/clan, school projection and history; change dismissal/rebellion to commit before clearing them.
- [ ] Add bounded startup/annual reconciliation for complete durable tuples and mixed-state retry; do not scan all world actors.
- [ ] Run focused rules, SQLite integration, Debug build and commit the general lifecycle slice.

### Task 4: City leader derived career convergence

**Files:**
- Create: `Code/core/court/CityLeaderCareerProjectionService.cs`
- Create: `Code/core/court/CityLeaderCareerProjectionRules.cs`
- Modify: `Code/patch/AW_PromotionPatch.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Test: `F:/tmp/AW3CourtExpansionRuleTests/Program.cs`

- [ ] Add failing tests for replacement, removal, repeated Harmony callbacks, career write failure, restart, and a leader changing again before retry; confirm RED.
- [ ] Record a stable desired projection from the post-vanilla city/leader state and enqueue it by city ID; never revert `City.setLeader/removeLeader`.
- [ ] Process a bounded deduplicated queue, re-read current vanilla authority before each attempt, close stale city tenure and open the current tenure idempotently, and retain `Unknown` for retry.
- [ ] Rebuild pending projections from live cities after load without creating duplicate appointment history, then run focused rules and commit the city projection slice.

### Task 5: Career biography and school feature static acceptance

**Files:**
- Verify: `Code/ui/windows/HistoryListWindow.cs`
- Verify: `Code/core/court/OfficialCareerService.cs`
- Verify: `Code/ui/windows/SchoolRosterWindow.cs`
- Verify: `Code/core/schools/HistoricalSchoolDescentService.cs`
- Verify: `Code/core/schools/HistoricalSchoolRuntime.cs`
- Test: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`
- Test: `F:/tmp/AW3HistoricalSchoolRuleTests/SpawnHarmonyIntegration/Program.cs`

- [ ] Assert the biography `career` filter reads durable tenures and displays active/ended records, kingdom/city archive fallback and localized end reasons.
- [ ] Assert the school roster entry, complete-member hierarchy, live portraits, standings and links are registered without whole-world UI scans.
- [ ] Assert descent hooks run after final baby identity, preserve failed source Actors, and isolate annual stage failures so one exception cannot freeze later years.
- [ ] Run Historical and Spawn Harmony harnesses; fix only proven static acceptance blockers and commit if code changes are required.

### Task 6: Full verification, review, deployment and push

**Files:**
- Verify: entire tracked repository
- Preserve: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0/.runtime/`

- [ ] Run Historical, Spawn Harmony, court expansion, correctness and pathfinding harnesses; every command must exit zero.
- [ ] Rebuild Debug and Release to `F:/tmp`; require zero errors and inspect warnings.
- [ ] Run `git diff --check`, locale-column validation, generated `bin/obj` audit and source searches for the removed guest compensation/live-first paths.
- [ ] Perform specification review followed by code-quality review; fix and re-run both reviews until no Critical or Important finding remains.
- [ ] Sync the tracked F-drive tree to the loaded D-drive mod while preserving `.runtime/`, then verify representative hashes and deployed source presence.
- [ ] Commit all reviewed slices, ensure `master` is clean and ahead only by intended commits, and push `master` to `origin`.
- [ ] Hand off a functional acceptance checklist covering ten Xia years, historical master descent, school roster/map/UI, career biography, save/load, government/heir, alliance/occupation, mandate tooltip, and absence of NRE/performance storms.

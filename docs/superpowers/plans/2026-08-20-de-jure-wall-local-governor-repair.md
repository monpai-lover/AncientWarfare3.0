# De Jure Map, Mandate Wall, And Local Governor Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task with checkpoints.

**Goal:** Repair the de jure map interaction, space mandate watchtowers along frontier walls, and keep local governor records synchronized with city leaders while removing royal-clan priority.

**Architecture:** Keep persisted de jure and wall stores unchanged. Add pure rule helpers for map refresh, tower spacing, and candidate scoring; wire runtime services to those helpers. Make the city leader the single source of truth for the root local office and the de jure seat governor.

**Tech Stack:** C#/.NET 9 rule-test harness, Unity WorldBox runtime, Harmony patches, SQLite-backed court persistence.

---

### Task 1: Add regression tests for de jure map ownership and refresh

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionPowerRulesTests.cs.txt`
- Create: `Code/core/court/DeJureRegionPowerRules.cs`

- [ ] Add pure rules: `ForcedMetaType()` returns `AWMapModeMetaTypes.HierarchicalVassal`; `ShouldRefreshAfterMutation(success, mapActive)` returns true only for successful active mutations; `InteractionLayer()` returns `HierarchicalVassalMapModeLayer.Cities` and `CityAdministrationMapLevel.Regions`.
- [ ] Add tests for all true/false combinations and include the new test source/production link in the test project and program dispatcher.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests -- --city-administration-mapmode` and the new de jure argument; confirm the new tests fail before runtime wiring.
- [ ] Commit only the new rules/tests with `test: cover de jure map interaction contract`.

### Task 2: Wire de jure powers to hierarchical map mode and refresh

**Files:**
- Modify: `Code/content/GodPowerLibrary.cs:1065-1090`
- Modify: `Code/core/court/DeJureRegionPowerService.cs:20-72`
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs:161-195,1151-1165`
- Test: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionPowerRulesTests.cs.txt`

- [ ] Set both de jure powers' `force_map_mode` to `AWMapModePowerRules.ResolveForcedMapModeForLayerPower()` only after extending that helper with a `ResolveForcedMapModeForPower(string)` overload that returns `AWMapModeMetaTypes.HierarchicalVassal` for the two de jure ids.
- [ ] Change `PrepareForDeJureInteraction` to set the custom asset active, select `Cities`, reset administration state, push the clicked representative kingdom, and force region level without changing persisted data.
- [ ] Add `RefreshAfterDeJureMutation` that invalidates aggregation/native caches, marks hierarchy dirty, requests labels, and requests one native redraw. Call it after create and assign success, including target-seat selection when the map is active.
- [ ] Preserve transient target reset when a power is unselected; never clear `DeJureRegionStore` from the button reset.
- [ ] Run the de jure rule tests and `dotnet build AncientWarfare3.0.sln` or the repository build command; commit as `fix: keep de jure powers on hierarchical region map`.

### Task 3: Add frontier tower spacing rules and tests

**Files:**
- Create: `Code/core/lineage/MandateBorderTowerSpacingRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/MandateBorderTowerSpacingRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] Implement deterministic pure helpers: `SelectSpaced(points, existing, interval)` walks each ordered connected component, keeps the first valid point, suppresses points with Manhattan distance below 10 from the last selected/existing footprint, and never selects reserved points; `SafetyBudget(wallLength)` returns a bounded count based on wall length.
- [ ] Test 10-tile spacing, existing tower suppression, disconnected components, empty input, and reserved point exclusion.
- [ ] Run the new tower-spacing argument and verify it fails before runtime integration; commit as `test: cover mandate frontier tower spacing`.

### Task 4: Integrate spaced towers with mandate wall manifests

**Files:**
- Modify: `Code/core/lineage/MandateBorderDefenseService.cs:14-20,450-535`
- Modify: `Code/core/lineage/MandateBorderWallRefreshService.cs:130-175,221-250`
- Modify: `Code/core/lineage/MandateBorderWallRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/MandateBorderWallRefreshRulesTests.cs.txt`

- [ ] Replace fixed yearly/war tower caps with a wall-length safety budget; retain a hard runtime ceiling to prevent runaway building attempts.
- [ ] Plan frontier wall points first, derive ordered frontier candidates, select approximately one tower every 10 tiles, and attempt construction at selected candidates with nearby fallback search.
- [ ] Collect complete footprints of existing and newly built watchtowers before wall placement, pass them as reserved passages to `TryPlanFrontier`, and ensure the final manifest contains wall points only.
- [ ] Keep towers during wall refresh and preserve race-specific architecture lookup/fallbacks.
- [ ] Add source assertions for spacing interval, reserved footprint plumbing, and removal of old independent caps; run wall and tower tests; commit as `fix: space mandate towers along frontier walls`.

### Task 5: Add authoritative local governor and clan-diversity rules/tests

**Files:**
- Create: `Code/core/court/LocalGovernorIdentityRules.cs`
- Create: `Code/core/court/CityLeaderCandidateScoringRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/LocalGovernorIdentityRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/CityLeaderCandidateScoringRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] Define pure identity rules: root local office matches only the live city leader; seat governor actor is the seat city's live leader only when the seat is controlled; mismatches are stale.
- [ ] Define candidate score as ability/merit/qualification score minus `ClanConcentrationPenalty * currentLeaderCount`, with no royal-clan bonus and no clan requirement.
- [ ] Test mismatch rejection, seat ownership, no royal priority, increasing clan penalty, and clanless candidate eligibility.
- [ ] Run the new arguments and commit as `test: cover authoritative local governors and candidate diversity`.

### Task 6: Synchronize court read/appointment/runtime paths

**Files:**
- Modify: `Code/core/court/LocalCourtAppointmentService.cs:20-110`
- Modify: `Code/core/court/CourtReadModelService.cs:90-125,180-225,280-320`
- Modify: `Code/core/court/DeJureRegionReadModelService.cs:25-65`
- Modify: `Code/patch/AW_CityLeaderPatch.cs:110-225`
- Modify: `Code/core/court/OfficialCareerStateService.cs:650-710,900-980`

- [ ] During local reconciliation, detect a root-office row whose actor is not `pCity.leader`, end it, and assign the root office to the live leader before filling subordinate seats.
- [ ] Make `BuildLocal` always select the live city leader as `LeaderNode` and use the same actor for root-office projection; do not display stale root rows.
- [ ] In de jure aggregation, set `GovernorActorId` from the legal seat's live leader only when that seat is controlled by the queried kingdom; retain a separate visual anchor if needed.
- [ ] Remove royal/other/common candidate buckets in `TryGetRealmLeader`; build one candidate list and call the new scoring rule with current clan-leader counts. Preserve eligibility, examination, military, and retry guards.
- [ ] Allow expired city-leader terms to select a fresh candidate; if none exists, extend the incumbent's term by the existing retry interval. Keep multi-city transfer as a fallback.
- [ ] Ensure leader replacement, root appointment, career state, regional projection, court layout, and label invalidation commit in one guarded operation; on persistence failure restore the previous pointer.
- [ ] Run all court and city-administration rule arguments plus a full build; commit as `fix: synchronize city leaders with local governors`.

### Task 7: Verify, deploy, and report

**Files:**
- Modify only generated build/deployment outputs if required by existing scripts.

- [ ] Run the complete rule suite with `dotnet run --project Tests/AncientWarfare3.Rules.Tests` and capture failures.
- [ ] Build the mod using the repository's existing build command and run `deploy-local.ps1` targeting `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`.
- [ ] Verify in runtime: de jure power opens hierarchical region colors, assignment refreshes immediately, towers appear about every 10 wall tiles with tower footprints open, and each local governor card matches `city.leader`; verify seat city leader is also regional governor.
- [ ] Inspect `git diff` and `git status` to ensure unrelated pre-existing worktree edits remain untouched. Commit any deployment metadata separately only if the repository script requires it.
- [ ] Report changed commits, test commands/results, deployment path, and any runtime-only limitation.

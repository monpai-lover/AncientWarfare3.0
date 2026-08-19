# Three Approved Systems Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the approved Xia-only historical-school access, war-refugee migration, and Shi-lineage mapmode designs as three independently testable feature slices.

**Architecture:** The school slice adds one pure access policy plus one runtime adapter and enforces it at every existing school-domain boundary. The refugee slice owns a bounded monthly state machine with durable actor/journey state and one physical leader route per household. The Shi mapmode slice builds bounded live-city snapshots from existing resident and lineage data, then reuses the school mapmode presentation and selection patterns without introducing a second genealogy UI.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, Unity UI, WorldBox publicized API, System.Data.SQLite, existing AW3 authority-cycle and mapmode infrastructure, net9.0 pure-rule test harness.

---

## File Structure

### Shared test baseline

- Modify `Tests/AncientWarfare3.Rules.Tests/RuntimeRegressionSourceGuardTests.cs.txt`: normalize CRLF/LF before source assertions.

### Historical-school Xia access

- Create `Code/core/schools/HistoricalSchoolXiaAccessRules.cs`: pure academy, travel and lecture access decisions.
- Create `Code/core/schools/HistoricalSchoolXiaAccessService.cs`: authoritative live-city adapter over `LineageService` and `XiaizationService`.
- Modify academy construction/repair, travel/education, lecture queue/action and venue-provider services at their entry and commit boundaries.
- Create `Tests/AncientWarfare3.Rules.Tests/HistoricalSchoolXiaAccessRulesTests.cs.txt` and register it in the rules test project/program.
- Create `Tests/AncientWarfare3.Rules.Tests/HistoricalSchoolXiaAccessSourceGuardTests.cs.txt` for domain-boundary coverage.

### War refugees

- Create `Code/core/lineage/WarRefugeeRules.cs`: pure threat, quota, eligibility, destination, acceptance, travel, return and assimilation decisions.
- Create `Code/core/lineage/WarRefugeeModels.cs`: journey states, threat/destination facts and durable snapshots.
- Create `Code/core/lineage/WarRefugeePersistence.cs`: SQLite schema and idempotent journey/origin writes.
- Create `Code/core/lineage/WarRefugeeThreatService.cs`: one bounded monthly city-threat snapshot.
- Create `Code/core/lineage/WarRefugeeJourneyService.cs`: household ownership, leader route, follower cohesion, abstract travel, arrival, return and settlement.
- Create `Code/core/lineage/WarRefugeeService.cs`: authority-cycle coordinator and bounded monthly city/household cursors.
- Create `Code/patch/AW_WarRefugeePatch.cs`: lifecycle invalidation, birth-culture and movement-task hooks.
- Modify `Code/core/performance/AWAuthorityCycleService.cs` and restore/reset plumbing.
- Add pure-rule and source/integration tests to the rules test project.

### Shi lineage mapmode

- Create `Code/core/lineage/CityShiInfluenceRules.cs`: exclusive role weighting, stable tie-breaking and percentages.
- Create `Code/core/lineage/CityShiInfluenceSnapshotService.cs`: bounded dirty/demand queues and live resident rebuilds.
- Create `Code/core/policy/ShiLineageMapModeRules.cs`: deterministic colors and focus-share blending.
- Create `Code/core/policy/ShiLineageMapModeService.cs`: activation, focus, tooltip, map dirty throttling and city selection.
- Create `Code/core/policy/ShiLineageMapBottomBarController.cs` and `Code/ui/items/ShiLineageCompositionElement.cs`: selected-city branch rows and genealogy commands.
- Modify map power/meta registration, tooltip, meta-type selection, lineage tab and deferred frame processing.
- Add pure-rule and source/integration tests to the rules test project.

---

### Task 1: Restore a portable clean-test baseline

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/RuntimeRegressionSourceGuardTests.cs.txt`

- [ ] **Step 1: Reproduce the failing clean-worktree test**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: FAIL at the persisted historical-master source assertion when checkout line endings are CRLF.

- [ ] **Step 2: Normalize source text in the test helper**

Change `Read` to return:

```csharp
return File.ReadAllText(path).Replace("\r\n", "\n");
```

- [ ] **Step 3: Re-run the full rules baseline**

Expected: `Rule tests passed.`

- [ ] **Step 4: Commit**

```powershell
git add -- Tests/AncientWarfare3.Rules.Tests/RuntimeRegressionSourceGuardTests.cs.txt
git commit -m "test: normalize runtime source guard line endings"
```

### Task 2: Add the authoritative school Xia-access rule

**Files:**
- Create: `Code/core/schools/HistoricalSchoolXiaAccessRules.cs`
- Create: `Code/core/schools/HistoricalSchoolXiaAccessService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/HistoricalSchoolXiaAccessRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing rule tests**

Cover valid native Xia, fully Xiaized foreign, partial foreign, dead city and dead owner for all three named methods:

```csharp
True(HistoricalSchoolXiaAccessRules.CanHostAcademy(true, true, false));
True(HistoricalSchoolXiaAccessRules.CanReceiveSchoolTravel(true, false, true));
False(HistoricalSchoolXiaAccessRules.CanHostLecture(true, false, false));
False(HistoricalSchoolXiaAccessRules.CanHostAcademy(false, true, true));
```

- [ ] **Step 2: Run the targeted test and verify RED**

Run with `--historical-school-xia-access`; expected compile failure because the rule does not exist.

- [ ] **Step 3: Implement the pure rule and live adapter**

The rule receives `cityValid`, `nativeXiaOwner`, and `fullyXiaizedCity`; all named decisions return their shared conjunction. The service validates city/owner lifecycle and resolves native/full Xia status once.

- [ ] **Step 4: Run targeted and full rule tests**

Expected: both pass.

### Task 3: Enforce Xia access across academies, travel and lectures

**Files:**
- Modify: `Code/core/schools/HistoricalSchoolAcademyConstructionService.cs`
- Modify: `Code/core/schools/HistoricalSchoolAcademyRepairService.cs`
- Modify: `Code/core/schools/HistoricalSchoolTravelService.cs`
- Modify: `Code/core/schools/HistoricalSchoolEducationJourneyService.cs`
- Modify: `Code/core/schools/HistoricalSchoolActionService.cs`
- Modify: `Code/core/schools/HistoricalSchoolActivityQueue.cs`
- Modify: `Code/core/schools/HistoricalSchoolVenueProvider.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/HistoricalSchoolXiaAccessSourceGuardTests.cs.txt`

- [ ] **Step 1: Write a failing source guard**

Assert construction checks happen before tile/zone work, travel checks exist at candidate/preparation/arrival boundaries, and lecture checks exist at planning/enqueue/prepare/commit plus venue resolution.

- [ ] **Step 2: Verify RED**

Expected: missing `HistoricalSchoolXiaAccessService` calls.

- [ ] **Step 3: Add fail-closed boundary checks**

Use `CanHostAcademy`, `CanReceiveSchoolTravel`, and `CanHostLecture`; route invalid in-flight journeys through current cancellation/recovery, and finish invalid lectures through existing idempotent cancellation.

- [ ] **Step 4: Verify source guard, full rules and main build**

- [ ] **Step 5: Commit the complete school slice**

```powershell
git commit -m "feat: restrict historical schools to Xia regions"
```

### Task 4: Define refugee rules and state contracts

**Files:**
- Create: `Code/core/lineage/WarRefugeeModels.cs`
- Create: `Code/core/lineage/WarRefugeeRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/WarRefugeeRulesTests.cs.txt`
- Modify: rules test project/program.

- [ ] **Step 1: Write failing tests for threat and quota**

Test no departure from a rear city, 1-3%, 5-10%, famine cap 15%, minimum floor and deterministic output.

- [ ] **Step 2: Write failing tests for eligibility and ranking**

Test every excluded role, domestic/partner/neutral order, enemy exclusion, food/housing/capacity safety and reservation overbooking.

- [ ] **Step 3: Write failing tests for journeys, return and assimilation**

Test leader promotion, abstract fallback timing, one-year continuous safety, voluntary settlement, five-year grace, one actor-year evaluation and increasing deterministic assimilation chance.

- [ ] **Step 4: Verify RED, then implement minimal pure models/rules**

Use stable integer hashes from journey/actor/year IDs; never call global random state.

- [ ] **Step 5: Verify targeted and full tests**

### Task 5: Add durable refugee persistence

**Files:**
- Create: `Code/core/lineage/WarRefugeePersistence.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/WarRefugeePersistenceSqlTests.cs.txt`

- [ ] **Step 1: Write failing SQLite round-trip tests**

Cover journey/member/origin fields, state transition idempotency, capacity reconstruction, duplicate actor ownership rejection and archive retention after settlement.

- [ ] **Step 2: Verify RED**

- [ ] **Step 3: Implement version-safe `CREATE TABLE IF NOT EXISTS` schema and transactions**

Keep active journey rows separate from immutable origin records; use an actor-ID uniqueness boundary for active ownership.

- [ ] **Step 4: Verify persistence tests**

### Task 6: Build bounded threat and journey services

**Files:**
- Create: `Code/core/lineage/WarRefugeeThreatService.cs`
- Create: `Code/core/lineage/WarRefugeeJourneyService.cs`
- Create: `Code/core/lineage/WarRefugeeService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/WarRefugeeSourceGuardTests.cs.txt`

- [ ] **Step 1: Write failing source/integration guards**

Require one monthly threat capture, bounded city/household budgets, one route owner per household, destination reservation release, idempotent arrival and no P0 registration.

- [ ] **Step 2: Implement monthly snapshot and household batching**

Reuse city residents and existing RTS/war/famine facts once per city; exclude military and authority roles before household construction.

- [ ] **Step 3: Implement physical/abstract journey progress**

The leader owns the path, followers use existing follow movement, failed/cross-sea routes transition to a timed abstract arrival, and destination invalidation reranks safely.

- [ ] **Step 4: Implement arrival, return, settlement and assimilation**

Call normal `joinCity`/`joinKingdom` only on committed arrival; preserve origin rows; evaluate return after twelve safe months; assign host culture through the existing culture path.

- [ ] **Step 5: Verify targeted tests and main build**

### Task 7: Wire refugee lifecycle and authority scheduling

**Files:**
- Create: `Code/patch/AW_WarRefugeePatch.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Locales/en.json`, `Locales/cz.json`, `Locales/ch.json`

- [ ] **Step 1: Extend source guards for reset/restore/replica boundaries**

- [ ] **Step 2: Process refugee work after military RTS work and outside P0**

- [ ] **Step 3: Add load recovery, actor death, city change and host-born child hooks**

- [ ] **Step 4: Verify full rules, JSON parsing and main build**

- [ ] **Step 5: Commit the complete refugee slice**

```powershell
git commit -m "feat: add bounded war refugee migration"
```

### Task 8: Define Shi influence snapshots and colors

**Files:**
- Create: `Code/core/lineage/CityShiInfluenceRules.cs`
- Create: `Code/core/policy/ShiLineageMapModeRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/CityShiInfluenceRulesTests.cs.txt`

- [ ] **Step 1: Write failing role-precedence tests**

Test weights 1/2/4/6/8/10 with the highest role applied exactly once per actor.

- [ ] **Step 2: Write failing ordering and percentage tests**

Test total weight, highest-member/living-count/creation-time/ID ties, empty city and one-branch 100%.

- [ ] **Step 3: Write failing deterministic-color tests**

Test stable branch-ID colors, neutral absent color and focus-share blending.

- [ ] **Step 4: Implement and verify pure rules**

### Task 9: Build the bounded city Shi snapshot service

**Files:**
- Create: `Code/core/lineage/CityShiInfluenceSnapshotService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/CityShiInfluenceSourceGuardTests.cs.txt`
- Modify: existing birth/death/city/office/succession invalidation call sites.

- [ ] **Step 1: Write failing source guards for dirty and demand queues**

- [ ] **Step 2: Implement bounded snapshots from live city residents and `SHI_ID`**

Resolve branches from the existing lineage query/cache, ignore malformed residents, and prioritize selected/hovered city demand.

- [ ] **Step 3: Wire invalidation for city, Shi, noble/office, king/heir/leader and ownership changes**

- [ ] **Step 4: Verify targeted and full tests**

### Task 10: Add the Shi mapmode and genealogy bottom bar

**Files:**
- Create: `Code/core/policy/ShiLineageMapModeService.cs`
- Create: `Code/core/policy/ShiLineageMapBottomBarController.cs`
- Create: `Code/ui/items/ShiLineageCompositionElement.cs`
- Modify: `Code/core/policy/AWMapModeMetaTypes.cs`
- Modify: `Code/core/policy/AWMapModeMetaLibrary.cs`
- Modify: `Code/content/GodPowerLibrary.cs`
- Modify: `Code/ui/AW_LineageTab.cs`
- Modify: `Code/patch/AW_MapModeMetaTypePatch.cs`
- Modify: `Code/patch/AW_MapModeTooltipPatch.cs`
- Modify: `Code/patch/AW_DeferredRuntimeWorkPatch.cs`
- Modify: mapmode localization files.

- [ ] **Step 1: Write a failing mapmode source guard**

Require a separate power/meta type, city color getter, top-three tooltip, focus mode, selected-city bottom bar and `FamilyTreeWindow.OpenBigTree(shiId)` row command.

- [ ] **Step 2: Register power, meta asset and lineage-tab button**

- [ ] **Step 3: Implement city drawing, tooltip, focus and reset/dirty behavior**

- [ ] **Step 4: Implement the bottom bar by reusing the school tab lifecycle/layout pattern**

Invalid IDs render disabled; valid rows call the existing family tree window directly.

- [ ] **Step 5: Verify source guard, localization JSON and main build**

- [ ] **Step 6: Commit the complete Shi mapmode slice**

```powershell
git commit -m "feat: add Shi lineage influence mapmode"
```

### Task 11: Full regression and integration review

**Files:** all files touched above.

- [ ] **Step 1: Run targeted tests for all three slices**

- [ ] **Step 2: Run the complete rules harness**

Expected: `Rule tests passed.`

- [ ] **Step 3: Build `AncientWarfare3.csproj`**

Expected: zero warnings and zero errors.

- [ ] **Step 4: Parse all modified JSON locales as UTF-8**

- [ ] **Step 5: Run `git diff --check` and inspect commit boundaries**

- [ ] **Step 6: Perform runtime smoke checks**

Validate a foreign non-Xia academy is dormant, a threatened civilian household reaches a safe city, and a selected Shi row opens its existing genealogy.

---

## Self-Review

- Spec coverage: all academy/travel/lecture boundaries, refugee threat-to-settlement lifecycle and Shi mapmode presentation/navigation requirements map to explicit tasks.
- Isolation: each feature ends in its own commit and can be tested without enabling the later features.
- Performance: no new P0 refugee work, no map-draw world scan, one route owner per household, and all queues are bounded.
- Compatibility: old saves need no migration, foreign academies remain present but dormant, refugee tables are additive, and genealogy uses the existing window.
- Placeholder scan: no deferred implementation markers remain.

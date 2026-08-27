# De Jure Effective Seat And Contiguity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make domestic de jure maps preserve the clicked physical realm and use its effective seat, while enforcing six-city contiguous regions for new and legacy cities.

**Architecture:** Keep legal region identity and global labels anchored to `LegalSeatCityId`. Domestic map state, labels and click matching use `EffectiveSeatCityId`. A pure graph rule identifies the legal-seat connected component; event-driven assignment and the existing one-shot world-load repair create or join only adjacent regions.

**Tech Stack:** C#/.NET 9 rule tests, WorldBox runtime types, existing AW3 de jure store and hierarchical map services.

---

### Task 1: Domestic realm and effective-seat map behavior

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/GlobalDeJureMapModeSourceGuardTests.cs.txt`
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`

- [ ] **Step 1: Write failing source-guard assertions**

Assert that `PrepareForDeJureInteraction`, country-layer clicks, region-layer ownership checks and `IsCityInFocusedKingdom` use `city.kingdom.id` directly; assert domestic label/meta construction reads `EffectiveSeatCityId`, while `BuildGlobalDeJureRegionSources` retains `legal.SeatCityId`.

- [ ] **Step 2: Run the rules test executable and verify RED**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

Expected: FAIL because domestic paths still call `ResolveRepresentative` and use `region.SeatCityId`.

- [ ] **Step 3: Apply the minimal domestic-map fix**

Use physical kingdom IDs when entering and validating domestic region mode. Resolve domestic label anchors and map meta IDs from `EffectiveSeatCityId`, with the clicked member as a defensive fallback. Use the effective seat for region breadcrumbs and focused-region comparisons. Leave global region source construction on the legal seat.

- [ ] **Step 4: Run the rules tests and verify GREEN**

Run the command from Step 2. Expected: the new map guards pass.

### Task 2: Adjacent-only deterministic new-city assignment

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/DeJureNewCityAssignmentRulesTests.cs.txt`
- Modify: `Code/core/court/DeJureNewCityAssignmentRules.cs`
- Modify: `Code/core/court/DeJureNewCityAssignmentService.cs`

- [ ] **Step 1: Write failing rule tests**

Cover: non-adjacent candidates return `-1`; more adjacent members win; seat distance breaks equal adjacency; region ID breaks the final tie; ineligible six-city regions are excluded.

- [ ] **Step 2: Run tests and verify RED**

Expected: the existing selector picks non-adjacent nearest regions or rotates among adjacent seats.

- [ ] **Step 3: Implement the minimal selector and runtime facts**

Filter to `Eligible && AdjacentMemberCount > 0`. Order by descending adjacent-member count, ascending seat distance, then region ID. Populate `AdjacentMemberCount` from the founded city's same-kingdom neighboring city IDs. If selection returns `-1`, call the existing store creation operation with reason `city_created_isolated_region` rather than leaving the city unassigned.

- [ ] **Step 4: Run tests and verify GREEN**

Expected: all assignment rules and existing capacity guards pass.

### Task 3: Legacy disconnected-region repair

**Files:**
- Create: `Code/core/court/DeJureRegionContinuityRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionContinuityRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Code/core/court/DeJureRegionStore.cs`

- [ ] **Step 1: Write failing pure graph tests**

Add tests where a legal seat reaches a chain of members, excludes a disconnected island, tolerates one-city regions, caps the retained component at six deterministically, and returns the same result on repeated input.

- [ ] **Step 2: Run tests and verify RED**

Expected: build fails because `DeJureRegionContinuityRules` does not exist.

- [ ] **Step 3: Implement the pure breadth-first continuity rule**

Given a seat ID, member IDs and their adjacency map, return up to six reachable members in deterministic breadth-first order, always retaining the valid seat first.

- [ ] **Step 4: Integrate one-shot and dirty-region repair**

At `RepairAfterWorldLoaded`, repair active region membership after migration. For each disconnected or over-capacity member, remove it from the old region and attach it only to an adjacent region with capacity using the same deterministic ordering as new-city assignment; otherwise create a one-city region. Record membership changes, update versions/revision, clear aggregation once, refresh the map once, and preserve explicit retired-region empty states.

- [ ] **Step 5: Run tests and verify GREEN**

Expected: continuity tests and read-path guards pass; no periodic world enumeration is introduced.

### Task 4: Full verification

**Files:**
- Verify only.

- [ ] **Step 1: Run focused rules suite**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 2: Build the mod**

Run: `dotnet build AncientWarfare3.csproj`

- [ ] **Step 3: Check patch hygiene**

Run: `git diff --check`

Expected: tests and build succeed, with no whitespace errors and no unrelated files staged.

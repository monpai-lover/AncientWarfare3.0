# Global De Jure State Map Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make the hierarchy map's city layer show every active de jure state globally, place each state label at its seat city, and keep clicking a state opening its city detail view without requiring a country click first.

**Architecture:** Keep the existing country layer and city-detail breadcrumb state. Treat the root of the city layer as a global region overview: zone metadata resolves directly through the legal de jure region containing the physical city, and label discovery builds region sources for all kingdoms with no focus filter. Clicking a region pushes its owning kingdom and seat city into the existing city-detail state. Preserve same-kingdom automatic assignment and capital-anchored save migration in `DeJureRegionStore`.

**Tech Stack:** C#/.NET Framework 4.8, Harmony patches, Unity map metadata/nameplate pipeline, source-guard rule tests.

---

### Task 1: Add failing regression guards

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/DeJureNewCityAssignmentRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/GlobalDeJureMapModeSourceGuardTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing source guards**

Assert that the city-layer root is treated as a region layer, global region sources accept `pKingdomId < 0`, and region labels use `SeatCity.city_center`. Also assert the de jure store still contains same-kingdom candidate and capital-first migration guards.

- [ ] **Step 2: Run the focused guard command**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -p:EnableDefaultCompileItems=false -- --global-dejure-map
```

Expected: FAIL because the current city-layer root still resolves country metadata and labels are not source-guarded to the global region path.

### Task 2: Make city-layer root resolve global legal regions

**Files:**
- Modify: `Code/core/policy/CityAdministrationMapModeRules.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`

- [ ] **Step 1: Define the root-city region state contract**

Add a rule-level predicate for the city-layer root to be a region overview. Keep `IsCityMemberLayer` unchanged for the focused city detail level.

- [ ] **Step 2: Resolve zone metadata through legal regions globally**

When the city layer is at root, call `RegionalGovernmentAggregationService.TryFindRegion` for the physical city without comparing against a focused kingdom. Return the region meta keyed by `RegionId` and `SeatCityId`; only fall back to the physical kingdom when no legal region exists.

- [ ] **Step 3: Keep global region label recording unfiltered**

In the native draw pass, skip `IsCityInFocusedKingdom` for the root global region layer. Accumulate every region's zones under its seat city and publish one label per region.

### Task 3: Reuse region source discovery for the global root

**Files:**
- Modify: `Code/core/policy/HierarchicalVassalLabelDiscoveryJob.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapLabelRuntime.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`

- [ ] **Step 1: Build region sources with no kingdom filter**

Use `BuildCityAdministrationRegionSources(_cities, -1L)` at the city-layer root. Ensure discovery still scans every live kingdom and every live city, rather than the current focused kingdom.

- [ ] **Step 2: Use a stable global region cache key**

Keep the `region:-1` batch prefix at the global root and include `RegionId` plus `SeatCityId` in source keys so two states with equal names cannot share a label cache entry.

- [ ] **Step 3: Place labels at the seat city center**

In `PublishNativeCityLabels`/region source conversion, use `SeatCity.city_center` for region placement and keep the display name as `RegionalGovernmentRules.AdministrativeLabel(region.RegionName, region.RegionTitle)`.

### Task 4: Make global region clicks enter the existing city detail view

**Files:**
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`
- Modify: `Code/core/policy/CityAdministrationMapModeRules.cs`

- [ ] **Step 1: Resolve clicked region globally**

At the global city-layer root, resolve the clicked city's legal region. If mapped, push the region's owning kingdom and seat city into `CityAdministrationState`; if unmapped, preserve the existing no-op behavior.

- [ ] **Step 2: Preserve focused-region and city-detail behavior**

Do not change clicks while a kingdom or region is already focused: region click continues to focus the region, and a second click on its seat/inner city continues to inspect or pop as before.

### Task 5: Verify and integrate

**Files:**
- No additional production files.

- [ ] **Step 1: Run focused source guards**

Run the global de jure map guard and the existing court/de jure guard command. The repository-wide test project may still report its known duplicate Compile and pathfinding API errors; record those separately.

- [ ] **Step 2: Build Debug and Release**

```powershell
dotnet build AncientWarfare3.csproj -c Debug --no-restore --nologo
dotnet build AncientWarfare3.csproj -c Release --no-restore --nologo
```

Expected: both builds complete with zero errors and zero warnings.

- [ ] **Step 3: Review the diff**

Run `git diff --check` on the touched files and verify no unrelated worktree changes were reverted. Do not push or deploy until explicitly requested.

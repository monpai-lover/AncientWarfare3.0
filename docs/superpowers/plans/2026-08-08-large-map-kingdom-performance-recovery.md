# Large Map Kingdom Performance Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the non-pathfinding main-thread work that scales with map size, city-zone count, and kingdom count in no-war saves, while preserving current atlas output and city-survival behavior.

**Architecture:** Runtime simulation records only compact ownership events and enqueues bounded local repair work. Atlas geometry is captured once on explicit preview/export, map-mode geometry invalidation is incremental, and kingdom creation performs one color assignment, one archive write, and no forced full-map clear. Continuous diagnostics retain the worst frame in each reporting window so annual and lifecycle spikes cannot escape sampling.

**Tech Stack:** C#/.NET Framework 4.8, Unity/WorldBox, Harmony, SQLite, PowerShell source guards, .NET 9 rule-test harness.

## Implementation Status (2026-08-08)

Tasks 1-6 are implemented and have focused rule/source-guard coverage. Task 7
now uses `LocalizedTextManager.current_language?.id` everywhere instead of the
inaccessible private field; final full build/deployment validation is pending.

Validation update (2026-08-08): all eight performance source guards pass; the
Actor runtime, Actor scale, monthly authority, runtime diagnostics, empty-city,
atlas, and Xiaization naming slices pass. The production net48 build succeeds
with zero errors and three pre-existing unused-field warnings, and its DLL plus
source tree are deployed to the local WorldBox mod directory with matching
hashes. The unfiltered rules suite advances through the performance tests but
stops at the pre-existing Zhulu contract requiring
`WarExhaustionSettlementRuntimeService` to use `ZhuluPeaceGuard`; that behavior
is explicitly outside this plan and was not changed.

Additional root-cause fixes discovered during the static follow-up:

- World-object presentation snapshots are disabled. Building, projectile,
  resource-throw, and world-light rendering now uses the native visible
  collections behind explicit simulation read boundaries instead of copying
  and rescanning every world object at render cadence.
- Custom Actor overlay snapshots are disabled. The presentation bridge
  rebuilds vanilla sparse overlay lists once per visible snapshot, so banner,
  status, favorite, avatar, social, and food rendering no longer each scan all
  visible Actors.
- Small Actor presentation captures no longer use a synchronous
  `Parallel.For`; parallel capture begins only after the batch can amortize
  worker dispatch and join overhead.
- Actor interpolation state now follows Actor lifetime through a
  `ConditionalWeakTable`, matching Cultiway's ownership model and removing the
  periodic 600-frame scan of all historical interpolation entries.
- Monthly kingdom work uses one cursor batch per month and a shared immutable
  kingdom snapshot. It no longer allocates one queue object per kingdom per
  catch-up month or independently recopies the kingdom manager for each
  monthly service.
- Name-integration, culture-naming, and institutional Xiaization migrations
  use round-robin queues and resumable candidate cursors. Shared Actor-index
  invalidation also invalidates list cursors, failed Actors are retried, and
  world-load kingdom restoration is capped at four kingdoms per authority
  cycle instead of issuing every SQLite query in one frame.
- Kingdom `updateAge` reuses its mutation-safe snapshot buffer. Deferred
  annual metrics accumulate across the complete kingdom batch and flush once
  when it becomes idle instead of saving and clearing every metric after every
  stage of every kingdom.

- Civilian enemy preparation no longer adds a global enemy pre-scan. It
  delegates to the native kingdom/chunk/range cache, which avoids the prior
  peace-time all-kingdom scan.
- New kingdoms and successions queue only their own western-lineage
  reconciliation; old-save recovery remains the only full-world migration.
- RTS war lifecycle reconciliation reuses an ordered snapshot until records
  structurally change.
- Authority-cycle services reject idle work before allocating war, vacancy, or
  replenishment batches.
- Civil-service scheduling performs at most one persisted-session recovery
  lookup per runtime rebuild and the legacy credential backfill walks one
  cached kingdom snapshot instead of restarting a global country scan every
  authority cycle.
- The vacant-mandate annual candidate pass aggregates each realm's vassal
  power through one kingdom-to-suzerain index. It no longer invokes the
  recursive global `GetVassals` scan once per candidate kingdom.

Static follow-up confirmed that the heir minimap already has a world-change
index plus incremental updates, and both presentation renderers share the
same gameplay visibility signature in a frame. Neither is an outstanding
country-scale runtime root cause.

---

### Task 1: Remove runtime atlas tile persistence

**Files:**
- Modify: `Tests/KingdomAtlasSourceGuard.ps1`
- Modify: `Code/patch/AW_ChroniclePatch.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Code/core/atlas/KingdomAtlasHistoryService.cs`
- Modify: `Code/core/atlas/KingdomAtlasZoneArchiveService.cs`

- [ ] **Step 1: Make the atlas source guard require generation-time-only geometry.**

The guard must reject `CaptureCityGeometry`, `CaptureCityEvent`, `FlushForSave`, and `KingdomAtlasZoneArchiveService.Read` from city lifecycle, chronicle, save, and history construction paths. It must require preview/export to use one frozen `ArmyRtsPlanWorldTerrainSnapshot` captured by `KingdomAtlasLiveTerrainService`.

- [ ] **Step 2: Run the guard and verify RED.**

Run: `pwsh -NoProfile -File Tests/KingdomAtlasSourceGuard.ps1`

Expected: FAIL because destruction/founding/transfer/save/history still reference the zone archive.

- [ ] **Step 3: Remove all runtime archive calls and legacy reads.**

Keep `city_found` and `city_transfer` chronicle rows. Remove destruction geometry capture because zero-population cities are intentionally retained. Turn `KingdomAtlasZoneArchiveService` into a compatibility shell with no simulation call sites, or delete it only if project references prove it is unused.

- [ ] **Step 4: Run the guard and atlas rule tests.**

Run: `pwsh -NoProfile -File Tests/KingdomAtlasSourceGuard.ps1`

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --kingdom-atlas-live-terrain`

Expected: both PASS.

### Task 2: Make `City.addZone` invalidation incremental

**Files:**
- Modify: `Tests/HierarchicalVassalMapModeInvalidationSourceGuardTests.ps1`
- Modify: `Code/patch/AW_ChroniclePatch.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeLabelLayer.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapLabelRuntime.cs`

- [ ] **Step 1: Add a failing guard for the changed-zone contract.**

Require `CityAddZone_Postfix(City __instance, TileZone pZone)` and `MarkCityZoneGeometryDirty(__instance, pZone)`. Forbid calls to full-city `CollectZoneIds(pCity.zones)` from that incremental entry point.

- [ ] **Step 2: Run the focused guard and verify RED.**

Run: `pwsh -NoProfile -File Tests/HierarchicalVassalMapModeInvalidationSourceGuardTests.ps1`

Expected: FAIL because the postfix currently invalidates by rescanning the full city.

- [ ] **Step 3: Add the changed zone to existing cached ID sets and invalidate only its zone metadata.**

If the hierarchical map mode is inactive and no visited-focus label cache exists, return immediately. Preserve the full-city method for rename, transfer, removal, and explicit rebuild paths.

- [ ] **Step 4: Run hierarchical map source guards.**

Run: `pwsh -NoProfile -File Tests/HierarchicalVassalMapModeInvalidationSourceGuardTests.ps1`

Run: `pwsh -NoProfile -File Tests/HierarchicalVassalMapLabelLifecycleSourceGuard.ps1`

Expected: both PASS.

### Task 3: Collapse kingdom creation color work

**Files:**
- Create: `Tests/KingdomCreationPerformanceSourceGuard.ps1`
- Modify: `Code/core/lineage/KingdomVisualRandomizationService.cs`
- Modify: `Code/core/lineage/MetaColorCacheService.cs`
- Modify: `Code/patch/AW_KingdomColorPatch.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`

- [ ] **Step 1: Add a source guard that captures the single-write contract.**

Require one precomputed `HashSet<int>` of used kingdom/alliance colors. Forbid `MetaColorCacheService` from calling `Kingdom.updateColor`, forbid `dirtyAndClear`, and forbid `KingdomArchiveWriter.Upsert` from the generic `updateColor` postfix.

- [ ] **Step 2: Run the guard and verify RED.**

Run: `pwsh -NoProfile -File Tests/KingdomCreationPerformanceSourceGuard.ps1`

Expected: FAIL on the second color update, full-map clear, repeated archive write, and nested color scan.

- [ ] **Step 3: Set visuals once and archive once at `OnKingdomFounded`.**

`PickColorIndex` builds the used-color set once, then tests candidates in O(1). Refresh sprite/cache state without recursively invoking `updateColor`. Rely on vanilla kingdom creation dirtying, with at most `setDrawnZonesDirty()` when an explicit refresh is required.

- [ ] **Step 4: Run the new guard and production build.**

Run: `pwsh -NoProfile -File Tests/KingdomCreationPerformanceSourceGuard.ps1`

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Expected: guard PASS and zero build errors.

### Task 4: Replace permanent empty-city polling with an event queue

**Files:**
- Create: `Code/core/lineage/EmptyCityResettlementRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/EmptyCityResettlementRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Code/core/lineage/EmptyCityResettlementService.cs`
- Modify: `Code/patch/AW_EmptyCitySurvivalPatch.cs`

- [ ] **Step 1: Write failing queue/backoff rule tests.**

Test that a newly empty or neutral city is queued once, a failed attempt receives an exponentially bounded retry time, a resident arrival removes pending work, and an authority cycle never scans `World.world.cities`.

- [ ] **Step 2: Run the focused rules and verify RED.**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --empty-city-resettlement`

Expected: compile failure naming the missing rules.

- [ ] **Step 3: Implement coalesced city IDs with bounded retry.**

City population/ownership lifecycle hooks enqueue or cancel IDs. Each authority cycle inspects at most four due entries; failures back off to a low-frequency retry and no global city enumerator remains.

- [ ] **Step 4: Run focused rules and empty-city guards.**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --empty-city-resettlement`

Run: `pwsh -NoProfile -File Tests/EmptyCitySurvivalSourceGuard.ps1`

Expected: both PASS.

### Task 5: Bound enclosed-zone repair allocation

**Files:**
- Modify: `Tests/EnclosedUnownedZoneSourceGuard.ps1`
- Modify: `Code/patch/AW_EnclosedUnownedZonePatch.cs`
- Modify: `Code/core/lineage/EnclosedUnownedZoneRepairService.cs`

- [ ] **Step 1: Require ownership-transition filtering and one-pass load sweep.**

The guard must require old/new city capture around `TileZone.setCity`, enqueue only a newly unowned zone or unowned neighbours of a real ownership change, and forbid the immediate load path from invoking per-zone component BFS.

- [ ] **Step 2: Run the guard and verify RED.**

Run: `pwsh -NoProfile -File Tests/EnclosedUnownedZoneSourceGuard.ps1`

Expected: FAIL because every `setCity` currently queues changed and neighbouring zones and load repair may repeat BFS.

- [ ] **Step 3: Filter queue ingress and share traversal state.**

Skip no-op owner assignments and owned candidates. Use one visited set for a resumable world-load component sweep; keep the existing fixed per-cycle candidate and component limits.

- [ ] **Step 4: Run the enclosed-zone guard.**

Run: `pwsh -NoProfile -File Tests/EnclosedUnownedZoneSourceGuard.ps1`

Expected: PASS.

### Task 6: Capture unsampled worst frames and annual stages

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/RuntimePerformanceDiagnosticRulesTests.cs.txt`
- Modify: `Code/core/policy/RuntimePerformanceDiagnosticRules.cs`
- Modify: `Code/core/policy/RuntimePerformanceDiagnostic.cs`
- Modify: `Code/core/policy/KingdomAnnualWorkService.cs`

- [ ] **Step 1: Add failing continuous-window diagnostic tests.**

Require constant-time per-frame max selection, no per-frame formatting, and a snapshot carrying frame wall time, authority stage, atlas, DB, zone invalidation, city lifecycle, and annual-stage identifiers.

- [ ] **Step 2: Run the diagnostic tests and verify RED.**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --runtime-diagnostic-rules`

Expected: FAIL because the existing sampler only profiles every 120th frame.

- [ ] **Step 3: Record every frame total and retain only the current maximum snapshot.**

Detailed stage counters remain opt-in and allocation-free. At the 120-frame boundary, emit the retained maximum and reset it. Annual work exposes its current kingdom/stage so one unsplittable stage can be identified before further behavior changes.

- [ ] **Step 4: Run diagnostic tests.**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --runtime-diagnostic-rules`

Expected: PASS.

### Task 7: Restore loadability and verify the complete fix

**Files:**
- Modify: `Code/core/naming/AWLocalizedNameService.cs`
- Modify: `docs/superpowers/plans/2026-08-08-large-map-kingdom-performance-recovery.md`

- [ ] **Step 1: Replace inaccessible locale field access with the public localization API used elsewhere in the mod.**

Use `LocalizedTextManager.getCulture()` or a public language accessor; do not reflect a private field on a hot path.

- [ ] **Step 2: Run all focused guards and the full rules suite.**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore`

Run each source guard changed by Tasks 1-5. Expected: all PASS.

- [ ] **Step 3: Build production and inspect the game log after deployment.**

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Require zero build errors and an in-game log showing Ancient Warfare 3 enabled. A run where `LocalizedTextManager.language` disables the mod is invalid.

- [ ] **Step 4: Reproduce the acceptance matrix.**

Test large map/many kingdoms/no war, kingdom creation, sustained expansion, and annual rollover. Confirm no atlas tile SQL occurs during simulation and compare the retained worst-frame stages before and after the fix.

## Scope Boundaries

- Do not change Zhulu/total-war behavior, RTS targeting, war settlement, or path worker count.
- Do not mutate Unity or WorldBox live objects from worker threads.
- Do not delete old atlas tables from existing saves; stop reading/writing them in live simulation only.
- Preserve zero-population cities and their zones.

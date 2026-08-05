# Kingdom Atlas Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a historical Kingdom Atlas window that reconstructs two-country territory changes from persisted chronicle and city-zone archives, exports incremental PNG pages and optional GIFs, and shows each node's yearly chronicles.

**Architecture:** Keep historical event reconstruction, zone geometry, replay, rasterization, artifacts, and UI in separate components. Generation consumes only SQLite/archive models and never samples live `World.world`, armies, or map-mode state; Unity is used only for the UI and batched font texture work. Existing `WideWindowChrome`, `ArmyRtsPlanGifEncoder` conventions, `HistoryQuery` schema, and hierarchical map-mode label geometry are reused through narrow adapters.

**Tech Stack:** C# 11/net48, Unity UI, SQLite archive tables, Newtonsoft.Json manifest serialization, existing rule-test executable (`net9.0`), and the existing `WideWindowChrome`/map-mode label rules.

---

## File Map

- Create `Code/core/atlas/KingdomAtlasModels.cs`: immutable event, node, geometry, raster, cursor, and progress models.
- Create `Code/core/atlas/KingdomAtlasRules.cs`: pure sorting, owner replay, color precedence, output-key, resolution, and progress rules.
- Create `Code/core/db/KingdomAtlasCityZoneTableItem.cs`: persistent city-zone geometry table definition.
- Create `Code/core/atlas/KingdomAtlasGeometryArchive.cs`: geometry capture at city/save lifecycle boundaries and archive-only reads.
- Create `Code/core/atlas/KingdomAtlasHistoryQuery.cs`: raw SQLite queries for transfer pairs and yearly kingdom chronicle rows.
- Create `Code/core/atlas/KingdomAtlasReplay.cs`: immutable owner-state replay and two-participant filtering.
- Create `Code/core/atlas/KingdomAtlasRasterizer.cs`: territory, coast, and participant-boundary pixels only.
- Create `Code/core/atlas/KingdomAtlasLabelRenderer.cs`: map-mode label placement/style reuse and off-screen glyph rasterization.
- Create `Code/core/atlas/KingdomAtlasArtifactWriter.cs`: PNG/GIF files, manifest, atomic cursor updates, and cancellation.
- Create `Code/core/atlas/KingdomAtlasGenerationService.cs`: batch scheduling, progress callbacks, and preview frame loading.
- Create `Code/ui/components/KingdomAtlasMapViewport.cs`: zoom/pan input for the preview image.
- Create `Code/ui/windows/KingdomAtlasWindow.cs`: wide window, controls, node navigation, chronicle panel, and progress state.
- Create `Tests/AncientWarfare3.Rules.Tests/KingdomAtlasRulesTests.cs.txt`: pure replay, color, cursor, and output-key tests.
- Create `Tests/AncientWarfare3.Rules.Tests/KingdomAtlasSourceGuard.ps1`: verifies generation code does not reference live world/army sampling APIs.
- Create `Locales/aw3_kingdom_atlas.csv`: Chinese/English/Traditional-Chinese labels, tooltips, status, and errors.
- Modify `Code/patch/AW_KingdomTabPatch.cs`: add atlas button directly below the existing chronicle button.
- Modify `Code/ui/AW_LineageWindowIds.cs`: register `KINGDOM_ATLAS` window ID.
- Modify `Code/patch/AW_ChroniclePatch.cs`: notify geometry archive on city creation, zone addition, and city destruction without adding a second Harmony patch for the same methods.
- Modify `Code/patch/AW_SavePatch.cs`: flush geometry archive before lineage archive export and clear atlas runtime state on new world.
- Modify `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`: include atlas tests, production pure files, and source guard target.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`: add `--kingdom-atlas` test slice.

## Task 1: Define Pure Atlas Models and Rules

**Files:**
- Create: `Code/core/atlas/KingdomAtlasModels.cs`
- Create: `Code/core/atlas/KingdomAtlasRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/KingdomAtlasRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing rule tests.** Define test data containing three cities, two same-year transfers, a duplicate event, three historical colors, and two resolutions. Assert stable `(world_time,event_id)` ordering, duplicate removal, participant filtering, color precedence, `Percent(completed,total)`, and deterministic output key.

```csharp
internal static class KingdomAtlasRulesTests
{
    public static void Run()
    {
        OrdersAndDeduplicatesEvents();
        KeepsOnlyEventParticipants();
        UsesHistoricalColorPrecedence();
        BuildsStableIncrementalKey();
    }
}
```

- [ ] **Step 2: Register the tests and production files.** Add the `.cs.txt` test, `KingdomAtlasModels.cs`, and `KingdomAtlasRules.cs` links to the test project; call `KingdomAtlasRulesTests.Run()` in the normal test path and in the `--kingdom-atlas` branch.
- [ ] **Step 3: Run the slice and verify it fails.** Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --kingdom-atlas`; expected failure is missing atlas types/methods.
- [ ] **Step 4: Implement the minimal pure model/rule API.** Add `KingdomAtlasHistoryEvent`, `KingdomAtlasNode`, `KingdomAtlasZoneCell`, `KingdomAtlasColor`, `KingdomAtlasCursor`, `KingdomAtlasGenerationKey`, and `KingdomAtlasProgress`. Add `OrderAndDeduplicate`, `ResolveHistoricalColor`, `FilterParticipants`, `BuildGenerationKey`, and `Percent` with deterministic comparisons and no Unity references.
- [ ] **Step 5: Run the slice and verify it passes.** Run the same command; expected output is `Kingdom atlas rules passed.`
- [ ] **Step 6: Commit the pure contract.** `git add Code/core/atlas Tests/AncientWarfare3.Rules.Tests/KingdomAtlasRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt && git commit -m "feat: define kingdom atlas pure contracts"`.

## Task 2: Persist City-Zone Geometry Without Per-Frame Sampling

**Files:**
- Create: `Code/core/db/KingdomAtlasCityZoneTableItem.cs`
- Create: `Code/core/atlas/KingdomAtlasGeometryArchive.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/KingdomAtlasSourceGuard.ps1`
- Modify: `Code/patch/AW_ChroniclePatch.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `AncientWarfare3.csproj` only if a source guard target is required by the production build.

- [ ] **Step 1: Add the table definition and archive API tests.** The table must include `CITY_ID`, `GEOMETRY_VERSION`, `X`, `Y`, `WATER`, `NEIGHBOR_MASK`, `CITY_NAME`, and `CENTER_X/CENTER_Y`; tests assert one row per zone coordinate, replacement by geometry version, and removal on city destruction.
- [ ] **Step 2: Add the source guard before implementation.** The guard must fail if `KingdomAtlasGenerationService`, `KingdomAtlasReplay`, or `KingdomAtlasRasterizer` references `World.world`, `City.zones`, `WorldTile`, `Army`, `War`, or `MapMode`.
- [ ] **Step 3: Implement archive writes.** `CaptureCity(City)` copies zone coordinates and neighbor/water facts into an in-memory pending set; `MarkCityDirty(City)` invalidates that set; `FlushForSave()` writes a complete versioned replacement to SQLite before the lineage database is exported; `ReadSnapshot(SQLiteConnection)` returns immutable cells only.
- [ ] **Step 4: Wire existing lifecycle hooks.** Call `KingdomAtlasGeometryArchive.CaptureCity(__instance)` from the existing `NewCityEvent_Postfix` and `CityAddZone_Postfix`, call `RemoveCity(__instance)` from `DestroyCity_Postfix`, call `FlushForSave()` from `AW_SavePatch.TryPrepareForSave()` before `TryExportLineageArchive`, and call `ResetWorld()` from `GenerateNewMap_Postfix`.
- [ ] **Step 5: Run the source guard and archive tests.** Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/AncientWarfare3.Rules.Tests/KingdomAtlasSourceGuard.ps1`; expected output is `Kingdom atlas source guard passed.` Then run the atlas test slice.
- [ ] **Step 6: Commit the geometry archive.** `git add Code/core/db/KingdomAtlasCityZoneTableItem.cs Code/core/atlas/KingdomAtlasGeometryArchive.cs Code/patch/AW_ChroniclePatch.cs Code/patch/AW_SavePatch.cs Tests/AncientWarfare3.Rules.Tests/KingdomAtlasSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj && git commit -m "feat: persist kingdom atlas city geometry"`.

## Task 3: Query Transfer Nodes and Yearly Chronicles

**Files:**
- Create: `Code/core/atlas/KingdomAtlasHistoryQuery.cs`
- Create: `Code/core/atlas/KingdomAtlasReplay.cs`
- Modify: `Code/core/atlas/KingdomAtlasModels.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/KingdomAtlasRulesTests.cs.txt`

- [ ] **Step 1: Add pairing tests.** Use raw event fixtures for one `city_found`, one paired `city_lost/city_gained`, and two same-time transfers. Assert each node has city ID, old/new Kingdom IDs, event ID, year, snapshot names/colors, and sorted yearly chronicle rows.
- [ ] **Step 2: Implement SQLite node query.** Query `CityHistory` for `city_found/city_transfer` and `KingdomHistory` for `city_lost/city_gained`, pair loss/gain by `TARGET_ID` and `WORLD_TIME`, and reject transfers with no two reliable Kingdom IDs. Do not parse owner IDs from localized content.
- [ ] **Step 3: Implement yearly chronicle query.** Read raw `KingdomHistory` rows for each node participant and filter by the same `Date.getRawDate(world_time)[2]` year conversion used by `HistoryWriter`; return `HistoryEntry` snapshots without live-kingdom normalization.
- [ ] **Step 4: Implement replay.** Build `Dictionary<long,long>` city owners from `city_found`, apply transfer events in order, then create each node's visible cell owner map. Only the node's old/new IDs are visible; all other owners map to transparent/background.
- [ ] **Step 5: Run the atlas slice.** Expected output remains `Kingdom atlas rules passed.` and includes owner replay/pairing assertions.
- [ ] **Step 6: Commit history reconstruction.** `git add Code/core/atlas Tests/AncientWarfare3.Rules.Tests/KingdomAtlasRulesTests.cs.txt && git commit -m "feat: reconstruct kingdom atlas history nodes"`.

## Task 4: Render Two-Country Territory and Map-Mode Names

**Files:**
- Create: `Code/core/atlas/KingdomAtlasRasterizer.cs`
- Create: `Code/core/atlas/KingdomAtlasLabelRenderer.cs`
- Create: `Code/core/atlas/KingdomAtlasPngEncoder.cs`
- Modify: `Code/core/atlas/KingdomAtlasModels.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/KingdomAtlasRulesTests.cs.txt`

- [ ] **Step 1: Add raster assertions.** Fixture cells for two countries, water, an unrelated third country, and a long country name. Assert output dimensions preserve aspect ratio, unrelated cells are background, coast/boundary pixels are present, and only two label records are emitted.
- [ ] **Step 2: Implement CPU rasterization.** Project world coordinates to the selected long-edge resolution; fill visible cells with the node's historical color; mark water-adjacent outer edges as coastline and participant-adjacent edges as the shared boundary. Do not add city, army, arrow, or front layers.
- [ ] **Step 3: Reuse map-mode label placement.** Call `HierarchicalVassalMapModeGeometry.CalculateLabelPlacement`, `CalculateRenderedCharacterSize`, and the existing country color/outline rules. Produce immutable label placements before any Unity texture work.
- [ ] **Step 4: Implement glyph rasterization and PNG bytes.** Use the same `LocalizedTextManager.current_font` and map-mode outline colors on the Unity main-thread batch; write RGBA pixels to a PNG encoder that uses `DeflateStream` and atomic row filtering so tests can validate dimensions and PNG signature without Unity.
- [ ] **Step 5: Run raster and PNG tests.** Run the atlas slice and inspect the first generated fixture PNG with a PNG header/dimension assertion; expected output is pass.
- [ ] **Step 6: Commit rendering.** `git add Code/core/atlas Tests/AncientWarfare3.Rules.Tests/KingdomAtlasRulesTests.cs.txt && git commit -m "feat: render kingdom atlas territory and labels"`.

## Task 5: Add Incremental PNG/GIF Artifacts and Progress

**Files:**
- Create: `Code/core/atlas/KingdomAtlasArtifactWriter.cs`
- Create: `Code/core/atlas/KingdomAtlasGenerationService.cs`
- Modify: `Code/core/atlas/KingdomAtlasModels.cs`
- Modify: `Code/core/lineage/AW3SaveDirectoryRegistry.cs` only if an atlas-specific save lookup is needed.
- Modify: `Tests/AncientWarfare3.Rules.Tests/KingdomAtlasRulesTests.cs.txt`

- [ ] **Step 1: Add manifest/cursor tests.** Assert atomic manifest updates after each completed node, resume skips `(event_id,resolution,geometry_version)` keys, failed nodes do not advance the cursor, and `Percent` reports 0/100 at boundaries.
- [ ] **Step 2: Implement manifest DTOs and atomic writer.** Store save/world ID, kingdom ID, geometry hash, resolution, output format, node metadata, completed count, and last complete event ID in `aw3_kingdom_atlas/manifest.json`; write temporary files then replace destination.
- [ ] **Step 3: Implement PNG generation batches.** `StartPngGeneration(kingdomId,resolution,callback)` loads archive snapshots, replays nodes, rasterizes one node per batch, writes its PNG, updates manifest, and emits `KingdomAtlasProgress` after each page.
- [ ] **Step 4: Implement GIF as a secondary path.** Read completed same-resolution PNG pixel frames (or the in-memory indexed adapter), pass them to the existing GIF encoding convention, and write one GIF without re-querying the live world. PNGs remain if GIF encoding fails.
- [ ] **Step 5: Implement cancellation and world reset.** Preserve the last complete cursor on cancellation, expose `Cancel()`, and clear pending jobs/preview state on `ResetWorld()`.
- [ ] **Step 6: Run artifact tests with a temporary directory.** Expected: manifest resumes after an injected write failure and GIF failure leaves PNGs intact.
- [ ] **Step 7: Commit artifacts.** `git add Code/core/atlas Tests/AncientWarfare3.Rules.Tests/KingdomAtlasRulesTests.cs.txt && git commit -m "feat: export incremental kingdom atlas artifacts"`.

## Task 6: Add Window, Map Viewport, and Kingdom Entry Button

**Files:**
- Create: `Code/ui/components/KingdomAtlasMapViewport.cs`
- Create: `Code/ui/windows/KingdomAtlasWindow.cs`
- Modify: `Code/ui/AW_LineageWindowIds.cs`
- Modify: `Code/patch/AW_KingdomTabPatch.cs`
- Modify: `Code/core/atlas/KingdomAtlasGenerationService.cs`

- [ ] **Step 1: Add the window ID and entry button.** Register `KINGDOM_ATLAS = "aw_kingdom_atlas"`; add `AW_KingdomAtlasTabButton` to the same `Tabs Right` rail immediately after `AW_KingdomHistoryTabButton`; use an atlas/map icon and open `KingdomAtlasWindow.Open(current.id)`.
- [ ] **Step 2: Implement the viewport input component.** Add `IBeginDragHandler`, `IDragHandler`, and `IScrollHandler`; clamp zoom to `0.5f..4f`, update anchored position around the pointer, and keep the preview image inside the viewport bounds.
- [ ] **Step 3: Build the wide window shell.** `KingdomAtlasWindow` derives from the existing abstract window pattern, calls `CreateAndInit(AW_LineageWindowIds.KINGDOM_ATLAS)`, attaches `WideWindowChrome`, and lays out a left map/controls column plus a right chronicle column.
- [ ] **Step 4: Add node controls and preview.** Add previous/next buttons, resolution dropdown, PNG/GIF buttons, cancel button, progress text, and an image preview. Disable generation for missing save directory, missing geometry archive, missing colors, or empty event nodes.
- [ ] **Step 5: Render the right chronicle panel.** For the selected node, show year, city transfer text, both historical country names/colors, and the two yearly event lists from `KingdomAtlasHistoryQuery`; show a localized empty state when a side has no rows.
- [ ] **Step 6: Wire progress and cleanup.** Subscribe to generation callbacks, update `completed/total (percent)` and stage text, keep generation alive when the window closes, cancel only on explicit cancel, and unsubscribe/reset on world switch.
- [ ] **Step 7: Commit UI.** `git add Code/ui/components/KingdomAtlasMapViewport.cs Code/ui/windows/KingdomAtlasWindow.cs Code/ui/AW_LineageWindowIds.cs Code/patch/AW_KingdomTabPatch.cs Code/core/atlas/KingdomAtlasGenerationService.cs && git commit -m "feat: add kingdom atlas window"`.

## Task 7: Add Localization and Build/Test Guards

**Files:**
- Create: `Locales/aw3_kingdom_atlas.csv`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add localized keys.** Include `aw_kingdom_atlas`, `aw_kingdom_atlas_desc`, `aw_kingdom_atlas_previous`, `aw_kingdom_atlas_next`, `aw_kingdom_atlas_generate_png`, `aw_kingdom_atlas_generate_gif`, `aw_kingdom_atlas_continue`, `aw_kingdom_atlas_progress`, `aw_kingdom_atlas_missing_geometry`, `aw_kingdom_atlas_missing_color`, `aw_kingdom_atlas_no_events`, `aw_kingdom_atlas_cancel`, and yearly-chronicle empty/error labels in `key,cz,en,ch` format.
- [ ] **Step 2: Register the source guard and test slice.** Add `KingdomAtlasSourceGuard.ps1` to the test project and add an MSBuild `BeforeTargets="Build"` Exec entry matching the existing source-guard pattern. Add `--kingdom-atlas` output text to `Program.cs.txt`.
- [ ] **Step 3: Run focused verification.** Run the atlas slice, the source guard, and `dotnet build AncientWarfare3.csproj -c Release`; expected: atlas slice/source guard pass and production build has zero errors.
- [ ] **Step 4: Commit localization/guards.** `git add Locales/aw3_kingdom_atlas.csv Tests/AncientWarfare3.Rules.Tests && git commit -m "test: cover kingdom atlas integration contracts"`.

## Task 8: Runtime Verification and Final Integration

**Files:**
- Modify only files required by failed verification; do not revert unrelated dirty worktree changes.

- [ ] **Step 1: Run the complete rule suite.** `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`; record any pre-existing unrelated failure separately from atlas failures.
- [ ] **Step 2: Build Release.** `dotnet build AncientWarfare3.csproj -c Release`; require zero compile errors and inspect warnings involving atlas files.
- [ ] **Step 3: Perform a save-directory smoke test.** In a loaded world with at least one transfer, open the kingdom window, open the atlas, verify the two-country map and correctly historical colors, generate one PNG, close/reopen the window, and confirm the same PNG is loaded from the manifest.
- [ ] **Step 4: Perform incremental recovery.** Interrupt after a known node, restart PNG generation, verify the progress starts after the last complete event ID, and confirm no duplicate PNG files are created.
- [ ] **Step 5: Perform UI interaction checks.** Verify window drag/resize, map wheel zoom, map drag, previous/next node navigation, right-side yearly chronicles, explicit cancellation, and GIF failure preserving PNG output.
- [ ] **Step 6: Review the final diff.** `git diff master --stat`, `git status --short`, and `git diff --check`; ensure no unrelated files are staged and all atlas commits are present.

## Plan Self-Review

- **Spec coverage:** The tasks cover persisted geometry, event pairing/replay, historical color precedence, map-mode text, two-country-only rendering, zoom/pan UI, yearly chronicles, PNG/GIF output, manifest/cursor recovery, localization, source guards, and Release/runtime verification.
- **Completeness scan:** No unfinished marker or unspecified implementation step is used; each task names files, methods/data responsibilities, commands, and expected verification.
- **Type consistency:** `KingdomAtlasHistoryEvent`, `KingdomAtlasNode`, `KingdomAtlasZoneCell`, `KingdomAtlasProgress`, and `KingdomAtlasGenerationKey` are introduced in Task 1 and consumed by later tasks. `KingdomAtlasGeometryArchive` is the only geometry source for replay/rasterization, and `KingdomAtlasGenerationService` is the only UI-facing generation entry point.
- **Scope check:** The feature remains one cohesive plan because the archive, replay, renderer, artifacts, and window are independently testable but share one node/manifest contract.

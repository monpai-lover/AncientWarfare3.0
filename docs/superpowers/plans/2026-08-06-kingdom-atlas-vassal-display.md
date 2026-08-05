# Kingdom Atlas Vassal Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task with verification checkpoints.

**Goal:** Render historical vassals with their suzerain's color while preserving independent boundaries and country labels.

**Architecture:** Persisted `VassalRelation` rows are converted into node-time relation snapshots. `KingdomAtlasRules` resolves display colors and visible owner IDs without mutating physical city ownership. History service supplies snapshots and historical names; rasterizer consumes the node maps.

**Tech Stack:** Existing C#/.NET Framework rules assembly, SQLite archive, pure atlas rasterizer, script-backed focused tests.

---

### Task 1: Add relation snapshot model and pure resolver tests

**Files:**
- Modify: `Code/core/atlas/KingdomAtlasModels.cs`
- Modify: `Code/core/atlas/KingdomAtlasRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/KingdomAtlasRulesTests.cs.txt`

- [ ] Add `KingdomAtlasVassalRelationSnapshot` with relation ID, vassal ID, suzerain ID, names/colors, contract tier, start/end times.
- [ ] Add `KingdomAtlasNode.DisplayColors`, `Kingdoms`, and `VassalRelations` read-only properties.
- [ ] Add `ResolveDisplayOwner`, `BuildDisplayColors`, and `BuildVisibleOwnerIds` pure methods. Resolution must use node time, retain subject IDs, stop on invalid/cyclic chains, and fall back to the subject color.
- [ ] Add tests for direct/nested subjects, ended relations, cyclic relations, and unrelated owners; run the focused rules test and verify the new assertions fail before implementation and pass after it.

### Task 2: Load historical vassal relations and subject metadata

**Files:**
- Modify: `Code/core/atlas/KingdomAtlasHistoryService.cs`

- [ ] Query all persisted `VassalRelation` columns needed for historical display.
- [ ] For every node, filter relation rows by the node's ordered event time/id and build historical kingdom snapshots from event colors/names plus relation row fallbacks.
- [ ] Include event parties and their historical vassals in visible zones; keep `CityOwners` unchanged.
- [ ] Populate node `DisplayColors` and kingdom metadata without reading `World.world`, live kingdoms, or map-mode state.

### Task 3: Render subject colors and labels independently

**Files:**
- Modify: `Code/core/atlas/KingdomAtlasRasterizer.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/KingdomAtlasArtifactRulesTests.cs.txt`

- [ ] Resolve fill colors through `DisplayColors`, but compare physical IDs for boundaries.
- [ ] Generate labels for every visible historical kingdom, using the kingdom's own name and resolved display color.
- [ ] Add a raster test with two adjacent subjects sharing one color and two independent labels/boundaries.

### Task 4: Verify integration and source restrictions

**Files:**
- Test: `Tests/KingdomAtlasSourceGuard.ps1`

- [ ] Keep the source guard rejecting live-world, live-map-mode, and current-color reads from atlas generation code.
- [ ] Run focused rules/artifact tests, source guard, and `git diff --check`.
- [ ] Record any unavailable full-build prerequisites without altering unrelated dirty files.

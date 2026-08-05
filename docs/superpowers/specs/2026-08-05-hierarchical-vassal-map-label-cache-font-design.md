# Hierarchical Vassal Map Labels and Font Selection

## Goal

Keep the hierarchical vassal MapMode visually unchanged while removing
repeated label work during the vanilla zone redraw loop. Add a persistent
MapMode-only font selector with the bundled FZbeiwei font as the default and
several system-font alternatives.

## Scope

This design applies only to the world-space country and city labels owned by
the hierarchical vassal MapMode. It does not change
`LocalizedTextManager.current_font`, family-tree text, diplomacy windows,
civil-service exam windows, or any other AW3 UI.

The vanilla kingdom zone renderer remains authoritative for zone colors,
borders, selection, animation, and water rendering. AW3 only caches and
publishes its additional labels and the required mixed-zone water correction.

## Existing Constraints

- `ZoneCalculator.redrawZones()` periodically invokes the active map asset's
  `draw_zones` callback. AW3 must not replace or bypass that lifecycle.
- The current native draw pass clears `NativeDrawMetaCache` and all native label
  accumulators at the end of every pass, causing repeated ownership and label
  work.
- The asynchronous label pipeline already has cache entries, source
  generations, zone indexes, and dirty hooks for city, kingdom, hierarchy, and
  view changes.
- The current map label layer already avoids rebuilding a `TextMesh` when text,
  position, size, angle, and split-label gap are unchanged.

## Design

### 1. Persistent native draw snapshot

Introduce a native draw snapshot generation keyed by:

- world lifecycle generation;
- selected country/city layer;
- hierarchy focus;
- hierarchy generation;
- current MapMode asset identity.

`NativeDrawMetaCache`, native country aggregates, and native city aggregates
remain valid across vanilla redraws while this key is unchanged. A redraw with
no invalidation reuses the cached representative ownership and existing label
placements. The native zone renderer still runs normally for vanilla colors and
border behavior.

The snapshot is invalidated by:

- city ownership changes;
- city zone geometry changes or city removal;
- kingdom creation/destruction or suzerain changes;
- hierarchy focus or selected layer changes;
- map asset changes;
- world clear/load.

Repeated calls to `setDrawnZonesDirty()` remain allowed. They only cause the
vanilla redraw and do not automatically invalidate the AW3 label snapshot.

### 2. Shared zone geometry cache

Cache immutable zone geometry snapshots keyed by zone ID and a geometry
signature containing the zone tile count, ground count, center, and lifecycle
generation. A snapshot contains the visible-land tile coordinates and a cached
water tile-ID list for mixed zones.

The discovery/build pipeline consumes the snapshot instead of rereading and
reclassifying every `WorldTile` for each label batch. Zone snapshots are
replaced when the existing city-geometry dirty hooks report a change.

The water patch writes the cached water IDs during a native pass. It does not
re-run tile-type checks for every redraw. The cache is cleared on world reset
and invalidated with the zone geometry signature.

### 3. Single-pass label geometry

Refactor pure geometry so one source build produces:

- unique visible-land tiles;
- largest four-neighbour connected component;
- area, centroid, span, and angle metrics;
- candidate anchors and fitted envelope capacity.

`CalculateMetrics()` and `CalculateLabelPlacement()` must not independently
deduplicate or rescan the same tile list. The result is a pure immutable
geometry record safe to consume by the worker task and the cache layer.

### 4. Dirty-only collision layout

Country placement is computed in world coordinates and does not depend on the
camera. Camera movement only controls label visibility/sorting; it does not
invalidate geometry.

The collision pass reuses the vanilla `NameplateText` semantics:

- stable priority ordering;
- cached axis-aligned overlap rectangles;
- `Rect.Overlaps()` equivalent checks.

Unlike vanilla, AW3 does not hide a colliding label. It tries alternate anchors
inside the label's own largest connected component and chooses the position
with the lowest overlap cost. A spatial grid limits checks to nearby accepted
labels instead of comparing every pair.

The collision layout is rerun only when a label's text, font generation,
placement, size, angle, active set, hierarchy focus, or relevant territory
changes.

### 5. TextMesh measurement cache

Add a MapMode font generation and glyph measurement cache. A glyph's measured
bounds are keyed by font identity, glyph text, font style, and probe size.
Outline passes reuse the primary glyph measurement rather than querying
`Renderer.bounds` independently.

Existing `LabelNode` layout guards remain authoritative. A font generation
change forces layout/measurement refresh for existing visible labels without
forcing zone color redraw.

### 6. MapMode font selection

Add a `SELECT` item to the existing `AWPerformanceSettings` configuration group:

| Index | Display | Font candidates |
| ---: | --- | --- |
| 0 | AW3 FZbeiwei | bundled `ABPackages/aw3_fzbeiwei` asset, default |
| 1 | HeiTi / SimHei | `SimHei`, `Heiti SC`, `Chinese Black` |
| 2 | SongTi / SimSun | `SimSun`, `Songti SC`, `Chinese Song` |
| 3 | Microsoft YaHei | `Microsoft YaHei`, `Chinese Yahei` |
| 4 | KaiTi | `KaiTi`, `Kaiti SC`, `Chinese Kai` |
| 5 | FangSong | `FangSong`, `STFangsong`, `Chinese FangSong` |
| 6 | Western fallback | `Arial`, `Noto Sans`, `Liberation Sans` |

The callback receives the selected integer, clamps it to the supported range,
loads/caches the selected font, and increments the MapMode font generation.
System fonts are created with `Font.CreateDynamicFontFromOSFont()` and warmed
with representative CJK and Latin characters. Missing fonts, missing glyphs,
or unavailable materials fall back to the bundled FZbeiwei font and write one
diagnostic warning per option.

Previously created system fonts remain cached for the session so changing the
selection does not repeatedly allocate dynamic fonts or invalidate active
materials. The selected index is player configuration state, not save-world
state.

The option label and all seven choices receive `cz`, `en`, and `ch`
localization entries using the existing `ModConfigureWindow` select-key
convention.

## Data Flow

1. Existing city/kingdom/hierarchy hooks mark the relevant AW3 generation
   dirty.
2. The vanilla `draw_zones` callback paints zones as before.
3. AW3 resolves zone ownership from the persistent native snapshot and only
   rebuilds label aggregates when the snapshot key or dirty generation changes.
4. Dirty label sources use cached zone geometry and run pure geometry work in
   the existing worker pipeline.
5. The label layer publishes only changed `TextMesh` layout/style state.
6. A font selection changes only the font generation, measurement cache, and
   label layout; it does not trigger a world zone redraw.

## Error Handling

- A failed worker build leaves the last accepted label snapshot visible.
- A stale world, hierarchy, or font generation result is rejected without
  touching active labels.
- A missing system font falls back to bundled FZbeiwei.
- If no font can be loaded, use the existing Arial/`current_font` fallback and
  keep the MapMode functional.
- World clear/load clears native snapshots, zone geometry cache, dynamic font
  caches, and label nodes.

## Tests and Verification

Add pure rule/source tests for:

- native snapshot reuse and every invalidation cause;
- zone geometry signature changes;
- cached water list reuse;
- single-pass geometry result equivalence;
- collision dirty gates and stable priority ordering;
- font option clamping, candidate order, fallback, and generation changes;
- default configuration index `0` and all seven localized option keys.

Manual verification must cover:

- large map first entry and repeated redraw at stable state;
- city ownership, zone expansion, hierarchy changes, and map-layer changes;
- main map/minimap sorting layers and label size cap;
- each font option with Chinese and Western country names;
- unavailable system-font fallback;
- switching fonts while labels are visible without changing zone colors or
  other AW3 windows.

## Non-Goals

- No global game-font replacement.
- No replacement of the vanilla kingdom zone renderer.
- No shader, mesh, or curved-boundary work in this change.
- No DLL compilation as part of the design/spec phase.

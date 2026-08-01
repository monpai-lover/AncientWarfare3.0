# Hierarchical Vassal Natural Boundary Mesh Design

## Goal

Replace the hierarchical-vassal map mode's per-zone fill and per-edge
`LineRenderer` rendering with event-driven, chunked meshes that produce
natural polygon borders, preserve political topology, never fill across
water, and remain responsive on large worlds.

## Scope

- Replace both the country layer and city layer rendering.
- Replace both political-region fill and political boundary lines.
- Keep the existing hierarchy navigation, click behavior, labels, and
  nameplates unless integration changes are required to bind them to the new
  snapshot revision.
- Remove the active-mode dependency on `ZoneCalculator.drawZoneMeta` and the
  current one-`LineRenderer`-per-zone-edge implementation.
- Do not redesign the underlying vassal relationship model.

## Visual Hierarchy

The renderer uses three boundary levels:

1. The outer boundary of a suzerain system is the thickest line.
2. Borders between vassal realms inside the same suzerain system use a medium
   line.
3. Borders between cities use the thinnest line.

The country layer displays levels 1 and 2. The city layer displays all three
levels. A suzerain system uses its stable primary color. Vassal realms and
cities receive deterministic variations of that color so adjacent regions
remain distinguishable without losing their shared hierarchy identity.

Boundary ribbons use the owning region's color with a narrow dark outline.
Shader parameters provide anti-aliasing, smooth joins, controlled
transparency, and subtle blending with the map. At ordinary zoom levels the
result should resemble a natural political-map polygon rather than a staircase
of tile-sized right angles.

## Topology And Natural Curves

The authoritative topology starts from actual tile cells, not `TileZone` 8x8
bounds. For each visible land cell, the extractor examines its four sides and
emits an edge only when the opposite side has a different displayed owner or
is not visible land. Shared edges are emitted once.

The raw edge graph identifies:

- 90-degree turns;
- endpoints and closed loops;
- three-or-more-region junctions;
- islands and inland-water holes;
- narrow isthmuses;
- chunk-boundary continuation points.

Raw corner points are topology anchors, not final rendered vertices. Ordinary
stair-step points are simplified with a bounded Douglas-Peucker pass. Protected
junctions, narrow passages, small islands, and chunk continuation anchors are
never removed. The simplified chains are fitted with constrained Catmull-Rom
or cubic Bezier curves and sampled adaptively.

Every proposed smooth segment is checked against the immutable land and
ownership raster. A segment that would cross water, enter a third region,
close a narrow passage, change a junction, or change loop winding has its
smoothing strength reduced. If no safe curve exists, that local chain falls
back to its original topology. Correct ownership always takes priority over
smoothness.

## Water And River Rules

Political fill is clipped to visible land cells. No fill triangle may cover a
water, ocean, or lava cell. Mixed land/water zones are therefore represented by
their real land outline rather than their rectangular zone boundary.

The available tile facts reliably expose liquid and ocean state but do not
provide a river identifier suitable for this renderer. The snapshot therefore
classifies non-ocean liquid components conservatively:

- narrow, continuous, elongated components may be classified as river
  corridors;
- broad or uncertain components remain lakes or generic inland-water holes;
- ambiguous water always falls back to lake behavior.

A river becomes a political boundary only when the land on its two banks has
different displayed owners. In that case, the two-bank topology is unified
into one continuous river-aligned political boundary instead of producing two
unrelated shoreline borders. A river whose banks have the same owner remains
ordinary terrain and does not create a political boundary.

## Chunk Model

The world is partitioned into stable 32x32-tile chunks. Geometry capture adds
a halo around each dirty chunk so contours, rivers, smoothing, and junctions
can continue across chunk seams. The halo contributes topology but is clipped
out of the chunk's final owned triangles and boundary segments.

Each active layer has two render products per chunk:

- a fill mesh grouped into deterministic region-color submeshes or vertex
  colors;
- a boundary mesh with outer-system, vassal-realm, and city submeshes.

Fill and boundary meshes remain separate so minimap capture can keep political
fill while temporarily hiding dense boundary lines. Meshes, materials,
`MeshFilter` objects, and `MeshRenderer` objects are pooled and reused. Runtime
object count is bounded by the chunk count and active layer variants.

## Components

### Boundary Dirty Tracker

`HierarchicalVassalBoundaryDirtyTracker` receives territory and presentation
events:

- city capture or ownership transfer;
- zone addition, removal, reassignment, or destruction;
- kingdom creation or destruction;
- vassal/suzerain relationship changes;
- hierarchy focus or layer changes;
- land/liquid/ocean terrain changes;
- world load, clear, and map-mode activation.

It marks only affected chunks plus their immediate neighboring chunks. Dirty
events are coalesced by chunk ID and revision.

### Immutable Snapshot Capture

`HierarchicalVassalBoundarySnapshotCapture` runs on the main thread with a
bounded per-cycle budget. It copies primitive cell facts for dirty chunks and
their halo:

- coordinates and valid-cell flags;
- land, liquid, ocean, lava, and conservative river classification inputs;
- root suzerain-system ID;
- displayed realm ID for the current hierarchy focus;
- city ID;
- deterministic color values.

No `World`, `Kingdom`, `City`, `TileZone`, `WorldTile`, `UnityEngine.Object`, or
Unity collection is passed to background work.

### Background Topology Worker

`HierarchicalVassalBoundaryTopologyWorker` consumes immutable snapshots and
produces pure mesh drafts. It performs edge extraction, graph construction,
river-corridor analysis, endpoint protection, simplification, constrained
curve fitting, loop and hole validation, triangulation, and submesh
classification.

Work is latest-wins by chunk revision. Results older than the current chunk or
map-mode generation are discarded before upload. Background code does not call
Unity or WorldBox APIs.

### Main-Thread Mesh Layer

`HierarchicalVassalBoundaryMeshLayer` owns pooled Unity objects and applies a
bounded number of completed drafts per frame or authority presentation cycle.
It preserves the last valid chunk mesh until a replacement succeeds. A failed
draft marks the chunk dirty for retry instead of clearing the world overlay.

Country and city drafts may be produced from the same captured cell facts.
Switching layer or hierarchy focus uses a matching cached draft when its
revision is current; otherwise only chunks whose displayed ownership mapping
changed are rebuilt.

### Material Library

`HierarchicalVassalBoundaryMaterialLibrary` loads a shader AssetBundle built
with the same Unity editor version as the supported WorldBox release. The
bundle contains fill and boundary shaders that consume vertex color, boundary
tier, ribbon-side distance, and camera-scale parameters.

If the bundle is absent, incompatible, or fails to load, the renderer logs one
bounded warning and creates functional materials from `Sprites/Default`.
Fallback does not restore simultaneous legacy rendering.

## Rendering And Zoom

Boundary geometry is a ribbon mesh carrying centerline normal/extrusion and
edge-distance data. The shader uses those attributes for anti-aliasing, rounded
visual joins, and the dark outline. Orthographic camera scale is supplied as a
shared material parameter so apparent line width remains readable across zoom
levels without rebuilding geometry.

The fill renderer and boundary renderer use explicit sorting order and stable
Z values below labels and interaction markers. During minimap redraw, fill
roots stay enabled while boundary and label roots are temporarily hidden.

## Update Flow

1. A territory, hierarchy, or terrain hook marks affected chunks dirty.
2. Main-thread capture coalesces events and snapshots a bounded number of
   chunks with halo.
3. A background worker generates country and city mesh drafts.
4. Main-thread upload rejects stale revisions and updates pooled meshes.
5. Labels and click metadata consume the same visible snapshot revision.

The target normal update latency is the next simulation cycle or about 0.5
seconds. Completing a dirty chunk early makes it visible immediately; the
renderer does not wait for every dirty chunk before applying results.

The current every-15-frame whole-world revision hash is removed. A low-cost,
round-robin chunk audit may detect missed hooks, but it may inspect only a
bounded number of chunks per cycle and may not trigger a single-frame full-map
scan.

## Activation And Migration

On first activation, chunks are captured and built incrementally. Completed
chunks appear as they become ready; unbuilt chunks retain the normal map rather
than showing empty or invalid fill.

While the new renderer is active:

- legacy hierarchical-vassal `drawZoneMeta` fill is disabled;
- the current `HierarchicalVassalMapModeBoundaryLayer` LineRenderer path is
  disabled and replaced;
- the two renderers must never overlap;
- existing hierarchy navigation, labels, nameplates, click inspection, water
  restoration compatibility, and minimap guards are updated to use the mesh
  renderer's lifecycle.

World clear, load, or mod shutdown cancels outstanding generations, clears
queues, releases pooled meshes/material instances, and increments the global
generation so late background results cannot enter a new world.

## Failure Handling

- Snapshot mutation or stale object resolution marks the chunk dirty for a
  later capture.
- Background exceptions are isolated to the affected chunk and generation.
- Invalid polygons, self-intersections, winding changes, or failed
  triangulation fall back first to a less-smoothed contour and then to the raw
  tile-edge contour.
- Shader or AssetBundle failures use the built-in material fallback.
- Upload failures retain the last valid mesh and retry with bounded logging.
- Queue growth is capped; repeated dirty events coalesce into the newest
  revision.

## Verification

Pure geometry tests use synthetic ownership and terrain rasters covering:

- rectangles and disjoint regions;
- L-shaped and concave territories;
- islands, lakes, and multiple holes;
- mixed land/water zones;
- narrow isthmuses;
- two-region and three-region junctions;
- contours crossing chunk seams;
- different owners on opposite river banks;
- the same owner on both river banks;
- ambiguous broad inland water;
- stale worker generations;
- smoothing that attempts to cross water or a third owner.

Assertions include:

- no fill triangle covers a forbidden cell;
- no smoothed boundary enters a third region;
- protected junctions and loop winding remain stable;
- river borders are single, continuous chains;
- same-owner rivers do not emit political borders;
- adjacent chunk drafts share identical seam endpoints;
- fallback output remains renderable after smoothing or triangulation failure.

Integration source guards verify:

- no `LineRenderer` remains in the active hierarchical-vassal boundary path;
- no background worker references live WorldBox or Unity objects;
- legacy fill and new fill cannot render simultaneously;
- minimap capture hides boundary meshes but retains fill meshes;
- world clear cancels and invalidates pending results;
- shader fallback is present.

A large-map benchmark simulates sparse zone and ownership changes. A one-zone
change must rebuild only intersecting and neighboring chunks. Runtime checks
must show no per-frame whole-world scan, no unbounded Mesh/GameObject growth,
and no all-map replacement after local changes.

Manual visual acceptance requires:

- natural political-map curves at ordinary zoom;
- no visible tile staircase except where topology safety forces a local
  fallback;
- no cross-water fill or boundary shortcuts;
- continuous river borders;
- no cracks at chunk seams;
- stable three-region junctions;
- readable thick/medium/thin hierarchy;
- correct country/city switching and hierarchy drill-down;
- correct minimap behavior.

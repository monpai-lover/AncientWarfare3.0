# Hierarchical Country Map Native Post-Process Design

## Problem

The hierarchical vassal map renders territory colors before country names.
Country labels currently depend on a separate pipeline that discovers every
country, city, and zone, copies every visible land tile on the main thread in
bounded slices, and then computes label geometry on a worker. A large realm can
therefore remain unlabeled for several seconds. Continuous city-zone changes
can supersede an unfinished world batch and extend that delay.

Switching from the city layer to the country layer also leaves old city text
visible until the new full discovery batch reaches its publication stage.

## Goals

- Reuse WorldBox's native kingdom-zone renderer for the country layer.
- Preserve AW3's hierarchical projection: a subject's zones use the visible
  representative at the current drill-down level.
- Publish all visible country or city names in the same map redraw, without
  waiting for tile copying or background geometry.
- Hide the previous layer's text in the same frame as a layer switch.
- Preserve AW3 world-space text, localized font, dynamic sizing, minimap/main
  map sorting, click behavior, water restoration, and hierarchy drill-down.
- Preserve native Army flags and Army information while the hierarchical map
  mode is active.
- Update only affected representatives after ownership or hierarchy changes.
- Avoid any full-world or per-tile work in `MapBox.Update`.

## Non-Goals

- Changing the established country or city text appearance.
- Restoring the vanilla kingdom nameplate appearance.
- Reintroducing custom polygon, curve, shader, or boundary rendering.
- Persisting presentation caches in save data.
- Moving Unity or WorldBox objects to worker threads.

## Architecture

### Native Country Rendering

The hierarchical country asset delegates `draw_zones` to the native kingdom
map-mode draw function. The hierarchical asset keeps its own zone meta getters,
so the native renderer still asks AW3 which visible meta object owns each zone.
AW3 resolves a physical kingdom through `HierarchicalVassalHierarchyIndex` and
returns the representative for the current hierarchy focus.

AW3 remains responsible only for post-processing that the native kingdom mode
does not provide:

- hierarchical representative projection;
- non-ground and mixed-water cleanup;
- hierarchy-specific click and drill-down behavior;
- custom country text and minimap ordering.

The generic AW3 `DrawZones` loop is not used by the hierarchical country layer.
Other AW3 map modes are unchanged.

### Zone-Level Country Layout Accumulator

Vanilla WorldBox does not maintain a territory centroid, territory bounds, or
territory-sized text scale for kingdoms. Its kingdom nameplate anchors at the
capital `city_center` (also exposed through the kingdom location), while its
alternative nameplate path anchors at a visible actor. Those native anchors
are inexpensive but cannot place a map-spanning country name correctly.

During the native draw pass, each visible land zone contributes one bounded
sample to its representative's accumulator:

- zone identity;
- `centerTile` coordinates;
- `tiles_with_ground` as the sample weight;
- minimum and maximum center coordinates;
- weighted coordinate sums and weighted covariance inputs;
- a reference to the representative kingdom.

No `WorldTile[]` array is traversed or copied. Duplicate zone contributions are
ignored by zone ID. A missing center tile or non-positive land count is skipped.

At the end of the native draw pass, the accumulator derives:

- anchor: weighted mean of zone centers;
- angle: principal direction of weighted zone-center distribution;
- area proxy: sum of `tiles_with_ground`;
- span proxy: accumulated center bounds;
- size: the existing country size rule applied to the area/span proxy and
  clamped by the existing minimum and maximum;
- display text and two-character spacing: existing AW3 formatting rules.

For a representative with too little usable geometry, the fallback order is:

1. representative capital `city_center`;
2. first valid visible city center;
3. first valid zone center.

This matches the native kingdom map's capital-first behavior while retaining
AW3's map-sized text. The capital or kingdom location is never the normal
country-layout input; it is only the first fallback when the draw pass provides
insufficient usable zone statistics.

### Native City Anchors

The city layer follows the native city nameplate's primary map option. Each
valid city uses `city.city_center` as its text anchor. The existing AW3 city
text, font, color, size bounds, and sorting remain unchanged, but city labels no
longer copy city land tiles or run geometry workers. Because city text is small,
territory-envelope fitting and rotation are unnecessary.

Cities are collected from the live city list in one direct pass, capped only by
the existing maximum visible-label count rather than a multi-frame work budget.
A city without a valid center is skipped until a later redraw. Rename,
ownership, and center changes invalidate only that city's presentation entry.

### Army Visibility

The hierarchical map mode remains a territory presentation mode and does not
own or suppress the Army presentation layer. Native `drawArmies` execution,
its retained QuantumSprite group, and the existing Army information text stay
enabled while the mode is active.

The minimap icon filter may continue suppressing ordinary unit avatars, king
icons, leader icons, and other nonessential map markers. Army flags are an
explicit exception: they are not cleared and their draw prefix is not skipped.
They keep the native Army sorting layer and order so the country color overlay
cannot cover them. AW3 country and city text retains its established higher
label order where necessary, without moving Army flags behind the territory
renderer.

### Immediate Publication

The completed zone accumulator and native city anchors are published to the
existing AW3 TextMesh pool as part of the same redraw lifecycle. Country and
city labels do not enter
`HierarchicalVassalLabelDiscoveryJob` or
`HierarchicalVassalLabelBuildJob`, and they never wait for a worker result.

On a layer switch, all labels belonging to the previous layer are hidden
immediately before the native redraw begins. This operation uses the existing
layer-keyed TextMesh pool and does not wait for the new active-key set. Within
the same layer, existing labels remain visible until replacement data for the
same representative is ready. A failed same-layer redraw therefore cannot
clear all labels and leave an empty intermediate frame.

The asynchronous tile discovery/build pipeline is removed from both visible
country and city label paths. Its reusable geometry rules may remain for tests
or unrelated callers, but MapMode activation and layer switching do not start
those jobs.

## Invalidation

Country layout cache entries are keyed by world generation, hierarchy focus,
and representative kingdom ID. City entries are keyed by world generation and
city ID. A country or city rename updates only its text. A city zone or
ownership change marks the old and new visible representatives dirty. A vassal
relationship or drill-down change rebuilds the lightweight hierarchy index and
requests one native redraw, but it does not start a tile-geometry batch.

Repeated mutations before redraw are coalesced in a set of dirty representative
IDs. World clear invalidates the entire accumulator and label cache.

## Failure Handling

- If the native kingdom draw delegate cannot be resolved, AW3 falls back to the
  existing synchronous zone draw adapter, still using zone-level accumulation
  and never tile-level country geometry.
- Invalid or destroyed kingdoms, cities, and zones are skipped.
- A failed redraw keeps the last accepted labels visible and schedules one
  bounded retry; it does not publish a partial active-key set.
- World generation and hierarchy-focus checks reject stale redraw results.
- Exceptions remain isolated through the existing MapBox frame-stage guard.

## Performance Contract

- Country rendering uses the native kingdom draw path.
- Country label layout is `O(visible zones)`, not `O(land tiles)`.
- City label layout is `O(visible cities)` and uses `city_center` directly.
- A zone is sampled at most once per redraw.
- Army rendering performs no new world, zone, or unit scan; AW3 simply stops
  cancelling the native Army draw and retained flag group.
- No country/city label tile copy, worker task, or full-world label discovery
  runs after entering either layer.
- Entering or switching layers publishes visible names in the same redraw, with
  a practical target of one to two rendered frames after the click.
- The previous layer is hidden in the click frame and cannot remain visible
  while the next layer prepares.
- Territory changes request one native redraw for the affected presentation
  state and never create or restart a world label batch.

## Tests

Rules tests will cover:

- weighted zone-center anchor and deterministic angle calculation;
- capital, city, and zone fallback order;
- direct city-center placement;
- duplicate and water-only zone rejection;
- size clamping from the area/span proxy;
- dirty representative coalescing;
- keeping accepted labels visible until a complete replacement set exists;
- stale world/focus redraw rejection.

Source guards will require:

- the hierarchical country asset to use the native kingdom draw delegate;
- the country layer to feed a zone-level accumulator during the native pass;
- country and city labels to bypass tile-copy and worker build jobs;
- city labels to use the native `city_center` placement rule;
- layer selection to hide the previous label layer immediately;
- hierarchical minimap filtering to preserve native Army flag drawing and
  avoid clearing the Army QuantumSprite group;
- water restoration, custom TextMesh, and minimap ordering to remain intact.

Verification will run the complete rules suite, hierarchical label rules, all
hierarchical map source guards, `git diff --check`, deployment hash comparison,
and an in-game large-map check for immediate country-name publication.

## Acceptance Criteria

- On the reported large map, switching to the hierarchical country layer never
  shows colored countries without their names for several seconds.
- Country names appear within one to two rendered frames after the native zone
  redraw completes.
- City names appear from live city centers without a geometry backlog.
- Switching layers removes the old layer's text in the same frame.
- Country labels retain AW3 font, size cap, sorting, color, and hierarchy text.
- City labels retain their existing visual style.
- Army flags and Army information remain visible on both country and city
  hierarchy layers, including after entering and leaving minimap rendering.
- Drill-down, returning to root, minimap capture, water visibility, and zone
  clicks continue to work.
- No measurable multi-frame country-label worker backlog remains in the recent
  benchmark after entering the country layer.

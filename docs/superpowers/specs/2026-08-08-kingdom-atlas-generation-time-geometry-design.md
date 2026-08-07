# Kingdom Atlas Generation-Time Geometry Design

## Context

The kingdom atlas currently has two competing geometry paths:

- the intended path captures the current world terrain and city-zone mapping when the player requests an atlas preview or export;
- the legacy path scans every tile in a city's zones during city founding, transfer, and destruction, then persists those tiles in `KINGDOM_ATLAS_ZONE_ARCHIVE`.

The legacy path does not match the product requirement. The atlas is a historical ownership replay over the map geometry that exists when generation begins. Runtime city events should record ownership facts, not map pixels or tiles.

Zero-population cities are retained by the current city lifecycle rules, so the atlas does not need a special pre-destruction geometry snapshot to preserve cities that disappear because their population reaches zero.

## Decision

The kingdom atlas will use one geometry source: a frozen snapshot captured when preview or export generation begins.

During normal simulation, the chronicle will continue to persist city ownership events such as `city_found` and `city_transfer`. No founding, transfer, zone-addition, destruction, save, or annual-update path may scan city zones for atlas persistence or write atlas tile rows.

When generation begins, the atlas will:

1. Capture the current terrain once at the selected capture resolution.
2. Build the current `cityId -> zone cells` mapping from the captured world state.
3. Read persisted city ownership events.
4. Replay those events for each atlas node to obtain `cityId -> historical kingdomId`.
5. Project the historical owner onto the frozen current geometry.
6. Reuse that same frozen geometry for every PNG page and every GIF frame in the generation session.

This means atlas pages intentionally show historical ownership using the city-zone geometry present at generation time. They are not intended to reconstruct the exact physical borders that existed at each historical date.

## Runtime Data Flow

Normal simulation writes only compact event data:

```text
city founded/transferred
  -> CityHistory and KingdomHistory ownership events
  -> no zone iteration
  -> no tile inserts
```

`City.addZone` may continue to invalidate the live map-mode geometry cache because that cache serves the current interactive map. It must not trigger atlas archival work.

Saving the world checkpoints the ordinary lineage/history database. It does not perform a special atlas tile flush.

## Generation Data Flow

Preview and export share the same generation-time capture behavior:

```text
player requests preview/export
  -> capture current terrain and city IDs once
  -> build historical nodes from ownership events
  -> replay city owners at each node
  -> render pages against the frozen capture
  -> optionally persist generated PNG/GIF artifacts
```

The capture belongs to the atlas window or generation session. Changing node pages must not rescan the world. Starting a new generation session may take a new capture so newly acquired zones appear in newly generated output.

## Legacy Compatibility

The `KINGDOM_ATLAS_ZONE_ARCHIVE` table may remain in the database schema so old saves open without destructive migration. New code will neither read nor write it.

Existing archived tile rows are ignored. Removing the table or deleting its historical rows is out of scope because that would add migration risk without improving runtime behavior.

The `KingdomAtlasZoneArchiveService` runtime dependency will be removed from:

- city founding and transfer chronicle hooks;
- city destruction patches;
- atlas history node construction;
- save preparation and checkpoint validation.

The service and table model may then be deleted from compiled code if no schema bootstrap requires their types. If schema registration still requires the table item, only the inert table definition remains.

## Failure Handling

If generation-time terrain capture fails, atlas generation stops and reports the capture error. It must not fall back to legacy archived tiles because that would silently restore the unwanted runtime persistence contract and can produce mixed geometry.

Missing city IDs in historical replay are rendered as unowned for that node. A city that did not yet exist at a historical node therefore contributes no colored territory even if its current geometry is present in the frozen capture.

## Performance Invariants

- No atlas code runs per actor AI tick.
- No atlas code iterates `city.zones` from `City.addZone`, city founding, city transfer, city destruction, save, or annual update hooks.
- No atlas tile-level SQLite insert occurs during simulation.
- One generation session performs at most one live terrain capture.
- Page rendering scales with output resolution and node count only after the player explicitly starts preview or export generation.

## Verification

Source guards and rule tests will verify:

- atlas archive capture calls are absent from all city lifecycle patches and chronicle hooks;
- atlas history construction does not read `KingdomAtlasZoneArchiveService`;
- save preparation has no atlas-specific archive flush;
- preview and export both use the same frozen `ArmyRtsPlanWorldTerrainSnapshot`;
- historical ownership is projected by city ID without using current kingdom ownership;
- a city missing from ownership replay remains unowned;
- repeated page rendering reuses the capture instead of rescanning the world.

A build and source-guard run must pass before deployment. Runtime validation must first confirm that Ancient Warfare 3 loads successfully; performance logs from a run in which the mod was disabled are not valid evidence.

## Rejected Alternatives

### Preserve snapshots only for destroyed cities

This still introduces runtime zone scanning and tile persistence, while zero-population cities no longer disappear. It preserves a capability the atlas does not require.

### Periodic full-map snapshots

Periodic snapshots could reconstruct historical physical borders more accurately, but their storage and simulation cost directly conflict with the performance-first requirement.

### Continue reading old snapshots as a fallback

Mixing archived and generation-time geometry makes output dependent on save age and archive completeness. A single explicit geometry source is more predictable and testable.

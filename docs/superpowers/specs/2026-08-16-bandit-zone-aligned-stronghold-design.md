# Bandit Zone-Aligned Stronghold Design

## Goal

Make a newly generated bandit stronghold own exactly the native WorldBox
territory enclosed by its wooden wall. The smallest valid stronghold owns one
native `TileZone` (an 8 by 8 tile region). Land outside the wall remains with
the mother city.

This design supersedes the majority-coverage selection in
`2026-08-16-bandit-wall-zone-fit-design.md`.

## Native Constraint

WorldBox city ownership is stored on `TileZone`, not individual `WorldTile`
objects. Every `WorldTile.zone_city` resolves through its fixed 8 by 8 zone,
and city save data persists only zone coordinates. A zone cannot be split
between the stronghold and mother city without replacing the original city,
AI, border-rendering, and save systems.

The stronghold therefore uses complete native zones and makes the visible
wall conform to those zones.

## Territory Planning

Planning begins with the mother-city zone containing the selected civic core.
That seed is always included, so the minimum territory is one zone.

The existing Cultiway-style building bounds and six-tile margin remain the
desired size signal. The planner projects that desired enclosure onto the
smallest connected set of complete mother-city zones that covers the desired
core area. It removes unnecessary edge zones while preserving:

- the seed zone;
- connectivity through cardinally adjacent zones;
- all protected civic-core buildings needed by the new stronghold;
- at least one retained zone for the mother city.

If the desired area fits in the seed zone, the stronghold receives exactly
one zone. Additional zones are included only when the desired core enclosure
crosses their native boundaries. Disconnected zones are never transferred.

## Wall Planning

The bandit stronghold receives a dedicated zone-aligned wall planner. It
constructs the wooden wall from the outer tile edge of the selected zone
union. Consequently, every non-wall tile reached from the stronghold center
without crossing the perimeter belongs to a selected stronghold zone, and
every tile immediately beyond the perimeter belongs to an unselected zone.

Diagonal gaps are sealed using the existing Cultiway-style geometry rules.
The wall keeps the existing four-direction gate rule: one three-tile opening
is carved on each of the north, south, east, and west sides. Each opening is
chosen from the corresponding side near its midpoint using the existing
passable-land and road preference. Gates are carved only after the closed
logical perimeter is recorded, so an opening does not change territory
membership or let the logical enclosure leak into the mother city. Coastal
and blocked tiles use the existing original-terrain placement checks.

This planner is used only for bandit stronghold creation. The shared
`CultiwayStyleCityWallService`, ordinary city walls, and mandate frontier
walls retain their current geometry.

## Creation And Persistence

Preflight produces one authoritative plan containing the selected zones,
closed wall perimeter, carved wall points, center zone, and retained
mother-city zones. Commit creates the stronghold with the center zone, moves
exactly the planned zones through original `City.addZone`, and then places
the planned original wooden-wall tiles.

`FixedZoneKeys` continues to persist the selected zone coordinates and
`WallPoints` continues to persist the placed wall coordinates. Stronghold
fall returns all persisted stronghold zones to the mother city through the
existing transaction.

Existing saved strongholds are not migrated or rebuilt. Only strongholds
created after this change use zone-aligned walls.

## Failure Handling

Creation fails before mutation when no seed zone exists, the selected zones
are disconnected, the mother would retain no zone, or a closed perimeter
cannot be generated. Existing rollback restores zones, actors, walls, city
ownership, and government state in reverse order.

## Verification

Pure geometry tests cover:

- a one-zone stronghold and its exact perimeter;
- expansion to multiple zones when the desired core crosses a zone edge;
- removal of unnecessary edge zones;
- cardinal connectivity and diagonal-gap sealing;
- four three-tile cardinal gates without changing the closed logical
  enclosure;
- rejection when the mother city would retain no zone.

Source/runtime guards reject the old 50-percent coverage rule and require the
same selected zone set to drive both `City.addZone` and wall generation.

After focused tests pass, build the net48 project, deploy the source tree with
a timestamped backup, launch WorldBox visibly, and verify that a god-power
stronghold shows only its wall-enclosed native zones under the city map mode.

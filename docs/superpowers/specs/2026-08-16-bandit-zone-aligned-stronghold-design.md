# Fixed Four-Zone Bandit Stronghold Design

## Goal

Make every newly generated bandit stronghold own exactly four native WorldBox
`TileZone` regions. The wooden wall follows the union of those four zones and
land outside the wall remains with the mother city. When the stronghold is
suppressed, its zones return to the mother city and its wooden wall disappears,
restoring the top terrain that existed before construction.

This design supersedes both the majority-coverage selection in
`2026-08-16-bandit-wall-zone-fit-design.md` and the variable-size selection in
the previous revision of this document.

## Native Constraint

WorldBox city ownership is stored on `TileZone`, not individual `WorldTile`
objects. Every `WorldTile.zone_city` resolves through its fixed 8 by 8 zone,
and city save data persists only zone coordinates. A zone cannot be split
between the stronghold and mother city without replacing the original city,
AI, border-rendering, and save systems.

The stronghold therefore uses complete native zones and makes the visible
wall conform to those zones.

## Territory Planning

Planning begins with the mother-city zone containing the hall, then the
bonfire, then the city tile. That seed is always selected. The planner chooses
exactly three more mother-owned zones through cardinal adjacency.

Candidate four-zone sets are the complete 2 by 2 native-zone blocks containing
the seed. Ties use distance from the seed and stable zone coordinates. Irregular
four-zone shapes are not accepted. A mother city with exactly four zones may
transfer all four because the existing empty-city protection keeps the mother
city alive until suppression returns the zones. The original city's usual
growth threshold does not change the stronghold size: successful creation
always yields `stronghold.zones.Count == 4`.

Preflight evaluates candidates in that ranking order and selects the first one
whose terrain can support the required four-gate wall. A blocked coastal
candidate therefore falls through to the next compact connected candidate
instead of making the whole creation fail.

## Wall Planning

The dedicated bandit wall planner constructs the wooden wall from the outer
tile edge of the selected four-zone union. Consequently, every non-wall tile
inside the perimeter belongs to the stronghold and every tile immediately
beyond it remains outside the stronghold.

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

Preflight produces one authoritative plan containing exactly four selected
zones, the carved wall points, center zone, and retained mother-city zones.
Commit creates the stronghold with the center zone, moves exactly the four
planned zones through original `City.addZone`, and then places the original
`wall_wild` wooden-wall tiles.

`FixedZoneKeys` persists the four selected zone coordinates. Every persisted
wall point also records the identifier of the top tile that construction
replaced; an empty identifier represents no prior top tile. This state is
written before the active phase and survives save/reload.

Stronghold fall performs cleanup in this order:

1. mark the state as falling;
2. return residents and all four zones to the mother city;
3. for each recorded wall point, restore its original top tile only when the
   current tile is still `wall_wild`;
4. mark the state completed and remove the stronghold city.

The current-tile check preserves any later terrain replacement. Schema-2
strongholds have no recorded original top type; they remove a surviving
`wall_wild` by restoring `null`, exposing the native main terrain. Completed
states do not run wall cleanup again.

Existing saved strongholds are not migrated or rebuilt. Only strongholds
created after this change use zone-aligned walls.

## Failure Handling

Creation fails before mutation when no seed zone exists, no complete 2 by 2
mother-owned block containing it can be selected, or a four-gate perimeter
cannot be generated. The failure log records the
specific planning stage and relevant zone counts. Existing transactional
rollback restores zones, actors, walls, city ownership, and government state
in reverse order.

## Verification

Pure rule and geometry tests cover:

- exact selection of four zones from a larger city;
- preference for a 2 by 2 block containing the civic-core zone;
- rejection when no complete 2 by 2 block containing the seed exists;
- acceptance when a protected mother city transfers its final four zones;
- cardinal connectivity and diagonal-gap sealing;
- four three-tile cardinal gates without changing the closed logical
  enclosure;
- exact prior-top restoration data for every planned wall point;
- idempotent wall cleanup that ignores tiles no longer using `wall_wild`.

Source/runtime guards reject flood-fill selection and require the same
four-zone set to drive both `City.addZone` and wall generation. They also
require `CompleteFall` to restore walls before removing the stronghold.

After focused tests pass, build the net48 project, deploy the source tree with
a timestamped backup, launch WorldBox visibly, and verify that a god-power
stronghold reports exactly four zones under the city map mode. Capture the
stronghold and verify its city is removed, all four zones return to the mother,
and the recorded wooden walls disappear without overwriting later terrain.

# Outward Bandit Walls And Mandate Frontier Walls

## Goal

Move bandit city walls farther away from city buildings and replace Mandate
full-city wall rings with walls that follow the actual land frontier against
independent non-allied kingdoms.

This specification supersedes only the wall-margin and Mandate wall-geometry
sections of `2026-08-15-cultiway-city-wall-and-bandit-naming-design.md`.
Naming, government transitions, wall assets, and persistence behavior remain
unchanged.

## Confirmed Design

- Use the actual kingdom-contact frontier for Mandate border walls.
- A qualifying foreign neighbor is a living, non-neutral kingdom that is not
  in the Mandate political system, not in the same alliance, not a Mandate
  vassal, and not a Mandate tributary.
- Unclaimed land, water, allies, vassals, tributaries, and other territory in
  the Mandate political system do not create frontier walls.
- Bandit and Mandate walls continue to use one shared Cultiway-style wall
  facility, but call separate enclosure and frontier planning operations.
- Existing fixed wall tiles are not removed when diplomacy or government
  changes.

## Bandit Enclosure Geometry

Bandit governments continue to build one complete, one-tile-wide
`TopTileLibrary.wall_wild` enclosure around each owned city. Increase the
building-bound margin from three tiles to six tiles, giving approximately
three additional tiles of clearance on every side.

The enclosure remains clipped to connected, passable territory owned by the
city. Existing Cultiway-style diagonal closure, road gates, dock passages,
and core-reachability behavior remain unchanged. If a city does not own
enough land for the full six-tile clearance, the wall follows the farthest
valid connected boundary available inside that city.

## Mandate Frontier Geometry

The shared wall facility gains a frontier-planning operation. For each
selected border city it performs these steps:

1. Inspect cardinal neighbors of passable tiles owned by that city.
2. Select each own-side tile that directly touches land belonging to a
   qualifying foreign kingdom.
3. Add one inward layer of passable tiles owned by the same city, producing a
   two-tile-wide `TopTileLibrary.wall_order` frontier wall.
4. Add orthogonal bridge tiles where diagonal wall points would otherwise
   leave a corner gap, without crossing water, mountains, city ownership, or
   map bounds.
5. Carve a three-tile passage where an existing road reaches the wall. Do not
   add city-ring directional fallback gates or dock gates because an open
   frontier segment is not a closed city enclosure.
6. Place wall tiles with `WorldTile.setTopTileType`; do not use
   `MapAction.terraformTop`, remove buildings, or mutate city zones.

The wall is always placed on the protected city's side of the border. Corner
contact alone does not create a new frontier segment; diagonal handling only
connects segments already discovered from cardinal contact.

## Diplomatic Boundary Policy

One shared runtime predicate determines whether a neighboring kingdom is a
fortification target. It rejects:

- the Mandate kingdom itself;
- any kingdom whose recursive root suzerain is the Mandate kingdom;
- a direct Mandate tributary;
- a member of the Mandate kingdom's current alliance;
- neutral, destroyed, or missing kingdoms.

Every other living kingdom is a target even when no war is active. The same
predicate is used by border-city selection, border scoring, frontier-wall
planning, watchtower candidates, and border-army patrol selection so those
systems do not disagree about which side is the frontier.

## Repeated Decisions And Relationship Changes

Wall placement is idempotent: an existing correct `wall_order` tile is
counted as planned but not changed. A later border-defense decision can add
wall segments along newly qualifying borders.

The system does not remove old walls after an alliance, vassalage, tribute,
city transfer, government conversion, or kingdom extinction. This preserves
the established fixed-wall policy and avoids destroying unrelated original
or mod-created walls.

## Failure Behavior

Missing wall assets, invalid cities, absent world state, empty qualifying
frontiers, or terrain with no valid own-side placement fail closed. One city
failure does not prevent other selected border cities from being processed.
History continues to report only the number of wall tiles whose top-tile type
actually changed.

## Tests And Acceptance

Detached rules tests cover:

- a cardinal hostile contact producing an own-side two-layer wall;
- allies, recursive vassals, direct tributaries, neutral kingdoms, water,
  and unclaimed land producing no wall;
- an independent non-allied kingdom producing a wall without an active war;
- diagonal corner closure without creating walls from corner-only contact;
- road passages remaining three tiles wide;
- terrain and city-ownership clipping;
- repeated planning and placement being idempotent;
- the bandit enclosure using a six-tile margin.

Runtime source guards require the Mandate wall path to call frontier planning,
require all border systems to use the shared diplomatic target predicate,
preserve `wall_wild`, `wall_order`, and `setTopTileType`, and forbid
`MapAction.terraformTop` in both wall paths.

Acceptance in WorldBox:

1. A bandit transition creates visibly roomier wooden city walls than the
   previous three-tile-margin version.
2. A Mandate border-defense decision creates double stone walls only along
   actual land contact with independent non-allied kingdoms.
3. No Mandate wall appears along allied, vassal, tributary, water, or
   unclaimed boundaries.
4. Roads crossing a Mandate frontier retain a visible three-tile passage.
5. Repeating the decision does not duplicate or shift existing walls.

# Bandit Government Design

## Goal

Promote the peasant-rebel bandit route into a formal, selectable government
state. This makes the route easy to test from the existing policy UI while
keeping its diplomacy, territory, wall, naming, title, persistence, and
multiplayer behavior consistent.

## State Model

Add `KingdomPolicyDefs.ClassBandit` with persisted ID `peasant_bandit`.
Allowed manual transitions are:

```text
peasant_rebel -> peasant_bandit -> peasant_rebel -> ordinary class
```

- Only a current peasant-rebel government may enter the bandit government.
- A bandit government may switch only to the peasant-rebel government.
- It cannot jump directly to an ordinary class.
- After returning to the peasant-rebel government, existing ordinary class
  transitions are available.
- AI route selection uses the same transition service as the policy UI.

`POLICY_CLASS_STATE` is the current-government source of truth. The existing
`MANDATE_REBEL_ROUTE` field remains as compatible route metadata for origin,
history, and old saves. Runtime reads reconcile the two fields instead of
letting them diverge.

## Entering The Bandit Government

The transition first validates that the kingdom:

- is authoritative, alive, and currently a peasant-rebel government;
- has a valid persisted origin kingdom;
- owns at least one valid city;
- has the WorldBox managers required by the transition.

On success it performs the full bandit entry behavior:

1. Retain every currently owned city.
2. Persist every retained city ID as entry territory.
3. End every active war through `WarManager.endWar(..., WarWinner.Peace)`.
4. Persist the bandit route and `peasant_bandit` class.
5. Rename the realm to `<persisted root>贼` through the shared rename
   projection.
6. Project `大当家` for the ruler and `少当家` for the heir.
7. Capture and build the fixed national-border wooden wall.
8. Record the existing bandit route history event.

The persisted origin remains the only kingdom allowed to initiate direct
suppression. Bandits cannot initiate wars.

## Territory Rules

The entry territory is a persisted whitelist of city IDs.

- The transition never gives current cities back to the origin.
- A bandit realm cannot acquire a city outside the whitelist.
- It may recover a whitelisted city that it lost after entry.
- While in bandit government, all capture, direct transfer, and peace
  settlement boundaries enforce this rule.
- Leaving bandit government for the peasant-rebel government disables the
  whitelist restriction without deleting the persisted historical list.

This replaces the previous single-city invariant for bandit governments.

## Fixed National-Border Wall

At entry, wall capture operates on the union of all zones belonging to all
current kingdom cities:

- Recalculate each current city's neighboring zones through the original city
  API.
- A wall candidate is an eligible tile inside the kingdom whose neighboring
  tiles touch land outside the kingdom.
- Borders between two cities of the same kingdom are excluded.
- Roads, buildings, water, lava, blocked terrain, existing top tiles, and
  other original-invalid positions remain gaps.
- Placement uses only `TopTileLibrary.wall_wild` and
  `WorldTile.setTopTileType`.
- Sorted coordinates are persisted once. Later city or zone growth does not
  move or extend the wall.
- Annual repair inspects only persisted coordinates and retains the existing
  bounded repair budget. Origin suppression pauses repair.
- Conversion and kingdom extinction never remove wall tiles.

Cultiway's `WallShapeHelper` informed the passage and original-API principles,
but its building-bounds rectangles, staged inner/outer walls, towers, and
dynamic rebuild behavior are not copied because they do not represent the
entry-time national border.

## Leaving The Bandit Government

The only direct exit is `peasant_bandit -> peasant_rebel`. It reuses the
existing bandit-to-founding transition:

- persist route `founding` and class `peasant_rebel`;
- rename the realm to `<persisted root>义军`;
- stop wall repair and disable the entry-territory whitelist;
- retain every existing wooden wall tile;
- restore founding-route mobilization, expansion, and Mandate behavior;
- immediately restart the rebellion war against a valid surviving origin;
- record the existing conversion history event.

When the user subsequently changes `peasant_rebel` to an ordinary class, the
normal rebel settlement boundary must clear active rebel flags, the active
route field, rebel traits/titles, and the `义军` suffix. The visible kingdom
name returns to the persisted root. Historical origin, territory, and wall
records may remain inert for archive compatibility.

## Policy UI And Multiplayer

Add the bandit class to the existing class-state chooser with localized name,
description, icon, and disabled-condition text. The UI continues to submit
`AW3CommandRequest.SetPolicyClass`; it does not mutate kingdom data directly.

The authoritative policy command handler dispatches the three special
transitions before generic class assignment:

- peasant rebel to bandit;
- bandit to peasant rebel;
- peasant rebel with route metadata to an ordinary settled class.

All world and persisted mutations are rejected in replica sessions and apply
only through the authoritative command path. Replica presentation may read
and display the resulting class and route.

## Save Migration

Runtime restore reconciles old saves without replaying entry effects:

- active `bandit` route plus rebel government becomes `peasant_bandit` on
  authority;
- active `founding` route remains `peasant_rebel`;
- `peasant_bandit` without compatible route metadata is repaired to `bandit`
  only when its origin and entry data are valid;
- ordinary governments do not retain an active route;
- migration never ends wars, renames realms, captures territory, or builds
  walls.

Old one-city bandits receive an entry-territory whitelist containing their
currently owned valid city IDs. Their existing fixed wall coordinates remain
unchanged.

## Failure Handling

Preflight validation occurs before policy-class mutation. If it fails, the
old government and route remain unchanged and the policy command reports
failure.

After the transition commits, an individual wall-tile placement failure does
not roll back diplomacy or government state. The full coordinate list remains
persisted and bounded annual repair can fill eligible missing positions.
Malformed wall JSON, missing cities, or lost origin data must fail closed:
they cannot grant new territory, start arbitrary wars, or delete the kingdom.

## Verification

Detached rules tests cover:

- allowed government transitions;
- old-save class/route reconciliation;
- entry-territory whitelist acquisition;
- national-border membership and same-kingdom internal-border exclusion;
- staged bandit-to-rebel-to-ordinary exit;
- replica mutation rejection.

Source guards verify:

- the UI uses the existing multiplayer policy command;
- authority checks precede class, route, war, wall, and territory writes;
- original wall and war APIs remain in use;
- route code never deletes kingdoms or wall tiles;
- policy labels, titles, and history keys are localized.

Acceptance testing covers AI and manual entry, multi-city retention, fixed
national-border walls, peace, origin suppression, whitelist recovery, both
exit stages, save/load migration, and host/replica consistency.

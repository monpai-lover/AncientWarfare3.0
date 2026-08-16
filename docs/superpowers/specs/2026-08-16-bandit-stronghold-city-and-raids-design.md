# Bandit Stronghold City And Food Raids Design

## Scope

This design extends the peasant-bandit route with a real stronghold city,
fixed wall-bound territory, a test god power, route-specific ceremonial
titles, and physical food-raiding parties.

The canonical bandit kingdom name remains the outlaw root, for example
`虎踞`. The kingdom nameplate remains `虎踞贼`. The stronghold city is named
`虎踞寨`. Living ceremonial titles are `虎踞寨大当家` and
`虎踞寨少当家`.

## Core Model

Each active bandit kingdom owns exactly one stronghold city. The stronghold
is split from one persistent mother city. A mother city can have at most one
active stronghold, and a stronghold cannot be used as another mother city.

The original mother city object and ID remain stable. A new city object is
created for the stronghold. This preserves the mother city's history,
technology, leadership, and archive identity.

The bandit kingdom persists:

- stronghold city ID;
- mother city ID;
- original mother kingdom ID;
- fixed stronghold zone coordinates;
- wooden wall coordinates;
- active stronghold state;
- raid mission state and cooldown;
- kingdoms that currently possess temporary suppression rights.

## Stronghold Geometry

Wall size and shape continue to use the existing Cultiway-style city wall
planner. A god-power click selects the mother city only; it does not become
the wall center. The wall still encloses the mother's main building core by
the current six-tile-margin algorithm.

City territory moves only in complete WorldBox `TileZone` units. A zone is
assigned to the stronghold when:

1. its center tile lies inside the wooden wall;
2. its center can be reached from the stronghold center through the wall
   interior; and
3. it belongs to the selected mother city during preflight.

All other mother-city zones remain with the mother city. The transaction is
invalid unless both the stronghold and mother city retain at least one zone.

While the kingdom remains bandit, its stronghold may not acquire zones
outside the persisted fixed set. The mother city can grow normally outside
the wall. If the bandit kingdom converts back to the founding peasant-rebel
route, the stronghold remains a normal city, the wooden wall remains, and
zone growth is unlocked.

## Creation Transaction

`PeasantRebelBanditStrongholdService` owns the lifecycle. Automatic route
selection, manual government switching, and the god power call the same
preflight and commit entry point.

Preflight computes all wall and zone geometry and validates the mother city,
leader, mother kingdom, world managers, original wall asset, population, and
minimum viable split without mutating world state.

For automatic or manual route conversion, the existing rebel kingdom becomes
the bandit kingdom. For direct god-power creation, a local adult becomes the
ruler and the original WorldBox kingdom manager creates the new bandit
kingdom. Original kingdom metadata is copied using the same native pattern as
`City.makeOwnKingdom`.

When an existing multi-city rebel converts, the selected founding/mother city
is split and every other ordinary rebel city is returned to the original
mother kingdom. The bandit kingdom finishes the transaction owning only its
single new stronghold.

Commit performs the following ordered transaction:

1. create a real stronghold with `CityManager.newCity` in the enclosed center
   zone and run the original city initialization event;
2. replace the generated city name with `<outlaw root>寨`;
3. move enclosed zones to the stronghold using `City.addZone`;
4. let buildings follow their owning zones through the original city model;
5. move actors physically inside the wall into the stronghold and bandit
   kingdom;
6. force the ruler into the stronghold regardless of current position;
7. retain outside actors in the mother city and return the mother city to its
   original kingdom;
8. build and persist the wooden wall and mother/stronghold relationship;
9. activate the formal bandit government and refresh naming projections.

If the outside mother city has no adult resident or civic core after the
spatial split, the transaction reserves a minimum viable local household and
rebuilds an original basic civic core in the nearest retained outside zone.
This fallback applies only when the normal position-based split would leave
the mother city unable to operate.

Every mutation records enough prior state for reverse-order rollback. A
failure removes newly created objects and restores zones, actors, city and
kingdom ownership, walls, titles, and route metadata.

## Stronghold Fall

The city-capture boundary recognizes an active bandit stronghold before the
original transfer is applied. Capturing a stronghold does not transfer or
occupy it as a normal city.

The settlement is ordered and idempotent:

1. move surviving stronghold residents into the persistent mother city and
   its current kingdom;
2. return every stronghold zone to the mother city;
3. recalculate mother-city and neighbouring-zone geometry;
4. remove the stronghold through the original `CityManager.removeObject`;
5. allow the now cityless bandit kingdom to become extinct through the
   existing kingdom lifecycle.

The mother city is assumed to remain available because this mod already
prevents natural city destruction. Duplicate capture callbacks observe the
completed marker and perform no second transfer or removal.

## Ceremonial Titles

A shared pure title composer receives the canonical outlaw root and localized
route role. It produces:

- `<root>寨` + localized `大当家` for the living ruler;
- `<root>寨` + localized `少当家` for the current heir.

The composer is used by kingdom-window labels, genealogy projections,
ceremonial archive snapshots, household views, and other existing shared
appellation consumers. It never writes `寨`, `大当家`, or `少当家` into
`Kingdom.name`.

## God Power

A localized god power named `在此地放出土匪` is added with an existing
bandit/rebel visual asset. It accepts only a tile whose zone belongs to an
existing ordinary city.

Clicks on unowned land, non-city zones, active strongholds, or mother cities
that already own a stronghold fail with localized feedback. The power does
not search for the nearest city. It may create bandits directly from an
ordinary city without first changing that kingdom to the peasant-rebel
government.

The god power is a test and player-control entry point, not a separate
implementation. It calls the same authoritative creation transaction as AI
and manual government changes.

## Food Raid Lifecycle

Only active bandit strongholds raid. A raid is considered when stronghold food
is below `population * 2`, no raid is active, and the one-year post-mission
cooldown has expired.

Targets are land-reachable neighbouring cities owned by non-allied kingdoms.
Other active strongholds and cities without enough spare food are excluded.
Candidates are ordered by shorter route and greater stealable food.

A raid party contains three to eight real stronghold warriors, or at least
one when fewer are available. An existing general leads when possible. The
ruler and heir are never forced to participate. Raiders use original actor
pathfinding and physically travel to the target city.

Arrival does not occupy the city and does not automatically start a war. Food
is removed with the original city inventory API and carried by the mission
until surviving raiders return to the stronghold. If the party is wiped out,
the carried food is lost.

The transferred amount is the minimum of:

- the amount needed to bring the stronghold to `population * 5`;
- 25 percent of the target city's current food; and
- the target's food above its protected reserve of `target population * 2`.

The victim gains a three-year right to declare a suppression war against the
bandit kingdom. A later raid refreshes the expiry. The original mother kingdom
always retains suppression rights. A raid itself does not bypass the normal
war manager; if the victim chooses to declare, subsequent field combat uses
the original combat system.

Mission state records party members, target, carried food, stage, route facts,
and cooldown. Load recovery resumes a valid travel or return stage. Invalid
missions safely return surviving actors to the stronghold without creating or
destroying resources.

## Authority And Persistence

All city, zone, actor, resource, wall, government, and war-right mutations run
only on the simulation authority and are replicated through the existing AW3
multiplayer command and restore boundaries.

Restore validates IDs and fixed-zone coordinates, rebuilds runtime indexes,
and repairs recoverable ownership caches. It does not replay stronghold
creation, reroll names, rebuild entry walls, repeat resource transfers, or
reissue completed raid rewards.

## Failure Handling

Creation uses preflight plus rollback rather than partial best-effort writes.
Capture settlement and raid completion use persisted phase markers so repeated
callbacks are harmless. A failed inventory mutation restores both city
inventories to their observed pre-transfer amounts.

No route path creates custom wall assets, custom city containers, or custom
war objects. Original WorldBox APIs remain the mutation boundary wherever an
equivalent API exists.

## Verification

Detached rules tests cover wall-enclosed zone selection, connectivity,
minimum split viability, fixed-zone acquisition, title composition, god-power
target validation, food thresholds, target ranking, raid quantity, cooldown,
and conservation.

Runtime/source tests cover native city APIs, rollback ordering, capture
idempotence, restore non-replay, authority guards, localization keys, and the
single shared creation entry point.

Integration verification covers:

- creating a bandit stronghold from an ordinary city with the god power;
- mother city and outer zones returning immediately to the original kingdom;
- `Kingdom.name = 虎踞`, kingdom nameplate `虎踞贼`, city `虎踞寨`;
- ruler and heir ceremonial titles `虎踞寨大当家` and `虎踞寨少当家`;
- fixed stronghold zones while bandit and unlocked growth after conversion;
- stronghold fall returning population and zones before city destruction;
- a physical shortage raid, food conservation, return, cooldown, and victim
  suppression right;
- save/load during active stronghold and raid states;
- net48 build, deployment hash parity, and visible WorldBox startup logs.

# Bandit Stronghold History, Nine-Zone Territory, And Gate Towers Design

## Scope

This change extends the existing bandit stronghold lifecycle in three focused
ways:

- record establishment and suppression in kingdom and city chronicles without
  treating the records as territorial changes;
- replace the fixed four-zone stronghold with an exact three-by-three block of
  nine native `TileZone` objects; and
- place one native, architecture-matched watch tower at the center of each of
  the four gate openings.

The existing `city_found` event produced by native stronghold city creation is
retained and may continue to create an atlas node. The new custom chronicle
events never create atlas nodes.

## Nine-Zone Stronghold

The stronghold receives exactly nine native zones arranged as a complete
three-by-three block in zone coordinates. Candidate blocks must contain the
mother city's hall, bonfire, or city-center zone. Candidates are ranked with
the core in the center first, followed by distance from the core and a stable
coordinate ordering.

Candidate discovery enumerates the finite three-by-three blocks that can
contain the core zone. It does not enumerate arbitrary connected nine-zone
sets. Every coordinate in the selected block must resolve to a distinct zone
currently owned by the mother city.

The mother city must own at least ten zones before the split and must retain at
least one zone afterward. If no complete, wallable three-by-three candidate
exists, preflight fails without mutating the world. Automatic conversion,
manual government switching, and the bandit god power all use this same rule.

## Walls, Gates, And Towers

The existing Cultiway-style zone-perimeter wall planner remains authoritative.
It builds the wooden wall around the union of the nine selected zones and
carves one three-tile passage on each cardinal side.

The wall plan also returns the center tile of every carved passage. The center
tile receives one watch tower; the other two passage tiles remain open for
movement. A valid plan therefore has four distinct cardinal gate centers and
four buildable tower positions.

The tower asset is resolved through the stronghold city's native architecture
using the `order_watch_tower` build order. This selects the matching
`watch_tower_human`, `watch_tower_orc`, `watch_tower_elf`,
`watch_tower_dwarf`, or another compatible architecture asset without a
hard-coded species switch. Towers are created with the native
`BuildingManager` API, belong to the stronghold city and bandit kingdom, and
retain the original arrow attack behavior.

Stronghold state persists each tower's building ID, tile coordinate, and asset
ID. Creation rollback removes all towers already created. Save restoration
does not respawn a tower destroyed during normal combat. Suppression removes
every surviving tower or tower ruin identified by persisted state, then
restores the pre-wall top-tile state.

## Chronicle Events

Four dedicated, non-territorial event types are used:

- `bandit_stronghold_established` in the stronghold city chronicle;
- `bandit_suppression_victory` in the suppressing kingdom's history;
- `bandit_suppressed` in the bandit kingdom's history; and
- `bandit_stronghold_suppressed` in the stronghold city chronicle.

The establishment event states that the bandits settled there and established
the named stronghold. On suppression, the victor's event names the captured
stronghold and defeated bandit kingdom. The bandit event names the victor when
one exists. The city event records that the stronghold was destroyed.

Events use localized `HistoryText` composition and `HistoryTarget` links among
the stronghold, bandit kingdom, and suppressing kingdom. City and kingdom
history writes use deterministic projection keys based on the stronghold city
ID and relevant kingdom IDs, so retries cannot duplicate records.

The atlas territorial reader remains restricted to `city_found`,
`city_transfer`, `city_lost`, and `city_gained`. None of the four new event
types is added to that whitelist. Suppression does not emit city transfer,
city loss, or city gain events.

## Suppression Triggers And Attribution

Both of the following conditions enter the same idempotent stronghold-fall
transaction:

1. an enemy captures the active stronghold city; or
2. the active stronghold city's living population reaches zero.

For a direct capture, the occupying enemy kingdom is the suppressor. For
population-zero suppression, attribution is resolved in this order:

1. the enemy kingdom of the actor that most recently caused a stronghold
   resident's death;
2. the original mother kingdom, if it is currently at war with the bandit
   kingdom; or
3. no suppressor, for example when starvation ends the stronghold without an
   active enemy.

When there is no suppressor, the bandit kingdom and stronghold city still
receive their suppression events, but no other kingdom receives a victory
event. Starvation is not a separate abandonment state: when starvation reduces
the living population to zero, all nine zones still merge back into the mother
city, the towers and walls are removed, and the empty stronghold city is
deleted through the same fall transaction.

## Fall Transaction

The persisted `Falling` phase makes capture callbacks, population checks, load
recovery, and kingdom destruction callbacks converge on one settlement. The
transaction performs the following ordered work:

1. resolve and snapshot the suppressor before objects are removed;
2. write the suppressor, bandit kingdom, and stronghold city chronicle events;
3. move surviving residents to the persistent mother city;
4. return all nine stronghold zones to the mother city;
5. remove the four persisted watch towers or their ruins;
6. restore every wooden-wall tile to its saved top-tile type;
7. mark the stronghold state completed; and
8. remove the stronghold city, allowing the cityless bandit kingdom to become
   extinct through the existing lifecycle.

The city suppression event is written before city removal. Projection keys
make a retry after any partial failure safe. The older generic destruction
record is replaced by this lifecycle-owned history path so the same extinction
cannot be recorded twice.

## Persistence And Compatibility

The stronghold state schema is advanced to store tower records and suppression
attribution facts. Older readable states are normalized with empty tower data.
They remain suppressible and retain their existing fixed-zone geometry; the
upgrade does not silently resize already-created four-zone strongholds or add
towers to them.

Only newly created strongholds use the exact nine-zone layout and four gate
towers. This avoids destructive migration of live saves.

All world mutations remain authority-only and use the existing multiplayer
replication boundaries.

## Native Physical Food Raids

The raid lifecycle remains a small custom mission coordinator because the
original game has no complete neutral-city food raid behavior. The coordinator
continues to own target ranking, party membership, mission stage, cooldown,
and temporary suppression rights. It does not create or commandeer a native
`Army`: the original army attack tasks require a formal enemy, target normal
occupation, and bind the whole city army rather than a three-to-eight-person
raid party.

The physical operations use original WorldBox behavior and storage APIs:

- `City.reachableFrom` validates land reachability;
- `Actor.goTo` or the equivalent native tile-target behavior performs terrain
  pathfinding;
- `City.getTotalResourceSlots` enumerates actual food stock;
- `City.takeResource` removes the selected food from the victim;
- `Actor.addToInventory` places the stolen resources in surviving raiders'
  persistent inventories; and
- `Actor.giveInventoryResourcesToCity` deposits cargo after a raider returns
  to the stronghold.

Raid selection excludes warriors already carrying resources so mission cargo
cannot be mixed with unrelated work resources. Loot is distributed as evenly
as possible among the surviving party, with stable actor-ID ordering for the
remainder. The persisted mission records the expected per-actor cargo manifest
for validation and recovery, but that manifest is not a second inventory and
must never be deposited independently of the actors' real inventories.

On return, the coordinator calls the native unload operation as soon as a
surviving carrier enters the stronghold. The original `store_resources`
decision may unload a warrior first; recovery therefore computes delivery from
the observed actor inventory and stronghold storage transition and never adds
the same manifest twice.

When a carrier is killed, original actor death logic transfers carried
resources to the killer where applicable. Environment deaths lose their
cargo. If all carriers die, the raid enters cooldown without creating or
restoring food. This replaces the previous virtual `CarriedFoodByResourceId`
payload with physical, save-persistent cargo while retaining the mission stage
and audit manifest needed for idempotence.

The original personal robbery action, integer money/loot field, native army
grouping, enemy-city attack task, and city occupation path are not used. They
operate on different resources or would violate the requirement that a raid
does not itself declare war or transfer territory.

## Failure Handling

Preflight rejects a candidate unless all nine zones, four gates, four tower
tiles, the architecture-specific tower asset, and native managers are
available. A failure before commit has no side effects. A failure during
commit rolls back towers, wall tiles, zones, actors, city ownership, and route
metadata in reverse order.

During suppression, missing or already-destroyed towers are treated as cleaned
up. Wall restoration and zone return remain retryable. A history write failure
does not cause territorial events to be substituted.

## Verification

Detached rules tests cover:

- exact three-by-three candidate recognition and ranking;
- rejection of incomplete nine-zone blocks and mothers with no retained zone;
- four distinct cardinal gate centers and two open passage tiles per gate;
- architecture-order tower selection inputs;
- population-zero suppressor attribution; and
- deterministic history projection keys;
- exclusion of actors with pre-existing cargo;
- deterministic cargo distribution among surviving raiders; and
- physical-cargo recovery without duplicate delivery.

Source and runtime tests cover:

- native architecture and `BuildingManager` tower creation;
- transaction rollback and state schema normalization;
- capture and population-zero convergence on one fall path;
- tower and wall cleanup during suppression;
- native city storage deduction and actor inventory cargo;
- native return-to-city unload and death cargo transfer;
- all four dedicated chronicle event types;
- suppressor propagation into the fall transaction; and
- exclusion of new event types from atlas territorial queries.

Integration verification covers a newly created nine-zone stronghold for each
available base-game species, four working arrow towers at gate centers,
movement through both side tiles of every gate, direct-capture suppression,
population-zero suppression with and without an attributable victor, chronicle
content on all applicable subjects, physical food raids whose cargo survives
save/load and follows native death transfer, net48 build, deployment hash
parity, and visible WorldBox startup logs.

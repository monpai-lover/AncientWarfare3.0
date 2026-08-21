# Bandit Island Migration and Piracy Design

## Goal

Allow an established bandit kingdom that is losing a stronghold to evacuate by
temporary boat transport, found a replacement stronghold on an unoccupied
island, abandon the old stronghold safely, and use the island base to raid
coastal cities. The existing bandit kingdom identity is retained.

## Scope and Invariants

- No new kingdom is created during migration.
- The existing king, wars, suppression rights, historical identity, and
  kingdom ID remain unchanged.
- At every point there is at most one active stronghold city.
- The old city is never deleted. Its population and historical records remain.
- Migration does not end a war or grant a peace settlement.
- The existing RTS temporary-boat and P0 boarding, voyage, landing, and
  cleanup path is the only transport mechanism used.
- Island piracy targets coastal cities only. This design does not add
  ship-to-ship interception or civilian-vessel hunting.

## Persistent State

Extend `PeasantRebelBanditStrongholdState` with migration and island-base
projection fields. Missing fields in old saves use the land-stronghold
defaults.

- `StrongholdKind`: `Land` or `Island`.
- `MigrationStage`: `None`, `Evaluating`, `Boarding`, `Voyaging`, `Founding`,
  `Completed`, or `Failed`.
- `AbandonedStrongholdCityId`.
- `IslandCityId` and `MigrationTargetTileId`.
- `MigrationStartedYear` and `MigrationThreatCycles`.
- `PirateRaidEnabled` and `PirateRaidFailureCount`.

The state store must write after every stage transition. Runtime restoration
must resume only from a valid persisted stage and must never create a second
city or second active transport route.

## Escape Trigger

One authority cycle may evaluate a bandit kingdom. The trigger requires all of
the following:

1. The bandit is in an active suppression war and the hostile force is
   attacking or occupying the stronghold.
2. The bandit's effective military strength is below 60% of the hostile force,
   or the stronghold has fewer than four living residents.
3. The same weakness remains true for two consecutive authority cycles.
4. No raid, migration, rollback, or terminal stronghold-fall operation is
   active.

The threat-cycle counter resets when the stronghold recovers or the hostile
force leaves. A failed island search does not alter the war or destroy the
stronghold.

## Island Candidate Selection

An island is eligible when it has no active city, no active bandit stronghold,
an unbroken buildable land area, and at least one usable coastal landing tile.
Candidates are rejected when they are occupied, inside an active hostile
occupation area, or too close to another bandit base or raid target.

Candidates are ordered by:

1. Safety from the current suppressor's occupation and active combat area.
2. A valid coast-to-coast route within the existing transport system.
3. Larger buildable area.
4. Greater distance from the abandoned stronghold, subject to transport
   reachability.
5. Stable tile ID order as the final tie-breaker.

If no candidate passes, the bandit remains in the old stronghold and may try
again after the normal evaluation cooldown.

## Migration State Machine

### Evaluating

Lock the selected island, landing tile, old stronghold, king, and all living
members that can be evacuated. Suspend new raids. The old stronghold remains
the authoritative base until the voyage completes.

### Boarding

Provision one temporary RTS transport route. The P0 manifest includes the king
and all eligible living members. Members that cannot board remain in the old
stronghold; they are not deleted or reassigned early. A boat is considered
ready only after the captain and all required passengers are inside it.

### Voyaging

Use the existing P0 boat route and transport ownership predicates. No native
land movement task may steal the captain or passenger movement while the route
is live. If the route fails, cancel the request, clean the temporary boat, and
restore the old stronghold state.

### Founding

After all transported members reach stable land, create the new city on the
locked island tile. Transfer the transported roster and king to the new city,
set `StrongholdKind=Island`, enable piracy, and persist the new
`StrongholdCityId`. This is the commit point.

### Completed

After the new state is durable, clear the old stronghold binding and write a
`BanditStrongholdAbandoned` history event. If the old city was not occupied,
return it to the original kingdom; if already occupied, preserve the current
owner. Do not delete the city.

### Failed

Any error before the new stronghold is durable must restore the old city,
member cities, transport ownership, migration fields, and raid state. All
temporary boats and route bindings must be cleaned. A failed migration leaves
the war active and the bandit eligible for a later retry.

## Island Piracy

Island bases reuse `BanditRaidStage` and existing cargo accounting.

- Select only reachable coastal cities with valid landing tiles.
- Preserve existing exclusions for allied kingdoms, active strongholds, and
  invalid or empty targets.
- Assemble a bounded warrior party at the island city.
- Run `Outbound` through the RTS temporary-boat P0 route.
- Unload on stable land, execute the existing food/resource loot operation,
  then enter `Returning` and use the same route to the island.
- Deliver cargo only after the party reaches the island city.
- Destroy temporary boats and clear route ownership on success, cancellation,
  death, invalid target, or transport failure.
- Increment `PirateRaidFailureCount` on transport failure and enter a bounded
  cooldown after repeated failures. Do not spawn a boat every authority cycle.

No new war is declared by piracy. Existing bandit hostility and suppression
rights continue to govern the target and cargo effects.

## Performance and Safety

- Candidate island scans run in bounded, rotating authority work, never every
  actor tick.
- The expensive island geometry query is cached by world revision and
  invalidated after city creation, destruction, or terrain changes.
- Transport manifests are locked by kingdom ID and migration stage, preventing
  duplicate boats and duplicate member assignment.
- Existing population-fall processing must ignore a stronghold while its
  migration is in `Evaluating`, `Boarding`, or `Voyaging`.

## Tests

Add rule and source-guard coverage for:

- 60% strength and four-resident escape thresholds;
- two-cycle persistence and reset;
- island eligibility, coastal landing, ordering, and no-candidate fallback;
- legal migration-stage transitions and persisted resume behavior;
- boarding, voyage, landing, and founding rollback;
- old-city occupied versus unoccupied cleanup;
- single-active-stronghold invariant;
- coastal-only island piracy target selection;
- transport failure cooldown and temporary-boat cleanup;
- legacy save defaults and no duplicate migration after reload.

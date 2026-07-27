# Enclosed Unowned Zone Repair

## Goal

Remove isolated unowned land Zones that are fully enclosed by one kingdom,
without changing disputed borders or adding an annual full-map scan.

## Enclosure Rule

A Zone is eligible only when all of the following are true:

- it currently has no city;
- it is not on the world edge;
- it contains ground tiles;
- it has all four cardinal neighbours;
- every cardinal neighbour belongs to a live city and live kingdom;
- all four neighbouring cities belong to the same kingdom.

Diagonal neighbours do not determine enclosure. A Zone with an unowned cardinal
exit, a dead owner, or neighbours from different kingdoms remains unowned.

## Target City

The eligible Zone is assigned to a neighbouring city in the enclosing kingdom.
Candidates are ranked deterministically by:

1. number of cardinal sides shared with the Zone;
2. shortest squared Zone distance from the candidate city's centre;
3. lowest stable city id.

This permits two or more cities of the same kingdom to close an internal hole.
The repair uses the original `City.addZone` API so city lists, Zone ownership,
render dirtiness, city centres, and the original city-place cache remain in
sync. The Xia technology Zone cap does not reject an internal-hole repair: the
cap limits outward expansion, while this rule repairs territory already
completely enclosed by the kingdom.

## Event Flow

`TileZone.setCity` is observed after a successful ownership mutation. The hook
does not change ownership directly. It queues the changed Zone and its four
cardinal neighbours by coordinate, coalescing duplicate entries.

The authoritative simulation cycle drains a fixed number of queued candidates.
If a repair succeeds, the original ownership mutation hook queues the newly
affected neighbours, allowing adjacent holes to settle over later cycles
without recursive mutation.

World clear and load reset the runtime queue. Once the world-loaded event fires,
the service performs one bounded initial sweep over the current Zone list so
old saves containing holes can be repaired. The sweep advances through a small
fixed number of Zones per authoritative cycle and never repeats annually.
Multiplayer replica sessions do not run the authoritative repair because they
are already excluded by the authority-cycle gate.

## Safety And Performance

- Normal ownership changes inspect only the changed Zone and four neighbours.
- Queue membership is coalesced, so bulk captures and save loading cannot add
  duplicate work for the same coordinates.
- Candidate processing and initial-sweep advancement both have fixed per-cycle
  limits.
- No work runs from `MapBox.Update`, actor updates, city annual updates, or a
  recurring global Zone scan.
- Stale coordinates, destroyed cities, changed ownership, and a cleared world
  are revalidated at execution and become no-ops.
- Deterministic ranking prevents save/load order from changing the chosen city.

## Verification

Pure rule tests must prove:

- four sides owned by one city are eligible;
- multiple cities in the same kingdom are eligible;
- mixed kingdoms are ineligible;
- an unowned cardinal exit is ineligible;
- world-edge and groundless Zones are ineligible;
- shared-side count wins, then distance, then stable city id;
- the queue and initial sweep stop at their configured per-cycle budgets.

Source guards must prove:

- ownership is observed through `TileZone.setCity`;
- assignment uses `City.addZone`;
- the service is drained only by `AWAuthorityCycleService`;
- runtime state resets on world lifecycle reset;
- no `MapBox.Update` or annual global-Zone scan is introduced.

Build verification covers Debug and Release. Actual-game verification should
load an existing map with an enclosed neutral land Zone, confirm that it fills
without a frame spike, then create both a same-kingdom hole and a mixed-kingdom
border hole. Only the same-kingdom holes may be assigned.

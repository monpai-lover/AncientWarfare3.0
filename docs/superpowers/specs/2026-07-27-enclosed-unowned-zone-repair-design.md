# Enclosed Unowned Zone Repair

## Goal

Remove isolated connected regions of unowned land Zones that are fully
enclosed by one kingdom,
without changing disputed borders or adding an annual full-map scan.

## Enclosure Rule

A connected unowned component is eligible only when all of the following are
true:

- every Zone in it currently has no city and contains ground tiles;
- no Zone in it is on the world edge or lacks a cardinal neighbour;
- every cardinal neighbour outside the component belongs to a live city and
  live kingdom;
- all cities on the complete component boundary belong to the same kingdom;
- the component contains no more than 64 Zones in one bounded repair attempt.

Diagonal neighbours do not determine enclosure. Cardinally connected unowned
Zones are evaluated as one component, so a two-Zone internal hole is not
mistaken for two open exits. A component reaching the map edge, containing a
groundless Zone, exceeding the fixed traversal budget, touching a dead owner,
or bordering different kingdoms remains unowned.

## Target City

The eligible component is assigned to a neighbouring city in the enclosing
kingdom.
Candidates are ranked deterministically by:

1. number of cardinal sides shared with the complete component boundary;
2. shortest squared distance from the component centre to the candidate city's
   centre;
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

`City.setKingdom` is observed separately because conquering a whole city changes
the kingdom of every existing city Zone without calling `TileZone.setCity`.
Transferred cities enter a coalesced, resumable boundary queue. Each authority
cycle examines at most 16 of that city's Zones and queues only cardinal
neighbours that are still unowned.

At most four city-boundary records are dequeued per authority cycle, including
dead or otherwise invalid cities. If the same city changes kingdom again while
its scan is pending, its coalesced record is marked for rescan and restarts at
Zone index zero on its next pass.

The authoritative simulation cycle drains a fixed number of queued candidates.
Candidates with fewer than two owned cardinal neighbours are rejected before
allocating traversal state; every finite orthogonally enclosed component has at
least one corner satisfying this seed condition.
Each candidate uses an iterative, non-recursive traversal capped at 64 Zones.
If a repair succeeds, all Zones in the verified component are assigned through
the original ownership API; the ownership hook coalesces the resulting local
events.

World clear and load reset the runtime queue. Once the world-loaded event fires,
the service performs one bounded initial sweep over the current Zone list so
old saves containing holes can be repaired. The sweep advances through a small
fixed number of Zones per authoritative cycle and never repeats annually.
Multiplayer replica sessions do not run the authoritative repair because they
are already excluded by the authority-cycle gate.

## Safety And Performance

- Normal ownership changes inspect only the changed Zone and four neighbours.
- Whole-city transfers inspect at most 16 city Zones per authority cycle and
  at most four queued city records; they never scan unrelated cities.
- Queue membership is coalesced, so bulk captures and save loading cannot add
  duplicate work for the same coordinates.
- Candidate processing and initial-sweep advancement both have fixed per-cycle
  limits.
- A candidate examines at most 64 connected unowned Zones and never recursively
  traverses the map.
- Ordinary one-sided borders and open wilderness do not allocate component
  traversal collections.
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
- a multi-Zone hole enclosed by one kingdom is eligible as one component;
- a multi-Zone component on a mixed border remains ineligible;
- world-edge and groundless Zones are ineligible;
- shared-side count wins, then distance, then stable city id;
- the queue and initial sweep stop at their configured per-cycle budgets.

Source guards must prove:

- ownership is observed through `TileZone.setCity`;
- city conquest is observed through `City.setKingdom` and uses a bounded,
  resumable boundary scan;
- assignment uses `City.addZone`;
- the service is drained only by `AWAuthorityCycleService`;
- runtime state resets on world lifecycle reset;
- no `MapBox.Update` or annual global-Zone scan is introduced.

Build verification covers Debug and Release. Actual-game verification should
load an existing map with an enclosed neutral land Zone, confirm that it fills
without a frame spike, then create both a same-kingdom hole and a mixed-kingdom
border hole. Only the same-kingdom holes may be assigned.

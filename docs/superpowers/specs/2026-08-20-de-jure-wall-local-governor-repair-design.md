# De Jure Map, Mandate Wall, And Local Governor Repair Design

## Scope

This repair batch covers three confirmed defects:

1. De jure powers open the vanilla city map instead of the hierarchical
   city-administration region view and do not reliably refresh after a change.
2. Mandate frontier towers are capped independently from wall length instead
   of being spaced along the frontier wall, although existing tower footprints
   are already excluded from wall placement.
3. A local court's governor office can be occupied by an actor other than the
   city's authoritative `city.leader`; regional governors can also be projected
   from a non-seat city, and city-leader selection hard-prioritizes the royal
   clan.

The foreign-invasion and reverse-Xiaization feature is explicitly deferred.

## De Jure Power Map Contract

- Both de jure powers force the AW3 hierarchical-vassal meta type rather than
  vanilla `MetaType.City`.
- De jure interaction fixes the hierarchical map to the city-administration
  region level for the clicked kingdom. It must not fall through to the member
  city level while the power remains selected.
- Creating a de jure region, selecting a target seat, and assigning a city all
  preserve this view.
- A successful create or assignment invalidates regional aggregation, native
  zone-color caches, projected labels, and tooltips, then requests one native
  redraw. The updated legal regions must appear without closing or reselecting
  the map power.
- Selecting either power clears only its transient target selection; it must
  not reset the persisted de jure store.

## Mandate Frontier Tower Contract

- Tower candidates are derived from the same planned frontier line used by the
  mandate wall, not from unrelated city-building placement.
- Candidates are distributed at approximately ten wall tiles of separation
  along each connected frontier component. A nearby valid existing watchtower
  satisfies that interval and suppresses another tower.
- Invalid or unbuildable candidates are searched locally along the frontier;
  failure to place one tower does not abort the wall.
- The old per-decision and per-city tower caps no longer define coverage. A
  bounded safety budget based on planned wall length prevents runaway building
  attempts.
- The complete footprint of every successfully placed or existing watchtower
  is reserved before final wall placement. No wall top tile may be placed on a
  reserved tower footprint.
- Border refresh removes and rebuilds only wall tiles as it does today. Towers
  remain in place and are reused by the next spacing pass.
- Towers use the city's race/architecture watchtower asset, retaining the
  existing Xia and human fallbacks only when the architecture has no tower.

## Local Governor Identity Contract

- Every city has one authoritative local governor: its live `city.leader`.
  The root local-court office (displayed as the configured local-governor title,
  currently commandery governor) must reference exactly that actor.
- Reconciliation treats a root-office record belonging to another actor as
  stale. It closes the stale appointment, assigns the root office to the live
  `city.leader`, and keeps subordinate offices unchanged.
- Read-model fallback must never display an unrelated root-office holder when a
  live `city.leader` exists. Old saves are repaired through the normal career
  appointment path so history and career state remain authoritative.
- The regional governor is the de jure seat city's live `city.leader`. That
  actor simultaneously serves as the seat's local governor and the region's
  governor. The system must not substitute the leader of the highest-developed
  currently controlled member city.
- If the legal seat is not controlled by the viewing kingdom, that kingdom has
  no regional-governor actor for the region; it may use another controlled city
  only as a visual label anchor.

## City-Leader Selection And Rotation

- Royal-clan, other-clan, and common candidates enter one eligible candidate
  pool. Royal membership supplies no priority tier or fixed bonus.
- Candidate score continues to value governing ability, traits, qualification,
  merit, and valid service history.
- A soft concentration penalty is applied for every current city leader from
  the candidate's clan. The royal clan receives exactly the same penalty.
  Exceptional candidates can still win; there is no hard family quota.
- Clanless eligible commoners remain valid candidates. Formal appointment uses
  the existing `EnsureOfficialShiAndClan` path, so taking office creates noble
  identity instead of requiring prior nobility.
- Expired city-leader terms open a replacement selection instead of depending
  solely on a closed cycle that swaps the same governors between cities.
  Transfer remains possible, but a fresh qualified candidate can replace an
  incumbent. When no valid replacement exists, the incumbent receives a short
  retry extension rather than leaving the city leaderless.
- Any committed leader replacement updates the city pointer, local root office,
  career state, regional-governor projection when the city is a seat, court
  layout, and map labels as one logical operation.

## Error Handling And Compatibility

- Runtime reconciliation is idempotent: repeating it with matching
  `city.leader` and root appointment performs no write.
- Failed career persistence must not leave the local-court display and city
  pointer assigned to different actors.
- Existing valid de jure data, wall manifests, towers, subordinate local
  officials, and official histories are retained.
- Existing unrelated dirty worktree files are outside this repair and must not
  be reverted or included in its commits.

## Verification

- Pure tests cover de jure forced meta selection and refresh decisions.
- Pure tests cover roughly ten-tile tower spacing, existing-tower suppression,
  disconnected frontier components, and tower-footprint wall exclusion.
- Pure tests cover authoritative root-office selection, seat-leader regional
  projection, no royal priority, and increasing clan-concentration penalties.
- Runtime build verification must compile the mod and rule tests.
- Deployment verification must confirm that selecting either de jure power
  shows the hierarchical region colors, create/assign refreshes immediately,
  frontier walls leave tower footprints open, and local governor cards match
  the actual city leaders.

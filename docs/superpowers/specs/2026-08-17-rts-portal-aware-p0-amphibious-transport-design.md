# RTS Portal-Aware P0 Amphibious Transport Design

## Goal

Make RTS amphibious operations deterministic under large-step scheduling by
moving route selection, pickup, embarkation, sailing, landing, and completion
into the military P0 pipeline. Docks are preferred as route portals; a stable
shoreline pickup and landing pair is the fallback when a usable dock is absent.

This design supersedes the execution ownership described in
`2026-08-16-rts-vanilla-amphibious-transport-design.md`. WorldBox actor and boat
tasks may still be used for presentation and low-level state compatibility,
but their cross-frame behavior progress is no longer a prerequisite for an RTS
voyage to advance.

## Root Cause

AncientWarfare3 currently retains only part of Cultiway-Reborn's portal-aware
pathfinding design:

- `AWDockTransportService` can estimate a route through an entry and exit dock.
- `AWStreamingPathGenerator` reduces that route to one
  `AWMovementMethod.Transport` step whose tile is the final destination.
- The transport step does not carry the selected entry dock, exit dock, pickup
  sea tile, or landing tile.
- `ArmyRtsTransportService` therefore creates exact-final-target vanilla
  `TaxiRequest` objects and waits for WorldBox to derive the pickup route.
- Military P0 only starts helping after WorldBox has assigned a boat or moved a
  request into `Loading`.

This creates an ownership gap before embarkation. A temporary boat can be bound
without receiving a reliable pickup destination, while a soldier can reach the
vanilla embark tile without advancing to `BehTaxiEmbark`. The observed result is
`mission_assigned -> requested -> temporary_boat_bound -> assigned`, with no
`embarked`, `transporting`, or `landed` phase.

Cultiway-Reborn avoids this gap by representing a dock transition as a portal
with explicit entry and exit definitions. Despite the portal terminology, the
framework is a general transport abstraction: docks, train stations, and
teleport arrays are different portal assets that share route edges, driver
assignment, passenger manifests, and load/unload requests.

AncientWarfare3 also differs from Cultiway at the water-topology layer.
`AWDockTransportService` currently uses `ocean.region.island.id` as a water
component identifier. Cultiway instead performs a breadth-first traversal of
ocean regions, merges intersecting traversals, and connects docks only when
they belong to the same resulting water component. The current approximation
can reject a valid dock pair or associate endpoints incorrectly.

## Scope

The change covers RTS army voyages only:

- cross-island attacks;
- cross-water retreats and returns that use `ArmyRtsTransportService`;
- routes that become water-separated after terrain changes;
- temporary transport creation and destruction;
- military P0 scheduling for all active voyage stages.

It does not replace ordinary actor pathfinding, school travel, civilian taxi
requests, general WorldBox boat AI, or non-RTS portal systems.

## Route Model

Each active voyage stores an immutable physical route selected at voyage start:

- final mission target tile;
- entry portal identifier when a live dock is selected;
- entry land tile where the army assembles;
- pickup sea tile where the boat receives the army;
- exit portal identifier when a live dock is selected;
- destination sea tile where the boat unloads;
- landing land tile where members are placed;
- route source: `DockPortal` or `ShoreFallback`.

The selected route remains locked after the first member embarks. Before
embarkation, invalid or terrain-obstructed endpoints may cause a fresh route to
be selected. This prevents a sailing boat from being redirected by mission
refreshes while still allowing recovery from destroyed docks or edited terrain.

Dock selection follows the useful part of Cultiway's `PortalAware` pattern:

1. Snapshot live dock endpoints.
2. Select an entry reachable from the army's current land component.
3. Select an exit connected through the same water component and reachable to
   the target's land component.
4. Compare candidate cost as land-to-entry plus water crossing plus
   exit-to-target.
5. Resolve concrete adjacent land and sea tiles for both portals.

If no valid dock pair exists, the route resolver searches bounded stable
shoreline candidates on the source and destination components. The fallback
does not require a permanent port or an existing transport resource.

## Reusable Transport Portal Core

The implementation ports the reusable core of Cultiway's portal framework
rather than adding more special cases to `AWDockTransportService`. The first
registered transport type is `Dock`; train and teleport execution remain out
of scope, but the route contracts do not assume that a portal is a ship.

The core consists of four boundaries:

1. A transport portal registry stores live endpoint definitions and publishes
   immutable scalar snapshots for worker-thread route generation. Runtime
   `Building`, `WorldTile`, and `Actor` references are resolved on the main
   thread from stable IDs.
2. A dock water-connectivity service rebuilds ocean components when docks or
   relevant terrain topology change. It emits explicit directed connections
   between dock endpoints in the same water component, including estimated
   travel cost.
3. The portal-aware path generator compares direct movement with connected
   portal candidates and emits a transport step that retains entry and exit
   endpoint IDs. When direct land movement is impossible, a valid transport
   edge is admissible even when it is not cheaper than the geometric direct
   estimate.
4. A transport request stores the selected portal chain, assigned driver,
   expected passenger IDs, load/unload endpoint data, state, and progress
   timestamps. One RTS army voyage owns one request; it does not create one
   independent vanilla `TaxiRequest` per soldier.

The generic request states are `WaitingDriver`, `WaitingPassengers`, `Driving`,
`Landing`, `Completed`, and `Failed`. `ArmyRtsTransportService` maps those
states onto its visible RTS phases and advances them from military P0.

The existing `AWDockRouteRegistry`, `AWDockRouteCandidate`, and
`AWMovementMethod.Transport` contracts may be evolved or replaced, but the
result must preserve the connected portal pair through path generation and
execution. A final-target-only transport step is not a valid result.

## P0 Voyage State Machine

The authoritative states are:

```text
RoutePending -> AssembleAtEntry -> BoatToPickup -> Boarding
             -> Sailing -> Landing -> Complete
```

`RoutePending` resolves and locks the route. Failure leaves the voyage active
for bounded retry without issuing a land path to the unreachable final target.
For dock routes, this creates one transport request containing the full army
manifest and selected entry/exit endpoints.

`AssembleAtEntry` gives the captain the entry land tile as the movement target.
Members continue the existing RTS captain-follow behavior. This stage is
complete when the captain is within the boarding radius or is already inside
the assigned boat.

`BoatToPickup` provisions or binds one transport and drives it toward the
route's pickup sea tile from military P0. The boat does not wait for
`BehBoatFindRequest` or `BehBoatTransportFindTilePickUp` to select that tile.

`Boarding` begins when the boat is within four tiles of the pickup sea tile and
the captain is within four tiles of the entry land tile. Military P0 iterates
the voyage roster and invokes `Actor.embarkInto(boat)` for every valid member.
Members do not individually execute `force_into_a_boat`, and they do not need
to walk onto one exact embark tile. Boarding is complete only when every valid
member is inside the assigned boat; dead or detached members are removed from
the expected roster.

`Sailing` drives the assigned boat toward the destination sea tile from P0.
The voyage stores its own destination, so `boat_transport_go_unload` may be set
for compatibility and display but does not own progress. Combat, replenishment,
mission refresh, and ordinary actor scheduling yield to the active voyage.

`Landing` begins when the boat reaches the destination sea tile's bounded
arrival radius. P0 validates or re-resolves a stable adjacent land tile and
invokes `Actor.disembarkTo(boat, landingTile)` for every expected passenger.
The army is considered landed only when all valid members are outside the boat
on stable destination-side land.

`Complete` releases transport ownership, destroys only temporary boats created
for this voyage, and notifies the march and RTS controller services. The
original strategic mission and final target then resume.

## Scheduling And Ownership

Every active voyage is military P0 work from `RoutePending` through `Landing`.
The P0 scheduler must register both the assigned boat and all expected army
members as priority military actors for that cycle. No state transition depends
on the ordinary actor scheduler running later in the frame.

RTS phase tasks remain visible diagnostics only. They may display assembling,
boarding, sailing, or landing, but task action indices do not advance the
voyage. The controller must not overwrite route targets, start combat, run
replenishment ownership, or restore ordinary movement while the voyage is
active.

The generic AW pathfinder may still emit a transport-required result. That
result must retain the selected entry and exit portal IDs rather than collapsing
them to the final target tile. RTS consumes the route once, creates or adopts
the army-level transport request, and hands all subsequent physical execution
to `ArmyRtsTransportService`.

## Failure Recovery

- If an entry or exit dock is destroyed before boarding, resolve a new dock or
  shoreline route.
- If terrain changes before boarding, revalidate all route tiles and resolve a
  new route when necessary.
- If terrain changes after boarding, preserve the source route and only
  re-resolve the destination sea/land pair.
- If the assigned boat dies, remove invalid passenger bindings, provision a
  replacement boat, and return the voyage to `BoatToPickup` or `Landing`
  according to passenger location.
- If a member is alive but detached from the army, remove it from the expected
  roster so it cannot block completion.
- If no route can be resolved, retry with a bounded cooldown and emit the
  failed stage and endpoint data. Do not release the army into an unreachable
  land-movement task.
- If the strategic mission is cancelled before embarkation, cancel the voyage.
  If cancellation occurs after embarkation, land the army at the nearest stable
  friendly or source-side coast before releasing control.

## Performance

Portal and shoreline route selection occurs only when a voyage starts or an
endpoint is invalidated. P0 frame processing performs constant-time state
checks plus one pass over the army roster during boarding and landing. It does
not recalculate a general route for each member and does not scan all world
docks every frame.

Ocean connectivity is rebuilt on topology invalidation, not per request. Route
generation reads a revisioned immutable portal snapshot. An active pre-boarding
request whose snapshot revision is stale is revalidated once; an embarked
request retains its source endpoint and only re-resolves an invalid destination
endpoint.

The resolved physical route is cached in the voyage state. Boats use one locked
pickup target and one locked destination target, preventing repeated path
submissions and task resets.

## Diagnostics

The transport log records each authoritative transition with army, captain,
boat, route source, entry tile, pickup tile, destination sea tile, landing tile,
and final target. It also records why route validation or a state transition
failed. Expected phases are:

```text
route_selected
assembling
boat_to_pickup
boarding
embarked
sailing
landing
landed
complete
```

Repeated logs are suppressed unless the state, endpoint, or failure reason
changes.

## Testing And Acceptance

Pure rule tests cover:

- dock portal preference over shoreline fallback;
- water-component connection generation and invalidation;
- portal-aware steps retaining entry and exit endpoint IDs;
- army-level manifest and driver assignment;
- route locking after first embarkation;
- P0 transition conditions for every state;
- boarding and landing roster completion;
- destroyed endpoint and boat recovery;
- military P0 ownership during every active state;
- voyage completion and temporary-boat cleanup.

Source guards verify that RTS transport no longer requires
`force_into_a_boat`, `BehTaxiEmbark`, or vanilla boat loading action progress,
that route metadata is retained instead of returning a final-target-only
transport step, and that water connectivity is not inferred from
`ocean.region.island.id`.

Runtime acceptance requires all of the following:

1. With source and destination docks, the captain moves to the source dock,
   the boat moves to its pickup sea tile, the full army boards, sails, lands,
   and resumes the attack.
2. Without a source dock, the army selects a stable shoreline, a temporary boat
   is provisioned there, and the same P0 flow completes.
3. A route that becomes water-separated after terrain editing transitions from
   land movement into the P0 voyage without leaving the army stalled.
4. Large-step mode produces the same state sequence as normal scheduling.
5. No soldier or boat remains indefinitely in rally, loading, following, or
   replenishment while the voyage owns the army.
6. Temporary boats are destroyed after successful landing; permanent boats are
   preserved.

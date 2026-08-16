# RTS Vanilla Amphibious Transport Design

## Goal

Make an RTS-controlled army cross water through WorldBox's original taxi and
transport-boat behavior chain: spawn a temporary transport at a usable dock,
load the soldiers, sail to the mission destination, unload on the target
coast, and then resume the RTS mission and landing combat.

## Scope

This change covers physical transport for existing RTS armies. It does not
add abstract naval combat, teleport soldiers, replace WorldBox boat pathing,
or redesign army mission selection.

## Authoritative Flow

1. The RTS controller keeps the army mission and target tile authoritative.
2. When the target is on another island, `ArmyRtsTransportService` creates or
   reuses an exact-target `TaxiRequest` for each expected army member.
3. `ArmyRtsTransportProductionService` selects a usable friendly dock and
   creates a temporary transport boat there when the normal fleet cannot
   satisfy the request.
4. The temporary boat is bound to the request and starts WorldBox's original
   `boat_transport_go_load` task.
5. While the request is pending or assigned, soldiers wait without receiving
   a new RTS land route. When the request reaches `Loading`, each waiting
   soldier runs WorldBox's original `force_into_a_boat` task:
   `BehTaxiFindShipTile`, `BehGoToTileTarget`, then `BehTaxiEmbark`.
6. `Actor.embarkInto()` remains authoritative for entering the boat and
   switching the passenger to `sit_inside_boat`.
7. The boat completes `boat_transport_go_load`, switches to
   `boat_transport_go_unload`, sails with water pathing, and executes
   `BehBoatTransportUnloadUnits` at the destination coast.
8. `Boat.unloadPassengers()` and `Actor.disembarkTo()` remain authoritative
   for unloading. The passenger completes the original `short_move` landing
   task without RTS task reassertion.
9. Once an expected member is on stable land on the target island, the
   transport coordinator counts that member as landed. Landed members may
   defend themselves, but the army resumes its coordinated RTS advance only
   after every valid expected member has landed or become invalid.
10. The completed voyage releases taxi ownership, restores the RTS jobs and
    mission target, and disposes only temporary transport boats created for
    that voyage.

## Ownership Boundaries

`ArmyRtsTransportService` owns the mission destination, expected-member set,
retry policy, and temporary-boat lifecycle. It does not manually move actors
or boats.

WorldBox owns the physical transport sequence and state mutations:

- soldier pickup path and embark through `force_into_a_boat`;
- passenger state through `embarkInto()` and `sit_inside_boat`;
- boat loading and sailing through `boat_transport_go_load` and
  `boat_transport_go_unload`;
- unloading and landing through `BehBoatTransportUnloadUnits`,
  `unloadPassengers()`, `disembarkTo()`, and `short_move`.

The RTS task restorer and large-world P0 scheduler must yield while an actor
is executing required boat work. Required boat work includes an exact live
taxi request, `force_into_a_boat`, `embark_into_boat`, `sit_inside_boat`, being
inside a boat, and the post-unload `short_move` while the actor is not yet on
stable target-island land.

## Decision And Task Handoff

The current global decision gate prevents an RTS-owned soldier from reaching
the otherwise allowed `check_warrior_transport` decision. The fix will not
open all vanilla decisions for RTS actors. Instead, the transport coordinator
will explicitly hand an actor to the original boat-related task when its
authoritative taxi request is ready for loading.

The handoff must be idempotent. It must not repeatedly clean or restart an
actor already executing a boat-related task, because doing so resets the
original behavior action index and can prevent `BehTaxiEmbark` from running.

After unloading, RTS ownership returns only after the original landing task
has completed on stable land. Immediate nearby enemies may trigger normal
combat after the actor is ashore, but combat cannot interrupt loading,
passenger state, or the physical unload operation.

## Temporary Boat Rules

A temporary transport boat is created only at a valid usable friendly dock
selected by the existing route-provisioning service. The target does not need
a dock; the original boat unload behavior finds a reachable coast near the
mission target.

The boat must retain its original transport task until unloading finishes.
RTS combat and movement patches must not classify the transport boat as an
RTS army member or replace its task. Temporary boats are destroyed after all
passengers have unloaded and the voyage is complete. Existing permanent boats
are never destroyed by RTS voyage cleanup.

## Failure Recovery

- If no usable source dock exists, the cross-island route is rejected without
  teleporting the army.
- If no boat is assigned, the existing bounded timeout provisions or retries a
  temporary boat.
- If loading stalls, only waiting members receive refreshed taxi requests;
  embarked members keep their passenger state.
- If a boat dies, WorldBox unloads surviving passengers through its original
  death behavior. Members not on stable target-island land remain eligible for
  a replacement request.
- If the mission target changes before embarkation, stale requests are removed
  and a new exact-target voyage may begin. An embarked voyage is not retargeted
  mid-sail.
- Invalid or dead army members are removed from the expected-member set and do
  not block voyage completion.

## Testing

Pure rules tests will cover task handoff, protected vanilla boat tasks,
landing completion, and retry decisions. Source guards will verify that the
implementation invokes the original task identifiers and does not introduce
manual embark, disembark, or teleport logic.

Verification requires:

- focused RTS transport and military P0 rule slices;
- the complete rules test suite;
- a clean production build;
- deployment with source hash comparison;
- runtime testing of a cross-island attack: dock spawn, loading, sailing,
  coastal unload, RTS mission recovery, and combat after landing.


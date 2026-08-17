# RTS Transport Hot Path Optimization Design

## Goal

Remove performance regressions introduced by the portal-aware RTS transport
implementation while preserving the approved P0 voyage state machine,
temporary-boat behavior, route locking, boarding, sailing, and landing logic.

## Root Cause

The current implementation introduces work proportional to all active voyages,
all voyage members, all cities, or all world tiles in paths that can run once
per actor cycle or once per movement step:

- `OwnsTransportBoat` scans every transport state for each boat movement step.
- `RefreshMilitaryP0Priority` registers every voyage member, including actors
  already inside the boat, and those actors are processed again by P0.
- one voyage cycle independently runs roster cleanup, embarked checks, landed
  checks, and stage-specific checks over the same member dictionary.
- dock endpoint validation searches every city and building by dock ID.
- a topology revision can trigger a rebuild that scans all world tiles,
  ocean regions, cities, buildings, and shoreline endpoints.

The user log does not contain the transport phase sequence, so it cannot prove
a particular naval stall. These performance defects are established from the
new transport source and must be verified with a fresh runtime transport log.

## Constant-Time Ownership And Endpoint Lookup

`ArmyRtsTransportService` maintains an owned temporary-boat index keyed by
actor ID. Provisioning or binding a boat adds it; replacement, completion,
cancellation, disposal, and world clear remove it. `OwnsTransportBoat` becomes
an O(1) lookup and validates the indexed actor reference before returning true.

`AWDockTransportService` maintains a live dock-building index keyed by building
ID during topology rebuild. Endpoint validation uses that index instead of
scanning all cities and buildings. Stale entries fail validation and mark the
topology dirty for a later bounded rebuild.

## Single Voyage Census

Each processed voyage performs one member census after roster cleanup. The
census records valid member count, embarked count, stable landed count,
captain membership, and the actors requiring current-stage P0 work. Stage
resolution and diagnostics consume this snapshot instead of rescanning the
member dictionary.

Boarding and landing still perform the mutations required for each member, but
post-mutation completion is calculated by updating or rebuilding one census,
not by invoking multiple independent full-roster predicates.

## P0 Admission

Only actors that can make progress in the current stage enter the actor P0
priority index:

- during assembly, the captain is admitted for movement while followers keep
  their existing formation behavior;
- during boarding, unembarked valid members are admitted only when actor-side
  work is required;
- during sailing, the boat is driven by voyage P0 and passengers are not
  scheduled as independent actor work;
- during landing, only members requiring landing reconciliation are admitted.

Passengers remain protected from combat and unrelated RTS ownership even when
they are omitted from redundant actor execution.

## Topology Rebuild Coalescing

Transport topology rebuilds only after traversal dirty work has settled on a
stable topology-source revision. At most one rebuild may start for a revision
in a render frame. Multiple tile changes, including stronghold wall restoration
or terrain editing, are coalesced before the all-world scan.

Route requests made while topology is still changing reuse the last valid
immutable snapshot when its endpoints remain live, or return route-pending and
retry after the coalesced rebuild. They do not synchronously rebuild the world
once per army or once per changed tile.

## Behavioral Constraints

- Do not change route selection priority, voyage states, stage thresholds, or
  temporary-boat creation and destruction semantics.
- Do not reduce military P0 priority for the captain or active transport boat.
- Do not release embarked passengers to ordinary combat or movement tasks.
- Do not add background-thread access to live `Actor`, `City`, `Building`, or
  `WorldTile` objects.
- Do not suppress transport diagnostics needed to prove the runtime stage
  sequence.

## Verification

Rules and source-guard tests prove:

- owned boat lookup does not enumerate transport states;
- boat ownership indexes are removed on every release path;
- dock endpoint lookup does not enumerate all cities;
- one voyage census supplies stage predicates for a cycle;
- embarked passengers are omitted from redundant actor P0 admission;
- unchanged or unstable topology revisions do not trigger repeated rebuilds;
- the existing boarding, sailing, landing, cancellation, and disposal rules
  remain unchanged.

Release verification includes the focused RTS transport rules, the full rules
harness, a Release build, and source-deployment parity. Runtime acceptance
requires the complete `route_selected -> assembling -> boat_to_pickup ->
boarding -> sailing -> landing -> complete` sequence, with no material FPS
drop when several armies transport concurrently and no repeated topology
rebuild proportional to terrain changes or army count.

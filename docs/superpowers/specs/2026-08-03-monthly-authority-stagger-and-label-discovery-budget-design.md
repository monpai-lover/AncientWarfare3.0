# Monthly Authority Stagger and Label Discovery Budget Design

## Status

Approved design.

## Goal

Remove the recurring main-thread frame spike at game-month rollover without
reducing monthly gameplay work or changing its outcomes. Also prevent the
hierarchical-vassal map label precompute discovery stage from scanning an
unbounded number of empty or invalid kingdoms in one frame.

## Scope

This change covers four existing runtime paths:

- ruler-household monthly pregnancy processing;
- kingdom-policy monthly decision processing;
- wartime preparation monthly recruitment processing;
- hierarchical map label source discovery.

It does not redesign the native simulation scheduler, move live WorldBox
objects to worker threads, change map visuals, or change gameplay frequencies.

## Monthly Work Model

Each monthly service owns a private FIFO queue. On observing a new month, the
service snapshots the current live kingdom references into work items carrying
the observed month key. The month transition itself performs no per-kingdom
database work and no per-kingdom gameplay settlement.

On later authority cycles, each service drains a bounded number of queued
kingdom items. A single authority cycle may process only its configured small
batch. Invalid or destroyed kingdoms are skipped and still consume one item of
the batch budget so malformed entries cannot make traversal unbounded.

The services remain independent:

- ruler-household work retains its rotating candidate and pregnancy rules;
- kingdom-policy work retains per-kingdom month idempotency and all monthly
  point, decision, core-fabrication, and snapshot persistence behavior;
- preparation recruitment retains its current emergency, notice, restoration,
  persistence, and deferred recruitment behavior.

No service performs its previous `foreach (World.world.kingdoms)` settlement
loop directly from the month-change frame.

## Delay and Backlog Semantics

Monthly work may finish during the following game month. A new month appends a
new snapshot behind outstanding work; it does not force a synchronous flush and
does not discard older work.

Each work item preserves its original month key. Existing per-kingdom guards
remain the final idempotency authority. Reset and world-clear operations clear
all runtime queues and scheduled-month markers.

The design deliberately does not force end-of-month catch-up. Avoiding that
forced flush is required because it would recreate the original frame spike.
Normal simulation speeds provide many authority cycles per month, while a
temporary backlog remains semantically valid and drains gradually.

## Database Safety

SQLite access remains on the main thread because the operating connection is
shared and not proven thread-safe. Performance comes from limiting synchronous
queries and writes per frame, not from moving the connection to `Task.Run`.

Ruler-household queries and kingdom-policy snapshot upserts therefore execute
for only the small number of kingdoms drained in that authority cycle. Existing
write error handling remains unchanged.

## Shared Queue Rules

A small pure rules/helper unit defines the scheduling behavior used by all
three services:

- schedule a month only once;
- preserve FIFO order across months;
- drain no more than the item budget;
- report pending work accurately;
- clear all work on reset.

The helper stores no WorldBox or Unity state and is covered by deterministic
rules tests. Runtime services remain responsible for validating live kingdoms.

## Hierarchical Label Discovery Budget

The existing label pipeline remains intact: bounded main-thread snapshots,
worker-only geometry, generation-gated acceptance, persistent layout cache,
and publish-only-on-material-change behavior.

The discovery cursor currently advances past null kingdom containers and empty
city lists without consuming its city/source budget. A large number of such
entries can therefore be traversed in a single frame. The fix charges one
inspection unit whenever a kingdom container is examined, including null,
destroyed, invalid, or empty containers.

The cursor must yield when the inspection budget is exhausted even if it
produced no city source. Productive city capture continues to consume the same
budget, so the bound applies to total main-thread discovery effort rather than
only successful outputs.

The following established behavior is preserved:

- root-country and city labels precompute while the map mode is closed;
- active-view work has priority over inactive precompute;
- cached placements survive ordinary map-mode switches;
- equivalent text, position, size, angle, and gap do not update `TextMesh`;
- world clear cancels work and releases cache state.

## Observability

Add separate recent-runtime measurements for:

- ruler-household monthly queue drain;
- kingdom-policy monthly queue drain;
- preparation-recruitment monthly queue drain;
- total pending monthly work count through the existing diagnostics surface.

The measurements describe actual drain work, not fast month checks. Existing
school, diplomacy, mobilization, and hierarchical-label categories remain
available, but the new monthly categories prevent unrelated services from
hiding the source of a rollover spike.

## Failure Handling

- An exception in one kingdom item is contained by the service's existing
  behavior and must not drop later queued items.
- A destroyed kingdom is skipped without retry.
- A stale label discovery generation is discarded by the existing generation
  gates.
- A failed label job may retry through the existing bounded retry path, but it
  cannot trigger an unbounded rediscovery scan.
- No backlog is flushed synchronously during save, pause, map activation, or
  month rollover.

## Verification

Automated tests must prove:

- observing one month queues each supplied kingdom exactly once;
- repeated observation of the same month queues nothing new;
- each drain respects its item budget;
- a second month appends behind unfinished first-month work;
- reset removes pending work and allows the current month to be scheduled
  again;
- empty and invalid label-discovery containers consume inspection budget;
- label discovery yields after the configured number of inspections;
- existing hierarchical label cache, lifecycle, and generation tests remain
  green;
- source guards confirm the three monthly services no longer directly settle
  all kingdoms in a month-change `foreach` loop.

Manual large-world verification must show that month rollover no longer
produces the previous periodic red spike, monthly gameplay results continue to
arrive gradually, background label preparation remains responsive, and opening
the hierarchical map reuses cached names without changing its visuals.

## Deployment Constraints

Do not build the main mod DLL. Deploy only changed source and test-independent
runtime files to the installed AW3 source tree, then compare SHA256 hashes.

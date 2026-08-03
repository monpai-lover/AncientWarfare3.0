# AW3 Actor Runtime Performance Design

**Date:** 2026-08-02

**Status:** Approved for implementation planning

## Goal

Reduce AW3's per-Actor runtime overhead and batch-death frame spikes without
changing WorldBox's core Actor update frequency or degrading RTS army behavior.

## Evidence

The current baseline comes from 427 `[AW3 PERF]` samples in
`pre-shared-path-runtime-20260727.log`:

- Actor time averages 2.31 ms and peaks at 43.218 ms.
- `parallel_checks` totals 1204.822 ms and peaks at 9.806 ms.
- Actor AI totals 220.411 ms and peaks at 3.949 ms.
- presentation smoothing totals 169.953 ms and peaks at 1.342 ms.
- the largest Actor spike is a 39.591 ms death check in
  `BatchActors.updateDeathCheck -> Actor.checkDeath -> Actor.die`.
- war-time death processing reaches 24-141 ms, with `school_death` and
  `lineage_archive` as the dominant AW3 stages.
- current samples do not establish `Actor.updateAge` as a measured hotspot,
  but its AW3 postfix performs three unconditional service calls per Actor.

The 2026-08-04 follow-up audit adds two constraints to this baseline:

- `ActorKingdomSafetyService.FilterRuntimeActors` scans and temporarily edits
  the complete global Actor list before `UnitLayer.UpdateDirty` and
  `SimObjectsZones.checkUnits`. The UnitLayer prefix pays this cost even when
  the vanilla method immediately returns during normal gameplay. This is an
  O(actor count) cost per invocation, with O(actor count * invalid actors)
  restoration behavior and avoidable allocations.
- the latest attempted runtime did not produce a valid post-change sample
  because AW3 failed source compilation after an incomplete deployment. No
  runtime result is accepted until the deployed production source set matches
  the repository and both AW3 and its multiplayer child mod compile.

## Hard Constraints

- Do not reduce or replace WorldBox `Actor.updateParallelChecks` frequency.
- Do not reduce military, commander, guard, king, official, or civilian Actor
  update frequency.
- Do not reduce RTS decision frequency or change assembly, replenishment,
  combat, pursuit, occupation, or transport semantics.
- Never access live `Actor`, `City`, `Kingdom`, `WorldTile`, or Unity objects
  from a worker thread.
- Authoritative world mutations remain on the WorldBox main thread.
- Source-folder deployment remains the supported deployment method; do not
  deploy a DLL.

## Architecture

The optimization is divided into five independently disableable components:

1. zero-cost disabled diagnostics;
2. state-triggered AW3 age work;
3. snapshot-based and bounded death persistence;
4. dirty-driven Actor presentation snapshots;
5. deduplicated and bounded path requests.

Every component preserves the existing synchronous behavior as a fallback.
Failure in one component disables or bypasses that component without disabling
Actor AI, RTS, or unrelated AW3 systems.

## Event-Driven Actor Kingdom Safety

Actor kingdom repair is performed at Actor load, affiliation mutation, an
invalid enemy-check boundary, or an explicit bounded repair queue entry. Normal
rendering and zone processing do not isolate, remove, restore, or scan the
global Actor list before entering vanilla code.

Boundary prefixes validate only the Actor already supplied by vanilla and
queue that Actor when invalid. Normal and exceptional Harmony exits restore
temporary state exactly once. Actor disposal and world reset remove all repair,
failure-reporting, and throttle-cache entries associated with the old Actor or
world.

## Diagnostic Fast Path

`AW_ActorAiBenchmarkPatch`, `AW_ActorBatchBenchmarkPatch`, and
`AW_ActorRacePerformancePatch` currently remain on high-frequency Actor paths.
When performance diagnostics and benchmarks are disabled, their prefixes must
return before reading task, race, profession, or timing information.

When diagnostics are enabled, all Actor-detail instrumentation shares a fixed
per-frame budget. Calls beyond that budget contribute only allocation-free
aggregate counters. Postfixes must recognize an empty prefix state and return
immediately.

This component changes observation only. It must never suppress or defer Actor
simulation.

## State-Triggered Age Work

`AW_AgePatch` currently calls the following services for every Actor age tick:

- `DynasticTitleService.OnAgeUpdated`;
- `StandingArmyPeacetimeService.RefreshJob`;
- `DynasticReproductionService.ReleaseExistingMilitaryRole`.

Each Actor receives a lightweight AW3 age-work signature containing only the
inputs required by these services: age stage, profession, military membership,
war state, and dynastic-title eligibility. The postfix compares the current
signature with the last processed signature and invokes only the service whose
inputs changed.

Birth, adulthood, enlistment, retirement, appointment, dismissal, war start,
and war end mark the relevant signature component dirty. The normal age tick
remains a correctness fallback for missed events. This is AW3 work suppression,
not a change to WorldBox age or Actor scheduling.

## Death Processing Pipeline

`Actor.die` retains all operations required to make the Actor immediately dead
and remove it from live military, office, school, path, and presentation
indexes. Persistence and archive work is separated from those live-world
operations.

The main thread captures an immutable `ActorDeathSnapshot` containing primitive
values and copied strings only. Work items use
`worldGeneration + actorId + deathRevision + stage` as an idempotency key.
Separate bounded queues process school, lineage, and history stages in FIFO
order.

Pure DTO transformation and SQLite writes may run on a worker. Completion that
affects live indexes is returned through the main-thread completion queue and
revalidates world generation before committing. No worker closure may capture
a WorldBox or Unity object.

Queue saturation, SQLite busy responses, and worker faults fall back to a
budgeted main-thread retry queue. Retries are bounded and diagnostic messages
are rate-limited. A record is removed only after durable completion or an
explicitly reported terminal validation failure.

Saving establishes a persistence barrier: stop accepting new archive batches,
drain active writes and completions, then checkpoint. If the barrier cannot
complete within its configured timeout, the save fails explicitly rather than
writing a partially archived state. Load, world reset, and multiplayer replica
transitions invalidate results from older generations.

## Incremental Actor Presentation

`AWActorPresentationSnapshot` must stop rebuilding unchanged Actor records in a
fixed full-world pass. Birth, death, equipment, status, sprite, visibility, and
relevant ownership changes mark an Actor ID dirty.

The main thread captures dirty Actor presentation facts under a fixed time and
item budget. Pure DTO layout or copying may run off-thread. Published snapshots
use generation-stamped copy-on-write slots so the renderer always sees one
complete generation.

`calculateMainSprite` runs only when the Actor's sprite-related revision
changes. Initial world entry uses a bounded cursor to create the first complete
snapshot over multiple frames. World reset discards all pending captures and
published generations before accepting new work.

The frame-priority scheduler being disabled must not activate presentation
snapshot capture. If it is enabled later, it consumes the same dirty-driven
pipeline rather than introducing a second full scan.

## Path Request Deduplication

Path movement frequency is unchanged. Repeated equivalent calculations are
coalesced with the key:

`actorId + startRegion + targetRegion + movementType + terrainRevision`.

One active calculation owns the key; equivalent callers attach as waiters.
Completed immutable paths are reused while the key remains valid. Target
changes, meaningful start-region changes, death, boarding, disembarking,
terrain revision changes, and world reset invalidate the entry.

The cache has hard limits for active requests, completed entries, waiters per
request, and age. Expired or unreachable requests fall back to the existing
live movement behavior. RTS long-distance movement remains continuous and is
not split into lower-frequency steps.

## Gates And Failure Isolation

- Paused games do not apply simulation completions; pure database work may
  finish while paused.
- Multiplayer replicas do not execute authoritative death, age, or path writes.
- Loading and world reset advance the world generation before clearing queues.
- Stale completions are discarded without touching the new world.
- A module fault opens that module's synchronous fallback and emits a bounded
  diagnostic; it never disables Actor AI or RTS.
- Queue and cache sizes are observable through aggregate diagnostics without
  enabling per-Actor detail sampling.

## Implementation Order

1. Capture a repeatable baseline and add pure rules tests and source guards.
2. Add the diagnostic disabled fast path and bounded detail sampling.
3. Add age-work signatures and event-driven invalidation.
4. Add death snapshots, bounded queues, idempotency, and the save barrier.
5. Convert presentation capture to dirty-driven incremental snapshots.
6. Add path request coalescing, cache bounds, and lifecycle invalidation.
7. Run automated verification and the full game scenarios.
8. Deploy changed source files only after every functional and performance gate
   passes.

Each stage must be independently measurable and reversible. A later stage does
not compensate for a failed earlier functional gate.

## Automated Verification

Tests and source guards must prove:

- disabled diagnostics do not read task IDs or create Actor detail scopes;
- unchanged age signatures call none of the three AW3 age services;
- each relevant signature change invokes its service exactly once;
- death work survives duplicate submission, SQLite busy responses, save, load,
  and world reset without loss or duplication;
- an older world generation cannot commit into a newer world;
- worker delegates contain no live WorldBox or Unity object references;
- equivalent path requests calculate once and invalidated requests recalculate;
- every queue and cache obeys its item, time, and age bounds;
- replica and pause gates preserve authority and simulation behavior.

## Runtime Verification

Use the same approximately 20,000-Actor and 100-kingdom world for baseline and
optimized runs. Run 1x and 2x speed for at least 30 minutes each.

The war scenario uses ten cities and twenty dispersed armies and covers:

- 10-versus-10 assembly from distant positions;
- marching, combat, replenishment, and immediate re-engagement;
- land movement, water spans of five tiles or fewer, and ship transport;
- sequential city occupation and target reselection;
- mass casualties concurrent with school and lineage archival work;
- pause, resume, save, load, reset, and multiplayer replica transitions.

Any army that idles because of this optimization, loses its mission, changes
leader incorrectly, fails to replenish, or fails to transport is a blocking
regression regardless of frame-time improvement.

The immediate recovery benchmark additionally loads
`C:\Users\24908\AppData\LocalLow\mkarpenko\WorldBox\autosaves\1785772934`
through `AW3_BENCHMARK_LOAD_PATH`. After a two-minute warm-up it runs at 20x
for ten minutes and records FPS, P95/P99 frame time, Actor batch stages, RTS
controller stages, path submissions, deaths, GC, population, and active army
count. The first recovery gate is an average of at least 20 FPS. Missing 20x
selection, a compile/load failure, or an incomplete sample invalidates the run.

## Acceptance Criteria

- normal Actor frame-time P95 improves by at least 35 percent;
- Actor frame-time P99 is below 8 ms;
- `parallel_checks` P95 is below 4 ms without changing its update frequency;
- a batch-death main-thread frame remains below 16 ms;
- the death background queue normally drains within two seconds;
- active path counts stabilize and do not grow with runtime duration;
- managed memory has no persistent upward trend over a 30-minute run;
- there are no uncaught exceptions, stale-generation commits, duplicate archive
  rows, or incomplete saves;
- all RTS functional scenarios behave identically or better than the baseline.

If a target cannot be met without changing WorldBox Actor scheduling, retain
the functional constraints and report the remaining cost rather than silently
introducing Actor throttling.

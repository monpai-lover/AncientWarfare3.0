# RTS Controller And Path Pressure Design

## Goal

Remove the two demonstrated sources of RTS-related actor pressure without
reducing the update cadence of army movement, rally, combat, transport, or
recovery:

1. repeated per-city, per-tile hostile-military scans inside the RTS
   controller; and
2. unbounded competition between civilian and operational path requests.

The implementation must preserve live war responsiveness. A route, target,
city control change, transport state, or operational state transition must
be visible to an army on its next authority cycle. The optimization may delay
noncritical civilian movement, but it must not discard the actor's behavior.

## Evidence

The baseline sampled `aw3_army_rts_controller` 161 times for a total of
614.048 ms, with a 46.1 ms maximum sample. `CityAttackZoneService`
`HasHostileMilitaryInside` scans every tile in every city zone and creates a
capturing callback for each tile. It is invoked from objective classification
and controller target completion logic, so several armies can rescan the same
city during one authority cycle.

The same run generated 167,262 global path requests, reused none, and still
had 1,342 active paths at completion. The global `Actor.goTo` prefix currently
intercepts ordinary actors as well as RTS actors. Social, sleep, idle, and
other low-value requests can therefore consume the same request capacity as a
captain, troop rally, transport passenger, or scholar journey.

The baseline does not show ordinary formation maintenance as the primary
controller hotspot. Formation behavior is not changed by this design.

## Non-Goals

- Do not lower the update cadence of active army march, rally, assault,
  transport, combat, retreat, or recovery states.
- Do not make city-control or enemy-presence facts stale across an authority
  cycle after a relevant state-change event.
- Do not add an environment-variable control. Existing AW3 settings continue
  to control diagnostics.
- Do not alter the global traversal snapshot format, worker safety model, or
  WorldBox actor combat resolution.

## Design

### 1. Per-Cycle City Military Facts

`CityMilitaryThreatFacts` becomes the sole RTS reader for whether a city
contains hostile warriors. Its cache key is:

```text
world generation + authority cycle + war id + observing kingdom id + city id
```

The first lookup for a key performs the existing physical zone/tile scan.
Subsequent lookups in the same authority cycle reuse the Boolean result.
The cache is cleared when the world generation changes and is rotated at the
start of the next authority cycle. It is explicitly invalidated for a city
when city control changes, a target is completed or reassigned, a war starts
or ends, or a kingdom enters/leaves that war. This makes event-driven facts
available immediately instead of waiting for a time-to-live expiry.

The physical scan uses one reusable scan context and callback per lookup; it
does not allocate one closure per tile. The callback exits as soon as it finds
a hostile warrior. It stays on the main thread and reads only live WorldBox
objects, matching the current safety model.

The cache is strictly a controller-cycle fact cache, not a long-lived world
index. It cannot claim that a city is empty after a later controller cycle,
and it cannot outlive a world load.

### 2. Path Request Classification And Coalescing

Every `Actor.goTo` request is assigned a work class at submission time:

| Class | Examples | Scheduling rule |
| --- | --- | --- |
| Operational | RTS captain march, rally, recovery, troop catch-up, passenger transport, ship transport | serviced before all lower classes |
| Essential travel | historical school journey and ruler/official travel explicitly marked as a journey | serviced after operational work |
| Ambient | social, sleep, singing, idle wandering, and other ordinary civilian movement | bounded queue; deferred when higher classes are backlogged |

An actor may own one effective request. Submitting the same destination and
traversal options reuses its pending or streaming request. Submitting a new
destination cancels the superseded request before the replacement is queued.
Queue bookkeeping records both the original and replacement reason so that
coalescing is not mistaken for a path failure.

Workers take operational items first, then essential travel, then ambient
items. Ambient work receives a bounded service share when higher-priority work
is continuously present, so civilians eventually move but cannot flood the
queue. Request ownership, path consumption, worker-thread snapshot safety,
and cancellation semantics remain in `AWPathFinder` and
`AWPathMovementBridge`; classification cannot grant a second owner to an
actor.

### 3. RTS Controller Fact Reuse

`ArmyRtsControllerService` continues to evaluate active operational movement
on each authority cycle. It changes only its repeated strategic reads:

- target completion and objective classification query
  `CityMilitaryThreatFacts`;
- repeated target validation for the same city uses the current-cycle fact;
- target, route, mission, army roster, transport, supply, and combat state
  changes mark the affected controller state dirty immediately.

The controller must not use a stable-state throttle to skip active marching
or rallying. Caching removes duplicated work instead of postponing decisions.

### 4. Asynchronous Next-Cycle Planning

The expensive physical scan remains main-thread work because `WorldTile`,
`Actor`, `City`, and their collections are live Unity/game objects. They must
never be read by a worker thread. After the current-cycle cache has produced
its facts, the main thread may capture an immutable, pure-data summary for
each relevant city:

- world generation, authority cycle, war ID, and kingdom IDs;
- city ID, controller ID, and city-control revision;
- warrior counts grouped by kingdom ID and the current hostile-presence fact;
- route, target, supply, and objective revisions required to reject stale
  planning output.

Workers may use only these summaries to rank future targets, estimate front
pressure, and prepare the next war-director allocation. A result returns to
the main thread as a proposed plan, not an order. Before it can alter a
mission, the next authority cycle validates the live war, city control,
mission, route, and revision values. A mismatch discards the proposal and
continues using the current synchronous facts.

Async analysis is therefore an acceleration for war-director bulk scoring,
not a replacement for immediate RTS control. It cannot tell an army to march,
hold, retreat, or attack based solely on a stale snapshot.

The immediate hostile-presence scan retains its early-exit behavior. Building
an async snapshot must not turn it into a full city census: force figures are
copied only from existing army/garrison summaries. When such a figure is not
available, the snapshot marks it unknown and the background scorer uses a
conservative value. A deferred plan never causes an additional tile scan only
to improve its score.

### 5. Diagnostics

When the existing AW3 performance diagnostics setting is enabled, one sampled
summary includes:

- city military fact requests, physical scans, cache hits, and invalidations;
- city planning snapshots, background plans submitted/applied/rejected, and
  rejection reason by stale world, war, city-control, or mission revision;
- path submissions by class, same-request reuse, replacement cancellations,
  queue depth/high-water mark by class, and active requests by class;
- the existing controller stage timing, including `target_facts`.

Diagnostics remain aggregate-only. No per-tile, per-request, or per-frame
logging is emitted while diagnostics are disabled.

## Error Handling And Lifecycle

An invalid war, city, kingdom, or zone returns the same conservative `false`
result as the current implementation and is not stored as a valid fact. A
scan exception is likewise not cached. Cache and scheduler state are cleared
on world reset, load, shutdown, and when AW3 yields pathfinding ownership.

Priority classification must fail closed to `Ambient` for unknown behaviors;
explicit RTS route-provider submissions bypass name-based inference and are
always operational. This prevents an unrecognized modded behavior from
incorrectly receiving military priority.

## Validation

Rule tests must first demonstrate the old missing behavior, then verify:

1. identical hostile-military queries within a controller cycle physically
   scan once and return the first result;
2. the next cycle or an explicit invalidation requires a fresh scan;
3. invalid scan inputs are not cached;
4. same-actor/same-target submission reuses work;
5. a replacement target cancels the previous request;
6. operational work is chosen before essential and ambient work;
7. bounded ambient fairness still eventually selects ambient work;
8. an async plan built from a valid snapshot is accepted only when all
   required revisions remain valid;
9. an async plan with a stale world, war, city-control, or mission revision
   is rejected without altering the current army mission.

The complete rules suite and the AW3 build must pass. Live validation runs a
reproducible multi-city war with RTS, route visuals, and map information in
the same configuration as the baseline. Acceptance requires:

- no army mission, transport, rally, or combat regression;
- no actor left waiting on a cancelled replacement request;
- `target_facts` timing and physical city scans substantially below the
  baseline in the same save;
- nonzero request reuse and a bounded active/pending path population;
- no worker exception, Harmony exception, or stale-world access in Player.log.

## Delivery Order

1. Add pure rules for cycle cache keys, invalidation, and priority selection.
2. Add the failing tests and prove they fail against current behavior.
3. Implement city facts caching and rerun the focused tests.
4. Add path priority/coalescing tests, then implement the request scheduler.
5. Add snapshot/revision tests, then implement async next-cycle planning.
6. Extend aggregate diagnostics and run the complete rules suite plus build.
7. Deploy only after the build succeeds; collect a comparable live diagnostic
   sample before claiming a frame-time improvement.

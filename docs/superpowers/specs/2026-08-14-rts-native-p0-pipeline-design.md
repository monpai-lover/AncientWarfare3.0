# RTS Native P0 Pipeline Design

## Goal

Make RTS armies and Royal Guards reliable under large-step scheduling without
copying isolated pieces of WorldBox movement behavior. Military actors must
receive first-priority tactical updates, preserve the original task lifecycle,
enter combat when armies meet, yield to transport, and avoid unbounded
main-thread pathfinding spikes.

## Decision

Large-step military P0 will run the original Actor behavior sequence as a
cooperative, highest-priority stage. It will no longer execute target selectors
or `goTo` directly.

The supported order for each admitted actor is:

1. publish RTS strategic data needed by the original task;
2. yield immediately when custom transport, vanilla taxi, or boat ownership is
   active;
3. run original current-target and enemy-search checks;
4. run the original task verifier;
5. run original path advancement;
6. run original AI task behavior;
7. run original smooth movement once;
8. mark the actor processed so the normal post pipeline cannot process those
   stages a second time.

This restores original waits, task action indices, movement completion,
pathfinding failure handling, combat acquisition, and Royal Guard follow
behavior.

## Cooperative Priority Stage

`AWCooperativeActorPostRunner.Start` snapshots the current military-priority
actors and initializes a dedicated P0 stage. `Step` processes at most
`AWPerformanceSettings.SimulationBatchSize` actors per invocation and returns
control to the frame scheduler between chunks.

The ordinary Actor post stages do not begin until the military snapshot is
drained. This preserves the requirement that military movement receives
priority over ordinary simulation while avoiding one monolithic loop over all
military actors in a single scheduler step.

Actors removed, killed, transferred to combat, or transferred to transport are
unregistered while the snapshot is processed. Actors registered after the
snapshot begins enter the next logical Actor cycle.

## Native Task Lifecycle

P0 must not instantiate or invoke `BehCityActorCheckAttack`,
`BehFindTileNearbyGroupLeader`, `BehArmyRtsMission`, or
`BehArmyRtsRetreatTarget` directly. It prepares only the data those tasks read,
such as a locked city attack order or a current Royal Guard follow target.

The actor's existing task executes through `b6_updateAI`. Therefore:

- army captains retain `BehWarriorCaptainWait`;
- ordinary soldiers retain `BehRandomWait(1f, 2f)`;
- failed `BehGoToTileTarget` attempts stop the task normally instead of being
  retried every P0 cycle;
- `is_moving` and path exhaustion use the original `b4`/`b5` gates;
- Royal Guards no longer need a separate manual `goTo` branch.

RTS mission identity and locked target selection remain owned by
`ArmyRtsControllerService`; only physical execution returns to the native task
pipeline.

## Combat Ownership

Military P0 runs the original target validation and enemy search before task
movement. An actor that acquires a hostile target exits strategic P0 ownership
and is allowed to run the original combat task.

This ensures two RTS-controlled armies can discover each other during a field
encounter. Existing army-level engagement thresholds and target-city handoff
remain responsible for releasing the rest of the army to vanilla combat.

P0 must not clear a valid hostile target, replace a combat task, or advance a
strategic path after combat has taken ownership.

## Transport Ownership

The following states all suspend military P0 movement:

- the actor is inside a boat;
- `ArmyRtsTransportService` owns the actor;
- `TaxiManager` has an active request for the actor.

Task repair and P0 admission use the same transport predicate. Cross-island
retreat may create vanilla taxi requests once, after which P0 yields until the
request completes or is cancelled. No P0 cycle may rescan the full army merely
to recreate existing taxi requests.

## Royal Guard Behavior

Royal Guards use their existing protect/follow tasks through the native P0
sequence. The service may publish the king's current offset target, but P0 does
not call `goTo` directly.

While a guard is moving, original path movement remains authoritative. After a
path completes, the follow task's normal action order determines when a new
target is selected. Threat acquisition immediately transfers the guard from
follow movement to protection combat.

## Diagnostics

Diagnostics remain optional and must not change scheduling behavior. Captains,
Royal Guards, and anomalies may retain detailed stages, while ordinary members
remain deterministically sampled.

The new P0 stage records chunk boundaries and actor outcomes including native
AI execution, combat yield, transport yield, path advancement, and smooth
movement. It must not rebuild large path strings or emit one log entry per
ordinary soldier per stage.

Route visualization remains limited to 512 sampled points per army at the
existing 30-second capture cadence. It is separate from physical movement and
must not be consulted by P0.

## Failure Handling

- An invalid or dead actor is unregistered and does not stop the P0 chunk.
- One actor exception is logged and processing continues with the next actor.
- A failed native task remains under normal task cooldown/wait behavior.
- An unavailable transport target yields to the existing transport recovery
  logic instead of starting custom land movement.
- A completed P0 snapshot always advances to ordinary post processing; newly
  registered actors cannot keep the current snapshot open indefinitely.
- Processed actors are excluded from the later path and smooth movement stages
  to guarantee exactly one physical movement update per logical cycle.

## Verification

Rules tests must prove:

- P0 snapshot chunks are capped by `SimulationBatchSize` and drain before
  ordinary work is admitted;
- moving actors rely on the original verifier/path gates and are not manually
  re-primed;
- native AI runs only when combat and transport have not taken ownership;
- vanilla taxi requests count as transport ownership;
- Royal Guard P0 no longer requires manual `goTo` decisions;
- processed actors are excluded from later path and smooth movement stages.

Source guards must prove:

- P0 does not directly invoke native target selectors or `goTo`;
- P0 invokes the original enemy, verifier, path, AI, and smooth movement stages
  in order;
- the old synchronous all-actor loop is replaced by a cooperative P0 stage;
- field combat still uses the existing army-level release threshold;
- no route-visualization data enters physical movement decisions.

Runtime verification must cover same-island attack, field interception between
two RTS armies, long and one-tile follower paths, a moving king with Royal
Guards, cross-island retreat, and armies larger than 256 members. Logs must show
bounded P0 chunks, native waits between completed paths, combat acquisition,
and transport yield without repeated selector or `goTo` stages.

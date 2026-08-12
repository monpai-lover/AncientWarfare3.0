# RTS Land March Reliability Design

## Goal

Make an RTS army move as a unit during land marches under both native and
large-step scheduling. A captain must not leave most soldiers behind, and a
stranded follower must recover without losing or replacing the current RTS
mission.

The player-visible acceptance criteria are:

- the captain and at least 90 percent of the live eligible formation
  population keep advancing during a land march;
- a separated follower receives local recovery within seconds;
- the captain waits when the escort quorum is not ready;
- route rebuilding or captain-side escalation occurs only after bounded local
  follower recovery fails;
- recovery preserves the mission ID, war, target city, role and director
  generation;
- large-step scheduling does not reduce tactical movement responsiveness.

## Scope

This design covers RTS-controlled land movement from rally through advance,
assault approach, retreat and regroup. It changes formation observation,
escort gating, follower route recovery and related diagnostics.

It does not change strategic target selection, war allocation, naval
transport, combat ownership, recruitment, Royal Guard behavior or peacetime
city-border patrol. Transport continues to use its existing movement owner and
is exempt from the land escort gate while that owner is active.

## Architecture

The existing separation remains authoritative:

- `KingdomWarDirectorService` plans missions at a bounded strategic cadence;
- `ArmyRtsControllerService` advances mission state and owns tactical army
  jobs;
- `ArmyFormationService` incrementally observes live eligible members and
  publishes formation counters;
- `AWArmyMarchService` owns the shared leader trail, complete shared routes and
  bounded independent follower corrections;
- `ArmyStallWatchdogService` escalates persistent physical stalls.

No RTS system is globally removed from large-step scheduling. Strategic work
remains low frequency and bounded; admitted tactical actor behavior continues
to run at its short execution cadence. The reliability fix is implemented at
the controller, formation and march boundaries rather than by running the war
director every frame.

## Formation Observation Gate

Every new or materially changed mission starts a formation observation
generation tied to the army roster version. The generation records:

- expected live roster population;
- observed eligible followers;
- rallied eligible followers;
- captain presence;
- observation cursor and completion state;
- the mission identity and route revision being observed.

Mission publication and roster-version changes restart or safely rebase this
bounded observation. They do not clear the strategic mission or request a new
director assignment.

While observation is incomplete, a land-marching captain remains in rally or
escort hold. The controller prioritizes bounded follower job assertion and
shared-route installation during that period. An incomplete observation is
never interpreted as zero required escorts and never authorizes the captain to
depart alone.

Observation remains cursor based. Each controller item processes only its
existing bounded member slice; no frame performs a whole-army scan. Once the
slice cycle completes, the counters become the stable input to the escort
gate. A roster mutation starts a new generation without allocating a full
replacement list.

## Ninety Percent Escort Quorum

The land escort quorum is based on the resolved eligible formation
population, including the captain when present. The required nearby population
is `ceil(eligible population * 0.90)`.

The captain may advance only when:

- formation observation for the current roster and mission is complete;
- the captain is live and has a valid tile;
- the army has minimum operational force;
- at least the 90-percent quorum is rallied near the formation anchor; and
- neither combat nor transport has legitimately transferred movement
  ownership.

Immediate combat may temporarily own local actor movement, but it must not be
used as a general bypass that lets an isolated captain begin a strategic land
march. Transport retains its explicit exemption because its embarkation and
voyage state already coordinates the army separately.

The gate includes hysteresis to avoid stop-start oscillation. After departure,
minor transient loss below 90 percent does not stop the captain immediately.
The initial constants are a two-real-second grace window and a 75-percent
lower safety floor. The captain enters escort hold after two continuous seconds
below 90 percent, or immediately below 75 percent. Resuming requires the full
90-percent quorum. These constants are isolated in rules and covered by tests.

## Follower Movement And Recovery

Followers prefer the existing shared leader trail and complete shared-route
revision. A follower within formation tolerance holds its slot without
submitting a path. A materially separated follower receives a bounded local
correction toward its current formation target.

Recovery escalates in this order:

1. reassert the existing follower job and task;
2. clear only the follower's stale installed-route state;
3. reinstall the current shared-route revision;
4. submit one bounded independent local correction;
5. after ten real seconds of continued non-progress, use an alternate nearby
   formation slot;
6. after twenty real seconds of continued non-progress, use the existing
   teleport-to-captain fallback, only outside combat and transport.

Task reassertion occurs on the first eligible stalled observation. Clearing,
reinstalling and submitting the local correction occur after five real seconds
without position progress. Successful movement resets every escalation timer.

Each follower owns independent recovery timestamps and pending correction
state. One blocked member cannot serialize the whole army, consume another
member's correction slot or force immediate strategic replanning. Concurrent
independent corrections retain the existing per-army cap and timeout cleanup.

If fewer than 90 percent are rallied, the captain pauses while follower
recovery continues. Captain-side route rebuilding occurs only when the captain
or shared strategic route is itself stale, or when a completed follower
recovery window still cannot restore the quorum. Rebuilding preserves the
mission and publishes a new route revision for the same target.

## Mission Continuity

Formation recovery is tactical and must not call mission invalidation,
director reassignment or offensive-continuity fallback. The following fields
remain unchanged during follower recovery and same-target route rebuilding:

- army and kingdom IDs;
- war and target city IDs;
- mission role and proposal kind;
- front and player-order ownership;
- director generation and issue identity.

Only normal objective completion, invalid war participation, destroyed target,
army invalidation or explicit player replacement may end the mission. A route
revision is not a mission revision.

## Large-Step Scheduling

Formation observation and controller work consume the existing bounded RTS
controller budget. Large-step mode must admit the same tactical controller
work for each logical simulation pass as native mode; it must not defer actor
movement recovery until the next strategic director publication.

Follower AI tasks use short condition-based retries while an RTS mission owns
them. They do not enter social or generic idle tasks between route steps.
Strategic planning remains bounded and low frequency, so this design does not
increase whole-kingdom planning frequency or introduce per-frame army scans.

## Diagnostics

Sampled diagnostics distinguish observation delay from physical stalls. A
march sample records:

- expected roster, eligible living, observed and rallied counts;
- observation generation, cursor and completion;
- required 90-percent quorum;
- shared-route revision and installed follower count;
- followers in local correction, follower recovery and long-stall fallback;
- captain hold reason and hold duration.

The diagnostic path reports an explicit `formation_observation_pending` hold
instead of `formation_living=0` when a scan has not completed. Repeated zero
eligible population after a completed observation is treated as an invariant
failure and schedules bounded roster reconciliation.

## Failure Handling

- A missing shared route keeps the captain in escort hold and requests the
  existing route pipeline; it does not release the mission.
- A stale follower task reasserts only that actor's RTS task.
- A blocked formation slot tries bounded nearby alternatives before teleport.
- Combat and transport suspend follower stall timers so legitimate stationary
  periods do not escalate.
- A roster mutation invalidates the current observation generation and
  restarts bounded observation.
- If 90 percent can never be reached because eligible membership changed, the
  completed observation recalculates the population rather than waiting on
  dead, transferred or ineligible actors.
- Exceptions in one follower recovery advance the cursor and cannot stop the
  remaining army from being observed or repaired.

## Verification

Rules tests must prove:

- incomplete formation observation never authorizes captain departure;
- 89 percent rallied blocks departure and 90 percent permits it;
- hysteresis prevents one-sample stop-start oscillation but cannot permit a
  substantially detached captain;
- zero observed members before completion is represented as pending, not as
  an empty army;
- roster changes restart observation without changing the mission;
- follower recovery escalates in the specified order;
- local recovery and same-target route rebuilding preserve mission identity;
- combat and transport suppress inappropriate stall escalation;
- correction concurrency and memory remain bounded.

Integration and source guards must prove:

- captain movement reads the completed formation counters and 90-percent
  quorum;
- follower job assertion occurs while observation is pending;
- follower recovery does not call mission invalidation or war-director target
  reassignment;
- large-step controller admission remains separate from strategic planning;
- no new whole-army or whole-kingdom per-frame scan is introduced.

Runtime verification uses land-connected cities, armies below and above 128
members, narrow terrain, blocked formation slots and both scheduler modes. In
each run, the captain and at least 90 percent of live eligible members must
make sustained forward progress; deliberately stranded followers must recover
within seconds, and the current mission must remain unchanged throughout
recovery. Performance diagnostics must show bounded member slices and no new
frame spike proportional to total kingdom population.

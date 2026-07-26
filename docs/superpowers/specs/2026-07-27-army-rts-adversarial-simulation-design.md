# Army RTS Adversarial Simulation Design

## Goal

Add a deterministic deployment gate that exposes RTS armies to the same kinds
of interference that can occur in a live WorldBox war. The simulation must
fail when a viable army can remain operationally idle, when vanilla behavior
can steal RTS ownership, or when occupation and transport fail to advance the
war.

This is an offline test harness. It does not start Unity, mutate a player save,
or claim to replace the final in-game deployment test.

## Scope

The simulation covers:

- multiple armies and a chain of enemy cities;
- 80-percent rally readiness, losses, replenishment, and degraded departure;
- rally, march, deploy, assault, hold, pursue, retreat, regroup, and replenish;
- completed occupation followed by selection of another unconquered city;
- route waiting, physical no-progress, alternate endpoints, retreat, and
  replanning;
- cross-ocean requests, no-boat production, reservation, loading, departure,
  partial unloading, and reuse of a bounded fleet;
- valid captain preservation and invalid captain replacement;
- vanilla decision, local task, city attack-order, and follower-order writes;
- civilian combat protection;
- war-goal completion and the five-year exhaustion settlement boundary.

The harness does not reproduce WorldBox combat damage formulas, animation,
Unity pathfinding, or rendering. Existing runtime source guards and the later
in-game log test remain responsible for verifying Harmony patch installation
and actual engine integration.

## Architecture

Create `Tests/ArmyRtsAdversarialSimulation` as a standalone deterministic test
project. The project links the production RTS rule and state-model files it
exercises. Test-only world, kingdom, city, army, actor, route, boat, and war
records supply the mutable boundary normally provided by WorldBox.

The harness has four components:

1. `ScenarioState` owns all simulated entities and the fixed random seed.
2. `VanillaInterferenceDriver` attempts known conflicting writes.
3. `RtsSimulationDriver` applies production RTS decisions and modeled service
   effects in the same authority order used by the mod.
4. `ProgressOracle` evaluates state-specific liveness and emits a bounded
   per-army trace on failure.

The simulator may add a small pure production rule when the live controller
needs an explicit ownership decision, such as deciding whether to reassert an
RTS task after a foreign task write. Test-only shortcuts must not be added to
production services.

## Tick Order

Every unpaused tick executes this order:

1. apply scheduled world events, casualties, reinforcements, occupation, and
   war-state changes;
2. attempt vanilla decision, task, city-target, follower, and captain writes;
3. apply RTS ownership gates and bounded job/task repair;
4. run the kingdom assignment and army state transition model;
5. advance route, formation, transport, combat, and occupation outcomes;
6. sample watchdog and state-specific progress deadlines;
7. evaluate safety, ownership, target, and liveness invariants;
8. append a compact trace entry only when observable state changes.

Paused ticks may update neither deadlines nor world progress.

## Vanilla Interference Model

The driver attempts the following writes at deterministic points:

- choose a nonmilitary Decision and replace the current actor task;
- assign starvation/eating and social tasks while an army is marching;
- install a vanilla city attack target that differs from the RTS mission;
- issue random captain movement and follower movement orders;
- replace a living, valid captain;
- remove a dead or detached captain and request a replacement;
- retire one member and add one replacement member;
- leave a path in `Waiting` or report success without physical movement;
- deliver only part of an army during one boat trip.

RTS must reject strategic writes for actors with a live RTS mission. It may
temporarily yield to immediate vanilla melee, damage, death, and required boat
boarding. A valid captain replacement attempt must fail; replacement becomes
legal only after death, detachment, authority-role promotion, or invalid
kingdom membership.

## Progress Model

An army records progress when at least one of these changes:

- state;
- target city;
- route cursor or formation anchor;
- captain or formation position;
- living or rallied member count;
- assigned boat, embarked count, or landed count;
- occupation ownership or target completion;
- supply or organization while regrouping/replenishing;
- explicit recovery action.

State-specific production deadlines remain authoritative. The oracle must not
invent one global timeout that would allow a transport wait to expire like a
land march or penalize a paused room. When a deadline expires, the next legal
recovery action must occur within the next controller opportunity.

An army is considered incorrectly idle when all of these are true:

- its war and mission are valid;
- at least one legal enemy or recapture target exists;
- it has the minimum viable force or a live replenishment path;
- it is not paused, in immediate combat, embarked, or legitimately waiting on
  a bounded transport queue;
- neither operational progress nor a recovery transition occurs before the
  applicable deadline.

## Scenarios

### Land Campaign Continuation

Three armies attack a four-city enemy through two fronts. One city begins
already under friendly wartime control. The simulation attempts to return all
armies to that occupied city after every planning pass. The occupied city must
remain excluded when no enemy military actor is present, target reservations
must distribute viable armies, and every completed assault must wake the
affected mission index and produce a new target.

### Ownership and Lifecycle Interference

A marching army receives repeated vanilla Decision, training, eating, social,
and follower orders. The valid captain is also offered for replacement. RTS
ownership must remain authoritative, noncombat task writes must be rejected or
repaired within a bounded controller pass, and the captain identity must remain
stable. After the captain dies, one valid replacement must be selected without
discarding the army or its mission. Roster loss and replenishment must not
produce an empty shell or reset the mission indefinitely.

### Route Failure Ladder

A route first stays in planning, then reports ready without moving, then fails
at its endpoint. Recovery must proceed through route rebuild, alternate
endpoint, retreat, and target cooldown without cycling forever. A later valid
target must allow the army to resume offensive work.

### Cross-Ocean Queue

Two armies request transport while no usable ship exists. A route-valid dock
must create a production demand and immediately attempt original dock
production. Failed builds retry only after cooldown. The first available
transport or combat ship serves the oldest viable request, unloads the entire
army over as many trips as required, and then serves the next request. Naval
combat work cannot preempt an assigned army transport. No actor or ship may be
teleported by the simulator's expected production path.

### War Completion

The attacker captures only the cities required by its selected multi-term war
goal. Once the score satisfies those terms, the peace runtime must be queued
instead of assigning additional conquest targets. A separate unresolved war
crosses five years, accumulates annual exhaustion, and settles by the current
authoritative score when exhaustion reaches its forced-settlement boundary.

## Hard Invariants

The test fails immediately if:

- an RTS-owned actor accepts a vanilla strategic Decision;
- vanilla and RTS both own the same strategic movement tick;
- a valid captain changes identity;
- a king or city leader enters an army or contributes occupation;
- a military actor attacks a civilian or civilian building protected by the
  wartime rules;
- an occupied city without enemy military presence remains an assault target;
- a viable army has no progress and no recovery action past its deadline;
- a route failure ladder repeats an earlier recovery step indefinitely;
- a transport demand never reaches build attempt, reservation, loading, or a
  classified failure;
- a partial boat trip abandons remaining members without a live queue entry;
- war-goal satisfaction produces another offensive mission before peace;
- a war beyond the exhaustion boundary remains open without a settlement
  attempt.

## Diagnostics

Use a fixed seed and monotonically increasing tick number. Each army keeps a
ring buffer containing the most recent 64 state changes. A failure prints:

- seed, tick, room pause state, and war age;
- army, kingdom, war, target, role, posture, and state;
- captain, living, rallied, embarked, landed, supply, and organization;
- route state, progress cursor, recovery count, and transport outcome;
- the attempted vanilla write and the RTS ownership decision;
- the 64-entry state-change trace.

Successful runs print only scenario names, seeds, ticks, completed objectives,
and total recovery actions.

## Verification Strategy

Development follows RED-GREEN-REFACTOR. Each scenario is introduced as a
failing test against production rules or a missing ownership rule. The minimal
runtime change is then implemented and the scenario rerun. Source guards must
connect each pure ownership rule to the corresponding live Harmony or
controller entry point.

After the simulation passes, rerun all existing Army RTS, transport,
occupation, military lifecycle, war-goal, war-exhaustion, vassal attribution,
and school maritime tests. The main mod is not deployed by this specification.
Deployment and live-log verification remain a separate user-authorized step.

## Acceptance Conditions

- All scenarios pass under at least 32 fixed seeds.
- Each seed runs at least 10,000 ticks or reaches a valid completed settlement.
- No eligible army violates a state-specific progress deadline.
- Every injected strategic Decision is blocked for RTS-owned actors and
  preserved for actors outside RTS ownership or in off/shadow mode.
- A living valid captain remains stable; invalid captains are replaced once.
- Occupied targets advance, cross-ocean queues drain, and partial trips retain
  every remaining member.
- The route failure ladder terminates in recovery, retreat, or a new target.
- War goals and exhaustion prevent unbounded wars.
- All pre-existing regression tests remain green.


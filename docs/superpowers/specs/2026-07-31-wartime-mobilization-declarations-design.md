# Wartime Mobilization And Declaration Integrity Design

## Goal

Make wartime troops act as military units, make pre-war mobilization visibly
and reliably deploy to the frontier, and preserve army, guard, replenishment,
and declaration state across annexation and concurrent wars.

## Confirmed Rules

- A wartime military actor may eat, receive medical care, embark, disembark,
  retreat, rally, march, deploy, replenish, transport, defend, and fight.
  It may not socialize, sleep, sing, laugh, or execute other civilian leisure
  work while its kingdom is at war.
- Absorbing a vassal removes the absorbed crown's royal-guard identity and
  guard formation. Living former guards remain warriors of the new realm and
  are reindexed into ordinary armies of their current city; none may retain a
  guard flag, guard task, or army under the extinct crown.
- A replenishment operation ends immediately when its designated source city
  confirms that no usable reserves remain. It retains the normal 20-second
  window only while there is still a possible recruit to consume.
- A numerically dominant army does not retreat solely because supply or
  organization is low. It regroups in place when the target is effectively
  open or local force advantage remains decisive. Retreat requires tactical
  disadvantage, catastrophic losses, an unreachable/safe withdrawal need, or
  a genuine survival failure.
- Upon a diplomatic war notice, every living ordinary army receives a
  frontier assignment immediately. Recruitment continues by month and joins
  its assigned army; the declaration gate still waits for the existing 70%
  readiness and frontier-arrival requirements or the forced declaration year.
- Targeted decisions progress each of the game's four months. The monthly
  increment is one quarter of the existing yearly rate, so yearly speed and
  completion time do not change. UI reports monthly gain and remaining months.
- One kingdom may hold several pending declarations against different
  kingdoms. Each declaration has independent target, goal, notice timeline,
  deployment assignment, cancellation reason, and execution result.

## Architecture

### Wartime Task Gate

`WartimeMilitaryTaskGate` is a pure classifier backed by the existing active
military identity and live kingdom-war tests. It recognizes the allowed
military/survival task families and the blocked civilian families. A Harmony
prefix on `AiSystemActor.setTask` rejects blocked task transitions, including
job-selected transitions that bypass `Actor.setTask`. Prefixes on active
social, emotion, and sleep actions stop an already-running civilian task when
war begins between task selection cycles.

The gate does not issue a replacement civilian task. It leaves RTS ownership
to reassert the army's current rally, march, deployment, assault, transport,
or retreat task on the next authority cycle. Actors without an active RTS
mission may remain idle until their military director assigns one.

### Annexation Guard Reconciliation

`VassalService.TryAbsorbVassal` calls a dedicated reconciliation service only
after all city transfers and relation closure commit. The service snapshots
the absorbed kingdom's guards, dismisses their guard-only army/formation,
clears guard fields and traits, preserves warrior status, and asks the army
index and war director to rebuild ordinary city armies under the suzerain.
Failure before relation closure leaves guard state untouched; a reconciliation
failure after a committed annexation falls back to safe dismissal and queues a
bounded retry, never retaining a guard army owned by the absorbed kingdom.

### Replenishment And Retreat

The replenishment operation treats `confirmedExhausted` as a terminal outcome
on that authority cycle. It clears persisted operation fields, records the
unmet shortage, and immediately hands the army back to the director.

RTS transition facts gain a local force-advantage and target-open fact. The
state machine uses these before supply/organization retreat thresholds:
dominant units remain in assault/hold or regroup-in-place; only a non-dominant
or survival-critical army is assigned a retreat mission. The legacy retreat
path remains disabled in RTS-on mode.

### Pre-War Frontier Deployment

Deployment separates `may receive a frontier order` from `ready to declare`.
Any living required ordinary army is assigned a stable frontier target after
target discovery. Readiness and arrival continue to control the war-notice
gate. Monthly levy/replenishment changes refresh the existing assignment,
rather than waiting for an army to reach 70% before it is sent.

### Monthly Decision Progress

The existing authority cycle already processes monthly preparation work. A
monthly targeted-decision pass uses a monotonic four-month key to process a
kingdom once per game month. It spends at most `MAX_YEARLY_SPEND / 4` and
accrues at most one quarter of the current annual political gain. The yearly
policy pass skips targeted-decision spending, preventing double progress.
The display changes from annual gain/years to monthly gain/months.

### Multi-Declaration Ledger

The single `DIPLOMATIC_WAR_PENDING` projection is replaced as the source of
truth by a persisted declaration ledger. A record contains an immutable
signature, attacker/defender IDs, goal inputs, notice dates, lifecycle state,
and cancellation reason. The former fields remain a compatibility projection
for the highest-priority live record only.

Issuing validates only the requested attacker-defender pair and appends a
record. Each monthly/yearly authority pass advances every due record, and the
notice/deployment services index each signature separately. Ending, cancelling,
or starting one declaration removes only its own record and assignments.

## Error Handling And Lifecycle

All new runtime indexes rebuild from persisted declaration records on load and
clear on world reset. Invalid targets, ended wars, dead kingdoms, and stale
armies cancel only their own record/assignment. Unknown tasks are not granted
military permission. Guard reconciliation and replenishment completion are
idempotent, so a retry or repeated world callback cannot create duplicate
armies or restore a dismissed guard trait.

## Validation

Focused rule tests must prove that:

1. wartime soldiers reject leisure tasks but retain survival and military
   tasks, including a leisure task that began before war started;
2. annexing a vassal removes every absorbed guard identity and army reference
   while preserving each living former guard as a suzerain warrior;
3. zero confirmed source reserves complete replenishment immediately, whereas
   a nonempty source uses the normal operation window;
4. a numerically dominant force with low organization regroups/continues and
   a disadvantaged force retreats;
5. an understrength army receives a frontier assignment during preparation,
   while the declaration gate remains closed until readiness/arrival;
6. four monthly decision ticks equal one prior annual decision tick;
7. two declarations from one attacker persist independently, execute or
   cancel independently, and survive runtime rebuild.

The focused tests, existing complete suite, and `dotnet build
AncientWarfare3.csproj --no-restore` must pass. Live validation covers a
vassal annexation, an exhausted reserve source, a four-month decision, two
simultaneous notices, and a war with RTS enabled. Player.log must contain no
Harmony, persistence, duplicate-army, or stale-notice exception.

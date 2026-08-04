# RTS War Lifecycle and Movement Recovery Design

## Problem

RTS armies can receive a valid mission but remain stationary. Runtime evidence
shows the contradictory state `Installed`, zero local path nodes, no movement,
and no local target. The watchdog reasserts the task or rebuilds the strategic
route without reliably clearing all actor-side movement state.

Armies can also remain idle after objective turnover because there is no
system-wide invariant requiring every eligible wartime army to have an active
mission or an explicit, time-bounded reason for waiting.

Finally, AW3 currently retains RTS actor control inside the combat area. This
duplicates vanilla combat decisions. Casualty recovery repeatedly scans real
residents and emits thousands of diagnostic lines, even though wartime
replacement soldiers are intended to be generated units.

## Ownership Model

AW3 owns strategic movement and recovery. Vanilla owns tactical combat.

Each wartime army moves through these phases:

1. `PreparationRecruitment`
2. `StrategicMovement`
3. `VanillaCombat`
4. `Withdrawal`
5. `Replenishing`
6. `AwaitingObjective`

The controller retains the war mission, target, baseline strength, and prior
offensive mission while vanilla owns combat. Actor jobs are released only for
tactical combat; strategic state is not invalidated.

## Preparation Recruitment

During the preparation period, use the vanilla enlistment path and real city
residents to fill each army to its normal cap. AW3 may filter candidates but
must not generate preparation soldiers.

The filter excludes every recognized heir at every succession level, kings,
city leaders, officials, army captains from ordinary replacement slots, and
any identity already marked as protected by the existing identity system.
If eligible residents are insufficient, the army remains under strength.

## War Baseline and Thresholds

At formal war start, record each existing army's living strength as its fixed
baseline for that war. An army created after war start records its baseline
when it first receives a mission for that war. Captain replacement and later
casualties do not change the baseline.

Use integer comparisons without division:

- withdraw when `living * 100 <= baseline * 20`;
- resume operations when `living * 100 >= baseline * 80`.

The gap supplies hysteresis and prevents repeated withdrawal oscillation.

## Vanilla Combat Handoff

RTS moves an army toward its target city. Once the army is inside the target
city territory and a hostile combat unit is nearby, AW3 clears its tactical
actor jobs and lets vanilla AI choose targets, pursue, and fight. AW3 continues
to observe army strength, objective state, and war validity.

Passing through enemy territory without nearby hostile combatants does not
trigger handoff. RTS continues movement to the strategic target.

If the objective is completed or defenders are driven away, AW3 reacquires
strategic control and requests the next target. If the army reaches the 20%
withdrawal threshold, recovery takes priority over continued combat.

## Withdrawal and Replenishment

On withdrawal, clear vanilla attack targets and reacquire RTS movement
ownership. If the current city is controlled by the army's side and has no
nearby hostile combatants, replenish there. Otherwise choose the nearest safe
home city and retreat to it.

Wartime recovery soldiers are generated eligible soldiers and do not consume
real population. No soldiers are generated during active combat, transport,
or movement between cities. Replenishment stops at 80% of the fixed baseline.

After replenishment, restore the previous offensive mission if its objective
is still open. Otherwise request a new objective from the war director.

## Mission and Idle Invariants

Every live, eligible wartime army must be in exactly one of these conditions:

- active mission;
- preparation or replenishment;
- explicit reserve/cooldown with a reason and deadline;
- queued for director assignment.

Objective completion, invalid targets, captain replacement, and expired waits
must enqueue reconciliation. Reconciliation clears stale objective claims,
repairs a missing captain task when a controller mission exists, and queues an
unassigned eligible army for the director. Waiting without a reason or beyond
its deadline is invalid.

## Shared-Route Truth and Recovery

Treat an installed shared-route revision as reusable only while the actor is
following a non-empty local path, or has reached the recorded endpoint. A
matching historical revision without either fact is stale.

On movement recovery, clear actor shared-route metadata, cancel AW path
ownership, clear the local path and tile targets, and restart the expected RTS
task. Preserve the strategic mission. Existing escalation remains: reinstall,
rebuild route, alternate endpoint, then target handoff or withdrawal.

Recovery must be idempotent and must respect active combat and transport
ownership gates.

## Diagnostics

Log lifecycle transitions, recovery actions, failure reasons, and bounded
periodic summaries. Suppress identical per-work-item levy output. Emit the
first observation, material changes, and a periodic sample. Reset diagnostic
sampling when diagnostics are disabled or runtime state is cleared.

## Failure Handling

- A replacement captain inherits the war baseline, phase, and mission.
- A destroyed army loses all lifecycle state.
- War end cancels movement, withdrawal, and replenishment and releases actors
  to vanilla.
- An invalid retreat city triggers safe-city reselection.
- Failure to find preparation recruits never bypasses protected identities.
- Failure to find a legal objective produces an explicit bounded wait reason.

## Tests

1. Preparation recruitment excludes every heir, king, city leader, official,
   captain, and protected identity while retaining eligible residents.
2. War baselines are fixed at war start or first wartime assignment.
3. The 20% and 80% thresholds transition without rounding errors.
4. Target-territory enemy contact releases tactical control to vanilla.
5. Casualty withdrawal reacquires RTS control and selects occupied or safe
   home-city replenishment correctly.
6. Wartime replenishment generates soldiers without consuming residents and
   stops at 80%.
7. Objective completion restores or replaces the offensive mission.
8. Missionless or expired-wait wartime armies return to director assignment.
9. Matching shared-route revisions with empty non-following local paths are
   reinstalled; following routes are reused and endpoints are arrived.
10. Identical diagnostics are throttled while transitions remain visible.
11. Existing rules tests and the full mod build pass.

## Non-Goals

- Replacing vanilla tactical combat.
- Changing the 20% withdrawal or 80% recovery thresholds.
- Consuming real population for wartime recovery.
- Increasing global pathfinding worker counts.
- Disabling RTS diagnostics.

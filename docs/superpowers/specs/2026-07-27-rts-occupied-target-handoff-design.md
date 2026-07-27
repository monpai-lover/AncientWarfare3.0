# RTS Occupied Target Handoff Design

## Goal

Prevent an operational RTS Army from idling in a city that its side already
controls. Completing one offensive target must close that target, release all
stale ownership, and atomically advance the Army toward another open enemy
city. An occupied city may reopen only as a defensive objective after an
enemy military counterattack actually begins.

## Confirmed Runtime Failure

The live save shows a target-publication loop rather than a missing task:

- Army `119` repeatedly changes between city `18` and cities `2` or `6` in
  war `11`.
- Every reassignment to city `18` is followed by `target_completed`.
- Armies `1699`, `1557`, and `1725` show the same class of oscillation.
- The actor window continues to display the military-command task, but each
  target change resets operational route state before stable movement occurs.

The director already filters some completed front candidates, but completion
state is not enforced as one authoritative invariant at every publication
boundary. A stale director snapshot, coalition claim, reservation, or fallback
target can therefore republish a closed objective.

## Authoritative Objective State

Every city objective is projected as exactly one of these states for one war
side:

- `OpenAttack`: a live enemy city that is not controlled by the Army's side.
- `OpenDefense`: a city controlled by the Army's side where an enemy military
  counterattack is active.
- `ClosedOccupied`: a city controlled by the Army's side with no active enemy
  counterattack.
- `Unavailable`: a destroyed city, a city outside the active war, or a city
  whose owner and controller no longer produce a valid hostile objective.

An enemy counterattack is active when at least one hostile military actor has
entered the city zones, or the city's capture progress has begun advancing for
the enemy side. A distant enemy route or an uncommitted planning proposal is
not enough to open a defensive objective.

`ClosedOccupied` is a one-way close for offensive planning. Periodic planning,
stored intent, coalition tasks, target leases, and old snapshots cannot reopen
it as `OpenAttack`. It can reopen only as `OpenDefense` after the counterattack
condition becomes true. When the hostile military presence and hostile capture
progress both clear, it returns to `ClosedOccupied`.

## Proposal And Commit Flow

The war director produces an explicit proposal kind: `Attack`, `Defend`,
`FrontHold`, or `None`. A friendly anchor city is never substituted for a
missing attack target.

Before committing an `Attack` or `Defend` proposal, the authority side
revalidates all mutable facts:

- the Army is alive and its actual kingdom matches the proposing kingdom;
- the war is active and contains the Army's kingdom;
- the target city is alive and belongs to the same war;
- the target's authoritative objective state matches the proposal kind;
- the proposal did not come from an expired generation;
- an associated coalition claim still identifies the same open objective.

A failed validation rejects the proposal, releases the stale coalition claim
and leader reservation, and schedules one indexed replan. It does not clear a
still-valid current mission and does not publish a fallback movement target.

When an occupied target completes, the controller latches completion once,
stops the completed route, releases its mission index entry and coalition
ownership, and wakes the affected war and kingdom indexes. The next validated
target replaces the completed mission and route atomically. Route state is
cleared once, after the replacement is accepted, so intermediate snapshots
cannot leave the Army with a visible command but no stable destination.

## Front Hold

If no `OpenAttack` or `OpenDefense` objective exists, the Army enters the
explicit `FrontHold` state near its current front position. It keeps formation
and receives the localized task name `Front Hold`; it does not own a strategic
path and does not display the generic military-command task.

`FrontHold` is legal only while the indexed objective set is empty. A new enemy
city, an enemy counterattack, a changed war participant, or a peace event wakes
the relevant Army without a world scan. If an open objective exists, an
operational Army cannot remain in `FrontHold` or `Idle`.

## No-Idle Invariant And Recovery

While an active war has an open objective and the Army has minimum operational
force, exactly one bounded owner must explain its lack of movement: route
planning, published movement, immediate military combat, formation reform,
transport, or stall recovery.

- Up to three seconds without coordinate progress is allowed for route
  publication, short reform, and combat handoff.
- At three seconds, the watchdog reasserts the RTS actor task and verifies task
  ownership, route identity, and route cursor.
- At six seconds, it discards the route and submits an alternate valid entrance
  inside the same target city.
- At ten seconds, it rejects the blocked entrance. A land objective selects a
  new entrance; a cross-water objective enters the transport queue immediately.
- If the objective has closed, recovery does not retry it. The Army changes to
  another open objective in the same scheduling opportunity.

Formation and replenishment cannot become indefinite gates. An Army at or
above 80 percent establishment strength can depart. Reform, replenishment, and
transport waits retain their existing bounded recovery paths and must hand
control back to strategic movement after completion.

## Performance And Authority

Objective transitions are event-driven and indexed by war, target city,
kingdom, and Army. No actor tick, movement tick, renderer, or target validation
may scan all world cities, Armies, or actors.

Enemy counterattack detection reuses city-zone military-presence and capture
events. Commit validation is constant-time apart from existing indexed lookups.
Diagnostic output is rate-limited and records proposal kingdom, actual Army
kingdom, target owner, occupation controller, proposal source, objective state,
route identity, and rejection reason.

Only the authoritative host plans, validates, releases claims, and changes
missions. Multiplayer replicas display the synchronized state and task name but
do not reopen objectives or run recovery decisions.

## Verification

Focused automated coverage must prove:

- a completed occupied city cannot be selected by periodic planning;
- stale director snapshots and coalition claims cannot republish a closed city;
- a proposal whose kingdom differs from the Army's actual kingdom is rejected;
- a completed target hands off to another open city without an intermediate
  fallback attack mission;
- hostile military presence or hostile capture progress reopens an occupied
  city as defense, while a distant plan does not;
- clearing the counterattack closes the defensive objective again;
- an open objective prevents `Idle` and `FrontHold`;
- an empty objective set produces `FrontHold` without a strategic route;
- simulated no-progress intervals invoke the three-, six-, and ten-second
  recovery actions in order;
- transport, combat, reform, and replenishment completion resume strategic
  movement.

Runtime acceptance uses the same save and observes Armies `119`, `1699`,
`1557`, and `1725`. City `18` must not alternate with cities `2` or `6`, and it
must not reappear unless an enemy military counterattack begins there. For an
Army with an open target, the log must show one stable target and route while
the captain and the formation produce real coordinate progress. Completing
that city must lead to movement toward the next open city; if none exists, the
Army must visibly enter `FrontHold` instead of retaining an empty military
command.

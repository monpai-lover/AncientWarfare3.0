# RTS Frontier And Command Integrity Design

## Goal

Make pre-war armies visibly assemble on the real shared frontier and make live
RTS armies continuously advance and attack while a valid war objective exists.
Stabilize the Army captain identity and keep kings and city leaders completely
outside national-war combat and occupation.

## Confirmed Failures

- Pre-war deployment selects a city that borders the opponent but sends the
  army to the city center instead of the shared kingdom boundary.
- The attacker is not assigned a pre-war frontier deployment; only the notified
  defender enters the current deployment index.
- A live RTS mission can retain a route and visualization while the captain and
  formation remain physically idle.
- Combat-task preservation can prevent RTS from reclaiming an actor after the
  actor no longer has a valid immediate enemy.
- Captain identity can oscillate, making the flag appear and disappear at high
  frequency and breaking formation ownership.
- Current authority-role protection prevents Army membership but does not stop
  two non-warrior kings or leaders from selecting and damaging one another.

## Pre-War Frontier Deployment

Both the attacker and defender receive one deployment projection for the active
notice. Each side discovers its own land zones touching the opposing kingdom.
The deployment target is a stable, walkable tile on that side of the exact
shared border, not a city center and not a tile inside the opponent.

Facing border tiles are cached per notice and side. Discovery remains bounded
and deferred. Armies are distributed deterministically across the cached
frontier segments. Royal guards, kings, city leaders, invalid armies, and empty
army shells are excluded. If no shared land border exists, each side stages at
a stable friendly coast tile facing the opposing target city. Pre-war actors
remain on their own land; after war creation, the existing Army transport queue
takes ownership and carries them across. The fallback never chooses an
unrelated capital tile merely because no land frontier exists.

Only defender readiness continues to delay declaration completion. Attacker
deployment is an active preparation order but cannot indefinitely prevent the
war from starting.

## RTS Command Reclamation

An RTS Army with a live war, live target city, valid captain, and operational
force must always have exactly one actionable command owner. Vanilla strategic
Army decisions remain disabled while RTS is enabled.

Immediate actor combat may temporarily preempt movement only while the actor
has a live, hostile, attackable target in local range. A generic task marked
`in_combat`, a stale attack target, or a completed combat animation is not
sufficient to block RTS task reclamation. When local contact ends, the captain
returns to the RTS mission task and continues toward the current enemy city.

The controller distinguishes these conditions:

- route planning: the provider owns planning and the captain may wait;
- route published: the captain must have the RTS movement task and make physical
  progress;
- local combat: a valid nearby military target temporarily owns combat;
- contact cleared: RTS immediately reasserts movement or assault;
- target completed: the mission index assigns another unconquered enemy city.

`Assault` is not a permanent absorbing state. If contact disappears before the
target completes, the Army resumes approach into the enemy city. The captain
must cross the enemy boundary when the route and war objective require it.

## Stall Recovery

The watchdog samples physical captain movement, route identity, published
destination, task ownership, and live-contact state. A route with no captain
movement and no valid local combat is a command stall even if route planning
or visualization counters continue changing.

After three seconds without progress, recovery reasserts the existing mission
task before rebuilding the route. A second failure selects an alternate
reachable endpoint inside the same target city. Retreat is reserved for an
actually unreachable route, critical supply, or destroyed operational force;
it is not the default response to a lost actor task.

The acceptance invariant is strict: while a valid hostile target and sufficient
force exist, an Army cannot remain idle until retirement. It must advance,
fight military defenders, capture the city, and request the next target.

## Stable Captain Ownership

Every live RTS mission records one stable captain identity. A living Warrior
who still belongs to the Army and kingdom remains captain even while rallying,
moving, fighting, replenishing, or receiving new members. Routine maintenance,
formation refresh, recruitment, save repair, and vanilla captain checks cannot
replace that actor.

Replacement is allowed only when the captain is dead, rekt, no longer a current
Warrior, belongs to another Army or kingdom, or has an invalid runtime object.
One bounded replacement operation selects a new valid member, updates Army
data and RTS ownership atomically, and preserves the mission and route. This
prevents alternating captain writes and flag flicker.

## King And Leader War Exclusion

Kings and city leaders are authority actors, not military actors. During a
national war they:

- cannot select or retain a hostile attack target;
- cannot be selected as a hostile target by actors or combat boats;
- cannot deal or receive actor-supplied weapon damage;
- cannot join an Army or become its captain;
- cannot contribute occupation points or trigger city capture.

The protection applies at both `canAttackTarget` and final actor-damage entry
points so stale queued attacks cannot bypass it. It does not make authority
actors immune to age, disease, disasters, divine powers, or other non-war
damage. Existing capture, succession, and posthumous systems remain unchanged.

## Performance And Lifecycle

All hot-path checks use current Actor, Army, kingdom, mission, and cached border
indexes. There are no world-wide actor or Army scans during combat, movement,
rendering, or target checks. Border discovery, captain replacement, and cleanup
are bounded work items. New-map, load, war start/end, notice cancellation,
Army disposal, and RTS setting changes clear or rebuild the added runtime state.

Multiplayer replicas render synchronized captain, mission, and movement state
but never plan routes, replace captains, assign combat tasks, or mutate border
deployment.

## Verification

Automated coverage must prove:

- both sides select their own tile on a real shared land border;
- city-center fallback is not used when a valid shared border exists;
- attacker preparation cannot block the declaration deadline;
- stale `in_combat` tasks cannot suppress RTS reclamation;
- a published route with no contact triggers task reassertion before replan;
- `Assault` without contact resumes movement into the target city;
- a valid captain cannot be replaced, while an invalid captain is replaced once;
- kings and city leaders cannot attack, be attacked, or occupy in national war;
- non-war damage to kings and leaders remains valid;
- target completion causes the Army to attack another unconquered city;
- focused rule/runtime tests, source guards, and Debug/Release builds pass.

Runtime acceptance uses a fresh war with visible RTS routes. It observes both
pre-war armies at the exact shared border, follows one captain across the enemy
border, confirms the flag remains stable, confirms no unexplained three-second
idle interval, verifies only military actors fight, captures one city, and
observes the same Army or another assigned Army advance to the next target.

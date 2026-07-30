# Wartime Army Command Lifecycle Design

## Goal

Ensure every valid field army belonging to either side of an active war receives an RTS military order, including armies created or expanded after the war starts. Army map information must expose armies that are still assembling instead of hiding them because a mission has not yet been published.

## Confirmed Root Cause

`ArmyManager.newArmy` registers and schedules a new army before its roster is complete. At that point the army commonly contains only its captain, so the war director excludes it for being below the minimum operational force. Later `Actor.setArmy` calls refresh the strategic index, but roster changes do not schedule a new director generation. The army can therefore retain a native flag while never receiving an RTS mission.

War start registration already enumerates both attackers and defenders. The missing-order symptom affects either side when its armies are created or become operational after that initial planning pass.

The director does not intentionally default replacement armies to defense.
`ArmyManager.newArmy` publishes a captain-only army below the operational
threshold, while later `Actor.setArmy` roster notifications refresh the
strategic index without always queuing a new director generation. After the
previous attacking army is destroyed, the replacement can therefore retain no
current mission or a stale presentation until an unrelated replan occurs. The
allocation rules otherwise reserve defense only for the first army of a war
whose capital is actively threatened; an operational replacement with an open
enemy objective must remain eligible for assault.

## Command Lifecycle

1. War start and participant changes register every attacker and defender and schedule each participant kingdom.
2. Army creation, roster changes, and kingdom ownership changes refresh the strategic index and enqueue a coalesced director refresh for the affected kingdom.
3. The refresh is deferred until the current army mutation stack has completed, so the director captures the assembled roster rather than the captain-only intermediate state.
4. Multiple soldiers joining the same kingdom in one simulation slice produce one director refresh rather than one planning pass per soldier.
5. Destroying an attacking army releases its objective and coalition
   reservations and schedules its kingdom. A replacement army that later
   crosses the minimum operational threshold participates in that fresh plan
   and may inherit any still-open enemy objective.
6. The director continues to exclude invalid, destroyed, captainless, royal-guard-only, and dedicated-garrison armies from field missions. A depleted army remains visible as assembling or replenishing until it becomes operational.
7. A wartime field army that is below its required strength requests forced reinforcement instead of remaining indefinitely in the replenishment state.
8. Every operational field army in an active war is allocated to one of that kingdom's active wars. If no attack objective is currently open, it receives a reserve or defensive order anchored to a valid friendly city instead of being omitted.
9. A newly operational replacement army is not assigned `Defense` merely
   because the former assault army was destroyed. Only a current capital or
   homeland threat can consume a defense assignment, and capital defense uses
   only the required first slot; other available armies remain eligible for
   open attack objectives.
10. The army-information role `Defend` is a current RTS assignment, not a
   permanent army class. When its defended objective is stable and an
   offensive objective needs strength, the director may replace that mission
   with `Attack`. Active capital threats, nearby enemy pressure, and unmet
   defensive strength keep priority. Dedicated city garrisons, royal guards,
   and other special armies remain excluded from this conversion.
11. A periodic reconciliation remains as recovery for missed lifecycle notifications: a missionless operational wartime army causes a bounded kingdom replan.

## City Reserve Pool

Pre-war manpower is represented by persistent real-actor membership, not by a
numeric counter and not by a wartime population scan. A dedicated city reserve
pool service owns this state; `TemporaryLevyService` continues to own actors
only after they enter military service.

1. Each live city maintains an ordered set of eligible actor IDs. Reserve
   members remain civilians during peace and continue normal work, movement,
   relationships, and reproduction.
2. The target size is the existing city military capacity:
   `min(effective warrior_slots, floor(city population * 35%))`.
   This is a hard ceiling. Once a pool reaches it, that city's enrollment work
   stops immediately: adulthood events are ignored, no waiting list is kept,
   and maintenance does not scan additional candidates until a peace-time
   vacancy exists.
3. The primary enrollment path is event-driven. A patch on the original actor
   adulthood transition attempts to register a newly adult eligible resident
   in the current city's pool. If the pool is already at capacity, the actor
   remains an ordinary civilian and may be picked up by later maintenance.
4. Death, migration, kingdom change, enlistment, and eligibility changes remove
   membership through their existing lifecycle hooks. Removal is immediate and
   does not wait for a maintenance scan.
5. Peace-time maintenance is distributed across authority cycles as a repair
   path. A bounded round-robin cursor visits only a small number of cities and
   actor candidates per cycle. It validates persisted membership, fills missed
   event gaps, and trims a pool whose capacity has fallen. It never runs from a
   render-frame postfix.
6. During an active war notice or other established preparation state, the
   same bounded maintenance receives a larger work budget and prioritizes
   cities below target. This prepares manpower but does not enlist or teleport
   actors.
7. At formal war start, every participating kingdom freezes its current city
   pools. A surprise war freezes the partial pools that exist at that instant.
8. While a kingdom participates in one or more wars, all of its city pools are
   read-only except for removals. Death, migration, city or kingdom transfer,
   loss of eligibility, and enlistment remove an actor. No replacement may be
   added until all of that kingdom's wars have ended. Actors who become adults
   during the freeze are not enrolled retroactively until peace-time
   maintenance runs after unfreezing.
9. Concurrent wars share the same frozen pools. Consuming an actor for one
   army or war makes that actor unavailable to every other army and war.
10. After the kingdom leaves its last war, the pools unfreeze and return to
   gradual peace-time maintenance. Ending one of several concurrent wars does
   not unfreeze them.

## Persistence And Restore

Reserve membership survives save/load without regenerating manpower.

- Each member persists its reserve flag, source city ID, source kingdom ID,
  and reserve generation token in actor data.
- Each kingdom persists the current reserve generation and frozen state. The
  active-war index may confirm a frozen state after restore, but it must never
  clear a persisted freeze before war restoration completes.
- Each war snapshot persists a reserve-exhaustion contribution for both sides.
  It is keyed by war and participant side, capped at 20, and survives load so
  the same depletion cannot award exhaustion twice.
- Runtime city sets are rebuilt only from persisted actor membership. Restore
  may discard invalid records, but it must not search for or add replacement
  actors.
- Maintenance cursors are operational state and may restart safely after load;
  they do not affect membership while the kingdom remains frozen.
- A permanently transferred city cannot supply its former owner. Its old
  membership is removed during validation; the new owner may build a new pool
  only when that owner is not frozen.

## Forced Reinforcement

Forced reinforcement consumes the frozen city reserve and operates on real
actors rather than changing an army count:

1. Determine the army's approved target through
   `CityArmyReinforcementService`, preserving the shared city-capacity
   allocation and army priority rules.
2. Consume eligible actor IDs from the anchor city's pool first and then from
   deterministic nearby friendly-city pools. Never scan the live city
   population for replacements during war.
3. Revalidate every selected actor immediately. Invalid, dead, migrated,
   already recruited, protected, or enemy-controlled-city members are removed
   without replacement.
4. Preserve the existing donor-city population floor. Enemy-occupied cities
   cannot provide recruits, resources, or soldiers even if their frozen pool
   still contains records.
5. Convert selected actors to the appropriate wartime military role, attach
   them to the target `Army`, and ensure both `Actor.army` and `Army.units`
   agree.
6. Teleport the completed reinforcement batch to the army captain or valid
   rally tile, including before the army receives its first RTS mission.
7. Complete the batch in one deferred operation. Once the army reaches
   operational strength, clear the replenishment gate and enqueue a coalesced
   war-director generation immediately.
8. Apply the same process to attackers and defenders. If the frozen pools are
   exhausted, keep the army in an explicit manpower-shortage state. It cannot
   replenish again during that war merely because the city later gains
   population.
9. When an army assigned to `Attack` still has an approved reinforcement
   shortage and every usable frozen city reserve pool in its kingdom is empty,
   add 20 war exhaustion to that kingdom's side in the army's assigned war.
   This contribution is applied once per kingdom and war, persists in the war
   score snapshot, and is included in the existing clamped `0..100` exhaustion
   total. It is not represented as fabricated casualties. A defensive or
   reserve assignment alone cannot trigger it.

## Presentation

Army flag information must not require both an RTS projection and mission merely to be visible.

- With a valid mission, show the existing army name, strength, commander, and localized operation.
- While the director refresh is pending or the army is below operational strength, show the same basic identity data with a localized assembling/replenishing status.
- Invalid or destroyed armies remain hidden.

The fallback is diagnostic protection only. It does not replace command assignment.

## Performance Boundaries

- Peace-time reserve work uses bounded city and actor cursors and a single
  coalesced authority-cycle work item. Preparation increases those explicit
  budgets but remains bounded.
- War-time reinforcement reads indexed reserve IDs and never performs a full
  city-population or world-actor scan.
- Coalesce roster notifications by kingdom.
- Coalesce forced reinforcement by army so repeated director observations cannot create duplicate soldiers or duplicate teleports.
- Do not scan every actor or every army per render frame.
- Reuse `ArmyStrategicIndexService` cursors and the existing bounded war-director work queue.
- Run reconciliation on simulation/director cycles, never `MapBox.Update` presentation frames.

## Failure Handling

- If persisted membership is missing or corrupt, discard only the invalid
  records. Do not refill a frozen pool as a repair action.
- If a pool member becomes invalid between selection and enlistment, consume
  the record and continue within the current bounded batch. Do not substitute
  an unregistered resident.
- If war state restoration is incomplete, retain the persisted frozen state
  and defer maintenance rather than risk regenerating manpower.
- An exhaustion check must distinguish a truly exhausted kingdom-wide frozen
  reserve from a temporarily empty anchor city or a bounded scan that has not
  completed. Uncertainty defers the check instead of applying the penalty.
- If an army is mutated again before planning completes, invalidate the stale generation and retain only the latest coalesced refresh.
- If the army changes kingdom, refresh both the previous and current kingdom where available so stale missions are removed and the new owner receives a plan.
- If no valid friendly anchor exists, keep the army visible as awaiting orders and retry on the next bounded director cycle.

## Tests

Automated regression coverage will prove:

1. War participant enumeration includes both attackers and defenders.
2. Registering a new wartime army schedules a deferred kingdom refresh.
3. Expanding a captain-only army to operational strength schedules another refresh.
4. Repeated roster changes coalesce to one kingdom refresh.
5. Destroying an attacking army releases its old front reservation and queues
   the participant kingdom.
6. With an open enemy objective and no homeland threat, a newly operational
   replacement receives an attack assignment rather than defense.
7. With a real capital threat, only the required first slot is assigned to
   defense; additional replacement armies remain eligible for attack.
8. The adulthood transition enrolls an eligible resident only when its kingdom
   is unfrozen and its city pool has capacity.
9. A full pool skips new adults without creating a waiting queue or scanning
   further candidates during that maintenance visit.
10. Death, migration, kingdom change, enlistment, and eligibility loss remove
   the actor immediately.
11. An actor becoming adult during war is not enrolled.
12. Peace-time maintenance adds missed eligible actor IDs only up to the
   existing city military capacity and distributes repair work through bounded
   cursors.
13. Preparation maintenance accelerates bounded completion without converting
   reserve members into warriors.
14. War start freezes both attackers' and defenders' partial pools.
15. No actor can be added while a kingdom remains in any active war.
16. Concurrent wars cannot consume the same reserve actor twice and ending one
   war does not unfreeze a kingdom still participating in another.
17. Save/load restores exactly the persisted pool and never fills missing
    places in a frozen pool.
18. Death, migration, transfer, ineligibility, and prior enlistment remove a
    reserve record without wartime replacement.
19. Pool exhaustion prevents further reinforcement during the war.
20. Forced reinforcement selects real indexed actors without crossing the
    donor-city population floor.
21. Reinforced actors are assigned to the intended army and teleported to its
    captain or rally tile before or after its first mission.
22. Repeated reinforcement requests cannot attach or teleport the same actor
    twice.
23. Reinforcing a newly operational army queues a coalesced director refresh
    and leads to a real attack, defense, or reserve mission.
24. An attacking army with an unmet approved shortage and a confirmed empty
    kingdom-wide frozen reserve adds exactly 20 exhaustion to its side of that
    war.
25. The reserve-exhaustion contribution is applied at most once per kingdom and
    war, survives save/load, and does not trigger for defense-only armies or a
    merely empty anchor-city pool.
26. A normal field army currently shown as `Defend` can be reassigned to
    `Attack` after its defensive demand is satisfied and an offensive demand
    exists.
27. A threatened capital or under-strength defense prevents that reassignment,
    and dedicated garrisons or special armies never enter the conversion path.
28. An operational army with no open attack target receives a reserve or
    defense mission when a friendly anchor exists.
29. A missionless but valid army still renders basic map information with an
    awaiting-order status.
30. Existing RTS lifecycle and performance rule suites remain green.

## Out Of Scope

This change does not redesign RTS routing, combat tactics, city military
capacity, or the global RTS scheduler switch. It does not reserve actors by
removing them from normal civilian life, and it does not create a separate
numeric manpower currency. Actor sprite exceptions and Actor benchmark
sampling are separate performance fixes.

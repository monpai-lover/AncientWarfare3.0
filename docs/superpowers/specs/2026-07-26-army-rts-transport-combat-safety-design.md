# Army RTS Transport and Combat Safety Design

## Scope

This change fixes four connected RTS behaviors:

- An offensive Army can use the original WorldBox transport-boat system to reach a target city on another island.
- An Army stops treating a city as an assault target after its side controls the city and no hostile warriors remain there.
- Army combat between civilized kingdoms can target only living actors whose current profession is `UnitProfession.Warrior`.
- Actors with `UnitProfession.King` or `UnitProfession.Leader` cannot attack an Army, be attacked or damaged by an Army, or contribute city capture points.

The separate multi-war-goal and automatic-peace feature is outside this fix and receives its own design and implementation cycle.

## Military Identity

`UnitProfession.Warrior` is the sole authoritative military profession for actor combat and actor-supplied occupation. `Actor.hasArmy()`, `Actor.army`, group membership, flags, and minimap indexes are supporting runtime references only. They cannot make a non-warrior attackable or eligible to capture a city because those references may remain stale after retirement or reassignment.

The protection applies to wartime combat between civilized kingdoms when at least one side is a warrior/Army participant. It does not suppress environmental damage, hunger, disasters, animals, monsters, player powers, or non-war combat systems. Military buildings such as towers and barracks remain valid military targets; civilian production buildings remain protected by the existing occupation safety behavior.

## Cross-Island Transport

The existing `ArmyRtsTransportService` remains the single Army-level transport owner. When a captain in `March` or `Retreat` has a target on another island, the service creates or joins one original `TaxiRequest` and proactively registers every valid living Army member on the source island. Followers no longer depend on each actor receiving an RTS behavior tick before the request becomes a full Army request.

Transport target identity uses the exact target tile ID. Replacing a mission with another city on the same destination island therefore cancels the old Army membership and creates or joins the correct request instead of silently retaining the old city.

Timeouts use `Time.realtimeSinceStartupAsDouble`, not world time. A pending request has 120 real seconds to receive a boat. A request with an assigned boat has 240 real seconds without observed boat movement before route recovery is invoked. Pause and game speed do not consume these deadlines incorrectly. Movement of the assigned boat refreshes the progress deadline.

The original `TaxiManager` and boat behavior remain responsible for boat selection, loading, sailing, and disembarkation. After the captain lands on the destination island, RTS releases transport ownership for landed members and resumes the existing land route to the target city. Replica clients never create transport requests.

## Captured Target Completion

Frozen wartime control remains the authoritative city-control signal. When control changes, `OnTargetCompleted` latches completion for Armies indexed to that exact city and clears their old land route, transport request membership, formation anchor, pursuit route, tile target, and actor combat target.

Before entering `Pursue`, the controller checks the target city's zones for a living actor who:

- belongs to a kingdom currently hostile to the Army kingdom; and
- has current profession `UnitProfession.Warrior`.

If at least one such warrior remains, bounded pursuit is allowed. If none remains, pursuit is skipped, the mission transitions directly to `Idle`, the mission is invalidated, and `KingdomWarDirectorService.OnArmyChanged` schedules selection of another live enemy city that is not already controlled by the Army's side. Non-warriors never keep an assault mission alive.

The scan runs only for the currently processed Army after its target is complete. It examines the target city's existing unit collection or zone units with early exit and does not scan all world actors.

## Combat and Occupation Guards

Pure rules determine whether an actor is a wartime military combatant and whether hostility or damage must be suppressed. Runtime hooks apply the same predicate at two boundaries:

- `BaseSimObject.canAttackTarget` prevents selection of invalid actor targets.
- `Actor.getHit` prevents already-retained or late attack references from damaging protected non-warriors.

The damage guard is necessary because changing target selection alone does not clear attacks already in progress. On target completion, RTS also clears invalid `beh_actor_target` references owned by the completed mission.

`City.addCapturePoints(BaseSimObject, int)` rejects actor-supplied capture points unless the actor is alive, not rekt, belongs to a valid kingdom, and is currently a `Warrior`. The kingdom overload remains unchanged so watchtowers and other non-actor city systems continue to work. King and Leader actors therefore cannot occupy cities even if they carry stale Army membership.

## Failure Handling

- Missing Army, captain, kingdom, target tile, or world state rejects transport without throwing.
- A failed or timed-out transport releases only members of the affected Army from a shared request and notifies `ArmyStallWatchdogService` once.
- Mission replacement releases stale transport and route state before the new mission is assigned.
- If a hostile warrior leaves, dies, retires, or changes profession after control freezes, the next bounded controller pass skips or ends pursuit and selects a new city.
- Replica, loading, paused scheduling, and world-reset gates retain their existing authority behavior.

## Verification

Implementation follows red-green TDD with focused slice tests:

- `ArmyRtsTransportSlice` proves that the captain proactively registers all valid Army members, exact target changes rebuild ownership, real-time deadlines are independent of world time, movement refreshes the deadline, landing releases ownership, and replicas create no request.
- `ArmyRtsRulesSlice` proves that a completed assault with no hostile warrior transitions to `Idle`, while a completed assault with a hostile warrior may enter bounded pursuit.
- `OccupiedCityCivilianProtectionSlice` proves that only Warrior-to-Warrior wartime actor combat is allowed for Army combat; Unit, Nothing, King, and Leader are protected in both directions; stale Army membership grants no exception; and only a valid Warrior contributes actor capture points.
- Release build must complete with zero errors. Deployment copies only the mod folder contents and does not launch WorldBox.


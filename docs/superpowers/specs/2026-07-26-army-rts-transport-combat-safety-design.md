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

### Naval Transport Priority

AW3 may bind any usable friendly combat-capable boat to an RTS `TaxiRequest`; it does not mutate the shared `ActorAsset.is_boat_transport` flag. Dedicated transport boats rank ahead of combat-capable boats. Fishing and other `skip_fight_logic` boats are not military transport fallbacks. A boat is unavailable when it is dead, rekt, already carrying passengers, already assigned to another taxi request, owned by another kingdom, or unable to reach both coasts through its current ocean.

Binding a combat-capable boat uses the original `boat_transport_go_load` and `boat_transport_go_unload` tasks with a forced action. This deliberately preempts patrol, combat, or trade work because an Army transport request has higher wartime priority than naval combat. The original unload behavior finishes the taxi request, clears passengers, and ends the temporary job; the boat then returns to its normal actor decisions. AW3 keeps no permanent ship-type mutation and releases its reservation on cancellation, timeout, death, world reset, or mission replacement.

When no usable dedicated or combat-capable boat can serve a pending Army request, AW3 registers a short-lived transport-production demand on a friendly finished dock connected to both request coasts. The next original city `produce_boat` action still calls `Docks.buildBoatFromHere`, pays the original resource cost, respects dock capacity, creates the civilization's own `actor_asset_id_transport`, and performs the original kingdom/city initialization. AW3 changes only the random boat choice while that exact dock demand is active. Requests are coalesced and cooldown-limited; successful assignment, timeout, mission cancellation, or world reset clears stale demand.

The original `TaxiManager.list` remains the shared waiting queue. RTS Army voyages and historical-school master/study journeys both register original `TaxiRequest` instances. A transport that unloads one request ends its temporary job and returns to the original boat decision loop, where it may accept another waiting request. Historical-school requests recheck capacity while waiting so the loss of an earlier boat cannot strand them permanently.

A kingdom may own at most three living dedicated transport boats through this emergency production path. If the number of pending unassigned requests is no greater than the existing dedicated fleet, passengers wait for those boats to rotate instead of causing more construction. If pending demand exceeds the fleet and the fleet is below three, one route-compatible dock receives one coalesced demand. Combat-capable ships remain an RTS wartime fallback and do not count against this dedicated civilian transport cap.

## Decisive Long March

The strategic route provider validates one complete land route from the captain's current tile to the final city tile. Streamed path steps are planning data only and never become one-tile movement orders. The controller consumes the route stream without advancing the formation anchor. Only after the provider reports completion does the controller publish the final destination as the captain's single movement target.

The RTS captain task uses unlimited pathfinding regions (`0`) for that final target. It must not impose the previous two-region limit. While the captain travels, repeated director refreshes for the same physical destination reuse the existing route and do not clear movement. The route is rebuilt only after target replacement, traversal-generation invalidation, explicit cancellation, transport transition, or watchdog-confirmed lack of physical progress.

Followers continue to receive bounded local formation corrections around the moving captain. They do not independently pathfind to the enemy city. The watchdog samples the captain's physical position against a stable route identity; consuming planning steps does not count as movement and actual movement resets recovery pressure.

## Wartime Scheduling Priority

Active-war RTS work is latency-sensitive rather than background maintenance. Controller processing, route-stream draining, transport lookup, target-completion wakeups, and stall sampling receive higher bounded per-frame budgets than peacetime maintenance. The bounds still prevent a single Army from monopolizing a frame, but they must be large enough that a ready route, available boat, completed occupation, or three-second stall is acted on promptly instead of waiting through long round-robin queues.

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

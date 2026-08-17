# Bandit Stronghold City Mutation Boundary Design

## Goal

Prevent bandit suppression from modifying `CityManager`'s live city set while
`CityManager.update` is enumerating it. Stronghold capture, chronicles, unit
transfer, zone restoration, wall restoration, and final city disposal must
still complete deterministically without pausing the simulation scheduler.

## Root Cause

`City.finishCapture` calls
`PeasantRebelBanditStrongholdService.TryHandleCapture`. A primary stronghold
capture queues `CompleteFall`, whose final operation calls
`World.world.cities.removeObject(stronghold)`. The user log shows
`HashSet<City>.Enumerator.MoveNext` failing inside `CityManager.update`, proving
that a stronghold city-set mutation can overlap the live city enumeration.

The existing deferred-work queue is not a sufficient safety contract. It
controls when work is requested, but the destructive operation itself does not
verify that the city manager is outside an enumeration-sensitive stage.

## Scope

This change covers removal of a bandit stronghold city during suppression,
runtime restoration, founding transition, orphan cleanup, and transaction
rollback. It does not change suppression eligibility, the 100-percent capture
requirement, chronicles, territory restoration, bandit pressure, or ownership
rules.

## Mutation Boundary

A small main-thread city-mutation scope records whether
`CityManager.update(float)` is active. A Harmony prefix enters the scope and a
finalizer always leaves it, including when a city update throws.

Stronghold cleanup is split into two idempotent phases:

1. `CompleteFall` completes the logical fall: resolve actors and cities,
   record chronicles, return actors and zones to the mother city, remove
   towers, restore walls, clear raid state, and persist `Completed`.
2. A coalesced stronghold-city disposal item re-resolves the city by ID and
   removes it only when the city-update scope is inactive. If the scope is
   active, the item requeues itself without repeating logical fall work.

Every direct stronghold-city removal path uses the same disposal boundary.
Rollback may restore transaction state immediately, but disposal of a newly
created stronghold city remains deferred until the scope is safe.

## Failure Handling

- Disposal is idempotent: an absent or already-destroyed city is success.
- A stale city ID cannot remove a replacement object with different persisted
  stronghold ownership.
- Requeueing is coalesced by stronghold city ID.
- Exceptions cannot leave the city-update scope permanently active.
- Logical fall completion is not replayed merely because physical city
  disposal had to wait.
- The scheduler keeps its existing pause-on-unhandled-error behavior; this
  change removes the invalid collection mutation rather than swallowing it.

## Verification

Rules and source-guard tests prove:

- a disposal request made during city enumeration does not remove the city;
- the same request removes the city after leaving the scope;
- repeated requests coalesce and remain idempotent;
- `CompleteFall` persists completion before requesting disposal;
- suppression, founding cleanup, restore cleanup, and rollback do not call
  `CityManager.removeObject` outside the guarded disposal helper;
- the scope is released through a finalizer path.

Runtime verification captures a stronghold at 100 percent in large-step mode
and confirms that suppression completes without `Collection was modified`,
without scheduler pause, and without leaving a live completed stronghold city.

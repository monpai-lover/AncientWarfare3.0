# RTS Post-Return Native Release Design

## Goal

When a wartime RTS army completes its return into a friendly safe city, RTS
ownership ends for that army. Surviving permanent actors return to native
WorldBox jobs. Synthetic soldiers remain protected during the march and are
eligible for removal only after this release boundary. A later war may enroll
the army into RTS again through the existing war-start flow.

## Current Failure

`WarArmyReturnService.Finish` clears the return queue and persisted return
intent, then releases only return-specific jobs. It does not enforce the
postcondition that the controller, mission index, persisted mission, runtime
movement state, and military P0 ownership are absent. It also calls the mod's
standing-army peacetime refresh, so completion is not a strict handoff to the
native AI. A surviving stale mission can therefore reassert an RTS march task
after the army appears to have arrived home.

## Ownership Boundary

Successful arrival has this ordered transition:

1. Remove the return queue entry and persisted return intent.
2. Invalidate any residual RTS controller and mission without starting a new
   return order.
3. Clear RTS paths, formations, transport ownership, tactical targets, and P0
   movement priority for the army.
4. Cancel RTS/return actor behaviours and assign each surviving permanent
   actor its native `getNextJob()` result.
5. Publish return completion. Synthetic demobilization may then remove
   synthetic soldiers because return ownership is no longer active.

The release operation must be idempotent. Missing controllers, disposed
actors, and already-cleared jobs are successful no-ops.

## Scope

The change applies only to successful return completion. Return cancellation
because a valid new mission was published keeps that mission and must not
perform the native handoff. Invalid or disposed armies retain the existing
cleanup behavior.

This change does not permanently opt an army out of RTS. Existing war-start
and participant-enrollment logic may assign a new mission during a later war.
It does not redesign standing-army peacetime patrols globally; the completed
return path itself must not explicitly install a mod peacetime job.

## Components

### ArmyRtsControllerService

Expose a narrowly scoped post-return release operation. It reuses the existing
controller invalidation cleanup but must not call `WarArmyReturnService.TryBegin`
and must not install peacetime mod jobs. It also releases any remaining actor
RTS tasks to native jobs.

### WarArmyReturnService

`Finish` invokes the post-return release after clearing return persistence and
before logging completion. `Cancel` remains unchanged because cancellation can
mean that a newly published RTS mission now owns the army.

### SyntheticMobilizationLedgerService

No new removal policy is required. Its existing rule defers removal while
`WarArmyReturnService.IsActive` is true. Clearing return ownership at `Finish`
continues to make synthetic soldiers eligible only after arrival.

## Failure Handling

Actor-level cleanup is best-effort so one invalid actor cannot prevent the
rest of the army from being released. Army-level RTS indices and persistence
must be cleared even when actor job assignment fails. Diagnostic completion
logging includes whether controller ownership remained after release.

## Verification

Regression coverage must prove:

- successful `Finish` calls the post-return controller release before the
  completion log;
- the post-return release invalidates mission ownership without beginning
  another return;
- surviving actors are unregistered from military P0 and assigned native
  jobs;
- the completion path does not call `StandingArmyPeacetimeService.RefreshJob`;
- cancellation for a valid replacement mission does not perform native
  release;
- return, wartime lifecycle, synthetic mobilization, and task-ownership slices
  remain green.

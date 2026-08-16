# Peacetime AI Release and RTS Member Pursuit Design

## Goal

During complete peace, ordinary warriors and generals must no longer be controlled by the AncientWarfare3 standing-army patrol job. After wartime return completes, vanilla `Actor.getNextJob()` must regain full control so actors can eat, socialize, reproduce, and perform other normal jobs without being pinned by an AW3 patrol task.

During active RTS combat, an ordinary soldier that has detected a valid enemy must retain that target and close to its own attack range. A captain's short tactical target envelope must not clear a member's movement target before `BehGoToActorTarget` can run.

## Root Cause

### Peacetime Patrol Ownership

`AW_EnlistPatch.GetNextJob_Asylum_Prefix` calls `StandingArmyPeacetimeService.GetJob`. In peace, that service can return `aw_standing_army_peacetime_job` for career soldiers. Therefore an actor released by the return-home flow is assigned the custom patrol job again on its next job selection. This overrides vanilla job selection and can leave the actor displaying a vanilla task while lacking a working vanilla movement cycle.

### Member Combat Target Loss

`BehArmyRtsMemberCombat` and `HasValidMemberCombatTarget` reuse `IsValidCaptainCombatTarget`. That validator requires the target to be within the captain's ten-tile tactical envelope. A member can therefore detect a live hostile actor at eleven or twelve tiles, but the member behavior clears `beh_actor_target` before the following `BehGoToActorTarget` step executes. The actor remains in combat state without a movement target and never closes to attack range.

## Peacetime Design

1. Stop assigning `aw_standing_army_peacetime_job` from every runtime entry point.
2. Let `Actor.getNextJob()` continue to the original game whenever no wartime RTS, return-home, wartime garrison, royal-asylum, or feudatory rule applies.
3. Keep the legacy job and task asset IDs registered for save compatibility. They are not used for new assignments.
4. Add one-way compatibility cleanup: when an actor is found in the legacy peacetime patrol job or task, cancel that stale behavior, clear patrol-only state, and restore vanilla job selection.
5. Cleanup must never interrupt an active wartime RTS mission, return-home mission, wartime garrison assignment, or royal-guard-specific control.
6. Existing calls to `StandingArmyPeacetimeService.RefreshJob` remain safe: outside protected military flows they may only release legacy patrol state, never assign it.

## Member Combat Design

1. Add a member-specific target-retention rule. A target remains chaseable while the member and target are alive, hostile, on the same island, and the member's army is still under the applicable RTS combat phase.
2. Do not apply the captain's ten-tile envelope to an ordinary member's retained target. The actor movement behavior is responsible for closing from detection distance to the actor's real attack range.
3. Use the member-specific validator for member task admission, current target retention, target search filtering, and the final attack action. Captain combat continues using the captain validator.
4. Keep the existing bounded local candidate scan and scheduler cadence. The fix must not add global actor scans or per-frame path recalculation.
5. If a member has a valid retained target outside attack range, set `beh_actor_target` and continue into `BehGoToActorTarget`; do not clear the target or fall back to follow.
6. Clear or replace the target only when it dies, becomes friendly, moves to another island, becomes invalid for the active siege, or the RTS combat phase ends.

## Non-Goals

- Do not change wartime RTS movement or combat.
- Do not change return-home destination or completion rules.
- Do not change wartime garrison behavior.
- Do not change royal guard following behavior.
- Do not physically remove legacy assets, because old saves may still reference their IDs.
- Do not enlarge the captain combat envelope.
- Do not restore vanilla `fighting` while RTS owns member combat.

## State Flow

```text
wartime / return / garrison state
    -> existing AW3 military job remains authoritative

complete peace with legacy patrol job/task
    -> cancel stale patrol behavior
    -> clear patrol cursor/state
    -> restore vanilla profession job
    -> next Actor.getNextJob() runs vanilla selection

complete peace without legacy patrol state
    -> AW3 does nothing
    -> Actor.getNextJob() runs vanilla selection

RTS member detects a valid hostile outside attack range
    -> retain member combat target
    -> BehGoToActorTarget closes the distance
    -> attack when the actor's real attack range is reached
    -> retain, replace, or clear according to target and combat-phase validity
```

## Verification

Add regression tests proving that:

- complete peace never selects the AW3 peacetime patrol job;
- a legacy patrol job/task is recognized for cleanup;
- cleanup is not requested for unrelated vanilla tasks;
- wartime return and garrison precedence remain unchanged;
- an ordinary member retains a live hostile target outside the captain's ten-tile envelope;
- a retained member target reaches the movement step instead of being cleared;
- dead, friendly, cross-island, and out-of-phase member targets are rejected;
- captain target-envelope behavior remains unchanged;
- the existing rules and adversarial RTS simulation suites still pass;
- the mod builds successfully for .NET Framework 4.8.

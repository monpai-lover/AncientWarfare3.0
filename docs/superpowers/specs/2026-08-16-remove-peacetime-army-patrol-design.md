# Remove Peacetime Standing-Army Patrol Design

## Goal

During complete peace, ordinary warriors and generals must no longer be controlled by the AncientWarfare3 standing-army patrol job. After wartime return completes, vanilla `Actor.getNextJob()` must regain full control so actors can eat, socialize, reproduce, and perform other normal jobs without being pinned by an AW3 patrol task.

## Root Cause

`AW_EnlistPatch.GetNextJob_Asylum_Prefix` calls `StandingArmyPeacetimeService.GetJob`. In peace, that service can return `aw_standing_army_peacetime_job` for career soldiers. Therefore an actor released by the return-home flow is assigned the custom patrol job again on its next job selection. This overrides vanilla job selection and can leave the actor displaying a vanilla task while lacking a working vanilla movement cycle.

## Design

1. Stop assigning `aw_standing_army_peacetime_job` from every runtime entry point.
2. Let `Actor.getNextJob()` continue to the original game whenever no wartime RTS, return-home, wartime garrison, royal-asylum, or feudatory rule applies.
3. Keep the legacy job and task asset IDs registered for save compatibility. They are not used for new assignments.
4. Add one-way compatibility cleanup: when an actor is found in the legacy peacetime patrol job or task, cancel that stale behavior, clear patrol-only state, and restore vanilla job selection.
5. Cleanup must never interrupt an active wartime RTS mission, return-home mission, wartime garrison assignment, or royal-guard-specific control.
6. Existing calls to `StandingArmyPeacetimeService.RefreshJob` remain safe: outside protected military flows they may only release legacy patrol state, never assign it.

## Non-Goals

- Do not change wartime RTS movement or combat.
- Do not change return-home destination or completion rules.
- Do not change wartime garrison behavior.
- Do not change royal guard following behavior.
- Do not physically remove legacy assets, because old saves may still reference their IDs.

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
```

## Verification

Add regression tests proving that:

- complete peace never selects the AW3 peacetime patrol job;
- a legacy patrol job/task is recognized for cleanup;
- cleanup is not requested for unrelated vanilla tasks;
- wartime return and garrison precedence remain unchanged;
- the existing rules and adversarial RTS simulation suites still pass;
- the mod builds successfully for .NET Framework 4.8.

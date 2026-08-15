# Warrior Movement and RTS Soldier Combat Lifecycle Repair

## Problem

The current soldier-combat changes created a circular admission gate. An
ordinary soldier may enter `aw_army_rts_member_combat` only when it already
has a valid personal combat target, while active target acquisition happens
inside that task. A soldier without a preassigned target therefore remains in
`warrior_army_follow_leader` and never reaches the search behavior. A soldier
that briefly loses its target is also restored to follow immediately.

Captain combat does not have this dependency. During active field combat the
captain stays in its dedicated combat task, repeatedly validates or searches
for a hostile target, and waits briefly before searching again when no target
is available.

Peaceful warriors have a separate ownership failure. The current standing-army
job contains only `aw_standing_army_peacetime_patrol`. The v1.1.2 release also
contained the native `make_decision` task and ended each patrol with a random
wait. Removing those entries made the patrol job monopolize the actor: native
hunger, eating, social, and other movement decisions cannot run. Wartime RTS
movement masks this problem by replacing the job with an RTS-owned task.

## Design

### Peaceful Warrior Decisions

The standing-army peacetime job will restore the v1.1.2 task composition:

1. Register native `make_decision` before the custom patrol task so hunger,
   eating, social, and other native decisions retain an execution path.
2. Keep the custom city-border patrol task, but allow normal social
   cancellation as in v1.1.2.
3. End a completed patrol with a bounded random wait before the next patrol
   cycle. A missing or current-tile patrol target also stops the current cycle
   after a short wait instead of repeating the same action indefinitely.
4. Do not register all peaceful warriors for military P0 execution. Ordinary
   large-step scheduling remains responsible for peaceful native behavior;
   P0 remains reserved for active military movement ownership.

### RTS Field Combat

Ordinary soldiers will use the same combat lifecycle as the working captain
combat behavior:

1. While an RTS mission has released the army into field combat, every live
   non-captain member remains eligible for the dedicated member-combat task,
   even when it has no current personal target.
2. Member combat validates the current behavior target with the same rules
   used by captain combat. If the target is invalid, it runs the same bounded
   hostile-target search used by captain combat.
3. If no target is found, the soldier clears the stale behavior target, waits
   briefly, and repeats the member-combat search step. It does not restore
   follow merely because one search returned no target.
4. Once the army-wide field-combat phase ends, the RTS controller may restore
   strategic follow or another mission task. Task ownership changes remain a
   controller decision rather than a side effect of transient target loss.
5. The captain/member attack action chain remains shared. The only intentional
   distinction is that captains use the captain task and ordinary soldiers use
   the member task.

Member-specific target-envelope code introduced by the broken change will no
longer control task admission or target retention. Existing unrelated fixes,
including same-P0 follower refresh, return-to-city-center behavior, and native
AI release after return, remain unchanged.

## State Flow

```text
peaceful standing-army job
    -> native make_decision (eat / social / other native work)
    -> city-border patrol
    -> bounded random wait

strategic follow
    -> army enters field combat
member combat
    -> validate current target
    -> find captain-style target when needed
    -> attack valid target
    -> wait and search again when temporarily targetless
    -> army exits field combat
strategic follow / next RTS mission task
```

## Tests

Rules and source-guard tests will prove that:

- the peacetime standing-army job retains native `make_decision` before
  patrol;
- patrol can yield to native social behavior and has a bounded wait;
- a missing or current-tile patrol target does not repeat forever;
- active field combat admits a non-captain without a personal target;
- a transient target miss repeats the combat search instead of restoring
  follow;
- vanilla fighting remains suppressed while RTS owns the battle lifecycle;
- ending field combat still permits the controller to restore follow;
- existing same-P0 movement, return, war-lifecycle, and full rule suites pass.

The mod will then be compiled, deployed to the active WorldBox mod directory,
and source deployment will be verified.

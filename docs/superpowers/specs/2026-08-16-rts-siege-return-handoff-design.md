# RTS Siege and Return Handoff Design

## Scope

Fix two task-ownership handoff failures without changing combat target selection,
attack range, damage, strategic target selection, pathfinding, or diagnostics:

1. An army can enter target-city siege state without its captain and eligible
   members retaining the dedicated siege combat task.
2. After a return-home order completes, ordinary soldiers can remain on the
   return follower job and appear permanently stuck on follow.

## Evidence and Root Cause

The runtime log records `AW3 RTS siege enter` for the affected army but contains
no subsequent `aw_army_rts_siege_combat` task for that army. The controller only
assigns siege tasks during the first transition into siege. Once
`SiegeCombatActive` is true, later controller passes return before repairing
task ownership. The captain assignment also passes through the general
`HasCaptainMission` gate, which can reject assignment during a stale tactical
ownership phase even though the controller has already activated siege.

Return completion does iterate the full roster, but each ordinary actor is sent
directly to `setJob(getNextJob())` while its return job is still current. This
does not provide a clean ownership boundary and can leave
`aw_army_return_home_follower` with `warrior_army_follow_leader` active after
the return record and RTS priority have been removed.

## Siege Ownership Design

Target-city siege remains an RTS-owned tactical mode. Entering siege will:

- validate the live mission, target city, and hostile target as today;
- set `SiegeCombatActive` and the cached siege target as today;
- force the live captain onto `SiegeCombatTaskId` without depending on the
  generic captain mission ownership gate;
- force each live non-captain warrior already inside the target city's core or
  border combat zone onto `SiegeCombatTaskId`;
- leave members outside that combat zone on vanilla follow-leader movement so
  they can enter the city normally;
- register affected actors in military P0 as today.

While siege remains active, the controller will repair task ownership rather
than treating the initial assignment as permanent. Captain repair is constant
work. Member repair uses the existing bounded job cursor/budget so a large army
does not add an unbounded per-controller-tick roster scan. A new siege entry
reopens the cursor so every eligible member is observed. The active-siege path
also invokes the existing ownership-repair interval and reopens the same cursor
when that interval expires.

Leaving siege keeps the existing behavior: clear siege state and cached target,
cancel combat behaviors, reset strategic movement runtime, and reopen normal
job assignment.

## Return Release Design

Return completion remains gated on the existing two-pass full-roster arrival
confirmation. Once complete, every actor will:

- be removed from military P0;
- release any AW path ownership and clear movement, tile, and attack targets;
- cancel the return behavior chain;
- explicitly clear the RTS return job before selecting a replacement job.

For an ordinary actor, the replacement is selected through the same static
vanilla selector used by peacetime recovery, `Actor.nextJobActor(actor)`. This
ensures the selector runs after RTS ownership has ended and cannot preserve the
return follower job by observing it as current state. Synthetic levies keep the
existing return-arrival confirmation and cleanup behavior.

The completion operation remains a one-time full-roster pass because return
completion already requires the complete roster to be present and verified.

## Failure Handling

- Missing or dead actors are skipped without preventing other roster members
  from being released.
- A failed vanilla job selection clears the actor job, allowing the normal game
  loop to select it later.
- A siege task repair failure is retried by the bounded ownership-repair pass
  while siege remains active.
- No new timeout, teleport, target replacement, or retreat behavior is added.

## Verification

Rules tests will cover:

- active siege repairs a captain that is not on the siege task;
- active siege repairs eligible in-city members after the first entry tick;
- out-of-city members remain on follow-leader movement;
- return release clears the old return job before vanilla job selection;
- return release covers every live ordinary member, not only the captain.

The complete rules suite and Release build must pass. Runtime verification must
show an `AW3 RTS siege enter` followed by siege tasks for the captain and
eligible in-city members, and a completed return with no surviving
`aw_army_return_home_follower` jobs.

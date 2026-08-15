# RTS Exclusive Retreat And Return Design

## Problem

Retreat and post-war return are military transit phases, but neither phase has
complete movement ownership.

During retreat, nearby enemies enter the military P0 combat boundary and clear
the captain's active path. The stall watchdog then treats slow native retreat
movement as a strategic-route stall, repeatedly rebuilding the route and
selecting alternate endpoints even though the retreat task remains locked to
one friendly city. The retreat task also runs at 0.8 speed, below ordinary
combat movement.

After peace, RTS invalidation immediately releases actors to peacetime jobs.
`WarArmyReturnService` then calls `goTo` only while the captain is not moving.
A social or patrol task therefore blocks the return order, and any direct path
that is issued can be overwritten by ordinary AI on the next update. Followers
have no return ownership at all.

## Approved Design

Retreat and return become explicit, exclusive military transit owners.

Retreat remains an RTS mission state. Until the captain reaches the selected
friendly city, the retreat task cannot yield to ordinary combat acquisition,
strategic alternate-endpoint recovery, social behavior, or peacetime patrol.
The captain keeps one native path to the fixed safe city, while members use the
native army-follow-leader task. Attackers may damage retreating actors, but the
retreating army does not stop to counterattack. A genuinely missing or stalled
native path may be reasserted to the same city after a bounded timeout.

Post-war return remains outside the RTS mission index so peace cannot revive a
war mission. It receives a dedicated captain job/task backed by
`WarArmyReturnService`, plus the native follower task for members. Active
return intent is a military ownership fact: it blocks peacetime job refresh,
keeps actors in military P0 scheduling, and ends only on arrival, death,
invalid kingdom ownership, or a newly published valid mission.

## Retreat Rules

- `Retreat` suppresses immediate-combat P0 preemption for its army actors.
- Stale attack targets are cleared without stopping or clearing retreat paths.
- The retreat captain task uses a 1.15 speed multiplier.
- Native path progress is measured physically; a moving native path is never
  rebuilt merely because its strategic route cursor is unchanged.
- Recovery reasserts the same retreat task and same city endpoint.
- `AlternateEndpoint`, target handoff, and offensive combat tasks are forbidden
  while a valid retreat city exists.
- Followers stay on `warrior_army_follow_leader` and cannot enter member combat.

## Return Rules

- `WarArmyReturnService.TryBegin` persists the order before peacetime jobs may
  claim the army and immediately asserts return jobs.
- The captain task resolves the current persisted safe-city target on every
  task cycle and uses native `BehGoToTileTarget` movement.
- Followers use a dedicated return follower job containing
  `warrior_army_follow_leader`.
- Return tasks cannot be cancelled by reproduction or socializing.
- Peacetime patrol eligibility is false while return intent is active.
- P0 admission treats an active return as a live military objective even with
  no RTS mission.
- Return processing repairs missing jobs/tasks instead of checking only
  `is_moving` or continuously clearing a healthy path.
- Cross-island return continues through the existing military transport owner.
- Arrival clears return persistence and P0 ownership, then refreshes normal
  peacetime jobs.

## Ordering

War and participant invalidation must perform these operations in order:

1. capture the affected live army and intended kingdom;
2. invalidate the wartime RTS mission and its tactical state;
3. begin and persist the return order;
4. assert return jobs for captain and followers;
5. allow peacetime refresh only after return completion.

`RefreshReleasedArmyPeacetimeJobs` and the standing-army peacetime service both
check active return intent, so callback timing cannot expose a one-frame social
task ownership window.

## Diagnostics

Sampled logs record return admission, target, captain task repair, follower job
repair, path issue, transport handoff, arrival, cancellation, and any attempt
by a peacetime job to claim a returning actor. Retreat diagnostics record
combat suppression and same-target path recovery.

## Verification

Pure rules tests prove:

- retreat suppresses combat preemption but ordinary march does not;
- retreat watchdog recovery never selects an alternate endpoint;
- active native movement is physical progress even with a static route cursor;
- an active return blocks peacetime job ownership;
- a social/patrol task is replaceable by the return owner even while moving;
- return P0 admission does not require an RTS mission;
- arrival releases return ownership and permits peacetime jobs.

Source guards prove that the production task is registered, active return jobs
are asserted after invalidation, followers use native leader following, and the
return service no longer gates command publication on `!is_moving`.

The focused rules suite, RTS lifecycle slices, full rules suite, and Release
build must pass before deployment.

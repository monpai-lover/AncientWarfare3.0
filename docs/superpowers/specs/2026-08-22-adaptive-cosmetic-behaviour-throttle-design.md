# Adaptive Cosmetic Behaviour Throttle Design

**Date:** 2026-08-22

**Status:** Approved

## Goal

Prevent high simulation speeds from being capped by civilian cosmetic AI.
Throttle sleep selection, socializing, happy laughter, and singing before they
perform target scans or submit movement paths, without changing military,
transport, survival, work, migration, or refugee behavior.

## Evidence

The 2026-08-22 runtime log ends with 6,662 generated paths. Of these, 6,243
are `Ambient`, 417 are `Operational`, and 2 are `EssentialTravel`. Active Actor
path sessions rise from 0 to about 1,768 while the city-threat physical scan
counter stabilizes at 69 and deferred runtime work falls from 60 to 2. The
late throughput limit therefore follows civilian ambient activity rather than
the remaining war count.

## Approach

Extend the existing `AWIdleBehaviourThrottleRules`, gate, service, and Harmony
patch. Do not reject requests inside `Actor.goTo`, change path ownership, alter
the P0 military lane, or cap pathfinder workers.

The behavior prefix returns `BehResult.Stop` when a cosmetic attempt is not yet
eligible. This ends only the current cosmetic attempt. Vanilla may choose the
activity again later; no Actor task, path, target, or persistent data is
mutated by the throttle.

## Covered Behavior

The policy recognizes these task families:

- social: `socialize_try_to_start_near_bonfire` and
  `socialize_try_to_start_immediate` through `BehTryToSocialize`;
- emotion: `happy_laughing` and `singing` through
  `BehTryFindTargetWithStatusNearby`;
- sleep selection: `BehDecideWhereToSleep` before it redirects to
  `sleep_inside` or `sleep_outside`.

No generic `Ambient` path is denied. Unknown or newly added tasks retain exact
vanilla behavior.

## Speed Tiers

Use the cooperative scheduler's captured requested speed, not the temporarily
normalized `Config.time_scale_asset` visible inside a logical simulation tick.

| Requested speed | Social cooldown | Laugh/sing cooldown | Sleep cooldown |
| --- | ---: | ---: | ---: |
| 0x-2x | 2.0 s | 1.5 s | disabled |
| above 2x through 4x | 4.0 s | 3.0 s | 4.0 s |
| above 4x | 8.0 s | 6.0 s | 10.0 s |

Cooldowns use unscaled real time. Stable Actor-ID jitter of up to 0.5 seconds
spreads attempts across frames without creating per-frame randomness or save
data.

## Eligibility And Safety

Only a valid, living, non-rekt civilian is eligible for throttling. The service
fails open and preserves vanilla execution when Actor state cannot be read.
It never throttles:

- warriors, army members, kings, or boats;
- Actors currently owned by the military P0/RTS movement path;
- unrelated tasks, including food, work, reproduction, migration, refuge,
  transport, combat, and return-home behavior.

Runtime cooldown state remains indexed by Actor ID. Actor disposal removes one
entry in O(1), and world clearing removes all entries. Nothing is written to
`ActorData`, saves, databases, or multiplayer state.

## Diagnostics

Add cumulative allowed and deferred counters for social, emotion, and sleep
attempts to the existing performance diagnostics. Counters must not enumerate
Actors or allocate per attempt. They are evidence for comparing 1x/2x and
5x/7x runs and are reset with the runtime world state.

## Tests

Follow red-green TDD with pure rules and gate tests:

- task IDs map to the correct behavior kind;
- speed boundaries resolve the exact cooldown table;
- sleep remains unthrottled at 0x-2x;
- cooldowns resume exactly at their deadline;
- different behavior kinds and Actor IDs remain independent;
- warriors, army members, kings, boats, and P0 actors are rejected from the
  throttle policy;
- unknown tasks and invalid runtime state fail open;
- the Harmony patch covers `BehDecideWhereToSleep` and delegates to the shared
  service;
- disposal and clear-world cleanup remain O(1) and complete.

Source guards must also ensure the implementation does not touch
`AWPathMovementBridge`, `Actor.goTo`, or military route submission.

## Acceptance

At 5x-7x, the diagnostic counts must show deferred cosmetic attempts while
military and essential path counters continue progressing. Actors must still
occasionally sleep, socialize, laugh, and sing. RTS movement, amphibious
transport, return-home movement, food, work, migration, and refuge must remain
unchanged. A same-save comparison should reduce ambient path generation or
improve actual simulation speed without introducing stuck Actor tasks.

## Rollback

The change is code-only and runtime-only. Reverting the throttle commit restores
the previous cadence; no save migration or cleanup is required.

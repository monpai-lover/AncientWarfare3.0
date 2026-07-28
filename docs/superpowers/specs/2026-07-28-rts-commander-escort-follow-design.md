# RTS Commander Escort Follow Design

## Goal

Replace the continuously enforced per-soldier RTS formation with a low-cost
commander escort model. Armies must keep moving with their commander without
forming a static line, release soldiers during combat, and regroup after combat.

## Behavior

- The commander remains the sole owner of long-distance strategic pathfinding.
- During Rally, March, Deploy, Retreat, and Regroup, ordinary soldiers follow
  stable, dispersed points within a small radius of the commander.
- Follow points use the royal-guard patrol pattern: deterministic actor-based
  offsets, bounded tile checks, and no overlapping fixed formation slots.
- A soldier already inside the loose follow radius waits instead of receiving a
  correction every AI step.
- A soldier outside the radius uses the shared commander route to reconnect.
  Local direct correction is only used for the final short approach.
- During Assault, Pursue, or any immediate combat, RTS follow ownership is
  released. Vanilla combat movement may separate the formation freely.
- After combat ends, the soldier returns to loose commander follow.
- Cross-water transport remains army-scoped. Escort follow must not pull an
  embarked or waiting soldier out of the transport workflow.

## State And Readiness

- Strict formation geometry is no longer a prerequisite for leaving Rally or
  Deploy.
- Readiness uses operational strength plus a bounded proximity quorum around
  the commander. It does not require soldiers to occupy exact slots.
- The watchdog observes the commander during strategic movement. It may inspect
  one distant follower only while proximity recovery is expected.
- A missing strategic route cannot be reported as successful merely because the
  soldiers have gathered around a stationary commander.

## Performance Contract

- Controller processing must not scan and place 16 formation members for every
  army on every pass.
- Follow target calculation is O(1) per actor with a bounded local tile search.
- Stable offsets are recomputed only when the commander changes tile or the
  coarse follow phase changes.
- No per-frame sorting, full-roster slot rebuild, or exact deployment geometry
  is allowed in the controller hot path.
- RTS diagnostics and plan GIF capture remain optional and are not relied upon
  for normal movement behavior.

## Compatibility

- Existing royal guards continue to follow the king through their own job.
- Ordinary RTS armies reuse the behavior pattern, not royal-guard identity,
  traits, jobs, or detached Army ownership.
- Captain continuity, transport queues, mission persistence, occupation
  completion, and player-issued orders keep their existing ownership rules.

## Verification

- Rule tests cover follow ownership for every RTS state and immediate combat.
- Rule tests prove strict slot readiness no longer gates March or Assault.
- Source guards reject controller hot-path calls to full formation observation.
- Deterministic simulations cover Rally to March to Assault to post-combat
  regroup without a stationary follower line.
- In-game verification compares the same save before and after the change:
  controller cost, FPS, commander movement, follower movement, capture, and
  next-city mission handoff.

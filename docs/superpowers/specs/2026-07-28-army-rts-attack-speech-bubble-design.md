# Army RTS Attack Speech Bubble Design

## Goal

When an AW3 army general genuinely begins an assault mission, show a short
localized map bubble above the general:

> 战至最后一刻，自刎归天

This is presentation-only. It must not alter RTS planning, state transitions,
pathfinding, scheduling, or combat outcomes.

## Chosen Approach

Use a dedicated AW3 text-bubble service driven by the existing RTS
visualization frame patch. The service observes only visible banner units at a
fixed low frequency and emits a bubble on the first observation of a qualifying
assault mission.

Alternatives considered:

- Patching the high-frequency `aw_army_rts_mission` behavior would detect the
  state directly, but would add work to every task execution and could repeat
  the message.
- Reusing `UnitTextManager.startNew(string)` would be simpler, but that manager
  belongs to the unit-window/avatar UI and does not follow actors on the map.
- Reusing the original icon-only social bubble cannot display arbitrary text.

The dedicated presentation service is therefore the smallest approach that
matches the requested behavior without coupling it to RTS authority logic.

## Trigger Rules

A candidate qualifies only when all of the following are true:

- The actor is the live captain of an indexed army and is currently visible.
- The RTS projection state is `ArmyRtsState.Assault`.
- The mission proposal kind is `ArmyRtsProposalKind.Attack`.
- The mission role is `ArmyRtsRole.Assault`.
- The original `talk_bubbles` option is enabled.

The event identity is `(armyId, warId, targetCityId, issuedTime)`. A mission can
emit at most once. Re-entering `Assault` for the same mission does not emit a
second bubble.

Rate limits:

- Scan interval: 0.35 seconds.
- Per-general cooldown: 10 seconds.
- Global emission interval: 0.5 seconds.
- Maximum simultaneous bubbles: 4.
- Display duration: 3 seconds.

## Rendering

Register or construct an AW3-owned `QuantumSpriteAsset` backed by
`QuantumSpriteWithText`. Reuse the original
`CommunicationLibrary.normal.getSpriteBubble()` background and the current
localized font. Position the bubble from
`Actor.getHeadOffsetPositionForFunRendering()` so that it follows the general.

The Chinese sentence may wrap to two lines to stay legible. The localization
key is `aw_army_rts_attack_oath`; other languages receive explicit fallback
text rather than the raw key.

The feature respects `talk_bubbles` and is independent of the AW3 route-line
visualization toggle.

## Runtime Lifecycle

The presentation service owns only transient dictionaries and active bubble
records. `MapBox.clearWorld` clears mission identities, cooldowns, and rendered
bubbles. Invalid or dead actors are dropped immediately. A rendering failure
clears display state and is logged once, while RTS gameplay continues.

## Performance Boundaries

- Never scan all actors or all armies.
- Read only `visible_units_with_banner` on the throttled scan.
- Reuse pooled render objects; do not instantiate per frame.
- Perform no database writes.
- Do not run any world-authority operation from the frame postfix.

## Testing

Pure rule tests cover:

- exact qualifying state, proposal kind, and role;
- rejection of non-assault, defense, hold, and invalid identifiers;
- stable event identity including `issuedTime`;
- mission-level deduplication;
- general and global cooldowns;
- simultaneous-bubble limit;
- disabled `talk_bubbles` behavior.

Source guards verify that the patch calls the service from the presentation
stage, clears it during `clearWorld`, and does not connect it to the RTS route
visual toggle. The mod build provides the compile-time integration check.

## Out Of Scope

- Changing the oath based on personality, faction, or battle result.
- Showing speeches for ordinary soldiers.
- Modifying original RTS decisions, task scheduling, or combat behavior.
- Adding a separate settings button beyond the original `talk_bubbles` option.

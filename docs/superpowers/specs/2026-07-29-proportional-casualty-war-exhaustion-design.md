# Proportional Casualty War Exhaustion Design

## Goal

Make severe wartime manpower losses increase visible war exhaustion in a
scale-independent way and allow an early peace attempt without creating an
automatic early surrender path.

## Approved Behavior

1. Casualty exhaustion uses deaths recorded by the active `War`.
2. Its denominator is the full wartime mobilization potential of every
   kingdom currently participating on that side, not the number of soldiers
   already fielded when war begins.
3. Each participant contributes its mobilization potential to its side once.
   A kingdom joining later adds its baseline once; leaving, losing cities, or
   losing population does not shrink the persisted denominator.
4. Casualty exhaustion is linear and capped at 60:

   ```text
   casualty exhaustion = clamp(round(deaths / mobilization baseline * 60), 0, 60)
   ```

   Therefore losses of 25%, 50%, 75%, and 100% produce 15, 30, 45, and 60
   exhaustion respectively.
5. Existing duration exhaustion and victory-relief rules remain separate and
   continue to compose with casualty exhaustion. Peace-term costs and war
   score are unchanged.
6. During the first three war years, a side at 30 or more exhaustion may pass
   the existing short-war peace gate. This only allows the normal settlement
   AI to evaluate and propose a legal offer using current war score. It does
   not force surrender or acceptance.
7. Total defeat and the existing both-sides-at-100 forced-settlement rule are
   unchanged.

## Authoritative Data Flow

`WarScoreRuntimeBridge.StartWar` reads each side's participants and sums
`WartimeMilitaryPotentialService.CountPotentialWarriors`. It passes both
baselines into `WarScoreService`, which persists them in `WarScoreSnapshot`.
Annual calibration reconciles participant membership and appends newly joined
participants exactly once through a persisted participant-baseline ledger.

`WarScoreService` combines persisted losses, persisted mobilization baselines,
duration, decisive occupation, and victory relief. The resulting attacker and
defender exhaustion values remain the single source used by settlement
acceptance, war planning, and presentation.

Old active snapshots receive a nonzero baseline on their first live
calibration. Historical completed snapshots are not rewritten.

## Peace AI

The peace scheduler reads the requester's exhaustion from the same
`WarScoreSnapshot` used by the negotiation window. A short, uninvaded war is
normally protected from premature peace exactly as before. The protection is
lifted only when requester exhaustion is at least 30; all existing position,
court, ruler, resolve, war-score, legality, cooldown, and acceptance checks
still apply.

## Presentation

The negotiation summary displays both sides directly below their war-score
summary:

```text
<kingdom>: War exhaustion 30/100
<kingdom>: War exhaustion 12/100
```

The existing responder-exhaustion acceptance factor remains, because it
explains offer acceptance. New Simplified Chinese, Traditional Chinese, and
English localization keys must be present. The layout must fit the existing
minimum window size without increasing the default size.

## Persistence And Compatibility

- Add attacker and defender mobilization baselines to `WarScoreSnapshot`.
- Add migration-safe SQLite columns with nonzero fallback repair.
- Persist participant-side baseline contributions so reloads cannot double
  count joined kingdoms.
- Clamp missing or corrupt baselines to at least one before division.
- Multiplayer replicas consume authoritative snapshot values and never
  calculate their own mobilization baseline.

## Tests

- Pure rules: 25/50/75/100 percent losses map to 15/30/45/60; invalid and
  over-cap inputs remain bounded.
- Peace rules: a short war below 30 exhaustion stays gated; at 30 it enters
  normal evaluation without becoming forced surrender.
- Service tests: start baselines persist, a late participant is counted once,
  reload does not double count, and legacy active snapshots are repaired.
- UI tests: both side values and all localization keys are bound, and the
  minimum window height still fits.
- Regression: duration exhaustion, victory relief, war-score composition,
  both-sides forced settlement, and peace-term pricing remain unchanged.

## Out Of Scope

- Changing war-score values or peace-term prices.
- Automatically ending a war from one side's casualty exhaustion alone.
- Reworking duration exhaustion thresholds.
- Changing recruitment or casualty recording.

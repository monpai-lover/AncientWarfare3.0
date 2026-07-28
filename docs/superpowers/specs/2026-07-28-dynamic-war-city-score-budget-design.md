# Dynamic War City Score Budget Design

Date: 2026-07-28

## Goal

Make occupied cities contribute enough war score for the scale and purpose of
the war. The current fixed city-score budget of 45 makes control of three out
of five pre-war cities stop around 30 points even when the occupied territory
is strategically dominant.

## Fixed Budget By War Type

The city-score budget is selected from the war type and remains fixed for the
life of that war:

| Budget | War types | Meaning |
| --- | --- | --- |
| 60 | `tributary_war`, `vassal_war` | Limited wars whose primary result is a subject relationship. |
| 75 | `reclaim`, `restoration_war` | Limited territorial or dynastic restoration wars. |
| 85 | `aw_normal_war`, `independence_war`, `general_rebellion_war`, `fief_independence_war`, `jingnan_war`, `succession_dispute_war`, `coup_restoration_war` | General conquest and internal sovereignty wars. |
| 100 | `tianming`, `tianmingrebel` | Total struggles for the Mandate and political order of the realm. |

Unknown vanilla or third-party war types use 85. Null or empty identifiers
also use 85.

## Scoring

Each occupied city keeps the existing inputs:

- its share of the owner's city count at the start of the war;
- development, population, Zone count, and building count;
- capital status;
- active war-goal status.

The proportional share uses the selected budget instead of the fixed value
45. The accumulated city component is clamped to that same budget. Battle,
war-goal, and casualty components keep their current rules, and the final war
score remains clamped to `-100..100`.

This change does not alter negotiation term prices, city cession costs,
resource prices, or AI preference weights.

## Data Flow

1. The runtime bridge reads the immutable war-type identifier from the active
   `War`.
2. A pure rule maps that identifier to the city-score budget.
3. City-control additions, reversals, and load-time reconciliation pass the
   same budget into the score service.
4. The service uses the budget both for the per-city proportional share and
   for clamping the accumulated city component.

No new database column is required. Existing wars are recalibrated from their
persisted control rows and live war type during normal restore/reconciliation.
This avoids stale or partially migrated budget state in old saves.

## Failure Handling

- Missing war assets fall back to budget 85.
- Invalid supplied budgets are normalized to the supported range 60–100.
- Reversal and recapture paths must use the same resolved budget as capture.
- A load-time control row whose city no longer exists is handled by the
  existing reconciliation cleanup and cannot retain phantom score.

## Verification

- Pure rule tests cover every listed war type, unknown types, and empty IDs.
- Scoring tests prove the proportional share uses 60, 75, 85, and 100.
- Aggregate tests prove each city component clamps at its selected budget and
  can return to zero after recapture.
- Source guards prove capture, reversal, and restore paths all supply the war
  type budget.
- Existing war-score and war-peace integration suites remain green.
- Autosave verification checks a multi-city war where three of five cities
  produce roughly three-fifths of the selected city budget before quality and
  capital modifiers, instead of being constrained by the old 45-point pool.

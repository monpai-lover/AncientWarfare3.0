# AI Border War Target Design

## Goal

Make autonomous AI war selection prefer realms sharing a land border with the source realm. If at least one eligible bordering target exists, non-border targets are excluded. If no eligible bordering target exists, the existing long-range target rules remain available.

## Scope

- Applies only to autonomous AI target selection.
- Covers the asynchronous strategy planner and the legacy `WarDecisionAI` path.
- Applies uniformly to normal, mandate-conquest, take-mandate, and zhulu candidate types.
- Does not change player declarations, diplomacy-window declarations, or the final war-creation guard.

## Architecture

`StrategyTargetFacts.Neighbor` remains the single border fact, populated by the existing `AreNeighbors` implementation. Candidate ranking will use a shared two-pass filter: evaluate all valid candidates, retain neighboring candidates when any exist, otherwise retain the full evaluated set. The legacy picker will use the same target-fact list and filter before selecting its first candidate.

## Behavior

1. Evaluate target legality and score with all existing casus-belli, mandate, vassal, alliance, power, and age rules.
2. Detect whether any evaluated candidate has `Neighbor == true`.
3. When true, discard every evaluated candidate with `Neighbor == false`.
4. When false, keep all evaluated candidates, preserving existing remote-war behavior.
5. Keep deterministic score and kingdom-id tie-breaking unchanged.

## Testing

Add pure rule coverage for: neighboring candidate preferred over a higher-scoring remote candidate; remote candidate allowed when no neighbor is eligible; special war kinds obey the same filter; and no-candidate input remains empty.

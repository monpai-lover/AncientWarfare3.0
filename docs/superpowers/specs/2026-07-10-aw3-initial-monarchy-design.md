# AW3 Initial Monarchy Design

## Goal

Prevent a newly founded managed-lineage kingdom from being classified as a republic before it receives its first king. Preserve the existing behavior in which a monarchy with an established royal line becomes a republic only after its hereditary candidates are exhausted.

## Scope

This change targets new games and new kingdoms. Existing saves do not require migration or inference from legacy lineage fields.

## State Model

Add a persisted kingdom-data flag named `aw_monarchy_established`.

- A new kingdom starts with the flag absent, which is equivalent to `false`.
- Selecting the first non-republic king sets the flag to `true`.
- Later hereditary or restored monarchs keep it `true`.
- Electing a republican leader does not establish a monarchy.

The flag separates these two otherwise identical vacancy states:

1. Initial vacancy: no king has ever been established, so select a founder from the normal city-leader fallback and remain a monarchy.
2. Extinction vacancy: a monarchy was established, but no valid hereditary heir remains, so the kingdom may become a republic.

## Selection Flow

For kingdoms managed by the AW succession system:

1. Use a valid registered or calculated hereditary heir when one exists.
2. If the kingdom is already a republic, use the ranked republican successor/election path.
3. If no monarchy has been established, select the normal city-leader succession candidate and mark that candidate as a leader-fallback monarch.
4. Only an established monarchy with no hereditary heir may enter the republic election path.

If an initial kingdom has no valid city leader yet, return no candidate without changing its government. A later vanilla king check can try again after a leader exists.

## Components

- `LineageKeys`: owns the persisted flag key.
- `SuccessionTransitionRules`: owns pure decisions for initial-founder selection and republic eligibility.
- `AW_HeirPatch`: routes the first vacancy to the city-leader founder path and records successful non-republic kings as an established monarchy.
- `RepublicGovernmentService`: supplies the established-monarchy state to the pure republic-entry rule.

## Tests

Add regression coverage proving that:

- an initial vacancy with electable people does not enter a republic;
- an established monarchy with no hereditary heir and electable people does enter a republic;
- a pending succession still cannot enter a republic;
- first-king selection uses the leader fallback only before monarchy establishment;
- existing republic succession behavior remains unchanged.

Run the focused succession-government tests, the broader rule-test suite used by the repository, and the main project build.

## Non-goals

- No migration or compatibility inference for existing saves.
- No change to hereditary candidate ordering.
- No change to republican candidate scoring or successor ranking.
- No change to unmanaged vanilla kingdoms.

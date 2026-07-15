# AW3 Kingdom Extinction School Affiliation Design

## Goal

Prevent a living school member from retaining a disposed kingdom reference
when a cityless civilization is removed, while preserving the rule that normal
scholarly travel does not change engine-level nationality.

## Root Cause

WorldBox removes a kingdom by calling `Kingdom.makeSurvivorsToNomads()`, which
moves each living actor to its actor asset's wild kingdom. AW3 globally guards
`Actor.joinKingdom` and `Actor.setKingdom` so travelling school members cannot
silently naturalize. During extinction the recorded home kingdom is still
alive, so this guard rejects the wild-kingdom transfer. `KingdomManager` then
disposes the old kingdom while the actor still references it. The next actor,
zone, or minimap update dereferences the disposed kingdom and repeatedly throws
`NullReferenceException`.

The observed run contains exactly this sequence: Tang lost its only city and
was destroyed while living school master Zou Yan remained. The resulting log
repeats failures in `SimObjectsZones.addUnit`, `UnitLayer.UpdateDirty`, and
`Actor.isAllowedToLookForEnemies`.

## Design

Add a pure `SchoolAffiliationTransferRules.AllowsExtinctionRelease` predicate.
It permits a guarded school actor's kingdom transfer only when all conditions
hold:

- the source is a live civilization;
- the kingdom manager's city index is stable;
- the source has no cities;
- the target kingdom asset exactly matches the actor asset's
  `kingdom_id_wild`.

`HistoricalAffiliationService.CanJoinKingdom` evaluates this predicate after
finding a school affiliation and before applying normal travel restrictions.
Both the public `joinKingdom` call and its nested `setKingdom` call therefore
receive the same answer. Transfers to foreign civilizations remain blocked,
and formally appointed guests still use `FormalAffiliationTransferScope`.

No save migration or world scan is added. `AW_KingdomExtinctionPatch` retains
its city-index guard and the original WorldBox survivor conversion remains the
only code that performs the faction mutation.

## Verification

The pure rules harness must prove the exact extinction case is allowed and
that a dirty city index, a surviving city, a non-civilization source, or a
non-wild target remains blocked. Source guards must prove
`HistoricalAffiliationService` uses the predicate and compares the target with
`ActorAsset.kingdom_id_wild`. Full source guards, Debug/Release builds, and
`git diff --check` must pass before deployment.

Runtime verification uses a fresh world with a school member in a one-city
kingdom. After the city is lost, the actor must have a live wild kingdom with a
non-null asset and the log must contain no new failures from the three original
stack signatures.

# Bandit Gate Tower And Single Plan Design

## Goal

Allow a valid 2 by 2 bandit stronghold wall to place four racial watch towers
without misreporting tower placement as a wall failure. Direct god-power
creation must plan once before creating a kingdom and commit that exact plan.

## Gate Towers

Each cardinal gate keeps its existing three-tile opening. Tower candidates
start at the gate center and move one tile at a time toward the stronghold
center, up to six tiles. A candidate is accepted only when the complete native
building footprint belongs to the four selected stronghold zones and the same
native check used by `BuildingManager.addBuilding` succeeds with
`BuildPlacingType.Load` and no city argument. Four distinct valid tower tiles
are required. Failure returns `aw_bandit_stronghold_tower_failed`; wall geometry
failure continues to return `aw_bandit_stronghold_wall_failed`.

## Single Plan Commit

`TryCreateDirect` obtains one authoritative plan while the ruler and mother
city are unchanged. Only after that plan succeeds may it call
`makeNewCivKingdom`. The new kingdom replaces the provisional bandit context
inside the plan, and a planned-commit method performs world mutation without
calling `TryPlan` again. Other callers may still use `TryCreate`, which plans
once and then delegates to the same planned-commit method.

## Failure Handling

Any geometry or tower failure occurs before actor, kingdom, city, wall, or
building mutation. Failures after kingdom creation retain the existing
transactional cleanup. No tower is force-placed over terrain or an existing
building.

## Verification

Pure tests cover inward cardinal candidate ordering. Source guards require
native footprint containment, native cityless placement parity, a separate
tower failure key, and exactly one `TryPlan` call in direct creation before
`makeNewCivKingdom`. The complete rule suite, localization validation, source
guards, net48 build, deployment hashes, and an in-game god-power attempt form
the acceptance check.

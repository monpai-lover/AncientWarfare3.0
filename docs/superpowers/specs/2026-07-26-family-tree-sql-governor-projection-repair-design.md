# Family Tree SQL And Governor Projection Repair Design

## Scope

Repair two independent runtime regressions without changing family-tree output,
court rules, archive schema, or the approved asynchronous UI architecture:

1. Family-tree bulk reads must execute against AW3's bundled SQLite 3.9.2.
2. A newly assigned city leader must not be reported as a failed career
   projection while actor and city kingdom references are still settling.

## Family-Tree Query

`LineageBulkQuery` will define a non-recursive `all_edges` CTE that combines
persisted `FamilyEdge` rows with both archived parent columns. `ancestors` and
`relatives` will each contain exactly one recursive SELECT over `all_edges`.
This retains the existing bounded, two-command bulk-read contract while
avoiding compound recursive terms unsupported by SQLite 3.9.2.

The existing real-SQLite integration suite is the regression test. It must
fail with `circular reference: relatives` before the change and pass after the
query is rewritten. The wide-family node, edge, ancestor-priority, and string
budgets remain unchanged.

## Governor Projection Timing

The `City.setLeader` postfix will classify the assignment before projecting:

- Invalid or obsolete assignment: ignore it.
- Stable assignment: actor, city, and city kingdom agree; project immediately.
- Settling assignment: the actor is the city's current leader but kingdom
  references do not yet agree; enqueue one coalesced runtime repair keyed by
  actor and city.

The deferred repair will resolve actor and city by ID, revalidate that the
actor is still the city's leader, and use the city's current kingdom as the
authority. It will not scan actors or cities, will not execute per frame, and
will not duplicate a committed appointment. A still-invalid assignment is
dropped with a diagnostic containing actor, city, actor-kingdom, and
city-kingdom IDs instead of the misleading generic projection warning.

## Failure Handling

Family-tree SQL errors continue through the bounded UI retry state and end in
the existing unavailable state. Governor repair uses the existing coalesced
deferred queue and runs only on the authoritative host; multiplayer replica
application remains excluded by the current Harmony gate.

## Verification

- Run `Tests/LineageBulkQuery.Integration.Tests` against the bundled SQLite
  assembly.
- Add pure rule tests for immediate, deferred, obsolete, and coalescing-key
  decisions before production changes.
- Run the focused court/source guards and Debug/Release builds.
- Deploy only the affected production files for a live-log check; success
  requires no `circular reference: relatives` and no false city-governor
  projection warning during normal leader assignment.

# Canonical Hierarchy Color Assignment Design

## Goal

Move hierarchy color selection out of local raster rendering and into a pure,
deterministic canonical assignment rule. Separate chunks and raster contexts
must consume the same owner-to-RGBA assignment and emit bit-identical colors.

## Public Model

`HierarchyColorIdentity` carries the stable identity facts needed to generate
bounded HSV candidates: displayed owner, root/system/realm/city identities,
tier, and root RGBA. Its comparison key contains all identity fields and tier;
RGBA is part of the canonical input but not a local rendering fact.

`HierarchyColorEdge` is an undirected canonical adjacency edge within one
tier. Construction normalizes its endpoints to
`(tier, minimumOwner, maximumOwner)`. Self-edges are ignored.

`HierarchyColorAssignment` owns copied, sorted arrays of
`(tier, displayedOwnerId)` keys and RGBA values. It exposes lookup without
returning mutable collections. The builder does not retain identity or edge
input references. It also exposes `IsValid` and a stable `FailureReason`, so a
valid empty-world assignment is distinct from a failed build.

## Assignment Rule

`BuildCanonicalColorAssignment(identities, edges)` performs these steps:

1. Copy and sort identities by their complete stable comparison key.
2. Deterministically collapse exact duplicates. Conflicting duplicate records
   for the same displayed owner return an invalid assignment.
3. Copy, normalize, sort, and deduplicate canonical edges. Ignore self-edges
   and edges whose tier/endpoints are absent from the identity set.
4. Visit owners in stable identity order. For each owner, select the first of
   32 bounded HSV candidates that differs from every already assigned adjacent
   owner. If all candidates collide, return an invalid assignment.
5. Return an immutable assignment snapshot with deterministic owner ordering.

The known owners `234` and `249` share candidate zero and therefore exercise
real candidate advancement when their canonical edge is present.

## Fill Integration

The authoritative `BuildFill` overload accepts a
`HierarchyColorAssignment`. It groups and triangulates the local raster as
before, but only looks up `(tier, owner)` colors in the supplied assignment. Missing
owner color or an invalid assignment returns one bounded fill failure and no
geometry. The authoritative path never derives colors from local adjacency,
chunk bounds, raster extent, or build order.

The existing convenience overload remains for compatibility and isolated
tests. It may produce identity-only candidate-zero colors, but it is explicitly
non-authoritative and does not perform adjacency-aware recoloring.

## Verification

Focused tests cover:

- copied and sorted input, reversed input order, duplicate identities, and
  normalized duplicate edges;
- the real `234`/`249` candidate-zero collision and canonical advancement;
- one global assignment consumed by two independent raster contexts, where
  one contains the `234`/`249` boundary and the other contains only owner
  `249` on a distant side;
- bit identity across chunk bounds and build order;
- country and city layers, including different roots;
- invalid conflicting identities, exhausted/missing owner assignments, and
  authoritative bounded failure.

Final verification runs the focused hierarchical boundary slice, the complete
Rules suite, and `git diff --check`. Mod DLL compilation and deployment remain
out of scope.

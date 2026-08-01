# Boundary Mesh Quality Hardening Design

## Scope

Harden Task 5 geometry and API boundaries without adding runtime dependencies.
All failures remain deterministic and bounded; mod compilation and deployment
remain out of scope.

## Ribbon Geometry And Inputs

`BuildRibbons` validates each input independently. Null entries, null/empty raw
points, invalid/non-finite curves, closed curves/raw chains, unsupported tiers,
and point-count budget violations increment `FailureCount` and skip only that
input. `BoundaryRibbonInput` retains invalid values so this boundary owns the
failure contract.

Segment footprint safety enumerates every raster cell in each triangle AABB.
Scratch-buffer triangle/cell clipping determines positive intersection area.
A forbidden water, invalid, or third-owner cell rejects the footprint when it
has positive covered area or a triangle segment crosses the cell interior.
Zero-area contact only along a cell boundary is allowed. A corner sliver cannot
hide between fixed samples.

When an accepted curve fails, the renderer constructs an effective raw
`BoundaryCurveDraft` from copied canonical raw points. Its endpoint tangents
come from the first and last non-zero raw segments. Resampling, width safety,
segment constraints, normals, and emitted vertices all use that effective raw
curve; accepted tangents never leak into fallback output.

## Polygon Validation And Budgets

Pair geometry uses triangle AABB broadphase, one pair-validation result, and
fixed scratch arrays reused across comparisons. It performs at most 1,000,000
triangle comparisons. Trace work is capped at 262,144 cells and 1,048,576
edges. Ear clipping accepts at most 8,192 vertices and 4,194,304 iterations.
Exceeding a budget returns deterministic invalid/fallback output.

Raw or accepted contours are scanned for finite coordinates before distance or
geometry work. A non-finite accepted contour may fall back only to a finite raw
contour. A non-finite raw contour returns a bounded
`BoundaryVisualPairDraft` with `IsValid == false` and a stable
`FailureReason`; no emitted/shared contour contains non-finite coordinates.

## Fill API Boundary

The public runtime entry point is named `BuildFillAuthoritative` and requires a
`HierarchyColorAssignment`. The compatibility entry point is internal and
named `BuildFillNonAuthoritativeForTests`. No public `BuildFill` overload
remains. A source guard enforces these names and, when the Task 8 worker exists,
rejects worker calls to the non-authoritative method.

## Verification

Tests first reproduce corner slivers, raw/accepted tangent divergence, invalid
ribbon entries, non-finite visual contours, and work-budget limits. Final
verification runs the focused boundary slice, full Rules suite, and diff
checks. The two existing documentation API-name corrections are preserved.

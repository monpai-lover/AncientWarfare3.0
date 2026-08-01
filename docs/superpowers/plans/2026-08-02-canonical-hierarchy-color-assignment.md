# Canonical Hierarchy Color Assignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a pure canonical hierarchy-color assignment and make authoritative fill rendering consume it across independent raster contexts.

**Architecture:** A new color rules module owns immutable identity, edge, and assignment snapshots plus deterministic HSV candidate selection. Mesh fill keeps geometry ownership, accepts the assignment through a new authoritative overload, and returns bounded failure when a displayed owner is absent.

**Tech Stack:** C#, .NET 9 Rules test executable, existing boundary policy models.

---

### Task 1: Pure Canonical Assignment

**Files:**
- Create: `Code/core/policy/HierarchicalVassalBoundaryColorRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryColorRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing assignment tests**

Add tests that build identities for city owners `234` and `249` under one
root, provide their canonical city edge in reversed and duplicated forms, and
assert candidate-zero collision plus distinct assigned colors. Rebuild from
reversed identity/edge arrays and assert identical `(tier, owner) -> RGBA`
results. Mutate the source arrays after the call and assert the assignment is
unchanged. Add a valid empty assignment and conflicting duplicate identity,
checking `IsValid` and `FailureReason` distinguish them.

- [ ] **Step 2: Run focused slice and capture RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --hierarchical-boundary-mesh-slice
```

Expected: compile failure because `HierarchyColorIdentity`,
`HierarchyColorEdge`, `HierarchyColorAssignment`, and
`BuildCanonicalAssignment` do not exist.

- [ ] **Step 3: Implement immutable models and deterministic assignment**

Implement value types whose ordering includes tier, displayed owner, root,
system, realm, city, and root RGBA. Normalize edges by tier and endpoint.
Copy/sort/deduplicate all input. Reject conflicting duplicate `(tier, owner)`
records with a stable reason. Greedily choose the first of 32 bounded HSV
candidates that differs from already assigned canonical neighbors. Store
copied sorted key/color arrays and expose:

```csharp
public bool IsValid { get; }
public string FailureReason { get; }
public int Count { get; }
public bool TryGetColor(BoundaryTier tier, long ownerId, out uint rgba);
```

- [ ] **Step 4: Run focused slice and confirm GREEN**

Run the command from Step 2. Expected: `Hierarchical vassal boundary mesh rules passed.`

### Task 2: Authoritative Fill Integration

**Files:**
- Modify: `Code/core/policy/HierarchicalVassalBoundaryMeshDraftRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryMeshDraftRulesTests.cs.txt`

- [ ] **Step 1: Write failing authoritative-fill tests**

Create one global assignment containing two roots, country identities, and
city identities including adjacent `234/249`. Build city fills from two
independent rasters: one contains the `234/249` boundary and the second contains
only owner `249` in its far-side context. Pass the same assignment to both and
assert owner `249` is bit-identical. Reorder builds and change chunk bounds,
then assert all colors remain identical. Assert country and city keys with the
same numeric owner remain distinct and different roots are stable. Pass an
assignment missing a visible owner and assert `FailureCount == 1`, zero
vertices, and no silent fallback.

- [ ] **Step 2: Run focused slice and capture RED**

Run the focused command. Expected: compile failure because authoritative
`BuildFill(raster, layer, bounds, assignment)` does not exist.

- [ ] **Step 3: Implement authoritative overload**

Add:

```csharp
public static BoundaryMeshDraft BuildFill(
    BoundaryCellRaster raster,
    BoundaryDisplayLayer layer,
    BoundaryChunkBounds bounds,
    HierarchyColorAssignment assignment)
```

Validate `assignment.IsValid`, look up every displayed `(tier, owner)` before
triangulation, and return `EmptyFailureDraft(1)` on a missing key. Remove the
raster-global adjacency recoloring helper from the authoritative path. Keep
the existing overload as a compatibility convenience that uses only stable
identity candidate-zero colors and document it as non-authoritative.

- [ ] **Step 4: Run focused slice and confirm GREEN**

Run the focused command. Expected: boundary mesh rules pass.

### Task 3: Verification And Follow-up Commit

**Files:**
- Verify all files from Tasks 1 and 2

- [ ] **Step 1: Run focused verification**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --hierarchical-boundary-mesh-slice
```

Expected: pass.

- [ ] **Step 2: Run full Rules verification**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
```

Expected: `Rule tests passed.`

- [ ] **Step 3: Check and selectively stage the diff**

```powershell
git diff --check
git add -- Code/core/policy/HierarchicalVassalBoundaryColorRules.cs Code/core/policy/HierarchicalVassalBoundaryMeshDraftRules.cs Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryColorRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryMeshDraftRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git diff --cached --check
```

Expected: no whitespace errors and only scoped files staged.

- [ ] **Step 4: Commit**

```powershell
git commit -m "fix: canonicalize hierarchy color assignments"
```

Do not compile the mod DLL or deploy.

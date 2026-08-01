# Hierarchical Vassal Natural Boundary Mesh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace hierarchical-vassal map mode zone drawing and per-edge `LineRenderer` objects with chunked fill and natural-boundary meshes that update incrementally, follow rivers between different owners, and never fill across water.

**Architecture:** Main-thread hooks coalesce dirty 32x32 chunks and capture immutable cell facts with a two-tile halo. A latest-wins background worker extracts protected topology, river corridors, safe natural curves, consolidated owner polygons, and pure mesh drafts; the main thread uploads pooled country/city fill and two-sided transition-boundary meshes and binds a custom shader bundle with a built-in fallback. Tiles are topology input only: there is no per-tile renderer and no local update replaces the whole map.

**Tech Stack:** C# 11/net48 mod runtime, .NET 9 standalone rule tests, Harmony, Unity `MeshFilter`/`MeshRenderer`, Unity ShaderLab and AssetBundle build pipeline, PowerShell source guards.

---

## File Structure

**Pure geometry and scheduling rules**

- Create `Code/core/policy/HierarchicalVassalBoundaryModels.cs`: immutable cells, chunk keys, edges, chains, curve samples, mesh drafts, revisions, and enums without Unity/WorldBox references.
- Create `Code/core/policy/HierarchicalVassalBoundaryChunkRules.cs`: floor division, halo bounds, dirty-neighbor expansion, capture/upload budgets, and stale-generation acceptance.
- Create `Code/core/policy/HierarchicalVassalBoundaryTopologyRules.cs`: cell-side comparison, boundary-tier classification, graph construction, protected endpoints, loops, and deterministic seam ownership.
- Create `Code/core/policy/HierarchicalVassalBoundaryRiverRules.cs`: conservative river-component classification, bank pairing, and different-owner river-border emission.
- Create `Code/core/policy/HierarchicalVassalBoundaryCurveRules.cs`: bounded simplification, constrained curve sampling, forbidden-cell checks, and raw-chain fallback.
- Create `Code/core/policy/HierarchicalVassalBoundaryPolygonRules.cs`: ring/hole grouping, chunk-interior clipping, deterministic triangulation, and less-smoothed/raw-contour fallback.
- Create `Code/core/policy/HierarchicalVassalBoundaryHeightRules.cs`: pack native 0-255 height samples and provide deterministic central-difference normals/light factors for tests and shader fallback.
- Create `Code/core/policy/HierarchicalVassalBoundaryMeshDraftRules.cs`: consolidated polygon fill and tiered two-sided transition-ribbon vertices/indices using primitive arrays.

**Runtime pipeline**

- Create `Code/core/policy/HierarchicalVassalBoundaryDirtyTracker.cs`: coalesced chunk revisions and bounded round-robin audit state.
- Create `Code/core/policy/HierarchicalVassalBoundarySnapshotCapture.cs`: main-thread WorldBox-to-immutable snapshot capture.
- Create `Code/core/policy/HierarchicalVassalBoundaryTopologyWorker.cs`: one background worker, latest-wins generation, completion queue, cancellation, and bounded diagnostics.
- Create `Code/core/policy/HierarchicalVassalBoundaryMeshLayer.cs`: pooled Unity fill/boundary objects and bounded main-thread upload.
- Create `Code/core/policy/HierarchicalVassalBoundaryMaterialLibrary.cs`: AssetBundle loading, shared materials, camera-scale properties, and `Sprites/Default` fallback.
- Modify `Code/core/policy/HierarchicalVassalMapModeService.cs`: revision ownership, dirty routing, new draw suppression, and removal of the every-15-frame world hash.
- Replace `Code/core/policy/HierarchicalVassalMapModeBoundaryLayer.cs`: compatibility facade delegating lifecycle/minimap calls to the mesh layer; remove all `LineRenderer` state.
- Modify `Code/core/policy/HierarchicalVassalMapModeSnapshot.cs`: expose primitive displayed-owner/color lookup required by capture without duplicating land-tile geometry.
- Modify `Code/core/policy/AWMapModeMetaLibrary.cs`: do not invoke legacy `draw_zones` while mesh rendering is authoritative.
- Modify `Code/core/lineage/VassalService.cs`: mark affected hierarchy chunks after successful `SetVassal` and `EndVassal`.
- Create `Code/patch/AW_HierarchicalVassalBoundaryDirtyPatch.cs`: territory and terrain hooks.
- Modify `Code/patch/AW_HierarchicalVassalMapLabelPatch.cs`: process capture, completions, mesh upload, labels, and world reset in a defined order.
- Modify `Code/patch/AW_HierarchicalVassalMapMinimapPatch.cs`: leave fill roots active while hiding boundary and label roots.

**Shader bundle**

- Create `Tools/HierarchicalVassalBoundaryShader/ProjectSettings/ProjectVersion.txt` using the supported WorldBox Unity editor version.
- Create `Tools/HierarchicalVassalBoundaryShader/Assets/Shaders/AW3HierarchicalVassalFill.shader`.
- Create `Tools/HierarchicalVassalBoundaryShader/Assets/Shaders/AW3HierarchicalVassalBoundary.shader`.
- Create `Tools/HierarchicalVassalBoundaryShader/Assets/Editor/AW3BoundaryBundleBuilder.cs`.
- Generate `GameResources/assetbundles/aw3_hierarchical_vassal_boundary` with the Unity batch builder; do not hand-edit the generated bundle.

**Tests**

- Create eight focused test files under `Tests/AncientWarfare3.Rules.Tests/` matching the eight pure rules files.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt` and `AncientWarfare3.Rules.Tests.csproj` to add `--hierarchical-boundary-mesh-slice`.
- Create `Tests/HierarchicalVassalBoundaryMeshSourceGuard.ps1`.
- Create `Tests/HierarchicalVassalBoundaryMeshPerformanceGuard.ps1`.

### Task 1: Primitive Models And Chunk Revisions

**Files:**
- Create: `Code/core/policy/HierarchicalVassalBoundaryModels.cs`
- Create: `Code/core/policy/HierarchicalVassalBoundaryChunkRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryChunkRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Add the focused test entry and failing tests**

Define tests for negative-coordinate floor division, 32-tile chunk bounds, two-tile halo bounds, 3x3 dirty expansion, world-edge clipping, and revision acceptance:

```csharp
Equal(new BoundaryChunkKey(0, 0),
    HierarchicalVassalBoundaryChunkRules.ForTile(31, 31));
Equal(new BoundaryChunkKey(1, 1),
    HierarchicalVassalBoundaryChunkRules.ForTile(32, 32));
Equal(new BoundaryChunkBounds(30, 30, 66, 66, 32, 32, 64, 64),
    HierarchicalVassalBoundaryChunkRules.CaptureBounds(
        new BoundaryChunkKey(1, 1), 128, 128));
Equal(9, HierarchicalVassalBoundaryChunkRules.DirtyNeighborhood(
    new BoundaryChunkKey(2, 2), 8, 8).Count);
Equal(true, HierarchicalVassalBoundaryChunkRules.AcceptResult(
    resultWorldGeneration: 4, currentWorldGeneration: 4,
    resultRevision: 9, currentRevision: 9,
    resultLayer: BoundaryDisplayLayer.Countries,
    currentLayer: BoundaryDisplayLayer.Countries));
Equal(false, HierarchicalVassalBoundaryChunkRules.AcceptResult(
    4, 5, 9, 9, BoundaryDisplayLayer.Countries,
    BoundaryDisplayLayer.Countries));
```

Register `--hierarchical-boundary-mesh-slice` to run all boundary test classes added by this plan.

- [ ] **Step 2: Run RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --hierarchical-boundary-mesh-slice
```

Expected: compilation fails because the boundary models and chunk rules do not exist.

- [ ] **Step 3: Implement immutable models and chunk rules**

Use value types with deterministic equality:

```csharp
public enum BoundaryDisplayLayer { Countries = 0, Cities = 1 }
public enum BoundaryTier { None = 0, City = 1, VassalRealm = 2, SuzerainSystem = 3 }
public enum BoundaryWaterKind { Land = 0, InlandWater = 1, Ocean = 2, Lava = 3 }

public readonly struct BoundaryChunkKey : IEquatable<BoundaryChunkKey>
{
    public BoundaryChunkKey(int x, int y) { X = x; Y = y; }
    public int X { get; }
    public int Y { get; }
    public bool Equals(BoundaryChunkKey other) => X == other.X && Y == other.Y;
    public override bool Equals(object value) => value is BoundaryChunkKey other && Equals(other);
    public override int GetHashCode() => unchecked(X * 397 ^ Y);
}

public readonly struct BoundaryCellFacts
{
    public BoundaryCellFacts(int x, int y, bool isValid, BoundaryWaterKind water,
        byte height, long systemId, long realmId, long cityId, uint rgba)
    {
        X = x; Y = y; IsValid = isValid; Water = water; Height = height;
        SystemId = systemId;
        RealmId = realmId; CityId = cityId; Rgba = rgba;
    }
    public int X { get; }
    public int Y { get; }
    public bool IsValid { get; }
    public BoundaryWaterKind Water { get; }
    public byte Height { get; }
    public long SystemId { get; }
    public long RealmId { get; }
    public long CityId { get; }
    public uint Rgba { get; }
    public bool IsLand => IsValid && Water == BoundaryWaterKind.Land;
}
```

Set `ChunkSize = 32`, `Halo = 2`, `CaptureBudgetPerFrame = 2`, and `UploadBudgetPerFrame = 2`. `DirtyNeighborhood` returns the changed chunk and all valid immediate neighbors in key order.

- [ ] **Step 4: Run GREEN and full rules suite**

Run the focused command and then the rules project without arguments. Expected: both exit 0.

- [ ] **Step 5: Commit the task files selectively**

```powershell
git add -- Code/core/policy/HierarchicalVassalBoundaryModels.cs Code/core/policy/HierarchicalVassalBoundaryChunkRules.cs Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryChunkRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "test: define hierarchical boundary chunk contracts"
```

### Task 2: Raw Edge Graph And Boundary Tiers

**Files:**
- Create: `Code/core/policy/HierarchicalVassalBoundaryTopologyRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryTopologyRulesTests.cs.txt`

- [ ] **Step 1: Write failing topology tests**

Build small rasters with `BoundaryChunkTestGrid` and assert:

```csharp
Equal(BoundaryTier.SuzerainSystem,
    HierarchicalVassalBoundaryTopologyRules.Classify(leftA, rightB,
        BoundaryDisplayLayer.Countries));
Equal(BoundaryTier.VassalRealm,
    HierarchicalVassalBoundaryTopologyRules.Classify(vassalA, vassalB,
        BoundaryDisplayLayer.Countries));
Equal(BoundaryTier.None,
    HierarchicalVassalBoundaryTopologyRules.Classify(cityA, cityB,
        BoundaryDisplayLayer.Countries));
Equal(BoundaryTier.City,
    HierarchicalVassalBoundaryTopologyRules.Classify(cityA, cityB,
        BoundaryDisplayLayer.Cities));
Equal(8, TopologyOfRectangle(2, 2).RawEdges.Count);
Equal(1, TopologyOfRectangle(2, 2).ClosedChains.Count);
Equal(true, TopologyOfThreeWayJunction().ProtectedVertices.Contains(
    new BoundaryGridPoint(1, 1)));
```

Add seam tests proving only the lexicographically lower chunk owns an edge that lies exactly on a chunk boundary.

- [ ] **Step 2: Run RED**

Run the focused slice. Expected: missing topology rule type.

- [ ] **Step 3: Implement deterministic edge extraction**

For each land cell, inspect north/east/south/west. Emit an oriented integer-grid edge when `Classify` returns a tier. Canonicalize edge endpoints so shared edges compare equal, retain the higher boundary tier, and retain both side owner IDs for color selection. Build an adjacency dictionary keyed by `BoundaryGridPoint`; graph degree other than two, tier changes, river transitions, and chunk seam points are protected anchors. Traverse unvisited edges in stable key order into open chains and closed loops.

- [ ] **Step 4: Run GREEN and commit**

Expected: rectangle, concavity, hole, junction, and seam tests pass.

### Task 3: Conservative River Corridors

**Files:**
- Create: `Code/core/policy/HierarchicalVassalBoundaryRiverRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryRiverRulesTests.cs.txt`

- [ ] **Step 1: Write failing river tests**

Cover an elongated one/two-cell-wide inland component, a broad lake, ocean,
different-owner banks, same-owner banks, a fork, and an ambiguous component:

```csharp
Equal(true, HierarchicalVassalBoundaryRiverRules.IsRiverCandidate(
    liquidCells: 12, maximumLocalWidth: 2, boundingWidth: 2,
    boundingHeight: 8, touchesOcean: false));
Equal(false, HierarchicalVassalBoundaryRiverRules.IsRiverCandidate(
    16, 4, 4, 4, false));
Equal(false, HierarchicalVassalBoundaryRiverRules.ShouldEmitPoliticalRiver(
    leftRealmId: 7, rightRealmId: 7));
Equal(true, HierarchicalVassalBoundaryRiverRules.ShouldEmitPoliticalRiver(
    leftRealmId: 7, rightRealmId: 9));
```

Assert that different-owner banks produce one center chain and suppress their two duplicate shoreline political chains.

- [ ] **Step 2: Run RED**

Expected: missing river rule type.

- [ ] **Step 3: Implement conservative component classification and bank pairing**

Flood-fill connected non-ocean liquid cells. A component is eligible only when it does not touch ocean, maximum local width is at most two cells, and its longest bounding dimension is at least twice its shortest. Trace its ordered skeleton by choosing endpoints/forks from liquid adjacency. Sample the nearest land owner on each normal side. Emit a river political chain only for stable, different, non-negative displayed owners; otherwise retain ordinary shore/hole topology.

- [ ] **Step 4: Run GREEN and commit**

Expected: river tests pass without changing lake/ocean loops.

### Task 4: Natural Curves With Topology Safety

**Files:**
- Create: `Code/core/policy/HierarchicalVassalBoundaryCurveRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryCurveRulesTests.cs.txt`

- [ ] **Step 1: Write failing curve tests**

Test bounded Douglas-Peucker simplification, protected-point retention,
adaptive Catmull-Rom sampling, and rejection against forbidden cells:

```csharp
SequenceEqual(new[] { p0, junction, p4 },
    HierarchicalVassalBoundaryCurveRules.Simplify(
        new[] { p0, p1, junction, p3, p4 },
        protectedPoints: new HashSet<BoundaryGridPoint> { junction },
        tolerance: 0.45f));
Equal(false, HierarchicalVassalBoundaryCurveRules.IsSafeSegment(
    waterCrossingSegment, ownershipRaster, leftOwner: 1,
    rightOwner: 2));
Equal(true, HierarchicalVassalBoundaryCurveRules.IsSafeSegment(
    riverCenterSegment, riverRaster, leftOwner: 1,
    rightOwner: 2));
Equal(true, HierarchicalVassalBoundaryCurveRules.IsSafeSegment(
    segmentShiftedIntoRightOwnerBy045, ownershipRaster,
    leftOwner: 1, rightOwner: 2));
Equal(false, HierarchicalVassalBoundaryCurveRules.IsSafeSegment(
    segmentShiftedIntoRightOwnerBy046, ownershipRaster,
    leftOwner: 1, rightOwner: 2));
```

Include narrow-isthmus and third-owner tests where the output must fall back to the raw chain.

Add table-driven cases for one/two-cell land bridges, straits, fjords, hooked bays, acute peninsulas, one-cell islands, small island chains, enclaves, diagonal-only contacts, T junctions, four-owner point contacts, zero-length/repeated points, two-edge chains, sharp turns, near reversals, minimal closed loops, map corners, and high world coordinates. Add seam cases crossing a chunk edge/corner and loops spanning two/four chunks; neighboring drafts must produce bit-identical seam position, tangent, and accepted smoothing strength regardless of rebuild order.

- [ ] **Step 2: Run RED**

Expected: missing curve rule type.

- [ ] **Step 3: Implement simplify, fit, validate, and fallback**

Use a maximum visual centerline deviation of `0.45f` tile units for ordinary boundaries and `0.15f-0.25f` near protected anchors and narrow topology. A candidate may enter either owner forming the shared edge, but not water, invalid cells, or a third owner. Never remove first/last, graph degree other than two, tier transition, river endpoint/fork, narrow-passage anchor, small-island anchor, or chunk seam anchor. Generate centripetal Catmull-Rom samples with maximum spacing `0.35f` tiles. Validate each segment with a supercover grid walk plus midpoint samples. Reject non-finite coordinates, duplicate-only chains, self-intersections, loop winding changes, and any candidate that changes diagonal point-contact topology. Reduce tangent scale through `1.0`, `0.5`, `0.25`, then return the raw chain when no safe candidate remains. Derive seam tangents only from the canonical halo chain so independently rebuilt neighbors produce identical derivatives.

- [ ] **Step 4: Run GREEN and commit**

Expected: safe curves pass and unsafe curves deterministically fall back.

### Task 5: Consolidated Polygon Fill And Two-Sided Boundary Drafts

**Files:**
- Create: `Code/core/policy/HierarchicalVassalBoundaryPolygonRules.cs`
- Create: `Code/core/policy/HierarchicalVassalBoundaryMeshDraftRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryPolygonRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryMeshDraftRulesTests.cs.txt`

- [ ] **Step 1: Write failing polygon and mesh-draft tests**

Assert rectangle, concave region, island, multiple-hole, chunk-clipped, and raw-contour fallback triangulation. Verify triangle centroids and supercover samples stay on valid land owned by the region or its paired boundary neighbor, remain within 0.45 tile of their shared raw edge, and never cover water/lava/third owners. Require the two visual polygons to share one contour with no overlap or gap and conserve their combined visible land area. Require substantial vertex reduction for a coherent 32x32 owner block and verify two-sided ribbon attributes:

```csharp
BoundaryPolygonDraft polygon =
    HierarchicalVassalBoundaryPolygonRules.BuildOwnerPolygon(grid, ownerId: 7);
Equal(2, polygon.Holes.Count);
Equal(true, polygon.Triangles.All(t =>
    grid.TriangleCoversOnlyAllowedVisualCorridor(t, ownerId: 7,
        maximumDeviation: 0.45f)));

BoundaryMeshDraft coherent =
    HierarchicalVassalBoundaryMeshDraftRules.BuildFill(grid.WithOwnerBlock(32, 32, 7));
Equal(true, coherent.Positions.Count < 128);
Equal(true, coherent.Positions.Count < grid.OwnedCellCount(7));

BoundaryMeshDraft borders =
    HierarchicalVassalBoundaryMeshDraftRules.BuildRibbons(chains);
Equal(true, borders.Indices(BoundaryTier.SuzerainSystem).Count > 0);
Equal(true, borders.Indices(BoundaryTier.VassalRealm).Count > 0);
Equal(true, borders.Indices(BoundaryTier.City).Count > 0);
Equal(leftOwnerColor, borders.LeftColorAt(0));
Equal(rightOwnerColor, borders.RightColorAt(0));
```

Assert identical seam endpoint coordinates, tangent constraints, and local half-widths for two neighboring chunk drafts. Add footprint tests where the curve centerline is valid but a fixed-width ribbon would enter water or a third owner; covering either owner in the defining pair is valid within the 0.45-tile corridor. Require local half-width reduction or raw-contour fallback outside that corridor. Add river tests proving the center line may follow river water while both political-color halves remain transparent over water. Assert realm/city colors are deterministic variations of their root suzerain system color, remain stable across rebuilds, and distinguish adjacent displayed owners without changing the root hierarchy identity.

- [ ] **Step 2: Run RED**

Expected: missing polygon and mesh-draft rule types.

- [ ] **Step 3: Implement consolidated polygon and transition-ribbon drafts**

Group accepted closed contours by displayed owner, classify winding into outer rings and holes, clip rings to the chunk interior, and triangulate each consolidated visual polygon deterministically. A shared smoothed contour is authoritative for both visual sides, so small slivers may display as the adjacent owner while logical tile facts remain unchanged. Validate output triangles against the pair corridor, maximum deviation, water/invalid/third-owner exclusion, pair-area conservation, and no overlap/gap; retry with reduced smoothing and finally the raw contour. If all attempts fail, return a bounded failure so the runtime retains the previous chunk mesh; never emit one quad per tile. Derive vassal-realm and city colors from the stable suzerain-system color plus a deterministic displayed-owner hash, with bounded value/saturation offsets and an adjacency fallback that selects the first distinguishable variant.

Boundary ribbons emit left/right vertices carrying centerline normal, signed edge distance, tier, left/right owner IDs, left/right RGBA, local half-width, and river/coast flags in primitive arrays. `ComputeSafeHalfWidth` samples the full cross-section against the immutable raster: either half may cover either owner forming the shared edge within the visual corridor, but neither may cover water, invalid cells, or a third owner. Reduce width near water, narrow passages, junctions, and third-owner corners; use the raw contour if the tier width still cannot fit. Preserve canonical seam endpoint tangents and widths so adjacent chunks produce the same ribbon. Use target tier widths `0.12f`, `0.20f`, and `0.32f` world units; shaders may apply camera-scale refinement only within the prevalidated width. Coastline ribbons mark the water-facing side transparent and retain the exact land clip. River-center ribbons keep both political-color halves transparent while over liquid and render only their central political line until reaching the banks.

- [ ] **Step 4: Run GREEN, full rules, and commit**

Expected: all pure geometry tests pass, coherent regions use consolidated polygon geometry, and no test path emits per-tile render primitives.

### Task 6: Real Height Relief Drafts

**Files:**
- Create: `Code/core/policy/HierarchicalVassalBoundaryHeightRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryHeightRulesTests.cs.txt`
- Modify: `Code/core/policy/HierarchicalVassalBoundaryModels.cs`

- [ ] **Step 1: Write failing height-rule tests**

Test native byte packing, invalid-edge clamping, flat normals, X/Y ramps, and bounded diffuse factors using the same reference math required from the shader:

```csharp
BoundaryHeightDraft flat = HierarchicalVassalBoundaryHeightRules.Pack(
    grid.WithUniformHeight(128), interiorSize: 32, halo: 2);
Equal(36, flat.Width);
Equal(36, flat.Height);
Equal((byte)128, flat.Samples[flat.Index(18, 18)]);
Near(new BoundaryFloat3(0f, 0f, 1f),
    HierarchicalVassalBoundaryHeightRules.NormalAt(flat, 18, 18, 2f), 0.0001f);

BoundaryHeightDraft ramp = HierarchicalVassalBoundaryHeightRules.Pack(
    grid.WithHeightRampX(), 32, 2);
Equal(true,
    HierarchicalVassalBoundaryHeightRules.NormalAt(ramp, 18, 18, 2f).X < 0f);
InRange(HierarchicalVassalBoundaryHeightRules.LightAt(
    ramp, 18, 18, lightDirection, 2f), 0.65f, 1.15f);
```

Add a world-edge case proving invalid halo cells copy the nearest valid edge height instead of introducing a false zero-height cliff. Add a revision test proving country and city drafts reference the same immutable `BoundaryHeightDraft` instance.

- [ ] **Step 2: Run RED**

Run the focused boundary slice. Expected: compilation fails because `HierarchicalVassalBoundaryHeightRules` and `BoundaryHeightDraft` do not exist.

- [ ] **Step 3: Implement native-height packing and reference lighting**

`BoundaryHeightDraft` owns a copied `byte[] Samples`, dimensions, chunk world origin, halo, and terrain revision. `Pack` preserves the native 0-255 value and edge-clamps invalid world cells. `NormalAt` uses normalized central differences:

```csharp
float dx = (right - left) / 255f * slopeScale;
float dy = (up - down) / 255f * slopeScale;
return Normalize(new BoundaryFloat3(-dx, -dy, 1f));
```

`LightAt` applies bounded ambient plus diffuse light and a small ridge term. Keep constants in the rules file so ShaderLab source guards can require matching `_ReliefStrength`, `_HeightTex_TexelSize`, and `_MapLightDirection` contracts. Height data remains primitive-only and has no Unity/WorldBox references.

- [ ] **Step 4: Run GREEN, full rules, and commit**

Expected: height tests, the focused boundary slice, and the full rules suite exit 0.

### Task 7: Dirty Tracker And Main-Thread Snapshot Capture

**Files:**
- Create: `Code/core/policy/HierarchicalVassalBoundaryDirtyTracker.cs`
- Create: `Code/core/policy/HierarchicalVassalBoundarySnapshotCapture.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeSnapshot.cs`
- Create: `Tests/HierarchicalVassalBoundaryMeshSourceGuard.ps1`

- [ ] **Step 1: Add a failing source guard**

Require the tracker to use `Dictionary<BoundaryChunkKey,long>` plus a queue of unique keys, require `CaptureBudgetPerFrame`, and reject `World`, `Kingdom`, `City`, `TileZone`, `WorldTile`, and `UnityEngine` references from all pure model/rule files. Require capture to create copied `BoundaryCellFacts[]`, copy `WorldTile.Height` into the native byte range, include explicit valid-cell facts at clipped world edges, and never store live world objects in the snapshot.

- [ ] **Step 2: Run RED**

```powershell
& Tests/HierarchicalVassalBoundaryMeshSourceGuard.ps1
```

Expected: missing tracker and capture service.

- [ ] **Step 3: Implement coalesced revisions and bounded capture**

`MarkTile`, `MarkZone`, and `MarkKingdom` expand through `DirtyNeighborhood`. Each key has a monotonically increasing revision; a duplicate mark updates the revision but does not add a duplicate queue node. Capture resolves the current visible hierarchy mapping and stable root/realm/city color variation on the main thread, copies a 32x32 interior plus two-tile halo including `(byte)Mathf.Clamp(tile.Height, 0, 255)`, and submits at most two snapshots per frame. Cells outside the world are copied as `IsValid == false`, never as water or unowned land. The compact terrain fingerprint includes height so missed height hooks are recoverable. A round-robin audit checks at most one chunk per simulation cycle and only marks a chunk when its compact ownership/terrain fingerprint changes.

- [ ] **Step 4: Run guard and commit**

Expected: source guard passes and pure rule suite remains green.

### Task 8: Latest-Wins Background Topology Worker

**Files:**
- Create: `Code/core/policy/HierarchicalVassalBoundaryTopologyWorker.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryWorkerRulesTests.cs.txt`
- Modify: `Tests/HierarchicalVassalBoundaryMeshSourceGuard.ps1`

- [ ] **Step 1: Write RED tests and guards**

Test generation acceptance through the pure chunk rules and require the worker to own a bounded input queue, one worker thread, and a completion queue. Reject Unity/WorldBox types and reject unbounded `Task.Run` creation.

- [ ] **Step 2: Implement the worker**

Use one background thread or the existing AW async coordinator with channel key `hierarchical_vassal_boundary`. Coalesce pending requests by `(worldGeneration, chunkKey)` and cap pending work at `worldChunkCount`; when saturated, replace the older revision for the same key and preserve a single rescan marker instead of growing the queue. One immutable chunk snapshot produces a `BoundaryChunkDraftSet` containing country geometry, city geometry, and one shared height draft. Process topology, river, curve, polygon, height, and mesh draft rules in sequence. Catch exceptions per request and enqueue either the draft set or a bounded failure record. `ResetWorld` increments generation, cancels/drains pending work and completions, and prevents late results from applying.

- [ ] **Step 3: Run focused/full tests and commit**

Expected: stale generation tests and source guards pass.

### Task 9: Shader Project And AssetBundle Loader

**Files:**
- Create: `Tools/HierarchicalVassalBoundaryShader/ProjectSettings/ProjectVersion.txt`
- Create: `Tools/HierarchicalVassalBoundaryShader/Assets/Shaders/AW3HierarchicalVassalFill.shader`
- Create: `Tools/HierarchicalVassalBoundaryShader/Assets/Shaders/AW3HierarchicalVassalBoundary.shader`
- Create: `Tools/HierarchicalVassalBoundaryShader/Assets/Editor/AW3BoundaryBundleBuilder.cs`
- Create: `Code/core/policy/HierarchicalVassalBoundaryMaterialLibrary.cs`
- Modify: `Tests/HierarchicalVassalBoundaryMeshSourceGuard.ps1`

- [ ] **Step 1: Extend source guards for bundle and fallback contracts**

Require bundle path `GameResources/assetbundles/aw3_hierarchical_vassal_boundary`, shader asset names `AW3/HierarchicalVassal/Fill` and `AW3/HierarchicalVassal/Boundary`, height properties `_HeightTex`, `_HeightTex_TexelSize`, `_HeightUvScaleOffset`, `_ReliefStrength`, and `_MapLightDirection`, a one-warning guard, `AssetBundle.Unload(false)`, and `Shader.Find("Sprites/Default")` fallback.

- [ ] **Step 2: Implement ShaderLab assets**

The fill shader consumes vertex color and `_OverlayAlpha`, uses transparent blending, and applies edge feathering without sampling outside the consolidated polygon geometry. It maps world position into the chunk `_HeightTex`, samples left/right/up/down native height, derives the same central-difference normal as `HierarchicalVassalBoundaryHeightRules`, and applies bounded ambient/diffuse/ridge lighting controlled by `_ReliefStrength` and `_MapLightDirection`. Flat height must produce neutral lighting and the shader must not displace vertices.

The boundary shader consumes left/right colors, signed edge distance in UV0, tier in UV1, `_CameraWorldPerPixel`, `_DarkOutline`, and `_EdgeSoftness`; it selects the left or right political color on the corresponding half, draws a narrow dark center line, uses the shared height light factor at reduced strength, and applies `fwidth`/`smoothstep` anti-aliasing. A coastline's water-facing half has zero alpha. Both runtime materials use explicit render queues/Z values below hierarchy labels, nameplates, click markers, and other interaction overlays.

- [ ] **Step 3: Implement deterministic Windows bundle builder**

```csharp
public static void BuildWindows()
{
    string output = "../../../../GameResources/assetbundles";
    Directory.CreateDirectory(output);
    BuildPipeline.BuildAssetBundles(output, new[] {
        new AssetBundleBuild {
            assetBundleName = "aw3_hierarchical_vassal_boundary",
            assetNames = new[] {
                "Assets/Shaders/AW3HierarchicalVassalFill.shader",
                "Assets/Shaders/AW3HierarchicalVassalBoundary.shader"
            }
        }
    }, BuildAssetBundleOptions.ChunkBasedCompression,
       BuildTarget.StandaloneWindows64);
}
```

Build with:

```powershell
& $env:AW3_UNITY_EDITOR -batchmode -quit -projectPath Tools/HierarchicalVassalBoundaryShader -executeMethod AW3BoundaryBundleBuilder.BuildWindows -logFile .runtime/boundary-shader-build.log
```

The implementation session must first set `ProjectVersion.txt` to the Unity version reported by the installed WorldBox player. If that editor is unavailable, stop this task and report the missing prerequisite; do not fabricate a bundle.

- [ ] **Step 4: Implement runtime material loading and fallback**

Resolve the bundle from `ModClass.Instance.GetDeclaration().FolderPath`, load both shaders, create one shared fill material and three boundary material variants, then unload only the bundle container with `Unload(false)`. Set stable default relief parameters without cloning materials per chunk. On any failure, create equivalent shared materials from `Sprites/Default`, disable height relief, and log once.

- [ ] **Step 5: Run source guard, inspect bundle file, and commit**

Expected: guard passes and generated bundle exists with nonzero length.

### Task 10: Pooled Main-Thread Mesh Renderer

**Files:**
- Create: `Code/core/policy/HierarchicalVassalBoundaryMeshLayer.cs`
- Replace: `Code/core/policy/HierarchicalVassalMapModeBoundaryLayer.cs`
- Modify: `Code/patch/AW_HierarchicalVassalMapMinimapPatch.cs`
- Modify: `Tests/HierarchicalVassalBoundaryMeshSourceGuard.ps1`

- [ ] **Step 1: Write a failing source guard**

Reject `LineRenderer`, `MaximumSegments`, one-GameObject-per-edge, one-quad-per-tile, and whole-map replacement names from the active path. Require `MeshFilter`, `MeshRenderer`, `Mesh.MarkDynamic`, fill and boundary roots, two-uploads-per-frame budget, mesh reuse, one reusable single-channel height texture per chunk, `MaterialPropertyBlock`, stale result rejection, camera orthographic-scale updates, bounded warning/retry state, and `SetMinimapHidden` to disable only boundary roots.

- [ ] **Step 2: Implement pooled chunk render entries**

Each `(chunkKey, layer)` entry owns two reusable GameObjects: fill and boundary. Each chunk, independent of layer, owns one reusable 36x36 single-channel height texture (`TextureFormat.R8`, with a supported single-channel fallback) and one cached `MaterialPropertyBlock`. Set `FilterMode.Bilinear`, `TextureWrapMode.Clamp`, and disable mipmaps so height gradients remain continuous without sampling neighboring atlas data. Country and city entries bind the same texture and world-to-height UV transform. Upload height bytes only when an accepted terrain revision changes, using `LoadRawTextureData` and `Apply(updateMipmaps: false, makeNoLongerReadable: false)`; never allocate a texture during an ordinary revision update.

Upload primitive arrays with `Mesh.Clear(false)`, `SetVertices`, `SetColors`, `SetUVs`, and `SetTriangles` for the fill/outer/vassal/city submeshes. Do not allocate a new Mesh on ordinary revision updates. Update the shared `_CameraWorldPerPixel` value only when orthographic scale or viewport height changes. A completed chunk becomes visible immediately; an unbuilt chunk leaves the normal map visible. Mesh or height upload exceptions retain the previous valid resources, enqueue one bounded retry, and log through a per-chunk throttled warning rather than clearing the overlay. A height-only failure binds a neutral one-pixel texture and leaves political geometry visible.

- [ ] **Step 3: Convert the old boundary layer to a compatibility facade**

Keep public/internal call sites stable (`ProcessFrame`, `Reset`, and `SetMinimapHidden`) but delegate to the new mesh layer. Delete all old edge scanning, zone rectangular edge resolution, segment cap, materials, and `LineRenderer` pools.

- [ ] **Step 4: Run source guard and commit**

Expected: no active `LineRenderer` path remains.

### Task 11: Runtime Event Wiring And Legacy Suppression

**Files:**
- Create: `Code/patch/AW_HierarchicalVassalBoundaryDirtyPatch.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`
- Modify: `Code/core/policy/AWMapModeMetaLibrary.cs`
- Modify: `Code/core/lineage/VassalService.cs`
- Modify: `Code/patch/AW_HierarchicalVassalMapLabelPatch.cs`
- Modify: `Code/patch/AW_HierarchicalVassalMapMinimapPatch.cs`
- Modify: `Tests/HierarchicalVassalBoundaryMeshSourceGuard.ps1`

- [ ] **Step 1: Extend guards and verify RED**

Require hooks for `TileZone.setCity`, `City.addZone`, `City.joinAnotherKingdom`, the `WorldTile.Height` setter, and all `WorldTile.setTileType`/`setTileTypes` overloads selected through `TargetMethods`. Require the patch to discover and hook available zone removal/destruction and kingdom creation/destruction mutation methods by exact reflected signature, while the bounded chunk audit remains the compatibility fallback for game versions where a named method is absent. Require successful `VassalService.SetVassal`/`EndVassal` to call hierarchy dirty routing. Reject `ComputeWorldRevision`, `RevisionCheckIntervalFrames`, and active `drawZoneMeta` calls for this map mode.

- [ ] **Step 2: Implement territory and terrain hooks**

Use prefix state to capture old city/kingdom/type/height and postfix logic to mark old and new zone/chunk neighborhoods only when facts changed. `WorldTile` patches call `MarkTile(__instance.x, __instance.y)`. The height-setter postfix exits immediately unless the hierarchical map renderer has an active world generation, preventing generation/load storms before activation. Zone removal/destruction marks the former bounds before references are cleared; kingdom creation/destruction marks its affected city chunks. Map-mode activation and world load mark all chunks once; hierarchy focus/layer changes mark only chunks whose displayed owner mapping changes, with all-chunks fallback when mapping comparison cannot be completed safely.

- [ ] **Step 3: Replace frame ordering and world reset**

`MapBox.Update` postfix order becomes: label/world revision events, bounded snapshot capture, drain completed drafts, bounded mesh upload, then label processing. Labels, nameplates, hierarchy click inspection, and water-restoration metadata may consume a revision only after the matching visible chunk revision has been accepted. `MapBox.clearWorld` and mod shutdown first cancel the boundary generation, then destroy pooled render roots/materials and clear label state. Minimap hides boundary/label roots and leaves fill roots enabled.

- [ ] **Step 4: Suppress legacy zone drawing**

While mesh authority is active, `AWMapModeMetaLibrary.HierarchicalVassalAsset.draw_zones` returns without calling `ZoneCalculator.drawZoneMeta`. If mesh authority cannot initialize, select one explicit legacy fallback mode for the whole map-mode session; never draw legacy and mesh overlays simultaneously.

- [ ] **Step 5: Run source guards, full rules, and commit**

Expected: all tests pass and source guard confirms one authoritative renderer.

### Task 12: Performance And Adversarial Geometry Verification

**Files:**
- Create: `Tests/HierarchicalVassalBoundaryMeshPerformanceGuard.ps1`
- Create: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryPerformanceTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add deterministic performance tests**

Generate a 512x512 synthetic world, build all chunk draft sets, then mutate one zone-sized 8x8 area and one height sample. Assert dirty expansion is at most nine chunks, unchanged chunk revisions remain accepted, worker queue count stays bounded by world chunk count, each draft set contains one height draft shared by its country/city geometry, height texture count stays bounded by world chunk count, and mesh vertex/index counts stay within calculated chunk maxima. Run the complete adversarial curve matrix from Tasks 4-5 under both country/city layers and all three tier widths. Randomize adjacent-chunk completion order and assert deterministic seam positions, tangents, widths, indices, and colors. Add failure-injection cases proving an invalid curve falls back to raw topology, a worker exception affects only one chunk, an upload retry preserves the last valid revision, and a height upload failure retains political geometry with neutral relief.

- [ ] **Step 2: Add source-level hot-path guards**

Reject full `World.world.kingdoms` traversal from `ProcessFrame`, `MapBox.Update` postfix, completion drain, and mesh upload. Reject per-tile/per-edge GameObject creation, one fill quad per source tile, per-result Mesh/Texture construction, duplicate country/city height textures, and any ordinary local-change path that clears or replaces all chunk meshes. Require capture/upload budgets and coalescing dictionaries.

- [ ] **Step 3: Run focused benchmark and full suite**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --hierarchical-boundary-performance
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
& Tests/HierarchicalVassalBoundaryMeshSourceGuard.ps1
& Tests/HierarchicalVassalBoundaryMeshPerformanceGuard.ps1
```

Expected: all commands exit 0; benchmark reports local dirty rebuilding only, coherent-region geometry remains far below tile count, draw object count remains bounded by chunk/layer count rather than tile count, and the measured capture-to-visible latency for a sparse edit remains within one simulation cycle or approximately 0.5 seconds under the synthetic budget.

- [ ] **Step 4: Commit verification assets**

Commit only the performance test and guard changes.

### Task 13: Deployment And In-Game Visual Acceptance

**Files:**
- Deploy all changed production `Code/`, `GameResources/assetbundles/`, and required shader metadata files to `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`.

- [ ] **Step 1: Review task-only diffs**

Check all new worker files for Unity/WorldBox references, inspect event hooks for duplicate dirty emissions, inspect old renderer removal, and run `git diff --check`. Preserve unrelated dirty worktree changes.

- [ ] **Step 2: Deploy source and bundle without compiling the mod DLL**

Copy only implementation files and the generated AssetBundle with relative paths. Do not compile or deploy a mod DLL unless the user separately authorizes it.

- [ ] **Step 3: Verify SHA-256 deployment equality**

Compare every deployed task file and bundle against the workspace and fail on any mismatch.

- [ ] **Step 4: Run in-game acceptance scenarios**

Verify country and city layers on a large saved world containing islands, lakes, rivers, narrow straits, enclaves, three-way junctions, nested vassals, flat plains, hills, and mountains. Capture screenshots at near, normal, and far zoom. Confirm natural curves, the two-sided political-color transition hides raster stair-steps at ordinary zoom, continuous different-owner river boundaries, same-owner rivers without political lines, no water fill, no third-owner crossing, no seam cracks or tangent kinks, correct thick/medium/thin hierarchy, real-height relief follows hills and mountains without moving political geometry, flat land remains visually neutral, and minimap behavior remains correct.

- [ ] **Step 5: Verify incremental updates and runtime stability**

Capture a city, add/remove a zone, change terrain between land and liquid, change a tile height, establish/end a vassal relation, switch country/city layers, drill into a vassal hierarchy, and clear/reload the world. Confirm affected regions and height textures update within the next simulation cycle or about 0.5 seconds, unchanged chunks do not rebuild, country/city layers share height resources, stale worker results never enter a new world, and Mesh/Texture/GameObject counts remain bounded.

- [ ] **Step 6: Selectively commit and push after user acceptance**

Use path-limited commits or a clean auxiliary index because the shared worktree contains unrelated changes. Push only after all automated and in-game acceptance checks pass.

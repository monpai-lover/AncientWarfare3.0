# Cultiway-Style City Walls And Bandit Names Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give bandit cities and Mandate border cities complete Cultiway-style walls through one shared tool, while replacing rebel and bandit realm names with stable Chinese outlaw roots and mutually exclusive `义军` / `贼` suffixes.

**Architecture:** A detached grid geometry class owns Cultiway's connected-land boundary, diagonal sealing, road/dock gates, and core-reachability rules. A WorldBox adapter converts a `City` into that grid, places original top-tile assets, and returns fixed coordinates; bandit and Mandate services remain policy owners. A separate detached naming rule and runtime word-library adapter own Chinese outlaw roots.

**Tech Stack:** C#/.NET Framework 4.8.1, .NET 9 detached rules tests, WorldBox 0.51 APIs, Newtonsoft.Json, PowerShell source guards, UTF-8 word libraries, Cultiway-Reborn MIT-licensed source adaptation.

---

## File Map

Create:

- `Code/core/lineage/CultiwayStyleWallGeometryRules.cs`: WorldBox-independent wall grid and Cultiway geometry.
- `Code/core/lineage/CultiwayStyleCityWallService.cs`: WorldBox `City` adapter and original wall placement boundary.
- `Code/core/lineage/PeasantRebelOutlawNameRules.cs`: deterministic root selection, validation, suffix stripping, and composition.
- `Code/core/lineage/PeasantRebelOutlawNameService.cs`: runtime access to the integrated word library and persisted-root repair.
- `word_libraries/default/土匪名根.txt`: dedicated UTF-8 root list.
- `THIRD_PARTY_NOTICES/Cultiway-Wall-MIT.txt`: packaged MIT notice for the adapted wall source.
- `Tests/AncientWarfare3.Rules.Tests/CultiwayStyleWallGeometryRulesTests.cs.txt`: detached geometry coverage.
- `Tests/AncientWarfare3.Rules.Tests/PeasantRebelOutlawNameRulesTests.cs.txt`: detached naming coverage.

Modify:

- `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`: link new production rules and tests.
- `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`: add a focused test slice.
- `Tests/PeasantRebelRouteRuntimeSourceGuard.ps1`: enforce shared-tool ownership and remove obsolete wall assumptions.
- `Code/core/lineage/PeasantRebelBanditWallService.cs`: delegate geometry and placement to the shared tool.
- `Code/core/lineage/PeasantRebelBanditRoute.cs`: keep the bandit entry call and require successful wall capture.
- `Code/core/lineage/MandateBorderDefenseService.cs`: delegate complete stone rings to the shared tool.
- `Code/core/lineage/MandateBorderWallRules.cs`: retain only wall asset and external-border rules still used outside wall construction.
- `Code/core/lineage/PeasantRebelRouteRules.cs`: delegate route-name composition to outlaw naming rules.
- `Code/core/lineage/PeasantRebelRouteService.cs`: create or repair a persisted outlaw root before route names.
- `Code/core/lineage/PeasantRebelGovernmentTransitionService.cs`: preflight a valid outlaw root before bandit mutations.
- `THIRD_PARTY_NOTICES.md`: document the Cultiway wall-source adaptation.

## Task 1: Establish The Detached Wall Test Slice

**Files:**

- Create: `Tests/AncientWarfare3.Rules.Tests/CultiwayStyleWallGeometryRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Register the focused test slice before production types exist**

Add these compile items beside `PeasantRebelRouteRulesTests.cs.txt`:

```xml
<Compile Include="CultiwayStyleWallGeometryRulesTests.cs.txt" />
<Compile Include="..\..\Code\core\lineage\CultiwayStyleWallGeometryRules.cs"
         Link="Production\CultiwayStyleWallGeometryRules.cs" />
```

Add before the full-suite calls in `Program.cs.txt`:

```csharp
if (args.Length == 1 && args[0] == "--cultiway-wall")
{
    CultiwayStyleWallGeometryRulesTests.Run();
    Console.WriteLine("Cultiway wall geometry rules passed.");
    return;
}
```

- [ ] **Step 2: Add failing geometry tests**

Create a test class whose `Run` method constructs small grids through this
helper:

```csharp
private static CultiwayWallGeometryInput Input(int width, int height,
    CultiwayWallPoint center, CultiwayWallBounds bounds,
    IEnumerable<CultiwayWallPoint> cityLand,
    IEnumerable<CultiwayWallPoint> passable,
    IEnumerable<CultiwayWallPoint> roads = null,
    IEnumerable<CultiwayWallPoint> docks = null,
    int wallWidth = 1, bool gates = true)
{
    return new CultiwayWallGeometryInput(width, height, center, bounds,
        cityLand, passable, roads ?? Array.Empty<CultiwayWallPoint>(),
        docks ?? Array.Empty<CultiwayWallPoint>(), wallWidth, gates);
}
```

The tests must assert all of the following with exact point membership:

```csharp
ClosedBoundaryExcludesDetachedLand();
SecondLayerPeelsInwardWithoutBreakingClosure();
DiagonalBoundaryReceivesFourWayBridge();
RoadGateWinsOverDirectionalFallback();
DockWithinEightTilesCarvesThreeTilePassage();
CoreReachabilityCarvesMinimumThreeTilePassage();
```

Use a 9x9 square land component centered at `(4,4)` for the base closed-ring
assertion, plus a detached `(8,8)` point that must not appear. Verify the ring
contains all four corners before gates, and verify every remaining diagonal
wall pair has at least one orthogonal bridge.

- [ ] **Step 3: Run RED**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
dotnet run --project `
  'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' `
  -- --cultiway-wall
```

Expected: compilation fails because `CultiwayStyleWallGeometryRules.cs` does
not exist.

- [ ] **Step 4: Commit the red tests**

```powershell
git add Tests/AncientWarfare3.Rules.Tests
git commit -m "test: specify Cultiway walls and outlaw names"
```

## Task 2: Implement Detached Cultiway Wall Geometry

**Files:**

- Create: `Code/core/lineage/CultiwayStyleWallGeometryRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CultiwayStyleWallGeometryRulesTests.cs.txt`

- [ ] **Step 1: Add immutable grid models**

Define `CultiwayWallPoint` with value equality and stable `GetHashCode`,
`CultiwayWallBounds`, and `CultiwayWallGeometryInput`. The input constructor
copies all collections into `HashSet<CultiwayWallPoint>` and rejects nonpositive
map dimensions or wall width:

```csharp
public readonly struct CultiwayWallPoint : IEquatable<CultiwayWallPoint>
{
    public CultiwayWallPoint(int x, int y) { X = x; Y = y; }
    public int X { get; }
    public int Y { get; }
    public bool Equals(CultiwayWallPoint other) => X == other.X && Y == other.Y;
    public override bool Equals(object value) =>
        value is CultiwayWallPoint other && Equals(other);
    public override int GetHashCode() => unchecked(X * 397 ^ Y);
}

public readonly struct CultiwayWallBounds
{
    public CultiwayWallBounds(int cx, int cy, int hx, int hy)
    { CenterX = cx; CenterY = cy; HalfWidth = hx; HalfHeight = hy; }
    public int CenterX { get; }
    public int CenterY { get; }
    public int HalfWidth { get; }
    public int HalfHeight { get; }
}
```

- [ ] **Step 2: Adapt connected-land and exterior flooding**

Implement `CultiwayStyleWallGeometryRules.Compute(input)` using the current
Cultiway master sequence:

```csharp
HashSet<CultiwayWallPoint> coreLand = GetCoreLand(
    input.CityLand, input.Center, input.MapWidth, input.MapHeight);
HashSet<CultiwayWallPoint> remaining = IntersectBounds(
    coreLand, input.Bounds, input.MapWidth, input.MapHeight);
HashSet<CultiwayWallPoint> exterior = FloodExterior(
    remaining, input.Bounds, input.MapWidth, input.MapHeight);
```

For each layer through `input.WallWidth`, collect points adjacent in four
directions to exterior or the clipped rectangle edge, seal diagonal gaps, add
the sealed boundary to the result, remove it from `remaining`, and add it to
`exterior`.

- [ ] **Step 3: Adapt gates and core reachability**

Implement these helpers with the same constants as Cultiway master:

```csharp
private const int ExitHalf = 1;
private const int RoadSearchRadius = 6;
private const int DockPassageDistance = 8;

private static void SealDiagonalGaps(...);
private static void CarveLandGates(...);
private static bool CarveRoadGate(...);
private static void CarveDockPassages(...);
private static void EnsureCoreReachable(...);
```

Road gates choose the point in each cardinal direction with a road within six
tiles, passable exterior, lowest lateral offset, then greatest projection.
Fallback gates use the same ordering without the road requirement. Dock
passages remove a 3x3 intersection around the nearest ring point no farther
than eight tiles from a dock tile. Core reachability uses a 0-1 BFS over core
passable land where crossing a wall costs one; when the minimum cost is
positive, remove a 3x3 intersection around every crossed wall point.

- [ ] **Step 4: Run GREEN and the existing rebel rules**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
dotnet run --project `
  'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' `
  -- --cultiway-wall
dotnet run --project `
  'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' `
  -- --peasant-rebel-routes
```

Expected: geometry tests and existing rebel-route tests pass.

- [ ] **Step 5: Commit geometry**

```powershell
git add Code/core/lineage/CultiwayStyleWallGeometryRules.cs `
  Tests/AncientWarfare3.Rules.Tests/CultiwayStyleWallGeometryRulesTests.cs.txt
git commit -m "feat: adapt Cultiway city wall geometry"
```

## Task 3: Add The Shared WorldBox City Wall Adapter And Attribution

**Files:**

- Create: `Code/core/lineage/CultiwayStyleCityWallService.cs`
- Create: `THIRD_PARTY_NOTICES/Cultiway-Wall-MIT.txt`
- Modify: `THIRD_PARTY_NOTICES.md`
- Modify: `Tests/PeasantRebelRouteRuntimeSourceGuard.ps1`

- [ ] **Step 1: Add failing source guards**

Load the shared service and both notices, then require:

```powershell
Require $sharedWall 'CultiwayStyleWallGeometryRules.Compute(' `
  'The WorldBox wall adapter must use detached Cultiway geometry.'
Require $sharedWall 'tile.setTopTileType(pWallType)' `
  'The shared tool must place the caller-selected original wall asset.'
Require $sharedWall 'building.asset.type' `
  'Remote utility filtering must follow Cultiway building bounds.'
Require $sharedWall 'building.asset.docks' `
  'Dock tiles must feed Cultiway passage carving.'
Forbid $sharedWall 'MapAction.terraformTop' `
  'The shared tool must not mutate terrain or destroy paths.'
Require $notice 'Cultiway-Reborn city-wall geometry' `
  'The adapted wall source needs an MIT notice.'
Require $packagedNotice 'Copyright (c) 2025 Inmny' `
  'The packaged wall notice must retain the Cultiway copyright.'
```

- [ ] **Step 2: Run the source guard to observe RED**

```powershell
& './Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'
```

Expected: failure because the shared wall service is absent.

- [ ] **Step 3: Implement the WorldBox adapter**

Create:

```csharp
internal sealed class CultiwayStyleCityWallResult
{
    internal CultiwayStyleCityWallResult(List<CultiwayWallPoint> points,
        int changed) { Points = points; Changed = changed; }
    internal IReadOnlyList<CultiwayWallPoint> Points { get; }
    internal int Changed { get; }
}

internal static class CultiwayStyleCityWallService
{
    private const int RadiusMin = 3;
    private const int RadiusMax = 60;
    private const int RemoteUtilityDistance = 16;
    private const int WallMargin = 3;

    internal static bool TryPlan(City pCity, int pWidth,
        bool pCarvePassages,
        out IReadOnlyList<CultiwayWallPoint> pPoints);
    internal static CultiwayStyleCityWallResult Build(City pCity,
        TopTileType pWallType, int pWidth, bool pCarvePassages);
}
```

`TryPlan` validates city, world, and width; obtains building bounds while
ignoring remote `type_windmill`, `type_mine`, and `type_crops`; collects city
land, passable land in the padded bounds, roads, blocked mountain/summit tiles,
and all tiles occupied by dock buildings. It calls the detached geometry and
sorts by X then Y. `Build` calls `TryPlan`, then revalidates
`tile.zone?.city == pCity`, non-water,
non-mountain, and non-summit, and calls `setTopTileType` only when the requested
wall type differs.

- [ ] **Step 4: Add MIT notices**

Append a `Cultiway-Reborn city-wall geometry` section to
`THIRD_PARTY_NOTICES.md` naming `WallShapeHelper.cs` and the wall portion of
`Plots.cs`. Create `THIRD_PARTY_NOTICES/Cultiway-Wall-MIT.txt` containing the
same source identification and full MIT grant/copyright text from Cultiway's
root `LICENSE`.

- [ ] **Step 5: Run source guard and net48 build**

```powershell
& './Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'
dotnet build AncientWarfare3.csproj --no-restore `
  -p:TargetFrameworkVersion=v4.8.1
```

Expected: guard passes through the new shared-tool section; build has zero
warnings and zero errors.

- [ ] **Step 6: Commit adapter and notices**

```powershell
git add Code/core/lineage/CultiwayStyleCityWallService.cs `
  Tests/PeasantRebelRouteRuntimeSourceGuard.ps1 `
  THIRD_PARTY_NOTICES.md THIRD_PARTY_NOTICES/Cultiway-Wall-MIT.txt
git commit -m "feat: add shared Cultiway city wall tool"
```

## Task 4: Route Bandit Walls Through The Shared Tool

**Files:**

- Modify: `Code/core/lineage/PeasantRebelBanditWallService.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditRoute.cs`
- Modify: `Code/core/lineage/PeasantRebelGovernmentTransitionService.cs`
- Modify: `Tests/PeasantRebelRouteRuntimeSourceGuard.ps1`

- [ ] **Step 1: Replace old guards with failing shared-tool guards**

Require the bandit wall service to contain:

```powershell
Require $wall 'CultiwayStyleCityWallService.Build(' `
  'Every bandit city must use the shared Cultiway wall tool.'
Require $wall 'TopTileLibrary.wall_wild' `
  'Bandits must retain original wooden walls.'
Require $wall 'foreach (City city in pKingdom.getCities())' `
  'Every retained bandit city must receive its own wall.'
Require $wall 'CultiwayStyleCityWallService.TryPlan(' `
  'Bandit entry must preflight complete city wall geometry.'
Forbid $wall 'city.border_zones' `
  'Bandit walls must no longer scan incomplete border zones.'
Forbid $wall 'TouchesOutsideKingdom' `
  'Bandit wall geometry belongs to the shared city tool.'
```

Run the guard and expect failure because the old implementation remains.

- [ ] **Step 2: Delegate capture and placement**

Replace the body of `CaptureAndBuild` after authority checks with:

```csharp
var points = new Dictionary<string, WallPoint>(StringComparer.Ordinal);
foreach (City city in pKingdom.getCities())
{
    if (city?.data == null || city.isRekt() || city.kingdom != pKingdom)
        continue;
    CultiwayStyleCityWallResult result =
        CultiwayStyleCityWallService.Build(city,
            TopTileLibrary.wall_wild, 1, true);
    foreach (CultiwayWallPoint point in result.Points)
        points[point.X + ":" + point.Y] =
            new WallPoint { x = point.X, y = point.Y };
}
PersistAndBuild(pKingdom, points.Values.ToList());
```

Change `CaptureAndBuild` to return `bool`; return false without persistence
when no city produces points. Update bandit entry to reject a failed capture
before applying `ClassBandit`. `PersistAndBuild` must only persist and ensure
the requested `wall_wild` at returned coordinates; it must not recalculate a
second geometry.

Add `CanCaptureAndBuild(Kingdom)` that checks authority, iterates every valid
owned city, calls `TryPlan(city, 1, true, out points)`, rejects an empty plan
for any city, and requires at least one city. Add this predicate to
`PeasantRebelGovernmentTransitionService.TryEnterBandit` before it calls the
route behavior. This preserves the preflight-before-peace contract.

- [ ] **Step 3: Align bounded repair eligibility**

Replace `MandateBorderWallRules.IsWallBuildTileTerrainValid` in
`CanRestoreAtRecordedPosition` with the shared final placement contract:

```csharp
return pTile?.Type != null && !pTile.IsWater() &&
       !pTile.Type.mountains && !pTile.Type.summit;
```

Do not clear old walls during route conversion or extinction.

- [ ] **Step 4: Run tests and build**

Run the source guard, `--cultiway-wall`,
`--peasant-rebel-routes`, and the net48 build. Expected: all focused checks
exit zero and build remains warning-free.

- [ ] **Step 5: Commit bandit integration**

```powershell
git add Code/core/lineage/PeasantRebelBanditWallService.cs `
  Code/core/lineage/PeasantRebelBanditRoute.cs `
  Code/core/lineage/PeasantRebelGovernmentTransitionService.cs `
  Tests/PeasantRebelRouteRuntimeSourceGuard.ps1
git commit -m "fix: build complete Cultiway-style bandit walls"
```

## Task 5: Route Mandate Border Walls Through The Shared Tool

**Files:**

- Modify: `Code/core/lineage/MandateBorderDefenseService.cs`
- Modify: `Code/core/lineage/MandateBorderWallRules.cs`
- Modify: `Tests/PeasantRebelRouteRuntimeSourceGuard.ps1`

- [ ] **Step 1: Add failing Mandate ownership guards**

```powershell
Require $mandateBorder 'CultiwayStyleCityWallService.Build(' `
  'Mandate border cities must use the shared Cultiway wall tool.'
Require $mandateBorder 'TopTileLibrary.wall_order' `
  'Mandate border cities must retain original order stone walls.'
Forbid $mandateBuildWalls 'pCap' `
  'Mandate walls must not stop midway through a ring.'
Forbid $mandateBuildWalls 'ShouldBuildWallAtOrderedIndex' `
  'Mandate walls must not create every-ninth-tile gaps.'
Forbid $mandateBuildWalls 'border_zones' `
  'Mandate wall geometry belongs to the shared city tool.'
```

Isolate the `BuildBorderWalls` method body before applying the forbids. Run the
guard and observe the expected old-loop failure.

- [ ] **Step 2: Replace partial wall construction**

Change the call site to:

```csharp
if (pWallCap > 0)
    result.walls += BuildBorderWalls(city);
```

Implement:

```csharp
private static int BuildBorderWalls(City pCity)
{
    TopTileType wall = ResolveBorderWallType();
    if (pCity?.data == null || wall == null) return 0;
    return CultiwayStyleCityWallService.Build(
        pCity, wall, 2, true).Changed;
}
```

Keep selected-border-city filtering, border armies, guards, tower limits,
history, and `ResolveBorderWallType`. Delete `IsWallCandidate`; retain
`TouchesExternalLandBorder` and `IsExternalLandBorderNeighbor` because tower
and patrol selection still use them.

- [ ] **Step 3: Remove obsolete gap rules**

Delete `GapInterval`, `ShouldBuildWallAtOrderedIndex`,
`CompareWallTileOrder`, and `IsWallBuildTileTerrainValid` from
`MandateBorderWallRules`. Keep `PreferredWallTopTileId` and
`IsExternalLandBorderNeighbor` unchanged.

- [ ] **Step 4: Verify and commit**

Run both focused slices, source guard, net48 build, and `git diff --check`.
Expected: all exit zero, build has zero warnings/errors, diff check is silent.

```powershell
git add Code/core/lineage/MandateBorderDefenseService.cs `
  Code/core/lineage/MandateBorderWallRules.cs `
  Tests/PeasantRebelRouteRuntimeSourceGuard.ps1
git commit -m "feat: complete Mandate city walls with shared geometry"
```

## Task 6: Implement The Chinese Outlaw Root Library And Rules

**Files:**

- Create: `Code/core/lineage/PeasantRebelOutlawNameRules.cs`
- Create: `word_libraries/default/土匪名根.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelOutlawNameRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Register and write the failing naming slice**

Add the test and linked production type to the rules-test project:

```xml
<Compile Include="PeasantRebelOutlawNameRulesTests.cs.txt" />
<Compile Include="..\..\Code\core\lineage\PeasantRebelOutlawNameRules.cs"
         Link="Production\PeasantRebelOutlawNameRules.cs" />
```

Add this focused entry to `Program.cs.txt`:

```csharp
if (args.Length == 1 && args[0] == "--bandit-outlaw-names")
{
    PeasantRebelOutlawNameRulesTests.Run();
    Console.WriteLine("Bandit outlaw naming rules passed.");
    return;
}
```

Create tests with `new[] { "赤眉", "黄巾", "绿林", "黑风" }` and the exact
assertions from the design: deterministic selection and library membership,
Han/library validation, Latin legacy replacement, repeated suffix
stripping, and exact founding/bandit composition. Run the slice and expect a
compile failure because `PeasantRebelOutlawNameRules.cs` is absent.

- [ ] **Step 2: Create the dedicated root library**

Add one trimmed unique root per line. Include at least 48 roots and no suffix:

```text
赤眉
黄巾
绿林
红巾
白莲
黑风
青龙
飞虎
伏牛
太行
梁山
洞庭
九山
连云
金刀
铁旗
神火
天雄
忠义
聚义
风云
苍狼
白马
乌衣
玄甲
赤旗
青天
大泽
长风
惊雷
烈火
翻江
镇山
过山
断岳
平海
横江
飞云
冲霄
金山
银山
黑山
青山
白水
赤水
金沙
龙门
虎踞
```

- [ ] **Step 3: Implement detached naming rules**

Create:

```csharp
public static class PeasantRebelOutlawNameRules
{
    public const string LibraryId = "土匪名根";

    public static string NormalizeRoot(string value)
    {
        string root = (value ?? "").Trim();
        bool changed;
        do
        {
            changed = false;
            foreach (string suffix in new[] { "义军", "贼" })
            {
                if (!root.EndsWith(suffix, StringComparison.Ordinal)) continue;
                root = root.Substring(0, root.Length - suffix.Length).Trim();
                changed = true;
            }
        } while (changed && root.Length > 0);
        return root;
    }

    public static bool IsValidLibraryRoot(string value,
        IReadOnlyList<string> roots)
    {
        string root = NormalizeRoot(value);
        if (!ContainsHan(root)) return false;
        return roots != null && roots.Any(candidate =>
            string.Equals(NormalizeRoot(candidate), root,
                StringComparison.Ordinal));
    }

    public static string SelectRoot(IReadOnlyList<string> roots, long seed)
    {
        string[] valid = (roots ?? Array.Empty<string>())
            .Select(NormalizeRoot).Where(ContainsHan).Distinct().ToArray();
        if (valid.Length == 0) return "";
        ulong mixed = Mix(unchecked((ulong)seed));
        return valid[(int)(mixed % (ulong)valid.Length)];
    }

    public static string ResolveRoot(string stored,
        IReadOnlyList<string> roots, long seed) =>
        IsValidLibraryRoot(stored, roots)
            ? NormalizeRoot(stored)
            : SelectRoot(roots, seed);

    public static string ComposeName(string root, string route) =>
        NormalizeRoot(root) +
        (route == PeasantRebelRouteIds.Bandit ? "贼" : "义军");
}
```

Implement `ContainsHan` over `\u3400-\u4DBF` and `\u4E00-\u9FFF`, and use one
documented SplitMix64-style `Mix` function so selection is deterministic.

- [ ] **Step 4: Run GREEN**

Run `--bandit-outlaw-names` and `--cultiway-wall`. Expected: both focused
slices pass with their exact success lines.

- [ ] **Step 5: Commit library and rules**

```powershell
git add Code/core/lineage/PeasantRebelOutlawNameRules.cs `
  word_libraries/default/土匪名根.txt `
  Tests/AncientWarfare3.Rules.Tests/PeasantRebelOutlawNameRulesTests.cs.txt `
  Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj `
  Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: add stable Chinese outlaw realm names"
```

## Task 7: Integrate Persisted Outlaw Roots Into Rebel Transitions

**Files:**

- Create: `Code/core/lineage/PeasantRebelOutlawNameService.cs`
- Modify: `Code/core/lineage/PeasantRebelRouteRules.cs`
- Modify: `Code/core/lineage/PeasantRebelRouteService.cs`
- Modify: `Code/core/lineage/PeasantRebelGovernmentTransitionService.cs`
- Modify: `Tests/PeasantRebelRouteRuntimeSourceGuard.ps1`

- [ ] **Step 1: Add failing runtime guards**

Require:

```powershell
Require $outlawName 'AWWordLibraryManager.Instance.GetWords(' `
  'Outlaw roots must come from the integrated UTF-8 word library.'
Require $outlawName 'PeasantRebelOutlawNameRules.ResolveRoot(' `
  'Persisted roots must be validated and migrated centrally.'
Require $route 'PeasantRebelOutlawNameService.EnsureRoot(' `
  'Manual and generated rebels must initialize an outlaw root.'
Require $government 'PeasantRebelOutlawNameService.EnsureRoot(' `
  'Bandit preflight must repair legacy roots before entry mutations.'
Forbid $route 'pFounder.generateName(MetaType.Kingdom' `
  'Rebel roots must not use the founder culture kingdom generator.'
```

Run the guard and observe failure on the missing service.

- [ ] **Step 2: Implement the runtime root service**

Create authority-neutral reads and an authoritative write:

```csharp
internal static class PeasantRebelOutlawNameService
{
    internal static bool EnsureRoot(Kingdom pKingdom, Actor pFounder,
        int pYear, out string pRoot)
    {
        pRoot = "";
        if (pKingdom?.data == null || pFounder?.data == null) return false;
        IReadOnlyList<string> roots = AWWordLibraryManager.Instance.GetWords(
            PeasantRebelOutlawNameRules.LibraryId);
        pKingdom.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
            out string stored, "");
        long seed = pKingdom.getID() ^ (pFounder.getID() << 1) ^
                    ((long)pYear << 32);
        pRoot = PeasantRebelOutlawNameRules.ResolveRoot(stored, roots, seed);
        if (pRoot.Length == 0) return false;
        if (!string.Equals(stored, pRoot, StringComparison.Ordinal))
            pKingdom.data.set(LineageKeys.MANDATE_REBEL_NAME_ROOT, pRoot);
        return true;
    }

    internal static bool HasValidRoot(Kingdom pKingdom)
    {
        if (pKingdom?.data == null) return false;
        pKingdom.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
            out string stored, "");
        return PeasantRebelOutlawNameRules.IsValidLibraryRoot(stored,
            AWWordLibraryManager.Instance.GetWords(
                PeasantRebelOutlawNameRules.LibraryId));
    }
}
```

Callers retain the existing multiplayer authority check before `EnsureRoot`.

- [ ] **Step 3: Replace route-root generation and composition**

In `TryInitializeRouteMetadata`, replace `generateName` and fallback logic with:

```csharp
int year = Date.getCurrentYear();
if (!PeasantRebelOutlawNameService.EnsureRoot(
        pRebel, pFounder, year, out string root)) return false;
```

Keep the persisted founding city, created year, origin city count, strength,
capital, and ruler fields unchanged. Change
`PeasantRebelRouteRules.ComposeName` to call
`PeasantRebelOutlawNameRules.ComposeName`.

- [ ] **Step 4: Repair legacy roots in bandit preflight**

At the start of the private `EnterBandit` resolver, after resolving founder
but before `TryEnterBandit`, call authoritative `EnsureRoot` with the current
year. In the shared `TryEnterBandit` preflight replace the raw nonempty-root
check with `PeasantRebelOutlawNameService.HasValidRoot(pRebel)`. This makes an
old `Giug贼` save migrate before war ending, territory capture, class changes,
wall placement, or history writes.

- [ ] **Step 5: Verify exact suffix transitions**

Run both focused rules slices and the source guard. Add assertions to existing
rebel-route tests for `ComposeName("赤眉义军", Bandit) == "赤眉贼"` and
`ComposeName("赤眉贼", Founding) == "赤眉义军"`. Run again and require both
slices to pass.

- [ ] **Step 6: Build and commit**

```powershell
dotnet build AncientWarfare3.csproj --no-restore `
  -p:TargetFrameworkVersion=v4.8.1
git diff --check
git add Code/core/lineage/PeasantRebelOutlawNameService.cs `
  Code/core/lineage/PeasantRebelRouteRules.cs `
  Code/core/lineage/PeasantRebelRouteService.cs `
  Code/core/lineage/PeasantRebelGovernmentTransitionService.cs `
  Tests/PeasantRebelRouteRuntimeSourceGuard.ps1 `
  Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt
git commit -m "fix: migrate rebel realms to Chinese outlaw names"
```

Expected: build has zero warnings/errors; diff check is silent.

## Task 8: Final Verification, Deployment, And Visible Runtime Test

**Files:**

- Verify: all files above
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run focused verification fresh**

```powershell
& './Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'
$env:DOTNET_ROLL_FORWARD='Major'
dotnet run --project `
  'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' `
  -- --cultiway-wall
dotnet run --project `
  'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' `
  -- --bandit-outlaw-names
dotnet run --project `
  'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' `
  -- --peasant-rebel-routes
dotnet build AncientWarfare3.csproj --no-restore `
  -p:TargetFrameworkVersion=v4.8.1
git diff --check
```

Expected: all three focused slices and the source guard pass; build reports zero
warnings/errors; diff check is silent.

- [ ] **Step 2: Audit the old wall paths are gone**

```powershell
rg -n "border_zones|ShouldBuildWallAtOrderedIndex|IsWallBuildTileTerrainValid|generateName\(MetaType.Kingdom" `
  Code/core/lineage/PeasantRebelBanditWallService.cs `
  Code/core/lineage/MandateBorderDefenseService.cs `
  Code/core/lineage/PeasantRebelRouteService.cs
```

Expected: no output. Confirm `rg -n "CultiwayStyleCityWallService.Build"` shows
exactly the bandit and Mandate runtime callers.

- [ ] **Step 3: Close WorldBox and deploy from the explicit worktree**

Close the visible WorldBox process gracefully when possible, then run:

```powershell
$source = (Resolve-Path '.').Path
$destination = 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
& './deploy-local.ps1' -SourceRoot $source -DestinationRoot $destination
& './Tests/VerifySourceDeployment.ps1' `
  -SourceRoot $source -DestinationRoot $destination
dotnet build (Join-Path $destination 'AncientWarfare3.csproj') `
  --no-restore -p:TargetFrameworkVersion=v4.8.1
```

Expected: timestamped backup, `DEPLOY-DONE`, complete SHA256 parity, and a
zero-warning/zero-error deployed build.

- [ ] **Step 4: Start a visible WorldBox process**

```powershell
Start-Process `
  'D:\SteamLibrary\steamapps\common\worldbox\worldbox.exe' `
  -WorkingDirectory 'D:\SteamLibrary\steamapps\common\worldbox'
```

Wait for a responsive `WorldBox` main window and confirm the current
`Player.log` contains AW3 `Loaded` with no AW3-specific exception/error/failure.

- [ ] **Step 5: Perform game acceptance checks**

In a disposable world:

1. Switch an ordinary realm to peasant rebel and confirm a Chinese
   `<root>义军` name.
2. Switch it to bandit and confirm the same `<root>贼` name.
3. Inspect every bandit city: each has its own complete single-layer wooden
   wall with road/dock gates and no accidental sparse gaps.
4. Switch back to peasant rebel and confirm the same `<root>义军`.
5. Execute Mandate border defense and confirm each selected border city gains
   a complete double-layer `wall_order` ring in one execution while guards and
   watchtowers still appear.
6. Save and reload; confirm names are stable and walls are not duplicated or
   rebuilt by restore.

- [ ] **Step 6: Record final status**

```powershell
git status --short --branch
git log -8 --oneline
```

Expected: clean feature worktree and all task commits present. Leave the
visible game running for user inspection.

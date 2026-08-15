# Bandit Zone-Aligned Stronghold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate every new bandit stronghold from at least one complete native WorldBox zone and place its wooden wall on the exact outer edge of the selected zones with one three-tile gate on each cardinal side.

**Architecture:** Keep the existing Cultiway city-wall planner as the desired-size signal, then project that signal onto a connected set of complete mother-city zones rooted at the civic core. A new pure geometry unit derives the exact outer boundary of the selected zone-tile union and carves four deterministic gate openings; a runtime adapter converts `TileZone`/`WorldTile` data into that pure model. `PeasantRebelBanditStrongholdService` consumes one authoritative zone-and-wall plan, while existing persistence and rollback continue storing fixed zone keys and placed wall coordinates.

**Tech Stack:** C#/.NET 4.8 mod runtime, detached .NET 9 rules tests, PowerShell source guards and deployment verification, original WorldBox `TileZone`, `City.addZone`, `WorldTile.setTopTileType`, and `TopTileLibrary.wall_wild` APIs.

---

## File Structure

- Modify `Code/core/lineage/PeasantRebelBanditStrongholdRules.cs`: replace majority coverage with center-rooted desired-zone projection.
- Create `Code/core/lineage/PeasantRebelBanditZoneWallRules.cs`: pure selected-land perimeter and guaranteed four-gate geometry.
- Create `Code/core/lineage/PeasantRebelBanditZoneWallService.cs`: runtime conversion from native zones and terrain to the pure wall plan.
- Modify `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`: select zones first, then request the matching wall plan.
- Modify `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt`: minimum-one-zone and connected desired-zone tests.
- Create `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditZoneWallRulesTests.cs.txt`: one-zone, multi-zone, irregular perimeter, and four-gate tests.
- Modify `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`: link the new pure production file and test file.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`: run both stronghold rule suites in the focused slice.
- Modify `Tests/BanditStrongholdWallZoneFitSourceGuard.ps1`: require zone-driven wall planning and forbid majority selection.

### Task 1: Replace Majority Selection With Native-Zone Projection

**Files:**
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt`

- [ ] **Step 1: Write the failing zone-selection tests**

Replace `SelectsOnlyConnectedInteriorZones` with tests that prove the center zone is the minimum and any connected zone touched by the desired enclosure is selected, regardless of percentage:

```csharp
private static void SelectsCenterAsMinimumNativeZone()
{
    var zones = new[] { Zone("center", 0, 64, "east"),
                        Zone("east", 0, 64, "center") };
    HashSet<string> selected =
        PeasantRebelBanditStrongholdRules.SelectZoneAlignedKeys(
            zones, "center");
    True(selected.SetEquals(new[] { "center" }),
        "the civic-core zone is the one-zone minimum");
}

private static void SelectsConnectedDesiredZonesWithoutMajorityThreshold()
{
    var zones = new[]
    {
        Zone("center", 64, 64, "east", "dry"),
        Zone("east", 1, 64, "center"),
        Zone("dry", 0, 64, "center"),
        Zone("isolated", 64, 64)
    };
    HashSet<string> selected =
        PeasantRebelBanditStrongholdRules.SelectZoneAlignedKeys(
            zones, "center");
    True(selected.SetEquals(new[] { "center", "east" }),
        "all and only connected zones touched by the desired enclosure transfer");
}
```

Call both methods from `Run()` and remove the strict-majority assertion.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
```

Expected: compilation fails because `SelectZoneAlignedKeys` does not exist.

- [ ] **Step 3: Implement the minimal center-rooted projection**

Add `HasDesiredLand` and replace the majority selector:

```csharp
public bool HasDesiredLand => EnclosedTileCount > 0;

public static HashSet<string> SelectZoneAlignedKeys(
    IReadOnlyList<BanditZoneFact> zones, string centerKey)
{
    var selected = new HashSet<string>(StringComparer.Ordinal);
    if (zones == null || zones.Count == 0 ||
        string.IsNullOrWhiteSpace(centerKey)) return selected;
    var byKey = zones.Where(zone => zone != null && zone.Key.Length > 0)
        .ToDictionary(zone => zone.Key, StringComparer.Ordinal);
    if (!byKey.TryGetValue(centerKey, out BanditZoneFact center))
        return selected;
    var pending = new Queue<string>();
    selected.Add(center.Key);
    pending.Enqueue(center.Key);
    while (pending.Count > 0)
    {
        BanditZoneFact current = byKey[pending.Dequeue()];
        foreach (string key in current.NeighbourKeys)
        {
            if (selected.Contains(key) ||
                !byKey.TryGetValue(key, out BanditZoneFact neighbour) ||
                !neighbour.HasDesiredLand) continue;
            selected.Add(key);
            pending.Enqueue(key);
        }
    }
    return selected;
}
```

Remove `IsMajorityEnclosed` and `SelectInteriorZoneKeys` so no caller can silently retain the rejected rule.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the command from Step 2.

Expected: `PeasantRebelBanditStrongholdRulesTests passed` and exit code 0.

- [ ] **Step 5: Commit the selection rule**

```powershell
git add Code/core/lineage/PeasantRebelBanditStrongholdRules.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt
git commit -m "fix: select bandit stronghold native zones"
```

### Task 2: Add Pure Zone-Perimeter And Four-Gate Geometry

**Files:**
- Create: `Code/core/lineage/PeasantRebelBanditZoneWallRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditZoneWallRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Register the new test and production files**

Add these entries beside the existing stronghold and Cultiway geometry entries:

```xml
<Compile Include="PeasantRebelBanditZoneWallRulesTests.cs.txt" />
<Compile Include="..\..\Code\core\lineage\PeasantRebelBanditZoneWallRules.cs"
         Link="Production\PeasantRebelBanditZoneWallRules.cs" />
```

In the `--bandit-stronghold` branch, invoke:

```csharp
PeasantRebelBanditStrongholdRulesTests.Run();
PeasantRebelBanditZoneWallRulesTests.Run();
Console.WriteLine("Bandit stronghold rule tests passed.");
return;
```

- [ ] **Step 2: Write failing one-zone and multi-zone perimeter tests**

Create a test class with `Run()` and assertions equivalent to:

```csharp
private static void OneZoneUsesItsExactOuterEdge()
{
    HashSet<CultiwayWallPoint> territory = Rectangle(8, 8, 15, 15);
    BanditZoneWallPlan plan = Plan(territory, Point(11, 11));
    SetEqual(RectanglePerimeter(8, 8, 15, 15),
        plan.ClosedWallPoints.ToHashSet(),
        "one native zone owns one exact perimeter");
}

private static void TwoAdjacentZonesUseOneUnionPerimeter()
{
    HashSet<CultiwayWallPoint> territory = Rectangle(8, 8, 23, 15);
    BanditZoneWallPlan plan = Plan(territory, Point(11, 11));
    SetEqual(RectanglePerimeter(8, 8, 23, 15),
        plan.ClosedWallPoints.ToHashSet(),
        "the shared zone edge is not walled");
}
```

`Plan` supplies territory plus one-tile padded passable land, no roads, and map size 40 by 40.

- [ ] **Step 3: Write failing four-cardinal-gate tests**

Compare `ClosedWallPoints` to `WallPoints` and require exactly one three-tile gap on every side:

```csharp
HashSet<CultiwayWallPoint> removed = plan.ClosedWallPoints.ToHashSet();
removed.ExceptWith(plan.WallPoints);
SetEqual(new HashSet<CultiwayWallPoint>
{
    Point(11, 8), Point(12, 8), Point(13, 8),
    Point(11, 15), Point(12, 15), Point(13, 15),
    Point(8, 11), Point(8, 12), Point(8, 13),
    Point(15, 11), Point(15, 12), Point(15, 13)
}, removed, "north, south, east, and west each have one three-tile gate");
```

Add an L-shaped union case and assert every wall point belongs to the selected territory and no shared internal edge appears in `ClosedWallPoints`.

- [ ] **Step 4: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
```

Expected: compilation fails because `BanditZoneWallPlan` and `PeasantRebelBanditZoneWallRules` do not exist.

- [ ] **Step 5: Implement the pure wall-plan model and closed perimeter**

Create an immutable output type. Calculate the closed perimeter directly from
the selected complete-zone tile union: a placeable territory point belongs to
the perimeter when at least one cardinal neighbour is absent from the
territory. This deliberately does not call `CultiwayStyleWallGeometryRules.Compute`
because that method always runs its reachability passage even when its gate
flag is false.

```csharp
public sealed class BanditZoneWallPlan
{
    public BanditZoneWallPlan(
        IReadOnlyList<CultiwayWallPoint> closedWallPoints,
        IReadOnlyList<CultiwayWallPoint> wallPoints)
    {
        ClosedWallPoints = closedWallPoints;
        WallPoints = wallPoints;
    }

    public IReadOnlyList<CultiwayWallPoint> ClosedWallPoints { get; }
    public IReadOnlyList<CultiwayWallPoint> WallPoints { get; }
}

public static BanditZoneWallPlan Build(int mapWidth, int mapHeight,
    CultiwayWallPoint center,
    IEnumerable<CultiwayWallPoint> territory,
    IEnumerable<CultiwayWallPoint> passable,
    IEnumerable<CultiwayWallPoint> roads)
{
    var land = new HashSet<CultiwayWallPoint>(territory);
    var passableSet = new HashSet<CultiwayWallPoint>(passable);
    CultiwayWallBounds bounds = BoundsOf(land);
    var closed = land.Where(point => passableSet.Contains(point) &&
        CardinalDirections.Any(direction =>
            !land.Contains(Offset(point, direction)))).ToHashSet();
    var opened = new HashSet<CultiwayWallPoint>(closed);
    CarveFourCardinalGates(opened, closed, bounds, roads);
    return new BanditZoneWallPlan(
        closed.OrderBy(point => point.X).ThenBy(point => point.Y).ToArray(),
        opened.OrderBy(point => point.X).ThenBy(point => point.Y).ToArray());
}
```

`BoundsOf` uses ceiling midpoints `(min + max + 1) / 2` and half extents
large enough to include both ends. Validate positive map dimensions, a
non-empty territory, an in-map center, and at least one closed wall point.

- [ ] **Step 6: Implement deterministic four-gate carving**

For `(0,1)`, `(1,0)`, `(0,-1)`, and `(-1,0)`, select a wall point with positive directional projection, smallest lateral distance from the bounds center, then greatest projection. Prefer candidates within the existing six-tile road radius. Remove every wall point within Chebyshev distance one of the selected point:

```csharp
private static void MarkThreeTilePassage(
    HashSet<CultiwayWallPoint> walls, CultiwayWallPoint gate)
{
    walls.RemoveWhere(point =>
        Math.Abs(point.X - gate.X) <= 1 &&
        Math.Abs(point.Y - gate.Y) <= 1);
}
```

Throw `InvalidOperationException("four cardinal gates unavailable")` if any cardinal side has no candidate. This makes creation fail during preflight rather than produce a partially gated wall.

- [ ] **Step 7: Run both focused geometry slices**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --cultiway-wall
```

Expected: both commands pass; existing Cultiway wall behavior is unchanged.

- [ ] **Step 8: Commit the pure geometry**

```powershell
git add Code/core/lineage/PeasantRebelBanditZoneWallRules.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditZoneWallRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: align bandit walls to native zones"
```

### Task 3: Add The WorldBox Runtime Wall Adapter

**Files:**
- Create: `Code/core/lineage/PeasantRebelBanditZoneWallService.cs`
- Modify: `Tests/BanditStrongholdWallZoneFitSourceGuard.ps1`

- [ ] **Step 1: Rewrite the source guard to fail on the old runtime path**

Require the new service and forbid majority-only tokens:

```powershell
$zoneWallServicePath = Join-Path $root `
    'Code/core/lineage/PeasantRebelBanditZoneWallService.cs'
foreach ($token in @('PeasantRebelBanditZoneWallRules.Build(',
        'ClosedWallPoints', 'WallPoints', 'zone.tiles',
        'TerrainCollectionPadding')) {
    if (-not $zoneWallService.Contains($token)) {
        throw "Zone-aligned wall runtime is missing $token"
    }
}
foreach ($forbidden in @('IsMajorityEnclosed',
        'SelectInteriorZoneKeys(', 'enclosedTileCount * 2')) {
    if ($rulesSource.Contains($forbidden) -or
        $strongholdService.Contains($forbidden)) {
        throw "Rejected majority rule remains: $forbidden"
    }
}
```

- [ ] **Step 2: Run the guard and verify RED**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
```

Expected: failure because `PeasantRebelBanditZoneWallService.cs` is absent.

- [ ] **Step 3: Implement runtime terrain capture**

Create `TryPlan(City mother, IReadOnlyCollection<TileZone> selectedZones, WorldTile center, out BanditZoneWallPlan plan)`. It must:

```csharp
var selected = new HashSet<TileZone>(selectedZones);
foreach (TileZone zone in selected)
foreach (WorldTile tile in zone.tiles)
{
    if (tile == null) continue;
    var point = new CultiwayWallPoint(tile.x, tile.y);
    allZoneTiles.Add(point);
    territory.Add(point);
    if (IsWater(tile)) continue;
    if (tile.Type?.road == true) roads.Add(point);
}
```

The logical territory includes every tile in each selected native zone,
including water and blocked terrain. Collect passable land across the selected
tile bounds plus one tile of padding, call
`PeasantRebelBanditZoneWallRules.Build`, and retain the returned wall points
only when their tile still belongs to a selected zone. The pure rule already
excludes non-passable perimeter points and fails if four valid cardinal gaps
cannot be produced. Return false on empty inputs, filtered empty walls, or
exceptions.

- [ ] **Step 4: Run the source guard and net48 build**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release -f net48 --no-restore
```

Expected: source guard passes and build reports 0 errors.

- [ ] **Step 5: Commit the runtime adapter**

```powershell
git add Code/core/lineage/PeasantRebelBanditZoneWallService.cs Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
git commit -m "feat: plan bandit walls from selected zones"
```

### Task 4: Integrate Zone-First Planning Into Stronghold Creation

**Files:**
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Modify: `Tests/BanditStrongholdWallZoneFitSourceGuard.ps1`

- [ ] **Step 1: Add failing integration-order assertions to the guard**

Require the stronghold service to calculate selected keys before planning the final wall:

```powershell
$selectionIndex = $strongholdService.IndexOf('SelectZoneAlignedKeys(')
$wallIndex = $strongholdService.IndexOf(
    'PeasantRebelBanditZoneWallService.TryPlan(')
if ($selectionIndex -lt 0 -or $wallIndex -le $selectionIndex) {
    throw 'Stronghold must select native zones before planning its wall'
}
foreach ($token in @('InteriorZones = interior',
        'WallPoints = zoneWallPlan.WallPoints.ToList()',
        'FixedZoneKeys = interior.Select(ZoneKey)')) {
    if (-not $strongholdService.Contains($token)) {
        throw "One authoritative zone/wall plan is missing $token"
    }
}
```

- [ ] **Step 2: Run the guard and verify RED**

Run the Task 3 guard command.

Expected: failure because the stronghold still plans the final wall before selecting zones.

- [ ] **Step 3: Change preflight to select zones first**

Keep `CultiwayStyleCityWallService.TryPlanDetailed` only as the desired-size signal. Resolve `centerZone` from the hall, then bonfire, then city tile; construct `BanditZoneFact` overlap counts; invoke:

```csharp
HashSet<string> interiorKeys =
    PeasantRebelBanditStrongholdRules.SelectZoneAlignedKeys(
        facts, ZoneKey(centerZone));
List<TileZone> interior = motherZones.Where(zone =>
    interiorKeys.Contains(ZoneKey(zone))).ToList();
List<TileZone> exterior = motherZones.Where(zone =>
    !interiorKeys.Contains(ZoneKey(zone))).ToList();
```

Retain `IsViableSplit(interior.Count, exterior.Count)` so the mother always owns at least one zone.

- [ ] **Step 4: Plan the wall from exactly the selected zones**

After split viability and before creating the transaction plan, call:

```csharp
if (!PeasantRebelBanditZoneWallService.TryPlan(
        pMother, interior, centerZone.centerTile,
        out BanditZoneWallPlan zoneWallPlan) ||
    zoneWallPlan.WallPoints.Count == 0)
{
    pFailureKey = "aw_bandit_stronghold_wall_failed";
    return false;
}
```

Assign `zoneWallPlan.WallPoints` to `PeasantRebelBanditStrongholdPlan.WallPoints`. Do not alter `BuildState`, `PlaceWalls`, `Rollback`, or old-state restore behavior; they already persist and restore the final wall points and fixed zones.

- [ ] **Step 5: Run focused and transaction checks**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --cultiway-wall
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdTransactionSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release -f net48 --no-restore
```

Expected: focused tests and guards pass, build reports 0 errors. Record but do not modify unrelated known baseline failures.

- [ ] **Step 6: Commit the integration**

```powershell
git add Code/core/lineage/PeasantRebelBanditStrongholdService.cs Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
git commit -m "fix: generate strongholds from wall-aligned zones"
```

### Task 5: Verify, Deploy, Launch, And Inspect

**Files:**
- Verify: `Code/core/lineage/PeasantRebelBanditStrongholdRules.cs`
- Verify: `Code/core/lineage/PeasantRebelBanditZoneWallRules.cs`
- Verify: `Code/core/lineage/PeasantRebelBanditZoneWallService.cs`
- Verify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run final focused verification**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --cultiway-wall
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdTransactionSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release -f net48 --no-restore
git diff --check
git status --short
```

Expected: focused slices and guards pass, build has 0 errors, diff check is clean, and status contains only intended files.

- [ ] **Step 2: Review the final implementation diff and commit any remaining intended changes**

```powershell
git diff HEAD~4 -- Code/core/lineage Tests/AncientWarfare3.Rules.Tests Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
```

Confirm the diff contains no migration of existing strongholds and no changes to mandate frontier or ordinary Cultiway wall callers.

- [ ] **Step 3: Deploy with a timestamped backup**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy-local.ps1 -SourceRoot 'F:\WorldBox New Mod\AncientWarfare3.0\.worktrees\peasant-rebel-dual-route' -DestinationRoot 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tests\VerifySourceDeployment.ps1 -SourceRoot 'F:\WorldBox New Mod\AncientWarfare3.0\.worktrees\peasant-rebel-dual-route' -DestinationRoot 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
```

Expected: deployment reports its timestamped backup path and verification reports all production files and SHA-256 hashes match.

- [ ] **Step 4: Start WorldBox visibly and verify startup**

Start `D:/SteamLibrary/steamapps/common/worldbox/worldbox.exe` without hidden-window flags. Wait for the visible `WorldBox` main window, then inspect the new `Player.log` session. Expected: Ancient Warfare 3 loads, C# compilation reports 0 errors, and no new exception references the zone-wall or stronghold services.

- [ ] **Step 5: Perform the gameplay acceptance check**

Use the god power to release bandits from a mother city with at least two zones. Under the city map mode verify:

1. a small core produces a one-zone stronghold;
2. larger desired cores add only connected native zones;
3. the wooden wall follows the selected-zone union rather than the old wall bounds;
4. north, south, east, and west each contain one three-tile gap;
5. every zone beyond the wall remains with the mother city;
6. opening, saving, and reloading the stronghold leaves its fixed zones and wall unchanged.

Record the observed stronghold and mother-city zone counts plus any runtime log exception before reporting completion.

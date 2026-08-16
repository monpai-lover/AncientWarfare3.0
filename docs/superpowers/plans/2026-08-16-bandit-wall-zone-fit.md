# Bandit Stronghold Wall-Zone Fit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make newly created bandit strongholds receive only connected zones whose tiles are mostly inside the generated wooden-wall interior.

**Architecture:** Expose the bounded connected land already computed by the Cultiway-style wall geometry, carry it through a detailed wall plan, and classify each mother-city zone by strict majority tile overlap. Persisted stronghold keys and existing save restoration remain unchanged.

**Tech Stack:** C# 10, .NET Framework 4.8, custom console rule tests, PowerShell source guards.

---

### Task 1: Define Majority-Enclosed Zone Eligibility

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdRules.cs`

- [ ] **Step 1: Write failing majority-overlap tests**

Replace boolean zone facts with tile counts and add cases for strict majority, exact half, minority, zero tiles, and connected selection.

```csharp
var zones = new[]
{
    Zone("center", 7, 10, "east", "half"),
    Zone("east", 6, 10, "center"),
    Zone("half", 5, 10, "center"),
    Zone("minority", 4, 10, "center"),
    Zone("empty", 0, 0, "center")
};

HashSet<string> selected =
    PeasantRebelBanditStrongholdRules.SelectInteriorZoneKeys(
        zones, "center");
True(selected.SetEquals(new[] { "center", "east" }),
    "only connected zones with strict majority wall overlap transfer");
```

Update the test helper to construct `BanditZoneFact(key, enclosed, total, neighbours)`.

- [ ] **Step 2: Run the focused test and verify RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
```

Expected: compilation fails because `BanditZoneFact` does not accept enclosed and total tile counts.

- [ ] **Step 3: Implement strict-majority eligibility**

Store non-negative `EnclosedTileCount` and `TotalTileCount` and expose:

```csharp
public bool IsMajorityEnclosed => TotalTileCount > 0 &&
    (long)EnclosedTileCount * 2L > TotalTileCount;
```

Update the breadth-first selection to use `IsMajorityEnclosed` for the seed and each neighbour. Keep key normalization and connectivity behavior unchanged.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the command from Step 2. Expected: `Bandit stronghold and raid rules passed.`

### Task 2: Expose the Wall Geometry's Enclosed Land

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/CultiwayStyleWallGeometryRulesTests.cs.txt`
- Modify: `Code/core/lineage/CultiwayStyleWallGeometryRules.cs`
- Modify: `Code/core/lineage/CultiwayStyleCityWallService.cs`

- [ ] **Step 1: Write failing enclosed-land geometry tests**

Add a test that calls `ComputeEnclosedLand` for connected city land extending beyond the requested bounds plus detached land. Assert that the result is only the bounded connected component. Call it with gates enabled and disabled and assert equal results.

```csharp
HashSet<CultiwayWallPoint> enclosed =
    CultiwayStyleWallGeometryRules.ComputeEnclosedLand(input)
        .ToHashSet();
SetEqual(Rectangle(2, 2, 6, 6), enclosed,
    "wall plan exposes its bounded connected interior land");
```

- [ ] **Step 2: Run the focused geometry test and verify RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --cultiway-wall
```

Expected: compilation fails because `ComputeEnclosedLand` is missing.

- [ ] **Step 3: Implement the pure enclosed-land API**

Reuse the existing private `GetCoreLand` and `IntersectBounds` operations:

```csharp
public static IReadOnlyList<CultiwayWallPoint> ComputeEnclosedLand(
    CultiwayWallGeometryInput input)
{
    if (input == null) throw new ArgumentNullException(nameof(input));
    return IntersectBounds(GetCoreLand(input), input)
        .OrderBy(point => point.X)
        .ThenBy(point => point.Y).ToArray();
}
```

- [ ] **Step 4: Carry geometry through a detailed city-wall plan**

Add `CultiwayStyleCityWallPlan` with `WallPoints` and `EnclosedLand`, plus `TryPlanDetailed`. The existing `TryPlan` delegates to the detailed method and returns only `WallPoints`, preserving its API for `PeasantRebelBanditWallService` and `Build`.

- [ ] **Step 5: Run the geometry test and verify GREEN**

Run the command from Step 2. Expected: `Cultiway wall geometry rules passed.`

### Task 3: Replace Bounding-Rectangle Zone Classification

**Files:**
- Create: `Tests/BanditStrongholdWallZoneFitSourceGuard.ps1`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`

- [ ] **Step 1: Write the failing runtime source guard**

Require `TryPlanDetailed`, `EnclosedLand`, per-zone tile counting, and the count-based `BanditZoneFact` constructor. Reject the old wall `Min/Max` and zone-center classification.

```powershell
foreach ($token in @('TryPlanDetailed(', '.EnclosedLand',
        'zone.tiles', 'enclosedTiles', 'totalTiles')) {
    if (-not $service.Contains($token)) {
        throw "Stronghold wall-zone fit is missing $token"
    }
}
foreach ($forbidden in @('wallPoints.Min(', 'wallPoints.Max(')) {
    if ($service.Contains($forbidden)) {
        throw "Stronghold still uses wall bounding rectangle: $forbidden"
    }
}
```

- [ ] **Step 2: Run the source guard and verify RED**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
```

Expected: failure reporting missing `TryPlanDetailed(`.

- [ ] **Step 3: Count wall-interior overlap per zone**

Request `CultiwayStyleCityWallPlan` from the wall service. Build a hash set of its enclosed points. For every mother-city zone, count non-null zone tiles and how many of those coordinates exist in the enclosed set, then create the count-based fact.

```csharp
int totalTiles = zone.tiles?.Count(tile => tile != null) ?? 0;
int enclosedTiles = zone.tiles?.Count(tile => tile != null &&
    enclosedLand.Contains(new CultiwayWallPoint(tile.x, tile.y))) ?? 0;
facts.Add(new BanditZoneFact(ZoneKey(zone), enclosedTiles,
    totalTiles, neighbours));
```

Use `wallPlan.WallPoints` for placement and persistence. Do not change state restoration or acquisition rules.

- [ ] **Step 4: Run focused tests and source guard**

Run the two focused console commands and the new source guard. Expected: all exit 0.

### Task 4: Verify, Commit, Deploy, and Launch

**Files:**
- Verify: `AncientWarfare3.csproj`
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run all relevant regressions**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --cultiway-wall
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdTransactionSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release -f net48
git diff --check
```

Expected: focused tests and guards exit 0; build reports zero errors.

- [ ] **Step 2: Commit the implementation**

```powershell
git add Code/core/lineage/CultiwayStyleWallGeometryRules.cs Code/core/lineage/CultiwayStyleCityWallService.cs Code/core/lineage/PeasantRebelBanditStrongholdRules.cs Code/core/lineage/PeasantRebelBanditStrongholdService.cs Tests/AncientWarfare3.Rules.Tests/CultiwayStyleWallGeometryRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
git commit -m "fix: fit bandit zones to stronghold walls"
```

- [ ] **Step 3: Deploy with a timestamped backup**

Stop only the identified WorldBox process if it is still running, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy-local.ps1 -SourceRoot 'F:\WorldBox New Mod\AncientWarfare3.0\.worktrees\peasant-rebel-dual-route' -DestinationRoot 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
```

Verify SHA-256 equality for the four changed production source files.

- [ ] **Step 4: Launch visibly and inspect the fresh log**

Start `D:/SteamLibrary/steamapps/common/worldbox/worldbox.exe` without a hidden-window option. Confirm the main window responds, Ancient Warfare 3 loads, wall-related patches load, and the fresh log contains no C# compile errors or runtime exceptions.

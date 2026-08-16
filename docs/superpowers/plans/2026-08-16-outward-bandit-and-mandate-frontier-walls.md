# Outward Bandit And Mandate Frontier Walls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give bandit city walls a six-tile building margin and make Mandate stone walls follow only real land frontiers against independent non-allied kingdoms.

**Architecture:** Keep `CultiwayStyleCityWallService` as the WorldBox adapter and final wall placer. Add a detached frontier geometry unit beside the existing enclosure geometry, and centralize Mandate diplomatic target classification so city selection, walls, towers, scoring, and patrols use one answer.

**Tech Stack:** C# detached rules on .NET 9, WorldBox net48 runtime APIs, PowerShell source guards, git, and local WorldBox mod deployment.

---

### Task 1: Detached Frontier Geometry

**Files:**
- Create: `Code/core/lineage/CultiwayStyleFrontierWallGeometryRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/CultiwayStyleFrontierWallGeometryRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing tests**

Register the test and linked production file in the rules-test project. Add
`CultiwayStyleFrontierWallGeometryRulesTests.Run()` to `--cultiway-wall`.
Tests must cover a two-layer own-side wall, open frontier segments, diagonal
bridges, a three-tile road passage, terrain/ownership clipping, and an empty
frontier.

```csharp
VerticalContactBuildsTwoOwnSideLayers();
IndependentSegmentsDoNotBecomeAClosedCityRing();
DiagonalSegmentsReceiveAnOrthogonalBridge();
RoadCrossingCarvesAThreeTilePassage();
InvalidAndForeignTilesAreClipped();
EmptyFrontierProducesNoWalls();
```

- [ ] **Step 2: Verify RED**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' -- --cultiway-wall
```

Expected: compile failure because the frontier input and calculator do not
exist.

- [ ] **Step 3: Implement minimal geometry**

Create `CultiwayFrontierWallGeometryInput` with `CityLand`, `Passable`,
`FrontierSeeds`, `Roads`, and `WallWidth`. `Compute` filters seeds to valid
city land, grows the requested number of cardinal layers inside valid city
land, seals diagonal gaps with deterministic orthogonal bridges, and removes
a 3x3 wall area around every road tile intersecting the wall. Return points
sorted by X then Y. Do not create fallback or dock gates.

```csharp
var available = input.CityLand.Where(input.Passable.Contains).ToHashSet();
var layer = input.FrontierSeeds.Where(available.Contains).ToHashSet();
for (int depth = 0; depth < input.WallWidth && layer.Count > 0; depth++)
{
    walls.UnionWith(layer);
    layer = layer.SelectMany(CardinalNeighbours)
        .Where(point => available.Contains(point) && !walls.Contains(point))
        .ToHashSet();
}
```

- [ ] **Step 4: Verify GREEN and commit**

Run `--cultiway-wall`; expect `Cultiway wall geometry rules passed.` Commit
the four files as `feat: add detached frontier wall geometry`.

### Task 2: Detached Diplomatic Frontier Policy

**Files:**
- Modify: `Code/core/lineage/MandateBorderWallRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/MandateBorderWallRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing policy tests**

Link `MandateBorderWallRules.cs`, add its tests to `--cultiway-wall`, and
assert `ShouldFortifyKingdom` accepts an independent living non-allied state
and rejects missing, destroyed, neutral, same-system, allied, and tributary
states. Also assert `IsExternalLandBorderNeighbor` rejects water, lava, and
blocked neighboring tiles.

- [ ] **Step 2: Verify RED**

Run `--cultiway-wall`. Expected: compile failure because the kingdom-only
policy does not exist.

- [ ] **Step 3: Extend the pure predicate**

```csharp
public static bool ShouldFortifyKingdom(bool pNeighborHasKingdom,
    bool pNeighborAlive, bool pNeighborNeutral,
    bool pSameMandateSystem, bool pSameAlliance,
    bool pMandateTributary)
{
    return pNeighborHasKingdom && pNeighborAlive && !pNeighborNeutral &&
        !pSameMandateSystem && !pSameAlliance && !pMandateTributary;
}

public static bool IsExternalLandBorderNeighbor(
    bool pFortificationTarget, bool pNeighborHasCity,
    bool pNeighborGround, bool pNeighborLiquid,
    bool pNeighborLava, bool pNeighborBlock)
{
    if (!pNeighborHasCity || !pFortificationTarget) return false;
    return pNeighborGround && !pNeighborLiquid && !pNeighborLava &&
        !pNeighborBlock;
}
```

- [ ] **Step 4: Verify GREEN and commit**

Run `--cultiway-wall`, then commit as
`test: specify Mandate frontier diplomacy`.

### Task 3: Runtime Adapter And Bandit Margin

**Files:**
- Modify: `Code/core/lineage/CultiwayStyleCityWallService.cs`
- Modify: `Tests/PeasantRebelRouteRuntimeSourceGuard.ps1`

- [ ] **Step 1: Add failing source guards**

Require exact `private const int WallMargin = 6;`,
`CultiwayStyleFrontierWallGeometryRules.Compute(`, `TryPlanFrontier(`, and
`BuildFrontier(`. Preserve guards for original assets, `setTopTileType`, and
the absence of `MapAction.terraformTop`.

- [ ] **Step 2: Verify RED**

```powershell
& './Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'
```

Expected: failure on the margin or missing frontier entry point.

- [ ] **Step 3: Implement runtime extraction and placement**

Change `WallMargin` from 3 to 6. Add `TryPlanFrontier(City, Kingdom, int,
out IReadOnlyList<CultiwayWallPoint>)`: collect passable city land and roads,
scan each own tile's cardinal `neighbours`, and seed a point only when the
neighbor is qualifying foreign ground. Call detached frontier geometry and
revalidate every final point with `CanPlaceAt`.

Add `BuildFrontier(City, Kingdom, TopTileType, int)`. Factor final placement
into one private method shared by enclosure `Build` and frontier
`BuildFrontier`; call `tile.setTopTileType(pWallType)` only when needed and
return planned points plus actual changed count.

- [ ] **Step 4: Verify and commit**

Run the source guard, `--cultiway-wall`, and net48 build. Expect all to pass
and zero build warnings/errors. Commit as
`feat: add shared frontier wall planning`.

### Task 4: Mandate Runtime Integration

**Files:**
- Modify: `Code/core/lineage/MandateBorderDefenseService.cs`
- Modify: `Tests/PeasantRebelRouteRuntimeSourceGuard.ps1`

- [ ] **Step 1: Add failing integration guards**

Require `CultiwayStyleCityWallService.BuildFrontier(`,
`IsFortificationTarget(`, `Alliance.isSame(`, and
`VassalService.GetTributarySuzerain(`. Forbid enclosure `Build(` inside the
isolated `BuildBorderWalls` method. Require the shared target predicate inside
`HasOutsideNeighbour`, `BorderScore`, and `TouchesExternalLandBorder`.

- [ ] **Step 2: Verify RED**

Run the source guard. Expected: failure because Mandate still builds complete
city rings.

- [ ] **Step 3: Implement the target predicate**

Add `internal static bool IsFortificationTarget(Kingdom pMandate, Kingdom
pNeighbour)`. Resolve runtime relationship facts and return
`MandateBorderWallRules.ShouldFortifyKingdom(...)`. It rejects missing,
destroyed, or neutral kingdoms, the Mandate itself, any kingdom whose root
suzerain is the Mandate, a direct Mandate tributary, and any kingdom in the
Mandate alliance. Every other living kingdom is a target even without an
active war.

```csharp
bool sameSystem = pNeighbour == pMandate ||
    VassalService.GetRootSuzerain(pNeighbour) == pMandate;
bool sameAlliance = Alliance.isSame(
    pMandate?.getAlliance(), pNeighbour?.getAlliance());
bool mandateTributary =
    VassalService.GetTributarySuzerain(pNeighbour) == pMandate;
```

- [ ] **Step 4: Align every border consumer**

Use the predicate in `HasOutsideNeighbour`, `BorderScore`, and tile-level
`TouchesExternalLandBorder`. Existing tower candidates and patrol selection
then inherit it. Change wall construction to accept `pMandate` and call:

```csharp
return CultiwayStyleCityWallService.BuildFrontier(
    pCity, pMandate, wall, 2).Changed;
```

Keep existing city limits, guards, towers, history, and the rule that old
walls are never removed.

- [ ] **Step 5: Verify and commit**

Run source guard, `--cultiway-wall`, `--peasant-rebel-routes`, net48 build,
and `git diff --check`. Commit as
`feat: build Mandate walls on hostile frontiers`.

### Task 5: Verification And Deployment

**Files:**
- Verify: all files above
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run fresh verification**

```powershell
& './Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'
$env:DOTNET_ROLL_FORWARD='Major'
dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' -- --cultiway-wall
dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' -- --bandit-outlaw-names
dotnet run --project 'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' -- --peasant-rebel-routes
dotnet build AncientWarfare3.csproj --no-restore -p:TargetFrameworkVersion=v4.8.1
git diff --check
```

Expected: all guards and slices pass; build has zero warnings/errors.

- [ ] **Step 2: Deploy from the explicit worktree**

Stop an existing WorldBox process gracefully. Deploy with `deploy-local.ps1`,
verify full path/SHA256 parity with `VerifySourceDeployment.ps1`, and build
the deployed `AncientWarfare3.csproj` for net48. Expect a timestamped backup,
complete parity, and zero warnings/errors.

- [ ] **Step 3: Start visible WorldBox**

Start `D:/SteamLibrary/steamapps/common/worldbox/worldbox.exe` with its game
directory as the working directory. Wait for a nonzero main-window handle,
confirm the current player log contains AW3 `Loaded`, and leave the visible
game running for manual wall acceptance.

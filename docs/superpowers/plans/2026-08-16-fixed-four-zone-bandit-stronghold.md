# Fixed Four-Zone Bandit Stronghold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every new bandit stronghold own exactly four connected native zones and remove its wooden wall, restoring prior terrain, when suppression destroys the stronghold.

**Architecture:** A pure graph rule ranks connected four-zone candidates with 2 by 2 blocks first. Runtime preflight tries those candidates in order until the existing zone-wall adapter can build four gates. Transaction snapshots persist each wall tile's previous top-type ID, and the existing `CompleteFall` lifecycle restores only tiles that are still stronghold wooden walls.

**Tech Stack:** C# 9, WorldBox/NeoModLoader runtime APIs, Newtonsoft.Json persistence, net9 pure-rule test harness, PowerShell source guards, net48 production build.

---

### Task 1: Rank Exact Four-Zone Candidates

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdRules.cs`

- [ ] **Step 1: Replace variable-size tests with exact-four candidate tests**

Add tests that build zone facts with native coordinates and cardinal neighbour keys, then assert:

```csharp
IReadOnlyList<IReadOnlyList<string>> ranked =
    PeasantRebelBanditStrongholdRules.RankFourZoneCandidates(
        zones, "1:1");
True(ranked.Count > 0, "a connected four-zone candidate exists");
True(new HashSet<string>(ranked[0]).SetEquals(new[]
{
    "1:1", "2:1", "1:2", "2:2"
}), "a 2 by 2 block containing the seed ranks first");
Equal(4, ranked[0].Count, "every candidate owns exactly four zones");
```

Add an irregular chain test that has no 2 by 2 block and expects one compact connected four-zone result. Add rejection tests for only three connected zones and for `IsViableSplit(4, 0)`. Replace the old enclosure-overlap flood-fill expectation.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
```

Expected: compilation fails because `RankFourZoneCandidates` and coordinate-aware `BanditZoneFact` do not exist.

- [ ] **Step 3: Implement bounded connected-subset ranking**

Change `BanditZoneFact` to retain `X`, `Y`, and neighbour keys. Implement:

```csharp
public static IReadOnlyList<IReadOnlyList<string>> RankFourZoneCandidates(
    IReadOnlyList<BanditZoneFact> zones, string centerKey)
```

Enumerate unique connected sets containing the seed until each set has four keys. Canonicalize each set by sorted keys to prevent duplicate search paths. Rank completed sets by:

```csharp
IsTwoByTwo(candidate) ? 0 : 1,
BoundingArea(candidate),
candidate.Sum(zone => Manhattan(zone, center)),
CanonicalKey(candidate)
```

Return only four-key candidates. Keep `IsViableSplit` and change callers to require `interiorCount == 4 && exteriorCount > 0`. Add:

```csharp
public static bool ShouldRestoreWall(string currentTopTypeId)
{
    return string.Equals(currentTopTypeId, "wall_wild",
        StringComparison.Ordinal);
}
```

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Task 1 focused command. Expected: `Bandit stronghold and raid rules passed.`

- [ ] **Step 5: Commit the pure rules**

```powershell
git add Code/core/lineage/PeasantRebelBanditStrongholdRules.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt
git commit -m "fix: rank fixed four-zone bandit strongholds"
```

### Task 2: Select The First Wallable Four-Zone Candidate

**Files:**
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Modify: `Tests/BanditStrongholdWallZoneFitSourceGuard.ps1`

- [ ] **Step 1: Make the source guard require exact-four selection**

Require `RankFourZoneCandidates(`, `candidateKeys.Count == 4`, a candidate loop before `PeasantRebelBanditZoneWallService.TryPlan(`, and `interior.Count == 4`. Forbid `SelectZoneAlignedKeys(`, `EnclosedLand`, and `enclosedTiles` in stronghold preflight.

- [ ] **Step 2: Run the source guard and verify RED**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
```

Expected: failure reporting missing fixed four-zone candidate selection.

- [ ] **Step 3: Replace enclosure flood-fill with ranked candidate probing**

Build facts from mother zones using `zone.x`, `zone.y`, and cardinal `zone.neighbours`. Require at least five mother zones. Iterate ranked candidates and call the existing wall adapter for each mapped four-zone set:

```csharp
foreach (IReadOnlyList<string> candidateKeys in candidates)
{
    if (candidateKeys.Count != 4) continue;
    List<TileZone> candidate = motherZones.Where(zone =>
        candidateKeys.Contains(ZoneKey(zone))).ToList();
    if (candidate.Count != 4) continue;
    if (!PeasantRebelBanditZoneWallService.TryPlan(
            pMother, candidate, strongholdCenter,
            out BanditZoneWallPlan candidateWall) ||
        candidateWall.WallPoints.Count == 0) continue;
    interior = candidate;
    zoneWallPlan = candidateWall;
    break;
}
```

If no candidate exists, return `aw_bandit_stronghold_split_failed`; if candidates exist but none support four gates, return `aw_bandit_stronghold_wall_failed`. Log stage, mother zone count, candidate count, and failure key through `ModClass.LogWarning` without logging every tile.

- [ ] **Step 4: Run guards, focused tests, and net48 build**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdTransactionSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release -f net48 --no-restore
```

Expected: all focused commands pass and the build reports 0 errors.

- [ ] **Step 5: Commit runtime selection**

```powershell
git add Code/core/lineage/PeasantRebelBanditStrongholdService.cs Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
git commit -m "fix: create bandit strongholds from four zones"
```

### Task 3: Persist And Restore Replaced Wall Terrain

**Files:**
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdState.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt`
- Modify: `Tests/BanditStrongholdTransactionSourceGuard.ps1`

- [ ] **Step 1: Add failing lifecycle rule and source-guard assertions**

Add pure assertions:

```csharp
True(PeasantRebelBanditStrongholdRules.ShouldRestoreWall("wall_wild"),
    "an unchanged stronghold wall is restored during suppression");
False(PeasantRebelBanditStrongholdRules.ShouldRestoreWall("road"),
    "later terrain changes are preserved");
False(PeasantRebelBanditStrongholdRules.ShouldRestoreWall(null),
    "an already removed wall is ignored");
```

Update the transaction guard to require `OriginalTopTypeId`, schema version 3, `RestoreWalls(` inside `CompleteFall`, a current `wall_wild` guard, `AssetManager.top_tiles.get`, and `setTopTileType(originalTopType)`. Require `RestoreWalls` before `BanditStrongholdPhase.Completed` and city removal.

- [ ] **Step 2: Run the tests and guard and verify RED**

Run the focused test and transaction guard. Expected: missing lifecycle persistence and cleanup failures.

- [ ] **Step 3: Persist transaction snapshots in schema 3**

Add `public string OriginalTopTypeId = "";` to `BanditStrongholdPoint` and bump `CurrentSchemaVersion` to 3. Change `BuildState` to consume transaction wall snapshots so every state point is built as:

```csharp
new BanditStrongholdPoint
{
    X = snapshot.Tile.x,
    Y = snapshot.Tile.y,
    OriginalTopTypeId = snapshot.TopType?.id ?? ""
}
```

Schema 1 and 2 remain readable; their missing value normalizes to an empty string and therefore restores no prior top layer.

- [ ] **Step 4: Restore walls from the existing fall lifecycle**

Implement `RestoreWalls(PeasantRebelBanditStrongholdState state)`. For each point, resolve the tile, skip it unless `ShouldRestoreWall(tile.top_type?.id)` returns true, resolve a non-empty original ID through `AssetManager.top_tiles.get`, and call `tile.setTopTileType(originalTopType)`. Call it after zones return and before setting the state to completed. Catch per-tile lookup failures, log once with counts, and allow later `RestoreRuntime` passes to retry unchanged walls.

- [ ] **Step 5: Verify lifecycle behavior and build**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdTransactionSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release -f net48 --no-restore
```

Expected: all commands pass and the build reports 0 errors and 0 warnings.

- [ ] **Step 6: Commit wall lifecycle cleanup**

```powershell
git add Code/core/lineage/PeasantRebelBanditStrongholdState.cs Code/core/lineage/PeasantRebelBanditStrongholdService.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt Tests/BanditStrongholdTransactionSourceGuard.ps1
git commit -m "fix: remove bandit walls after suppression"
```

### Task 4: Verify, Deploy, And Launch

**Files:**
- Verify: `Code/core/lineage/PeasantRebelBanditStrongholdRules.cs`
- Verify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Verify: `Code/core/lineage/PeasantRebelBanditStrongholdState.cs`
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run fresh final verification**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --cultiway-wall
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdTransactionSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release -f net48 --no-restore
git diff --check
git status --short
```

Expected: tests and guards pass, build has 0 errors and 0 warnings, diff check is empty, and status has no unintended files.

- [ ] **Step 2: Review the complete implementation diff**

```powershell
git diff ae00c688..HEAD -- Code/core/lineage Tests
```

Confirm exact-four selection, first-wallable fallback, schema compatibility, conditional wall restoration, and no changes to mandate/Cultiway shared wall callers.

- [ ] **Step 3: Deploy with backup and hash verification**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy-local.ps1 -SourceRoot 'F:\WorldBox New Mod\AncientWarfare3.0\.worktrees\peasant-rebel-dual-route' -DestinationRoot 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tests\VerifySourceDeployment.ps1 -SourceRoot 'F:\WorldBox New Mod\AncientWarfare3.0\.worktrees\peasant-rebel-dual-route' -DestinationRoot 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
```

Expected: deployment reports a timestamped backup and all production-file SHA-256 hashes match.

- [ ] **Step 4: Launch WorldBox visibly and inspect startup**

Start `D:/SteamLibrary/steamapps/common/worldbox/worldbox.exe` with `Start-Process` and no hidden-window flag. Wait for a non-zero main-window handle, then inspect the new `Player.log` session for compilation errors or exceptions involving bandit strongholds.

- [ ] **Step 5: Gameplay acceptance**

Use the god power on a mother city with at least five zones. Confirm the new stronghold owns exactly four zones, its wall has four three-tile openings, and creation succeeds when an alternate compact candidate is wallable. Let an enemy capture it and confirm the stronghold city disappears, its zones return to the mother city, and unchanged wooden wall tiles restore their previous terrain.

# Bandit Nine-Zone History, Towers, And Native Raids Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver one cohesive bandit-stronghold repair that creates exact nine-zone strongholds with architecture-matched gate towers, records non-territorial establishment and suppression chronicles, treats capture or zero population as suppression, and moves raid food through native actor inventories.

**Architecture:** Pure rule classes decide zone blocks, gate centers, suppressor attribution, and cargo distribution. `PeasantRebelBanditStrongholdService` remains the sole stronghold mutation boundary; `PeasantRebelBanditRaidService` remains the mission coordinator while native WorldBox APIs own movement, storage, actor inventory, death transfer, tower combat, and city removal. Persisted phase markers and projection keys make creation, fall, history, and cargo recovery idempotent.

**Tech Stack:** C# 11/net48, WorldBox `TileZone`/`BuildingManager`/`Actor`/`City` APIs, Harmony, Newtonsoft.Json, SQLite-backed `HistoryWriter`, net9 detached rule tests, PowerShell source guards.

---

## File Map

- `Code/core/lineage/PeasantRebelBanditStrongholdRules.cs`: exact three-by-three zone selection and pure fall attribution rules.
- `Code/core/lineage/PeasantRebelBanditZoneWallRules.cs`: return four gate centers as part of the wall plan.
- `Code/core/lineage/PeasantRebelBanditStrongholdPlan.cs`: carry planned gate-tower tiles/assets into commit.
- `Code/core/lineage/PeasantRebelBanditStrongholdState.cs`: schema-4 tower records, last hostile killer, and raid cargo audit data.
- `Code/core/lineage/PeasantRebelBanditStateStore.cs`: backward-compatible normalization.
- `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`: tower create/rollback/remove, chronicle writes, and unified fall transaction.
- `Code/core/lineage/PeasantRebelBanditRaidRules.cs`: cargo carrier filtering and stable distribution.
- `Code/core/lineage/PeasantRebelBanditRaidService.cs`: native actor inventory custody and unload.
- `Code/core/lineage/HistoryWriter.cs`: idempotent city history projection API.
- `Code/core/lineage/ChronicleKeys.cs`: four dedicated event constants.
- `Code/core/lineage/HistoryLocalizationRules.cs`: simplified Chinese, English, and traditional Chinese event text.
- `Code/patch/AW_ActorDeathPatch.cs`: capture last hostile killer and trigger population-zero settlement after death.
- `Code/patch/AW_CityOccupationAccelerationPatch.cs`: preserve direct occupier as suppressor.
- `Code/core/atlas/KingdomAtlasHistoryService.cs`: unchanged production whitelist, protected by a guard.
- `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt`: nine-zone and attribution tests.
- `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditZoneWallRulesTests.cs.txt`: gate-center tests.
- `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditRaidRulesTests.cs.txt`: physical cargo distribution tests.
- `Tests/BanditStrongholdHistoryAndTowerSourceGuard.ps1`: lifecycle/history/atlas/tower integration guard.
- `Tests/BanditRaidSettlementSourceGuard.ps1`: update virtual-cargo assertions to native inventory assertions.

### Task 1: Exact Three-By-Three Zone Selection

- [ ] **Step 1: Replace four-zone expectations with failing nine-zone tests.**

Add tests that build a complete coordinate grid and assert the returned first candidate is the centered nine-zone block:

```csharp
IReadOnlyList<IReadOnlyList<string>> ranked =
    PeasantRebelBanditStrongholdRules.RankNineZoneCandidates(
        Grid(0, 0, 3, 3), "1:1");
Equal(9, ranked[0].Count, "stronghold owns nine zones");
True(ranked[0].Contains("0:0") && ranked[0].Contains("2:2"),
    "centered three-by-three block ranks first");
Equal(0, PeasantRebelBanditStrongholdRules
    .RankNineZoneCandidates(GridMissing(2, 2), "1:1").Count,
    "an incomplete block is rejected");
True(PeasantRebelBanditStrongholdRules.IsViableSplit(9, 1),
    "nine zones plus one mother zone is viable");
```

- [ ] **Step 2: Run the focused test and confirm failure.**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
```

Expected: compilation fails because `RankNineZoneCandidates` does not exist or assertions still observe four-zone behavior.

- [ ] **Step 3: Implement finite nine-zone block enumeration.**

Replace combinatorial four-zone expansion with blocks whose lower-left coordinate can be at most two cells from the core:

```csharp
public static IReadOnlyList<IReadOnlyList<string>>
    RankNineZoneCandidates(IReadOnlyList<BanditZoneFact> zones,
        string centerKey)
{
    // Index by (X,Y), enumerate the nine possible 3x3 origins that
    // contain the core, require all nine coordinates, then rank centered
    // core first and use stable coordinate ordering.
}

public static bool IsViableSplit(int interiorCount, int exteriorCount)
{
    return interiorCount == 9 && exteriorCount > 0;
}
```

Update `TryPlan` to require at least ten mother zones, call the new API, and accept only nine-zone candidates.

- [ ] **Step 4: Run focused tests and the existing transaction guard.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdTransactionSourceGuard.ps1
```

Expected: both commands exit 0.

- [ ] **Step 5: Commit the zone change.**

```powershell
git add Code/core/lineage/PeasantRebelBanditStrongholdRules.cs Code/core/lineage/PeasantRebelBanditStrongholdService.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt Tests/BanditStrongholdTransactionSourceGuard.ps1
git commit -m "feat: expand bandit strongholds to nine zones"
```

### Task 2: Four Gate Centers And Persisted Tower Facts

- [ ] **Step 1: Write failing gate-center and schema tests.**

Extend the wall test to require four distinct centers and prove only the two side tiles remain open around each center:

```csharp
Equal(4, plan.GateCenters.Count, "four cardinal gates expose centers");
foreach (CultiwayWallPoint gate in plan.GateCenters)
{
    False(plan.WallPoints.Contains(gate), "tower center is carved from wall");
    True(plan.ClosedWallPoints.Contains(gate),
        "tower center belongs to the original perimeter");
}
```

Update `BanditStrongholdPersistenceSourceGuard.ps1` to require:

```powershell
foreach ($token in @('CurrentSchemaVersion = 4',
        'BanditStrongholdTower', 'TowerBuildingId', 'AssetId',
        'LastHostileKillerKingdomId')) {
    if (-not $state.Contains($token)) { throw "Missing $token" }
}
```

- [ ] **Step 2: Run tests/guard and confirm failure.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdPersistenceSourceGuard.ps1
```

Expected: `GateCenters` and schema-4 tokens are absent.

- [ ] **Step 3: Return gate centers and normalize schema-4 state.**

Change the plan model and carving method so `CarveCardinalGate` returns its selected center:

```csharp
public BanditZoneWallPlan(
    IReadOnlyList<CultiwayWallPoint> closed,
    IReadOnlyList<CultiwayWallPoint> opened,
    IReadOnlyList<CultiwayWallPoint> gateCenters)
{
    ClosedWallPoints = closed;
    WallPoints = opened;
    GateCenters = gateCenters;
}
```

Add persisted tower records and attribution:

```csharp
internal sealed class BanditStrongholdTower
{
    public long TowerBuildingId = -1L;
    public int X;
    public int Y;
    public string AssetId = "";
}

public List<BanditStrongholdTower> Towers = new();
public long LastHostileKillerKingdomId = -1L;
```

Normalize null lists/asset IDs while accepting schema versions 1 through 4.

- [ ] **Step 4: Run focused tests and persistence guard.**

Expected: both commands from Step 2 exit 0.

- [ ] **Step 5: Commit gate/state changes.**

```powershell
git add Code/core/lineage/PeasantRebelBanditZoneWallRules.cs Code/core/lineage/PeasantRebelBanditStrongholdState.cs Code/core/lineage/PeasantRebelBanditStateStore.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditZoneWallRulesTests.cs.txt Tests/BanditStrongholdPersistenceSourceGuard.ps1
git commit -m "feat: persist bandit gate tower positions"
```

### Task 3: Native Architecture-Matched Gate Towers

- [ ] **Step 1: Add a failing tower lifecycle source guard.**

Create `Tests/BanditStrongholdHistoryAndTowerSourceGuard.ps1` and require these tokens and ordering:

```powershell
foreach ($token in @('order_watch_tower',
        'architecture_asset.getBuilding(',
        'World.world.buildings.addBuilding(',
        'building.setKingdom(', 'Towers.Add(',
        'RemoveStrongholdTowers(',
        'World.world.buildings.removeObject(')) {
    if (-not $service.Contains($token)) { throw "Missing $token" }
}
```

Also assert tower creation occurs after `newCityEvent` and before state is persisted as `Active`, and tower removal occurs before wall restoration and city removal.

- [ ] **Step 2: Run the new guard and confirm failure.**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdHistoryAndTowerSourceGuard.ps1
```

Expected: failure reports missing architecture/tower lifecycle tokens.

- [ ] **Step 3: Add tower preflight, commit, rollback, and fall cleanup.**

Resolve the tower through the new stronghold's actor architecture rather than species IDs:

```csharp
BuildingAsset towerAsset = stronghold.getActorAsset()
    ?.architecture_asset?.getBuilding("order_watch_tower");
Building tower = World.world.buildings.addBuilding(
    towerAsset, gateTile, pCheckForBuild: true);
tower.setKingdom(plan.Context.Bandit);
state.Towers.Add(new BanditStrongholdTower
{
    TowerBuildingId = tower.getID(),
    X = gateTile.x,
    Y = gateTile.y,
    AssetId = towerAsset.id
});
```

Preflight validates four distinct buildable gate tiles and a non-null tower asset. Transaction rollback removes created towers. `CompleteFall` removes live towers and ruins referenced by state before calling `RestoreWalls`.

- [ ] **Step 4: Run all stronghold guards and net48 build.**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdHistoryAndTowerSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdTransactionSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdFallRestoreSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release -f net48 --no-restore
```

Expected: guards pass and build exits 0 with no warnings/errors introduced by this task.

- [ ] **Step 5: Commit tower runtime changes.**

```powershell
git add Code/core/lineage/PeasantRebelBanditStrongholdPlan.cs Code/core/lineage/PeasantRebelBanditStrongholdService.cs Tests/BanditStrongholdHistoryAndTowerSourceGuard.ps1
git commit -m "feat: add native gate towers to bandit strongholds"
```

### Task 4: Idempotent Chronicles And Zero-Population Suppression

- [ ] **Step 1: Write failing pure attribution tests and source assertions.**

Add pure tests for precedence:

```csharp
Equal(31L, ResolveSuppressor(lastKiller: 31, origin: 20,
    originAtWar: true), "last hostile killer wins attribution");
Equal(20L, ResolveSuppressor(lastKiller: -1, origin: 20,
    originAtWar: true), "warring origin is fallback");
Equal(-1L, ResolveSuppressor(lastKiller: -1, origin: 20,
    originAtWar: false), "starvation can have no victor");
```

Extend the source guard to require four event strings, `TryRecordCity`, projection keys, actor-death integration, and exclusion from atlas SQL:

```powershell
$events = @('bandit_stronghold_established',
    'bandit_suppression_victory', 'bandit_suppressed',
    'bandit_stronghold_suppressed')
foreach ($event in $events) {
    if (-not ($keys + $service).Contains($event)) { throw "Missing $event" }
    if ($atlas.Contains($event)) { throw "$event entered atlas queries" }
}
```

- [ ] **Step 2: Run focused tests/guard and confirm failure.**

Expected: attribution API, event constants, `TryRecordCity`, and death trigger are missing.

- [ ] **Step 3: Add history APIs, localized events, and unified fall inputs.**

Add a city projection overload mirroring `TryRecordKingdom`:

```csharp
public static bool TryRecordCity(City city, Kingdom context,
    string eventType, HistoryText content, HistoryTarget target,
    string projectionKey)
{
    return InsertProjection(CityHistoryTableItem.GetTableName(), context,
        eventType, content, city.data.name, target,
        projectionKey, ColumnVal.Create("CITY_ID", city.data.id),
        ColumnVal.Create("KINGDOM_NAME", context?.name ?? ""),
        ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(context)));
}
```

Add event constants/localization and record establishment after successful creation. Change capture to pass `pOccupier` into `CompleteFall`. Extend actor death state with the dying city ID and hostile attacker kingdom ID captured before `Actor.die` clears combat state. In `Die_Postfix`, call a stronghold service method that persists the hostile killer and, when the resolved city population is zero, enters `CompleteFall`.

Use projection keys:

```text
bandit-stronghold-established:<cityId>
bandit-suppressed-city:<cityId>
bandit-suppressed-kingdom:<banditId>:<cityId>
bandit-suppression-victory:<suppressorId>:<cityId>
```

Remove the old generic `RecordDestruction` call so kingdom teardown cannot duplicate suppression history.

- [ ] **Step 4: Run focused tests, history/tower guard, atlas guard, and build.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdHistoryAndTowerSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/KingdomAtlasSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release -f net48 --no-restore
```

Expected: all exit 0; custom events are absent from atlas territorial SQL.

- [ ] **Step 5: Commit history and zero-population settlement.**

```powershell
git add Code/core/lineage/HistoryWriter.cs Code/core/lineage/ChronicleKeys.cs Code/core/lineage/HistoryLocalizationRules.cs Code/core/lineage/PeasantRebelBanditRoute.cs Code/core/lineage/PeasantRebelBanditStrongholdRules.cs Code/core/lineage/PeasantRebelBanditStrongholdService.cs Code/core/lineage/PeasantRebelRouteService.cs Code/patch/AW_ActorDeathPatch.cs Code/patch/AW_CityOccupationAccelerationPatch.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt Tests/BanditStrongholdHistoryAndTowerSourceGuard.ps1
git commit -m "feat: record and settle bandit suppression"
```

### Task 5: Native Actor-Inventory Raid Cargo

- [ ] **Step 1: Add failing carrier and distribution tests.**

Add pure facts and assertions:

```csharp
False(PeasantRebelBanditRaidRules.CanJoinRaid(
    alive: true, warrior: true, ruler: false, heir: false,
    carryingResources: true), "preloaded warriors are excluded");
IReadOnlyDictionary<long, int> shares =
    PeasantRebelBanditRaidRules.DistributeCargo(
        new long[] { 9, 3, 6 }, 8);
Equal(3, shares[3], "lowest actor ID receives first remainder");
Equal(3, shares[6], "second actor receives second remainder");
Equal(2, shares[9], "last actor receives base share");
```

Rewrite `BanditRaidSettlementSourceGuard.ps1` to require `isCarryingResources`, `addToInventory`, `giveInventoryResourcesToCity`, and a per-actor audit manifest, while forbidding direct delivery from the old virtual cargo dictionary.

- [ ] **Step 2: Run focused tests and guard, confirm failure.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditRaidSettlementSourceGuard.ps1
```

Expected: carrier/distribution APIs are absent and the runtime still uses virtual cargo.

- [ ] **Step 3: Move custody to native actor inventories.**

Filter preloaded actors, remove food from the victim with `City.takeResource`, then distribute the actual removed amount among surviving actor IDs and call:

```csharp
carrier.addToInventory(resourceId, share);
```

Persist an audit manifest keyed by actor ID/resource ID after inventory mutation. On return, call `carrier.giveInventoryResourcesToCity()` only for carriers inside the stronghold, then clear the matching audit entry. Native death logic owns transfer to killers; missing/dead carriers are removed from the manifest without restoring food. Enter cooldown only when every audit entry is resolved.

- [ ] **Step 4: Run raid tests/guards and net48 build.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditRaidRuntimeSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditRaidSettlementSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release -f net48 --no-restore
```

Expected: all exit 0.

- [ ] **Step 5: Commit native cargo changes.**

```powershell
git add Code/core/lineage/PeasantRebelBanditRaidRules.cs Code/core/lineage/PeasantRebelBanditRaidService.cs Code/core/lineage/PeasantRebelBanditStrongholdState.cs Code/core/lineage/PeasantRebelBanditStateStore.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditRaidRulesTests.cs.txt Tests/BanditRaidRuntimeSourceGuard.ps1 Tests/BanditRaidSettlementSourceGuard.ps1
git commit -m "feat: carry bandit raid food in actor inventories"
```

### Task 6: Full Verification, Deployment, And Visible Launch

- [ ] **Step 1: Run all detached rules tests.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: all tests pass.

- [ ] **Step 2: Run every affected source guard.**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdTransactionSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdPersistenceSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdWallZoneFitSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdFallRestoreSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdHistoryAndTowerSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditRaidRuntimeSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditRaidSettlementSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/KingdomAtlasSourceGuard.ps1
```

Expected: every guard prints its pass message and exits 0.

- [ ] **Step 3: Build production net48 output.**

```powershell
dotnet build AncientWarfare3.csproj -c Release -f net48 --no-restore
```

Expected: build succeeds with 0 errors and 0 warnings.

- [ ] **Step 4: Commit any final guard-only corrections.**

```powershell
git status --short
git add Code Tests
git commit -m "test: verify bandit stronghold lifecycle repair"
```

Skip the commit when `git status --short` is empty.

- [ ] **Step 5: Deploy with backup and verify source parity.**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy-local.ps1 -SourceRoot 'F:\WorldBox New Mod\AncientWarfare3.0\.worktrees\peasant-rebel-dual-route' -DestinationRoot 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tests\VerifySourceDeployment.ps1 -SourceRoot 'F:\WorldBox New Mod\AncientWarfare3.0\.worktrees\peasant-rebel-dual-route' -DestinationRoot 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
```

Expected: deployment creates a timestamped backup and verification reports matching hashes.

- [ ] **Step 6: Launch WorldBox visibly and inspect the new log session.**

Stop only the identified existing WorldBox process, start `D:\SteamLibrary\steamapps\common\worldbox\worldbox.exe` without a hidden-window flag, wait for a non-zero main-window handle, and inspect the fresh `Player.log` tail. Expected: the process is responsive and no compilation/runtime exception mentions bandit strongholds, gate towers, history projections, or raid cargo.

# Enclosed Unowned Zone Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically assign a fully enclosed unowned land Zone to the deterministic best neighbouring city when all four cardinal neighbours belong to one kingdom.

**Architecture:** A game-independent rules class decides eligibility, target ranking, and work budgets. A runtime service coalesces ownership-change coordinates and drains a bounded queue from the authoritative simulation cycle; a Harmony patch observes `TileZone.setCity` and starts one bounded repair sweep after each world load.

The acceptance boundary is explicit: all four cardinal neighbours must belong
to the same kingdom, a world edge cannot count as enclosure, and a multiplayer
replica remains read-only through the existing authority-cycle gate.

**Tech Stack:** C# 11, .NET Framework 4.8 mod assembly, Harmony, isolated .NET 9 console tests, PowerShell source guards.

---

## File Structure

- Create `Code/core/lineage/EnclosedUnownedZoneRules.cs`: pure eligibility, ranking, and budget rules with no game dependencies.
- Create `Code/core/lineage/EnclosedUnownedZoneRepairService.cs`: coordinate queue, initial sweep, world-object validation, and original `City.addZone` call.
- Create `Code/patch/AW_EnclosedUnownedZonePatch.cs`: ownership observation and world-loaded event registration only.
- Modify `Code/core/performance/AWAuthorityCycleService.cs`: drain and reset the repair service within the existing authority gate.
- Create `Tests/EnclosedUnownedZoneRulesTests.cs.txt`: executable rule and source-integration regression tests.
- Create `Tests/EnclosedUnownedZoneRulesTests.csproj`: isolated test project.
- Create `Tests/EnclosedUnownedZoneSourceGuard.ps1`: performance and lifecycle source guards.

### Task 1: Add Failing Rule Tests

**Files:**
- Create: `Tests/EnclosedUnownedZoneRulesTests.cs.txt`
- Create: `Tests/EnclosedUnownedZoneRulesTests.csproj`

- [ ] **Step 1: Create the isolated test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="EnclosedUnownedZoneRulesTests.cs.txt" />
    <Compile Include="..\Code\core\lineage\EnclosedUnownedZoneRules.cs" Link="EnclosedUnownedZoneRules.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write tests against the wished-for rule API**

Create four `EnclosedZoneNeighbourFacts` values for each case and call:

```csharp
long selected = EnclosedUnownedZoneRules.SelectTargetCity(
    zoneAlreadyOwned: false, worldEdge: false, groundTileCount: 32,
    cardinalNeighbourCount: 4, zoneX: 10, zoneY: 10,
    new[] { north, south, west, east });
```

Assert these exact behaviours:

```csharp
Equal(10L, selected, "one city enclosing all sides is selected");
Equal(20L, selected, "same-kingdom city sharing three sides wins");
Equal(30L, selected, "equal shared sides use nearest centre");
Equal(40L, selected, "equal distance uses lowest stable city id");
Equal(-1L, selected, "mixed kingdoms remain disputed");
Equal(-1L, selected, "an unowned cardinal exit remains open");
Equal(-1L, selected, "world-edge zone cannot be enclosed");
Equal(-1L, selected, "groundless zone is not assigned");
Equal(8, EnclosedUnownedZoneRules.ResolveDrainCount(20, 8),
    "queue drain obeys fixed budget");
Equal(3, EnclosedUnownedZoneRules.ResolveSweepCount(100, 97, 64),
    "initial sweep stops at list end");
```

- [ ] **Step 3: Run the isolated tests and verify RED**

Run:

```powershell
dotnet run --project Tests/EnclosedUnownedZoneRulesTests.csproj -c Debug
```

Expected: build failure because `EnclosedUnownedZoneRules.cs` and its types do not exist.

- [ ] **Step 4: Commit the RED tests**

```powershell
git add Tests/EnclosedUnownedZoneRulesTests.cs.txt Tests/EnclosedUnownedZoneRulesTests.csproj
git commit -m "test: specify enclosed zone assignment"
```

### Task 2: Implement Pure Enclosure And Ranking Rules

**Files:**
- Create: `Code/core/lineage/EnclosedUnownedZoneRules.cs`
- Test: `Tests/EnclosedUnownedZoneRulesTests.cs.txt`

- [ ] **Step 1: Add the neighbour facts type**

```csharp
public readonly struct EnclosedZoneNeighbourFacts
{
    public readonly bool IsOwned;
    public readonly bool IsLive;
    public readonly long CityId;
    public readonly long KingdomId;
    public readonly int CityCenterX;
    public readonly int CityCenterY;

    public EnclosedZoneNeighbourFacts(bool pIsOwned, bool pIsLive,
        long pCityId, long pKingdomId, int pCityCenterX,
        int pCityCenterY)
    {
        IsOwned = pIsOwned;
        IsLive = pIsLive;
        CityId = pCityId;
        KingdomId = pKingdomId;
        CityCenterX = pCityCenterX;
        CityCenterY = pCityCenterY;
    }
}
```

- [ ] **Step 2: Implement deterministic target selection**

`SelectTargetCity` must reject an owned candidate, edge candidate, groundless
candidate, non-four-neighbour candidate, invalid neighbour, or mixed kingdom.
For eligible input, aggregate shared-side counts by city id and rank with:

```csharp
if (sharedSides != bestSharedSides)
    better = sharedSides > bestSharedSides;
else if (distanceSquared != bestDistanceSquared)
    better = distanceSquared < bestDistanceSquared;
else
    better = cityId < bestCityId;
```

Distance is calculated with `long` intermediates to avoid overflow. Return
`-1L` when no candidate is valid.

- [ ] **Step 3: Implement bounded work helpers**

```csharp
public static int ResolveDrainCount(int pPendingCount, int pBudget)
{
    return Math.Min(Math.Max(0, pPendingCount), Math.Max(0, pBudget));
}

public static int ResolveSweepCount(int pTotalCount, int pCursor,
    int pBudget)
{
    int remaining = Math.Max(0, pTotalCount - Math.Max(0, pCursor));
    return Math.Min(remaining, Math.Max(0, pBudget));
}
```

- [ ] **Step 4: Run the isolated tests and verify GREEN**

Run:

```powershell
dotnet run --project Tests/EnclosedUnownedZoneRulesTests.csproj -c Debug
```

Expected: `Enclosed unowned Zone rule tests passed.`

- [ ] **Step 5: Commit the pure rules**

```powershell
git add Code/core/lineage/EnclosedUnownedZoneRules.cs Tests/EnclosedUnownedZoneRulesTests.cs.txt
git commit -m "feat: decide enclosed zone ownership"
```

### Task 3: Add Failing Runtime Source Guards

**Files:**
- Create: `Tests/EnclosedUnownedZoneSourceGuard.ps1`

- [ ] **Step 1: Write lifecycle and performance guards**

The script must read the future service, patch, and authority files and assert:

```powershell
Require-Present $patch 'HarmonyPatch(typeof(TileZone), "setCity")'
Require-Present $patch 'ObserveOwnershipChange(__instance)'
Require-Present $patch 'HarmonyPatch(typeof(City), "setKingdom")'
Require-Present $patch 'ObserveCityKingdomChange(__instance)'
Require-Present $patch 'MapBox.on_world_loaded += OnWorldLoaded'
Require-Present $service 'Queue<long>'
Require-Present $service 'HashSet<long>'
Require-Present $service 'MaxCandidatesPerCycle = 8'
Require-Present $service 'MaxSweepZonesPerCycle = 64'
Require-Present $service 'MaxCityBoundaryZonesPerCycle = 16'
Require-Present $service 'MaxCityBoundaryRecordsPerCycle = 4'
Require-Present $service 'Queue<CityBoundaryScan>'
Require-Present $service 'rescanRequested = true'
Require-Present $service 'pTargetCity.addZone(pZone)'
Require-Present $authority 'EnclosedUnownedZoneRepairService.ProcessAuthorityCycle()'
Require-Present $authority 'EnclosedUnownedZoneRepairService.Reset()'
Require-Absent $patch 'HarmonyPatch(typeof(MapBox), "Update")'
Require-Absent $service 'OnWorldYear'
```

Also inspect the `TryRepair` method region and reject `World.world.cities`,
`World.world.kingdoms`, and any loop over the global Zone list. The initial
sweep may index `World.world.zone_calculator.zones` only inside its separately
bounded `AdvanceInitialSweep` method.

- [ ] **Step 2: Run the source guard and verify RED**

Run:

```powershell
pwsh -NoProfile -File Tests/EnclosedUnownedZoneSourceGuard.ps1
```

Expected: failure because the service and patch files are missing.

- [ ] **Step 3: Commit the RED source guard**

```powershell
git add Tests/EnclosedUnownedZoneSourceGuard.ps1
git commit -m "test: guard enclosed zone runtime integration"
```

### Task 4: Implement The Bounded Runtime Repair

**Files:**
- Create: `Code/core/lineage/EnclosedUnownedZoneRepairService.cs`
- Create: `Code/patch/AW_EnclosedUnownedZonePatch.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Test: `Tests/EnclosedUnownedZoneSourceGuard.ps1`

- [ ] **Step 1: Implement coordinate-coalesced observation**

Use `Queue<long>` plus `HashSet<long>`. Encode coordinates as two unsigned
32-bit halves of one `long`. `ObserveOwnershipChange` enqueues only the changed
Zone and `pZone.neighbours`; no ownership mutation occurs in the Harmony hook.

```csharp
public static void ObserveOwnershipChange(TileZone pZone)
{
    if (pZone == null) return;
    Enqueue(pZone);
    TileZone[] neighbours = pZone.neighbours;
    if (neighbours == null) return;
    for (int i = 0; i < neighbours.Length; i++) Enqueue(neighbours[i]);
}
```

- [ ] **Step 2: Implement the bounded world-loaded sweep**

`BeginInitialSweep` clears stale queued coordinates and sets `_sweepCursor = 0`.
Each authority cycle indexes at most 64 entries from
`World.world.zone_calculator.zones`, enqueuing them through the same coalescing
path. When the cursor reaches the current list count, set it to `-1`.

`ObserveCityKingdomChange` adds a transferred city to a coalesced resumable
queue. Before ordinary candidates drain, inspect at most 16 of its city Zones
and enqueue only unowned cardinal neighbours. This covers conquest without an
unbounded synchronous border scan. Dequeue at most four city records per cycle,
counting invalid records against that budget. A repeated transfer sets a rescan
flag so the coalesced record restarts at Zone index zero on its next pass.

- [ ] **Step 3: Implement candidate revalidation and assignment**

Resolve queued coordinates through `zone_calculator.getZone(x, y)`. Build four
facts from `pZone.neighbours`, requiring live `City`, live `Kingdom`, and a
non-null city centre Zone. Call `SelectTargetCity`, find that selected city
among the four neighbours, revalidate `pZone.city == null`, then use:

```csharp
pTargetCity.addZone(pZone);
```

Do not call `CityTechService`, scan any global collection, or modify Zone lists
directly.

- [ ] **Step 4: Add Harmony observation and load registration**

```csharp
[HarmonyPostfix]
[HarmonyPatch(typeof(TileZone), "setCity")]
private static void SetCity_Postfix(TileZone __instance)
{
    EnclosedUnownedZoneRepairService.ObserveOwnershipChange(__instance);
}

[HarmonyPostfix]
[HarmonyPatch(typeof(City), "setKingdom")]
private static void CitySetKingdom_Postfix(City __instance)
{
    EnclosedUnownedZoneRepairService.ObserveCityKingdomChange(__instance);
}

[HarmonyPostfix]
[HarmonyPatch(typeof(MapBox), nameof(MapBox.addLoadWorldCallbacks))]
private static void RegisterWorldLoaded_Postfix()
{
    MapBox.on_world_loaded -= OnWorldLoaded;
    MapBox.on_world_loaded += OnWorldLoaded;
}

private static void OnWorldLoaded()
{
    MapBox.on_world_loaded -= OnWorldLoaded;
    EnclosedUnownedZoneRepairService.BeginInitialSweep();
}
```

- [ ] **Step 5: Connect the existing authority gate**

Add this after noble pregnancy processing in `ProcessCycle`:

```csharp
EnclosedUnownedZoneRepairService.ProcessAuthorityCycle();
```

Add this in `Reset`:

```csharp
EnclosedUnownedZoneRepairService.Reset();
```

This keeps replica, pause, loading, and scheduler gates identical to all other
authority mutations.

- [ ] **Step 6: Run source and rule tests and verify GREEN**

Run:

```powershell
pwsh -NoProfile -File Tests/EnclosedUnownedZoneSourceGuard.ps1
dotnet run --project Tests/EnclosedUnownedZoneRulesTests.csproj -c Debug
```

Expected: both commands pass.

- [ ] **Step 7: Commit runtime implementation**

```powershell
git add Code/core/lineage/EnclosedUnownedZoneRepairService.cs Code/patch/AW_EnclosedUnownedZonePatch.cs Code/core/performance/AWAuthorityCycleService.cs Tests/EnclosedUnownedZoneSourceGuard.ps1
git commit -m "feat: repair enclosed unowned zones"
```

### Task 5: Regression And Build Verification

**Files:**
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Register the new focused checks in the unified guard**

Add guarded invocations for the isolated rules project and source guard near
the other focused test runners. Preserve the existing unrelated guard order.

- [ ] **Step 2: Run focused verification**

```powershell
dotnet run --project Tests/EnclosedUnownedZoneRulesTests.csproj -c Release
pwsh -NoProfile -File Tests/EnclosedUnownedZoneSourceGuard.ps1
```

Expected: both pass.

- [ ] **Step 3: Run the unified source guard**

```powershell
pwsh -NoProfile -File Tests/SourceGuardTests.ps1
```

Expected: the new enclosed-Zone checks pass. If an unrelated pre-existing guard
fails, record its exact file and assertion without changing unrelated code.

- [ ] **Step 4: Build Debug and Release**

```powershell
dotnet build AncientWarfare3.csproj -c Debug
dotnet build AncientWarfare3.csproj -c Release
```

Expected: both builds exit 0 with zero new errors or warnings.

- [ ] **Step 5: Inspect the scoped diff**

```powershell
git diff --check
git diff -- Code/core/lineage/EnclosedUnownedZoneRules.cs Code/core/lineage/EnclosedUnownedZoneRepairService.cs Code/patch/AW_EnclosedUnownedZonePatch.cs Code/core/performance/AWAuthorityCycleService.cs Tests/EnclosedUnownedZoneRulesTests.cs.txt Tests/EnclosedUnownedZoneRulesTests.csproj Tests/EnclosedUnownedZoneSourceGuard.ps1 Tests/SourceGuardTests.ps1
```

Expected: no whitespace errors and no unrelated edits in the scoped diff.

- [ ] **Step 6: Commit unified test registration**

```powershell
git add Tests/SourceGuardTests.ps1
git commit -m "test: cover enclosed zone repair"
```

Do not deploy while WorldBox is running. Actual-game validation follows the
design spec after a safe deployment window.

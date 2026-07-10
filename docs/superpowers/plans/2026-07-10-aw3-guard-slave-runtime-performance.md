# AW3 Guard And Slave Runtime Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the remaining guard/slave full-world scans, repeated ordinary-army scans, synchronous per-kill database writes, and misleading profiler labels without changing gameplay eligibility or army limits.

**Architecture:** Keep WorldBox object access in the existing services and put every new gate, cache decision, radius conversion, persistence decision, and lifecycle decision into public pure-rule methods covered by the existing executable test projects. Use the original public `Finder.getUnitsFromChunk` API for fixed-radius searches, a bounded runtime cache only for unbounded slave-frontline searches, and existing actor/city/army data as authoritative state.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, NeoModLoader, WorldBox `Finder`, SQLite lineage archive, existing console rule-test projects.

---

## File Map

- Modify `Code/core/lineage/ActorAiSearchThrottleRules.cs`: tile-radius-to-chunk-radius rule and cooldown pruning decisions.
- Modify `Code/core/lineage/RoyalGuardMaintenanceRules.cs`: ordinary-army guard-cleanup gate.
- Modify `Code/core/lineage/SlaveArmyMaintenanceRules.cs`: composition fallback, frontline cache/order, formation threshold, and duplicate-cleanup decisions.
- Create `Code/core/lineage/SlaveMeritPersistenceRules.cs`: pure merit milestone persistence rule.
- Modify `Code/core/lineage/RoyalGuardService.cs`: spatial threat search, guard cleanup fast path, cooldown cleanup, and profiler timing.
- Modify `Code/core/lineage/SlaveService.cs`: labor gate, spatial capture search, bounded formation count, frontline cache, order suppression, merit persistence, and profiler timing.
- Modify `Code/core/lineage/AWArmyService.cs`: duplicate cleanup only on create/re-anchor/load.
- Modify `Code/core/lineage/FiefMilitaryService.cs`: active-fief gate before slave-army composition inference.
- Modify `Code/core/policy/CityMaintenanceBenchmarkRules.cs`: accurate slave-army and actor-AI benchmark IDs.
- Modify `Code/patch/AW_RetirementPatch.cs`: use the correct slave-army benchmark label.
- Modify `Tests/CityMaintenanceRuleTests/Program.cs`: labor, army fast-path, formation, cache, order, duplicate, and merit regressions.
- Modify `Tests/RoyalGuardActionRuleTests/Program.cs`: guard cleanup and spatial radius regressions.
- Modify `Tests/SlaveCaptureRuleTests/Program.cs`: 80-tile capture-search radius regression.

### Task 1: Add failing pure-rule regressions for every performance decision

**Files:**
- Modify: `Tests/CityMaintenanceRuleTests/Program.cs`
- Modify: `Tests/RoyalGuardActionRuleTests/Program.cs`
- Modify: `Tests/SlaveCaptureRuleTests/Program.cs`

- [ ] **Step 1: Add the new test calls**

Add these calls to the three `Main` methods:

```csharp
// CityMaintenanceRuleTests
ExpectSlaveLaborPerformanceGate();
ExpectSlaveArmyPerformanceRules();
ExpectSpecialArmyCleanupLifecycle();
ExpectSlaveMeritPersistence();

// RoyalGuardActionRuleTests
ExpectGuardArmyCleanupGate();
ExpectGuardSpatialSearchRadius();

// SlaveCaptureRuleTests
ExpectCaptureSpatialSearchRadius();
```

- [ ] **Step 2: Add complete failing test methods**

Add to `CityMaintenanceRuleTests`:

```csharp
private static void ExpectSlaveLaborPerformanceGate()
{
    if (SlaveArmyMaintenanceRules.ShouldCheckSlaveLabor(
            pHasCity: true, pHasKingdom: true, pSlaveryEnabled: false,
            pAlreadyRecordedForKingdom: false, pMaintenanceDue: true))
        throw new Exception("Non-slavery cities must skip slave-labor resident scans.");
    if (!SlaveArmyMaintenanceRules.ShouldCheckSlaveLabor(
            pHasCity: true, pHasKingdom: true, pSlaveryEnabled: true,
            pAlreadyRecordedForKingdom: false, pMaintenanceDue: true))
        throw new Exception("Due slavery cities must run slave-labor recording.");
    if (SlaveArmyMaintenanceRules.ShouldCheckSlaveLabor(
            pHasCity: true, pHasKingdom: true, pSlaveryEnabled: true,
            pAlreadyRecordedForKingdom: true, pMaintenanceDue: true))
        throw new Exception("Recorded slave labor must remain a constant-time fast path.");
}

private static void ExpectSlaveArmyPerformanceRules()
{
    if (SlaveArmyMaintenanceRules.ShouldInferSlaveArmyComposition(
            pRoleMarkedSlaveArmy: false, pSlaveryEnabled: false))
        throw new Exception("Ordinary non-slavery armies must skip composition scans.");
    if (!SlaveArmyMaintenanceRules.ShouldInferSlaveArmyComposition(
            pRoleMarkedSlaveArmy: false, pSlaveryEnabled: true))
        throw new Exception("Legacy armies in slavery kingdoms retain composition fallback.");
    if (!SlaveArmyMaintenanceRules.HasReachedFormationThreshold(3, 3) ||
        SlaveArmyMaintenanceRules.HasReachedFormationThreshold(2, 3))
        throw new Exception("Slave formation counting must stop exactly at the minimum.");
    if (!SlaveArmyMaintenanceRules.ShouldReuseFrontlineTarget(
            pHasEntry: true, pTargetAlive: true, pStillHostile: true,
            pSameIsland: true, pNow: 10.0, pExpiresAt: 20.0))
        throw new Exception("A valid same-island frontline target should be shared.");
    if (SlaveArmyMaintenanceRules.ShouldReuseFrontlineTarget(
            pHasEntry: true, pTargetAlive: false, pStillHostile: true,
            pSameIsland: true, pNow: 10.0, pExpiresAt: 20.0))
        throw new Exception("Dead frontline targets must invalidate the cache.");
    if (SlaveArmyMaintenanceRules.ShouldIssueFrontlineOrder(
            pAlreadyTargetsActor: true, pIsMoving: true))
        throw new Exception("Identical active path orders must not be reissued.");
    if (!SlaveArmyMaintenanceRules.ShouldIssueFrontlineOrder(
            pAlreadyTargetsActor: true, pIsMoving: false))
        throw new Exception("Interrupted units must be allowed to resume the same target.");
}

private static void ExpectSpecialArmyCleanupLifecycle()
{
    if (SpecialArmyLookupCacheRules.ShouldCleanupDuplicates(
            pCreated: false, pReanchored: false, pPostLoadRepair: false))
        throw new Exception("Valid EnsureArmy cache hits must skip global duplicate scans.");
    if (!SpecialArmyLookupCacheRules.ShouldCleanupDuplicates(
            pCreated: true, pReanchored: false, pPostLoadRepair: false) ||
        !SpecialArmyLookupCacheRules.ShouldCleanupDuplicates(
            pCreated: false, pReanchored: true, pPostLoadRepair: false) ||
        !SpecialArmyLookupCacheRules.ShouldCleanupDuplicates(
            pCreated: false, pReanchored: false, pPostLoadRepair: true))
        throw new Exception("Create, re-anchor, and load repair must retain duplicate recovery.");
}

private static void ExpectSlaveMeritPersistence()
{
    if (SlaveMeritPersistenceRules.ShouldPersist(
            pOldMerit: 0, pNewMerit: 1, pPoints: 1,
            pMilestone: 4, pFreedomThreshold: 8))
        throw new Exception("An ordinary one-point kill must not synchronously write SQLite.");
    if (!SlaveMeritPersistenceRules.ShouldPersist(
            pOldMerit: 3, pNewMerit: 4, pPoints: 1,
            pMilestone: 4, pFreedomThreshold: 8))
        throw new Exception("Crossing a merit milestone must persist archive state.");
    if (!SlaveMeritPersistenceRules.ShouldPersist(
            pOldMerit: 1, pNewMerit: 5, pPoints: 4,
            pMilestone: 4, pFreedomThreshold: 8))
        throw new Exception("Important multi-point kills must persist archive state.");
    if (SlaveMeritPersistenceRules.ShouldPersist(
            pOldMerit: 7, pNewMerit: 8, pPoints: 1,
            pMilestone: 4, pFreedomThreshold: 8))
        throw new Exception("Freedom performs the authoritative final write and must avoid a duplicate write.");
}
```

Add to `RoyalGuardActionRuleTests`:

```csharp
private static void ExpectGuardArmyCleanupGate()
{
    if (RoyalGuardMaintenanceRules.ShouldInspectNormalArmyForGuards(
            pIsGuardArmy: false, pHasGuardStateHint: false))
        throw new Exception("Armies in kingdoms without guard state must skip member copies.");
    if (!RoyalGuardMaintenanceRules.ShouldInspectNormalArmyForGuards(
            pIsGuardArmy: false, pHasGuardStateHint: true))
        throw new Exception("A guard-state hint must retain compatibility cleanup.");
    if (RoyalGuardMaintenanceRules.ShouldInspectNormalArmyForGuards(
            pIsGuardArmy: true, pHasGuardStateHint: true))
        throw new Exception("The guard army itself must never be stripped.");
}

private static void ExpectGuardSpatialSearchRadius()
{
    if (ActorAiSearchThrottleRules.ChunkRadiusForTileRadius(10, 16) != 1 ||
        ActorAiSearchThrottleRules.ChunkRadiusForTileRadius(0, 16) != 0)
        throw new Exception("Guard spatial search must cover the exact minimum chunk square.");
}
```

Add to `SlaveCaptureRuleTests`:

```csharp
private static void ExpectCaptureSpatialSearchRadius()
{
    if (ActorAiSearchThrottleRules.ChunkRadiusForTileRadius(80, 16) != 5)
        throw new Exception("An 80-tile catcher radius must cover five 16-tile chunks.");
}
```

- [ ] **Step 3: Run all three focused projects and verify RED**

Run:

```powershell
dotnet run --project Tests/CityMaintenanceRuleTests/CityMaintenanceRuleTests.csproj --no-restore
dotnet run --project Tests/RoyalGuardActionRuleTests/RoyalGuardActionRuleTests.csproj --no-restore
dotnet run --project Tests/SlaveCaptureRuleTests/SlaveCaptureRuleTests.csproj --no-restore
```

Expected: compilation failures for the new rule methods and `SlaveMeritPersistenceRules`.

### Task 2: Implement the pure performance rules and make focused tests green

**Files:**
- Modify: `Code/core/lineage/ActorAiSearchThrottleRules.cs`
- Modify: `Code/core/lineage/RoyalGuardMaintenanceRules.cs`
- Modify: `Code/core/lineage/SlaveArmyMaintenanceRules.cs`
- Modify: `Code/core/lineage/SpecialArmyLookupCacheRules.cs`
- Create: `Code/core/lineage/SlaveMeritPersistenceRules.cs`

- [ ] **Step 1: Add radius and guard cleanup rules**

```csharp
public static int ChunkRadiusForTileRadius(int pTileRadius, int pChunkSize)
{
    if (pTileRadius <= 0) return 0;
    int chunkSize = Math.Max(1, pChunkSize);
    return (pTileRadius + chunkSize - 1) / chunkSize;
}
```

```csharp
public static bool ShouldInspectNormalArmyForGuards(bool pIsGuardArmy,
    bool pHasGuardStateHint)
{
    return !pIsGuardArmy && pHasGuardStateHint;
}
```

- [ ] **Step 2: Add slave maintenance/cache/order rules**

```csharp
public static bool ShouldCheckSlaveLabor(bool pHasCity, bool pHasKingdom,
    bool pSlaveryEnabled, bool pAlreadyRecordedForKingdom, bool pMaintenanceDue)
{
    return pHasCity && pHasKingdom && pSlaveryEnabled &&
           !pAlreadyRecordedForKingdom && pMaintenanceDue;
}

public static bool ShouldInferSlaveArmyComposition(bool pRoleMarkedSlaveArmy,
    bool pSlaveryEnabled)
{
    return !pRoleMarkedSlaveArmy && pSlaveryEnabled;
}

public static bool HasReachedFormationThreshold(int pCount, int pThreshold)
{
    return pCount >= Math.Max(1, pThreshold);
}

public static bool ShouldReuseFrontlineTarget(bool pHasEntry, bool pTargetAlive,
    bool pStillHostile, bool pSameIsland, double pNow, double pExpiresAt)
{
    return pHasEntry && pTargetAlive && pStillHostile && pSameIsland &&
           pNow <= pExpiresAt;
}

public static bool ShouldIssueFrontlineOrder(bool pAlreadyTargetsActor, bool pIsMoving)
{
    return !pAlreadyTargetsActor || !pIsMoving;
}
```

- [ ] **Step 3: Add duplicate lifecycle and merit persistence rules**

```csharp
public static bool ShouldCleanupDuplicates(bool pCreated, bool pReanchored,
    bool pPostLoadRepair)
{
    return pCreated || pReanchored || pPostLoadRepair;
}
```

Create `SlaveMeritPersistenceRules.cs`:

```csharp
namespace AncientWarfare3.core.lineage
{
    public static class SlaveMeritPersistenceRules
    {
        public static bool ShouldPersist(int pOldMerit, int pNewMerit, int pPoints,
            int pMilestone, int pFreedomThreshold)
        {
            if (pNewMerit >= pFreedomThreshold) return false;
            if (pPoints >= 4) return true;
            int milestone = System.Math.Max(1, pMilestone);
            return pOldMerit / milestone != pNewMerit / milestone;
        }
    }
}
```

- [ ] **Step 4: Run the three focused projects and verify GREEN**

Run the three commands from Task 1.

Expected: all three projects print their pass message.

### Task 3: Replace global actor searches with original-game spatial queries

**Files:**
- Modify: `Code/core/lineage/RoyalGuardService.cs`
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/core/policy/CityMaintenanceBenchmarkRules.cs`

- [ ] **Step 1: Add accurate actor-AI benchmark IDs**

Add and register:

```csharp
public const string RoyalGuardThreatScan = "aw3_ai_royal_guard_threat_scan";
public const string SlaveCatcherTargetScan = "aw3_ai_slave_catcher_target_scan";
public const string SlaveArmyFrontlineScan = "aw3_ai_slave_army_frontline_scan";
public const string SlaveMeritPersist = "aw3_slave_merit_persist";
```

- [ ] **Step 2: Refactor guard search to two bounded spatial scans**

Keep `GetDirectAttackThreat` before the spatial work. Replace enemy-kingdom loops with a helper that enumerates:

```csharp
Finder.getUnitsFromChunk(pOrigin,
    ActorAiSearchThrottleRules.ChunkRadiusForTileRadius(pRadius, 16),
    pRadius)
```

Call it first around the king with `PROTECT_RADIUS`, then around the guard with `FOLLOW_RADIUS`, carrying the same `best` and `bestDist`. Continue using `IsValidThreatForGuardCore`; wrap only both enumerations in `RoyalGuardThreatScan`.

- [ ] **Step 3: Refactor catcher search to the 80-tile spatial query**

Replace enemy-kingdom loops in `FindSlaveCaptureTarget` with:

```csharp
foreach (Actor target in Finder.getUnitsFromChunk(pCatcher.current_tile,
             ActorAiSearchThrottleRules.ChunkRadiusForTileRadius(radius, 16), radius))
{
    if (!CanCaptureTargetForKnownCatcher(pCatcher, target)) continue;
    int dist = Toolbox.SquaredDistTile(pCatcher.current_tile, target.current_tile);
    if (dist > maxDist || dist >= bestDist) continue;
    bestDist = dist;
    best = target;
}
```

Wrap the loop in `SlaveCatcherTargetScan`; preserve result marking and waits.

- [ ] **Step 4: Prune expired cooldown entries**

When a per-actor cooldown has expired, remove it before running the scan. When either cooldown dictionary exceeds 256 entries, remove entries whose `nextAllowed <= now`. Remove guard IDs explicitly in `DismissGuard`.

- [ ] **Step 5: Run focused tests and build**

Run:

```powershell
dotnet run --project Tests/RoyalGuardActionRuleTests/RoyalGuardActionRuleTests.csproj --no-restore
dotnet run --project Tests/SlaveCaptureRuleTests/SlaveCaptureRuleTests.csproj --no-restore
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: both tests pass and the build reports zero errors.

### Task 4: Gate irrelevant city and normal-army maintenance before enumeration

**Files:**
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/core/lineage/RoyalGuardService.cs`
- Modify: `Code/core/lineage/FiefMilitaryService.cs`

- [ ] **Step 1: Gate and stagger slave labor**

In `CheckCitySlaveLabor`, calculate `slaveryEnabled` before the timer. Return immediately when false. Replace `ShouldRunCityMaintenance` with `ShouldRunCityMaintenanceStaggered`, then pass the resolved states through `ShouldCheckSlaveLabor` before `CountSlaves`.

- [ ] **Step 2: Make food quota stop at the first slave**

Add:

```csharp
private static bool HasAnySlave(City pCity)
{
    if (pCity?.data == null) return false;
    foreach (Actor unit in pCity.getUnits())
        if (IsSlave(unit)) return true;
    return false;
}
```

Use it in `ResetSlaveFoodQuota` instead of `CountSlaves(pCity) > 0`.

- [ ] **Step 3: Add normal-army fast gates**

In `StripGuardsFromNormalArmy`, resolve the owning kingdom after excluding the guard army. Call `ShouldInspectNormalArmyForGuards(IsRoyalGuardArmy(pArmy), HasKingdomGuardStateHint(kingdom))` before allocating `new List<Actor>`.

In `RenameArmyIfSlaveArmy`, check the explicit slave role first. If it is not marked, resolve the kingdom and return before `IsSlaveArmy` when slavery is disabled.

In `FiefMilitaryService.RefreshArmyName`, return immediately after `IsActiveFief(city)` is false, before calling `SlaveService.IsSlaveArmy`.

- [ ] **Step 4: Run city maintenance tests and build**

Run:

```powershell
dotnet run --project Tests/CityMaintenanceRuleTests/CityMaintenanceRuleTests.csproj --no-restore
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: test pass message and zero build errors.

### Task 5: Remove exact pre-fill counts and repeated global army deduplication

**Files:**
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/core/lineage/AWArmyService.cs`
- Modify: `Code/core/lineage/SpecialArmyLookupCacheRules.cs`

- [ ] **Step 1: Replace exact formation counting with a threshold scan**

Add:

```csharp
private static int CountSlavesUpTo(City pCity, int pThreshold)
{
    if (pCity?.data == null) return 0;
    int count = 0;
    foreach (Actor unit in pCity.getUnits())
    {
        if (!IsSlave(unit)) continue;
        count++;
        if (SlaveArmyMaintenanceRules.HasReachedFormationThreshold(count, pThreshold)) break;
    }
    return count;
}
```

For an existing valid army, skip immediately only when it is full; otherwise enter the existing bounded 32-candidate fill pipeline without calling `CountSlaves`. For army creation, use `CountSlavesUpTo(..., MIN_SLAVES_FOR_SLAVE_ARMY)` and keep the current failure cooldown when the threshold is not reached.

- [ ] **Step 2: Restrict duplicate cleanup to lifecycle boundaries**

In `EnsureArmy`, track `bool created` and call `CleanupDuplicateArmies` only when `ShouldCleanupDuplicates(created, false, false)` is true. In `ReanchorArmy`, clean once after marking. In `RepairSpecialArmiesAfterLoad`, work from a snapshot, repair/cache roles first, then process one keeper per role/kingdom/anchor key and merge duplicates.

- [ ] **Step 3: Run city tests and build**

Run:

```powershell
dotnet run --project Tests/CityMaintenanceRuleTests/CityMaintenanceRuleTests.csproj --no-restore
dotnet run --project Tests/WarFabricationRuleTests/WarFabricationRuleTests.csproj --no-restore
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: both test projects pass and the build reports zero errors.

### Task 6: Share frontline targets, suppress duplicate paths, and batch merit persistence

**Files:**
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/core/policy/CityMaintenanceBenchmarkRules.cs`

- [ ] **Step 1: Add a bounded kingdom/island frontline cache**

Add a private cache entry with `targetId` and `expiresAt`, keyed by `kingdom.id + ":" + origin.region.island.id`. Before the global fallback, resolve the actor ID and pass live/hostile/island/time states through `ShouldReuseFrontlineTarget`. Cache both hits and misses for 10 time units. When the cache exceeds 128 entries, remove expired entries before inserting.

- [ ] **Step 2: Suppress identical active movement orders**

Before setting `beh_actor_target` and calling `goTo`, evaluate:

```csharp
bool alreadyTargets = unit.beh_actor_target == target;
if (!SlaveArmyMaintenanceRules.ShouldIssueFrontlineOrder(alreadyTargets, unit.is_moving))
    continue;
```

Keep the existing army-size, warrior, island, and life-state gates.

- [ ] **Step 3: Persist merit only at milestones or important events**

Capture `oldMerit` before adding points. After updating actor data, call `UpsertSlaveState` only when `SlaveMeritPersistenceRules.ShouldPersist(oldMerit, merit, points, 4, MERIT_FOR_FREEDOM)` returns true. Wrap that write in `SlaveMeritPersist`. When merit reaches freedom, let `FreeSlave` perform the single authoritative final upsert.

- [ ] **Step 4: Run city tests and build**

Run:

```powershell
dotnet run --project Tests/CityMaintenanceRuleTests/CityMaintenanceRuleTests.csproj --no-restore
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: test pass message and zero build errors.

### Task 7: Correct profiler labels and run the full verification gate

**Files:**
- Modify: `Code/core/policy/CityMaintenanceBenchmarkRules.cs`
- Modify: `Code/patch/AW_RetirementPatch.cs`
- Verify: all changed production and test files

- [ ] **Step 1: Correct the city-level label**

Add and register:

```csharp
public const string SlaveArmy = "aw3_city_slave_army";
```

Use `SlaveArmy` around `EnsureSlaveArmy` in `AW_RetirementPatch`. Leave `SlaveCatchers`, `SlaveCatchersJobGate`, and `SlaveCatchersTargetScan` available only for the dormant city job-assignment path so historical benchmark consumers do not crash.

- [ ] **Step 2: Run every rule-test project**

```powershell
$projects = Get-ChildItem -LiteralPath Tests -Recurse -Filter *.csproj | Sort-Object FullName
foreach ($project in $projects) {
    dotnet run --project $project.FullName --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Rule test failed: $($project.FullName)" }
}
```

Expected: all 11 projects print their pass messages and the command exits 0.

- [ ] **Step 3: Run a fresh full build and diff checks**

```powershell
dotnet build AncientWarfare3.csproj --no-restore
git diff --check
git status --short
```

Expected: build succeeds with zero errors, `git diff --check` prints nothing, and status contains only the planned files.

- [ ] **Step 4: Audit every approved behavior from code and tests**

Confirm:

1. disabled slavery returns before all slave-labor and normal-army composition scans;
2. kingdoms without guard hints return before allocating an army-member copy;
3. guards search 10/4-tile spatial areas and catchers search 80 tiles through `Finder`;
4. capture eligibility, health thresholds, guard threat validation, and gameplay constants are unchanged;
5. existing slave armies use the 32-candidate fill cursor without an exact pre-count;
6. special-army cache hits do not run global deduplication;
7. frontline cache entries are validated, bounded, and same-island only;
8. active identical `goTo` calls are suppressed but interrupted units resume;
9. ordinary slave kills skip SQLite and milestones/important/freedom events persist;
10. benchmark names describe the measured code.

- [ ] **Step 5: Commit the verified implementation**

```powershell
git add Code Tests docs/superpowers/plans/2026-07-10-aw3-guard-slave-runtime-performance.md
git commit -m "perf: 优化禁卫军与奴隶系统运行时开销"
```

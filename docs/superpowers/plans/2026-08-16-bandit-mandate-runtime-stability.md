# Bandit, Mandate Border, and Runtime Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make bandit stronghold capture deterministic, keep activated mandate frontier walls aligned with changed borders and passable through watch towers, and eliminate the two reported cooperative-runtime failure signatures.

**Architecture:** Keep the four fixes isolated behind pure rules and small runtime services. Persist mandate wall ownership per city, schedule world mutations through the existing coalesced authority queue, make worker wake-ups generation-aware, and validate prepared native path state before entering vanilla pathfinding.

**Tech Stack:** C#/.NET, Harmony patches, Newtonsoft.Json kingdom data, WorldBox city/tile APIs, `DeferredRuntimeWorkService`, PowerShell source guards, and the existing `AncientWarfare3.Rules.Tests` console harness.

---

## File Map

- `Code/core/lineage/PeasantRebelBanditStrongholdRules.cs`: pure settlement-decision rules.
- `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`: deferred capture and empty-stronghold settlement.
- `Code/patch/AW_CityOccupationAccelerationPatch.cs`: suppress vanilla stronghold capture/negotiation at 100%.
- `Code/core/performance/AWCooperativeActorPostRunner.cs`: reject invalid actor snapshots before enemy preparation.
- `Code/core/lineage/MandateBorderWallState.cs`: schema-versioned per-city wall manifests.
- `Code/core/lineage/MandateBorderWallStateStore.cs`: JSON persistence on kingdom data.
- `Code/core/lineage/MandateBorderWallRefreshRules.cs`: activation, affected-city, restoration, and tower-reservation rules.
- `Code/core/lineage/MandateBorderWallRefreshService.cs`: local deferred wall refresh and tile restoration.
- `Code/core/lineage/MandateBorderDefenseService.cs`: activate the lifecycle and separate wall cities from capped army cities.
- `Code/core/lineage/CultiwayStyleFrontierWallGeometryRules.cs`: remove exact reserved tower footprints from wall output.
- `Code/core/lineage/CultiwayStyleCityWallService.cs`: accept reserved frontier points and expose planned placement.
- `Code/patch/AW_MandateBorderWallPatch.cs`: observe city/zone ownership changes and queue only affected cities.
- `Code/core/performance/AWSimulationWorkerDispatchGate.cs`: per-worker single-consume generation assignments.
- `Code/core/performance/AWSimulationWorkerPool.cs`: use generation-aware dispatch and improve failure diagnostics.
- `Code/core/pathfinding/PreparedNativePathCommitRules.cs`: pure prepared-path fingerprint validation.
- `Code/core/pathfinding/AWPathMovementBridge.cs`: capture and validate native path fingerprints.
- `Code/patch/AW_GlobalPathfindingPatch.cs`: reject invalid native military path inputs before vanilla execution.
- `Code/core/lineage/PathfindingSafetyRules.cs`: classify the narrow global-path null failure.
- `Code/patch/AW_PathfindingSafetyPatch.cs`: convert the classified vanilla failure even while AW3 owns pathfinding.
- `Tests/AncientWarfare3.Rules.Tests/*.cs.txt`: focused rule and concurrency tests.
- `Tests/*SourceGuard*.ps1`: integration assertions where WorldBox runtime types cannot be instantiated.

### Task 1: Defer Bandit Stronghold Settlement Until the Correct Authority Boundary

**Files:**
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdRules.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Modify: `Code/patch/AW_CityOccupationAccelerationPatch.cs`
- Modify: `Code/core/performance/AWCooperativeActorPostRunner.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Create: `Tests/BanditStrongholdDeferredFallSourceGuard.ps1`

- [ ] **Step 1: Write failing settlement rule tests**

Add cases with this explicit decision table:

```csharp
Equal(BanditStrongholdFallAction.RecordSuppressorOnly,
    PeasantRebelBanditStrongholdRules.ResolveFallAction(
        population: 0, hostileKillerKingdomId: 42,
        captureFinished: false));
Equal(BanditStrongholdFallAction.QueueFall,
    PeasantRebelBanditStrongholdRules.ResolveFallAction(
        population: 0, hostileKillerKingdomId: -1,
        captureFinished: false));
Equal(BanditStrongholdFallAction.QueueFall,
    PeasantRebelBanditStrongholdRules.ResolveFallAction(
        population: 12, hostileKillerKingdomId: 42,
        captureFinished: true));
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
```

Expected: FAIL because `BanditStrongholdFallAction` and `ResolveFallAction` do not exist.

- [ ] **Step 3: Add the minimal pure decision rule**

```csharp
public enum BanditStrongholdFallAction
{
    None,
    RecordSuppressorOnly,
    QueueFall
}

public static BanditStrongholdFallAction ResolveFallAction(
    int population, long hostileKillerKingdomId, bool captureFinished)
{
    if (captureFinished) return BanditStrongholdFallAction.QueueFall;
    if (population > 0) return BanditStrongholdFallAction.None;
    return hostileKillerKingdomId > 0
        ? BanditStrongholdFallAction.RecordSuppressorOnly
        : BanditStrongholdFallAction.QueueFall;
}
```

- [ ] **Step 4: Route all settlement entry points through one coalesced queue**

Add `QueueFall(long cityId, long suppressorId)` using key `bandit_stronghold_fall:<cityId>` and `DeferredWorkClass.CriticalRuntime`. The queued action must re-resolve the city, kingdom, state, and suppressor before calling `CompleteFall`.

Change `TryHandleCapture` to set `pHandled = true`, queue the fall, and return `false`, so `City.finishCapture` never reaches negotiation or vanilla ownership transfer. Change hostile `OnBanditResidentDied` to persist `LastHostileKillerKingdomId` without settlement; environmental zero population queues settlement. Change `RestoreRuntime` to queue rather than synchronously mutate collections.

- [ ] **Step 5: Reject dead or detached actors before enemy preparation**

In `EnemyPrepareBatchWork.RunParallel`, skip entries unless all of these remain true at execution time:

```csharp
actor?.data != null && !actor.isRekt() && actor.isAlive() &&
actor.asset != null && actor.kingdom?.data != null
```

Do not catch `Actor.isAllowedToLookForEnemies`; the invalid snapshot must be excluded before calling it.

- [ ] **Step 6: Add and run the source guard**

The guard must assert that `OnBanditResidentDied` no longer directly calls `CompleteFall`, `TryHandleCapture` calls `QueueFall`, and the queue uses `EnqueueCoalesced` with `CriticalRuntime`.

Run:

```powershell
pwsh -NoProfile -File Tests/BanditStrongholdDeferredFallSourceGuard.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold
```

Expected: both PASS.

- [ ] **Step 7: Commit the bandit settlement fix**

```powershell
git add -- Code/core/lineage/PeasantRebelBanditStrongholdRules.cs Code/core/lineage/PeasantRebelBanditStrongholdService.cs Code/patch/AW_CityOccupationAccelerationPatch.cs Code/core/performance/AWCooperativeActorPostRunner.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt Tests/BanditStrongholdDeferredFallSourceGuard.ps1
git commit -m "fix: defer bandit stronghold settlement"
```

### Task 2: Define Persisted Per-City Mandate Wall Ownership

**Files:**
- Create: `Code/core/lineage/MandateBorderWallState.cs`
- Create: `Code/core/lineage/MandateBorderWallStateStore.cs`
- Create: `Code/core/lineage/MandateBorderWallRefreshRules.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/MandateBorderWallRefreshRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing state and refresh-rule tests**

Cover activation, local affected-city union, manifest replacement, and restoration protection:

```csharp
False(MandateBorderWallRefreshRules.ShouldRefresh(false, true));
True(MandateBorderWallRefreshRules.ShouldRefresh(true, true));
SetEqual(new long[] { 2, 3, 4 },
    MandateBorderWallRefreshRules.AffectedCityIds(
        changedCityId: 2, previousCityId: 3,
        neighbourCityIds: new long[] { 2, 4 }));
True(MandateBorderWallRefreshRules.ShouldRestore(
    currentTopTypeId: "wall_order", placedWallTypeId: "wall_order"));
False(MandateBorderWallRefreshRules.ShouldRestore(
    currentTopTypeId: "road", placedWallTypeId: "wall_order"));
```

- [ ] **Step 2: Run the focused wall suite and verify RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --cultiway-wall
```

Expected: FAIL because the new state and rules are absent.

- [ ] **Step 3: Implement schema-versioned state types**

Use these concrete fields:

```csharp
internal sealed class MandateBorderWallState
{
    internal const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public bool Activated { get; set; }
    public Dictionary<long, MandateBorderCityWallManifest> Cities { get; set; }
        = new Dictionary<long, MandateBorderCityWallManifest>();
}

internal sealed class MandateBorderCityWallManifest
{
    public long CityId { get; set; }
    public string WallTypeId { get; set; } = "";
    public List<MandateBorderWallPointState> Points { get; set; }
        = new List<MandateBorderWallPointState>();
}

internal sealed class MandateBorderWallPointState
{
    public int X { get; set; }
    public int Y { get; set; }
    public string OriginalTopTypeId { get; set; } = "";
}
```

Add `LineageKeys.MANDATE_BORDER_WALL_STATE = "aw_mandate_border_wall_state"`. The store must normalize null dictionaries/lists after `JsonConvert.DeserializeObject`, reject future schema versions, and write atomically as one kingdom data string.

- [ ] **Step 4: Implement and pass the pure rules**

Run the same focused command. Expected: PASS, including existing frontier-wall tests.

- [ ] **Step 5: Commit the persisted wall model**

```powershell
git add -- Code/core/lineage/MandateBorderWallState.cs Code/core/lineage/MandateBorderWallStateStore.cs Code/core/lineage/MandateBorderWallRefreshRules.cs Code/core/lineage/LineageKeys.cs Tests/AncientWarfare3.Rules.Tests/MandateBorderWallRefreshRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: persist mandate frontier wall ownership"
```

### Task 3: Make Watch Towers the Only Frontier Wall Passages

**Files:**
- Modify: `Code/core/lineage/CultiwayStyleFrontierWallGeometryRules.cs`
- Modify: `Code/core/lineage/CultiwayStyleCityWallService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CultiwayStyleFrontierWallGeometryRulesTests.cs.txt`

- [ ] **Step 1: Add a failing exact-footprint reservation test**

```csharp
HashSet<CultiwayWallPoint> actual = Compute(Input(
    land, land, Vertical(5, 1, 7),
    reservedPassages: new[] { Point(5, 4), Point(4, 4) }, width: 2));
False(actual.Contains(Point(5, 4)));
False(actual.Contains(Point(4, 4)));
True(actual.Contains(Point(5, 3)));
True(actual.Contains(Point(5, 5)));
```

Also change the mandate-focused expectation so no artificial road passage is required for mandate calls.

- [ ] **Step 2: Run and verify RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --cultiway-wall
```

Expected: FAIL because `reservedPassages` is not accepted.

- [ ] **Step 3: Add exact reserved points to the geometry input**

Add `HashSet<CultiwayWallPoint> ReservedPassages` and execute:

```csharp
SealDiagonalGaps(walls, available);
CarveRoadPassages(walls, pInput.Roads);
walls.ExceptWith(pInput.ReservedPassages);
```

The exact set removal is important: do not expand tower footprints into an artificial 3x3 opening.

- [ ] **Step 4: Expose reserved points through frontier planning**

Add an overload of `TryPlanFrontier`/`BuildFrontier` that accepts `IReadOnlyCollection<CultiwayWallPoint> pReservedPassages`. Existing callers pass an empty collection and retain current behavior.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --cultiway-wall
git add -- Code/core/lineage/CultiwayStyleFrontierWallGeometryRules.cs Code/core/lineage/CultiwayStyleCityWallService.cs Tests/AncientWarfare3.Rules.Tests/CultiwayStyleFrontierWallGeometryRulesTests.cs.txt
git commit -m "fix: reserve watch tower footprints in frontier walls"
```

### Task 4: Refresh Only Changed Activated Mandate Border Cities

**Files:**
- Create: `Code/core/lineage/MandateBorderWallRefreshService.cs`
- Modify: `Code/core/lineage/MandateBorderDefenseService.cs`
- Create: `Code/patch/AW_MandateBorderWallPatch.cs`
- Create: `Tests/MandateBorderWallLifecycleSourceGuard.ps1`

- [ ] **Step 1: Write the failing lifecycle source guard**

Require all of these integration points:

```text
ExecuteDecision -> Activate -> QueueKingdomRefresh
OnMandateWarStarted -> IsActivated -> QueueKingdomRefresh
City.setKingdom -> QueueAffectedCities
TileZone.setCity -> QueueAffectedCities
City.addZone -> QueueAffectedCities
refresh key -> mandate_border_wall_refresh:<cityId>
```

Forbid `OnMandateWarStarted` from calling wall construction when activation is false.

- [ ] **Step 2: Run the guard and verify RED**

```powershell
pwsh -NoProfile -File Tests/MandateBorderWallLifecycleSourceGuard.ps1
```

Expected: FAIL because lifecycle service and patch do not exist.

- [ ] **Step 3: Implement local restore-and-rebuild**

`QueueCityRefresh` must use `DeferredRuntimeWorkService.EnqueueCoalesced("mandate_border_wall_refresh:" + cityId, DeferredWorkClass.Persistent, ...)`. The action re-resolves the city and owner, restores only manifest points whose current top type still equals the recorded wall type, leaves every building untouched, and clears the old manifest before planning a replacement.

Collect reserved points from every live city building where:

```csharp
building?.data != null && !building.isOnRemove() &&
building.asset?.type == "type_watch_tower"
```

Use every tile in `building.tiles`; fall back to `building.current_tile` when the footprint list is empty.

- [ ] **Step 4: Separate wall cities from capped guard cities**

In `ReinforceBorder`, retain `allBorderCities = CollectBorderCities(pMandate)` for walls. Derive `armyCities = SelectBorderArmyCities(allBorderCities)` only for guards, towers, and army re-anchoring. On decision execution, build towers first, then refresh walls so the new tower footprint is open in the same operation.

Set `Activated = true` only in `ExecuteDecision`. `OnMandateWarStarted` returns before queueing wall work when the stored state is not activated.

- [ ] **Step 5: Patch only local ownership changes**

`AW_MandateBorderWallPatch` captures old city/kingdom and neighbor city ids in prefixes, then queues the union of old city, new city, and direct neighbors in postfixes. Duplicate notifications are harmless because the queue key is per city. Skip load-time transfers and multiplayer replica application.

- [ ] **Step 6: Pass the guard and focused tests**

```powershell
pwsh -NoProfile -File Tests/MandateBorderWallLifecycleSourceGuard.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --cultiway-wall
```

Expected: PASS.

- [ ] **Step 7: Commit the lifecycle**

```powershell
git add -- Code/core/lineage/MandateBorderWallRefreshService.cs Code/core/lineage/MandateBorderDefenseService.cs Code/patch/AW_MandateBorderWallPatch.cs Tests/MandateBorderWallLifecycleSourceGuard.ps1
git commit -m "feat: refresh activated mandate walls by border city"
```

### Task 5: Prevent Stale Worker Wake-Ups from Completing New Operations

**Files:**
- Create: `Code/core/performance/AWSimulationWorkerDispatchGate.cs`
- Modify: `Code/core/performance/AWSimulationWorkerPool.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/SimulationWorkerPoolTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write deterministic failing dispatch-token tests**

```csharp
var gate = new AWSimulationWorkerDispatchGate(2);
gate.Assign(0, 7);
Equal(7, gate.Consume(0), "assigned generation is consumed");
Equal(0, gate.Consume(0), "duplicate wake has no participation token");
gate.Assign(0, 8);
Equal(8, gate.Consume(0), "new operation receives its own generation");
```

Add an actual-pool stress case alternating 2,000 `RunIndexed(0, 9, ...)` calls with 2,000 `BeginIndexed(0, 1, ...)` calls and assert scheduled equals executed after every completion.

- [ ] **Step 2: Run the full harness and verify RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: FAIL because `AWSimulationWorkerDispatchGate` is absent.

- [ ] **Step 3: Implement single-consume generation assignments**

```csharp
internal sealed class AWSimulationWorkerDispatchGate
{
    private readonly int[] _assignedGenerations;

    internal AWSimulationWorkerDispatchGate(int workerCount)
    {
        _assignedGenerations = new int[workerCount];
    }

    internal void Assign(int workerIndex, int generation)
    {
        Volatile.Write(ref _assignedGenerations[workerIndex], generation);
    }

    internal int Consume(int workerIndex)
    {
        return Interlocked.Exchange(
            ref _assignedGenerations[workerIndex], 0);
    }
}
```

In `StartOperation`, assign before `Set()`. In `WorkerLoop`, consume after `WaitOne`; continue immediately on zero; call `SignalParticipantCompleted` only for the consumed generation. This makes a leftover event harmless even if it fires during the next operation.

- [ ] **Step 4: Extend inconsistent-completion diagnostics**

Include `generation`, `remainingParticipants`, and `completionMarked` alongside the existing `nextIndex`, `endIndex`, and `stopRequested` values.

- [ ] **Step 5: Run the stress test repeatedly**

```powershell
1..10 | ForEach-Object {
    dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw "worker stress iteration $_ failed" }
}
```

Expected: ten PASS runs and no `did not execute all scheduled work` message.

- [ ] **Step 6: Commit the worker fix**

```powershell
git add -- Code/core/performance/AWSimulationWorkerDispatchGate.cs Code/core/performance/AWSimulationWorkerPool.cs Tests/AncientWarfare3.Rules.Tests/SimulationWorkerPoolTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "fix: generation-gate simulation worker wakeups"
```

### Task 6: Validate Prepared Native Military Paths Before Serial Commit

**Files:**
- Create: `Code/core/pathfinding/PreparedNativePathCommitRules.cs`
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`
- Modify: `Code/core/performance/AWCooperativeActorPostRunner.cs`
- Modify: `Code/patch/AW_GlobalPathfindingPatch.cs`
- Modify: `Code/core/lineage/PathfindingSafetyRules.cs`
- Modify: `Code/patch/AW_PathfindingSafetyPatch.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/PreparedNativePathCommitRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathMovementSafetyRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing prepared-state tests**

Define decisions `Commit`, `RetryLater`, and `Drop`. Add this test helper and
cover the four cases below:

```csharp
private static PreparedNativePathFacts Facts(
    bool actorAlive = true, int currentTileId = 11,
    int preparedTileId = 11, bool currentRegionValid = true)
{
    return new PreparedNativePathFacts(
        actorExists: true, actorAlive: actorAlive,
        actorIdMatches: true, batchExists: true,
        currentTileValid: true, targetTileValid: true,
        currentRegionValid: currentRegionValid,
        targetRegionValid: true,
        currentTileId: currentTileId,
        preparedCurrentTileId: preparedTileId,
        currentTargetTileId: 20,
        preparedTargetTileId: 20,
        currentPathIndex: 2,
        preparedPathIndex: 2,
        currentHasGlobalPath: false,
        preparedHadGlobalPath: false);
}

Equal(PreparedNativePathCommitDecision.Commit,
    PreparedNativePathCommitRules.Decide(Facts()));
Equal(PreparedNativePathCommitDecision.Drop,
    PreparedNativePathCommitRules.Decide(
        Facts(actorAlive: false)));
Equal(PreparedNativePathCommitDecision.RetryLater,
    PreparedNativePathCommitRules.Decide(
        Facts(currentTileId: 12, preparedTileId: 11)));
Equal(PreparedNativePathCommitDecision.Drop,
    PreparedNativePathCommitRules.Decide(
        Facts(currentRegionValid: false)));
```

Add `PathfindingSafetyRules` cases proving only a `NullReferenceException` with non-null start/target is converted; `InvalidOperationException` still propagates.

- [ ] **Step 2: Run the path-focused harness and verify RED**

First add a `--runtime-stability` branch near the other focused branches in
`Program.cs.txt`. It must run, in order,
`SimulationWorkerPoolTests.Run()`,
`PreparedNativePathCommitRulesTests.Run()`, and
`PathMovementSafetyRulesTests.Run()`, print
`PASS: runtime stability slice`, and return.

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --runtime-stability
```

Expected: FAIL because prepared commit rules are absent.

- [ ] **Step 3: Implement the pure commit facts and capture a fingerprint**

Create a readonly `PreparedNativePathFacts` struct with the sixteen constructor
parameters used by the test helper. Implement
`PreparedNativePathCommitRules.Decide(PreparedNativePathFacts)` with this order:

```csharp
if (!facts.ActorExists || !facts.ActorAlive || !facts.ActorIdMatches ||
    !facts.BatchExists)
    return PreparedNativePathCommitDecision.Drop;
if (!facts.CurrentTileValid || !facts.TargetTileValid ||
    !facts.CurrentRegionValid || !facts.TargetRegionValid)
    return PreparedNativePathCommitDecision.Drop;
if (facts.CurrentTileId != facts.PreparedCurrentTileId ||
    facts.CurrentTargetTileId != facts.PreparedTargetTileId ||
    facts.CurrentPathIndex != facts.PreparedPathIndex ||
    facts.CurrentHasGlobalPath != facts.PreparedHadGlobalPath)
    return PreparedNativePathCommitDecision.RetryLater;
return PreparedNativePathCommitDecision.Commit;
```

Extend `AWPreparedPathMovement` with:

```csharp
internal long ActorId { get; }
internal int CurrentTileId { get; }
internal int TargetTileId { get; }
internal int LocalPathIndex { get; }
internal bool HadGlobalPath { get; }
```

Populate these fields only for the vanilla/native branch. Never dereference a tile without checking `tile?.data`.

- [ ] **Step 4: Return an explicit commit outcome**

At commit, collect live facts including actor alive/rekt, batch existence, current and target tile data, both regions, current cursor, and actor id. `Drop` clears unusable movement and returns false. `RetryLater` returns false without calling vanilla pathfinding. `Commit` calls `updatePathMovement` serially and returns true.

Update both commit loops in `AWCooperativeActorPostRunner`: call `actor.skipBehaviour()` only on a successful commit; retain a live `RetryLater` actor for the next pass; remove a dead `Drop` actor.

- [ ] **Step 5: Preflight native military `Actor.goTo`**

Before returning `true` from the `ShouldUseNativeMilitaryPath` branch, require:

```csharp
__instance?.data != null && !__instance.isRekt() &&
__instance.current_tile?.data != null &&
pTile?.data != null &&
__instance.current_tile.region != null && pTile.region != null
```

On failure, stop movement, clear old path and tile target, set `__result = ExecuteEvent.False`, and return `false`.

- [ ] **Step 6: Make the global-path finalizer a narrow defense-in-depth boundary**

Remove the unconditional `if (PathfindingOwnershipService.IsAw3Owner) return __exception;`. Call `ShouldConvertGlobalPathExceptionToNotFound` for every vanilla `RegionPathFinder.getGlobalPath` exception. When classified, set `NotFound`, clear `last_globalPath`, emit one rate-limited diagnostic containing start/target tile and region ids, and return `null`. Do not catch exceptions outside this exact method.

- [ ] **Step 7: Run path and full regression suites**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --runtime-stability
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: both PASS.

- [ ] **Step 8: Commit the path lifecycle fix**

```powershell
git add -- Code/core/pathfinding/PreparedNativePathCommitRules.cs Code/core/pathfinding/AWPathMovementBridge.cs Code/core/performance/AWCooperativeActorPostRunner.cs Code/patch/AW_GlobalPathfindingPatch.cs Code/core/lineage/PathfindingSafetyRules.cs Code/patch/AW_PathfindingSafetyPatch.cs Tests/AncientWarfare3.Rules.Tests/PreparedNativePathCommitRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/PathMovementSafetyRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "fix: validate native path state before commit"
```

### Task 7: Build, Deploy Source, and Run Runtime Acceptance

**Files:**
- Verify: `AncientWarfare3.csproj`
- Verify: `deploy-local.ps1`
- Verify: `Tests/VerifySourceDeployment.ps1`
- Verify: `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`

- [ ] **Step 1: Run all focused guards**

```powershell
pwsh -NoProfile -File Tests/BanditStrongholdDeferredFallSourceGuard.ps1
pwsh -NoProfile -File Tests/MandateBorderWallLifecycleSourceGuard.ps1
pwsh -NoProfile -File run_relevant_guards.ps1
```

Expected: all PASS.

- [ ] **Step 2: Run the full rules harness and Release build**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Release
```

Expected: zero test failures and zero build errors.

- [ ] **Step 3: Confirm the worktree contains only intended changes**

```powershell
git status --short
git diff --check
```

Expected: only task files are changed; `.claude/worktrees/rts-army-overhaul` remains untouched and unstaged.

- [ ] **Step 4: Deploy source with the repository script**

```powershell
pwsh -NoProfile -File .\deploy-local.ps1 -SourceRoot 'F:\WorldBox New Mod\AncientWarfare3.0' -DestinationRoot 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
```

Expected: timestamped deployment backup followed by `DEPLOY-DONE`.

- [ ] **Step 5: Verify source parity**

```powershell
pwsh -NoProfile -File Tests/VerifySourceDeployment.ps1 -SourceRoot 'F:\WorldBox New Mod\AncientWarfare3.0' -DestinationRoot 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
```

Expected: all production source files and SHA256 hashes match.

- [ ] **Step 6: Run the in-game acceptance sequence**

Start WorldBox visibly. In one save:

1. Create a bandit stronghold, kill all residents with an enemy, confirm the city remains until capture reaches 100%, then confirm direct destruction without negotiation and with chronicles on both sides.
2. Create another stronghold and starve it to zero, confirm deferred return to the mother city and wall/tower cleanup.
3. Execute `mandate_border_defense`, move an army through a frontier watch tower, and confirm no wall top tile exists under the tower footprint.
4. Transfer one frontier city or zone. Confirm only that city and direct neighbors remove old walls and rebuild the new border; all towers remain.
5. Start a mandate war before and after decision activation. Confirm only the activated case refreshes walls.
6. Run large-mode RTS movement for at least ten simulation minutes and inspect the log for both exact signatures:
   - `Simulation worker did not execute all scheduled work`
   - `RegionPathFinder.getGlobalPath` followed by `AWPathMovementBridge.CommitPreparedPathMovement`

Expected: neither signature appears, the game does not pause, RTS armies continue moving, and no actor-window or city lifecycle regression occurs.

- [ ] **Step 7: Commit verification metadata only if implementation added it**

If runtime evidence files are intentionally tracked, add only those explicit files and commit them. Otherwise leave generated logs and screenshots untracked.

# AW3 Complete Cultiway Pathfinding Replacement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace AW3's partial full-map path rewrite with Cultiway's reusable, lazy, cursor-driven search/movement and complete vanilla dock/boat lifecycle, while proving a main-thread performance improvement.

**Architecture:** Keep AW3's Harmony-owner arbitration and adapt the complete Cultiway request, recovery, path search, fast movement, portal, and transport behavior into AW3-focused files. Search reads traversal data lazily through a live adapter and reuses worker-local buffers; one latest-request slot per actor bounds retarget churn. All WorldBox mutation remains on the main thread.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, WorldBox Actor/boat AI, Cultiway-Reborn MIT reference implementation, NeoModLoader, .NET 9 pure search/rule harness, PowerShell source guards.

**Execution constraint:** Execute inline on `master` after the school-runtime plan. Do not create a branch, worktree, or subagent. Preserve unrelated user changes.

**Design reference:** `docs/superpowers/specs/2026-07-15-aw3-school-runtime-cultiway-performance-design.md`

**Authoritative upstream:**

- `F:/WorldBox New Mod/Cultiway-Reborn-master/Source/Core/Pathfinding/`
- `F:/WorldBox New Mod/Cultiway-Reborn-master/Source/Patch/PatchAboutPathfinding.cs`
- `F:/WorldBox New Mod/Cultiway-Reborn-master/Source/Utils/PriorityQueuePreview.cs`

---

## File Map

New focused units:

- `Code/core/pathfinding/AWPathLifecycleRules.cs`: pure request-key, recovery, latest-slot, and world-generation rules.
- `Code/core/pathfinding/AWTraversalCell.cs`: immutable on-demand traversal cell with fixed neighbors.
- `Code/core/pathfinding/AWLiveTraversalSource.cs`: WorldTile-to-cell adapter used only by workers for reads.
- `Code/core/pathfinding/AWPathSearchCore.cs`: Cultiway multi-label route search over an abstract traversal source.
- `Code/core/pathfinding/AWPathSearchWorkspace.cs`: worker-local reusable node, heap, label, and tile buffers.
- `Code/core/pathfinding/AWPortalDefinition.cs`: lightweight dock portal/connection snapshots.
- `Code/core/pathfinding/AWPortalRegistry.cs`: living vanilla dock registry.
- `Code/core/pathfinding/AWWaterConnectivityService.cs`: dirty-event water graph rebuild.
- `Code/core/pathfinding/AWPortalRequest.cs`: passenger/driver route state.
- `Code/core/pathfinding/AWPortalManager.cs`: reusable portal requests and lifecycle cleanup.
- `Code/patch/AW_CultiwayTransportPatch.cs`: boat AI loading, driving, unloading, and destruction integration.
- `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs`: pure request/search/recovery/portal tests.

Files replaced in place to preserve external AW3 references:

- `AWPathTypes.cs`, `AWPathRequest.cs`, `AWPathStream.cs`, `AWPathFinder.cs`,
  `AWPathRecoveryManager.cs`, `AWPathMovementBridge.cs`, `AWPathfindingBootstrap.cs`, and
  `AW_GlobalPathfindingPatch.cs`.

Files removed after references reach zero:

- `AWTraversalSnapshot.cs`;
- `AWTraversalCache.cs`;
- `AWTraversalRules.cs`;
- `AWStreamingPathGenerator.cs`.

---

### Task 1: Establish Failing Cultiway-Parity and Performance Tests

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add the initial pure lifecycle tests**

Create `PathfindingPerformanceTests.cs`:

```csharp
using AncientWarfare3.core.pathfinding;

internal static class PathfindingPerformanceTests
{
    public static void Run()
    {
        var key = new AWPathRequestKey(91, true, false, false, 4);
        True(key.Matches(91, true, false, false, 4),
            "same target and options reuse");
        Equal(false, key.Matches(92, true, false, false, 4),
            "new target supersedes");
        Equal(false, key.Matches(91, false, false, false, 4),
            "changed water option supersedes");

        var slot = new AWLatestPathSlotRules();
        Equal(AWPathSlotAction.Enqueue, slot.Submit(false, false),
            "idle actor queues first request");
        Equal(AWPathSlotAction.ReplacePending, slot.Submit(true, false),
            "queued actor replaces pending request without another queue node");
        slot.WorkerStarted();
        Equal(AWPathSlotAction.StoreAfterRunning, slot.Submit(false, true),
            "retarget while running stores one latest request");
        True(slot.WorkerFinished(), "worker completion queues latest request once");
        Equal(1, slot.MaximumQueuedNodes, "one actor slot has at most one queue node");

        Equal(4, AWPathLifecycleRules.RetryLimit(AWPathFailureReason.StepBlocked),
            "blocked step retries four times");
        Equal(2, AWPathLifecycleRules.RetryLimit(AWPathFailureReason.TransportFailed),
            "transport retries twice");
        Equal(0, AWPathLifecycleRules.RetryLimit(AWPathFailureReason.Unreachable),
            "unreachable route is terminal");
        True(AWPathLifecycleRules.AcceptWorldGeneration(12, 12),
            "current world output is accepted");
        Equal(false, AWPathLifecycleRules.AcceptWorldGeneration(12, 13),
            "stale world output is rejected");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException($"{name}: expected true");
    }
}
```

Call `PathfindingPerformanceTests.Run();` before the final success line in `Program.cs`.

- [ ] **Step 2: Link planned pure production files**

Add:

```xml
<Compile Include="..\..\Code\core\pathfinding\AWPathLifecycleRules.cs" Link="Production\AWPathLifecycleRules.cs" />
<Compile Include="..\..\Code\core\pathfinding\AWPathSearchCore.cs" Link="Production\AWPathSearchCore.cs" />
<Compile Include="..\..\Code\core\pathfinding\AWTraversalCell.cs" Link="Production\AWTraversalCell.cs" />
```

- [ ] **Step 3: Add path source-guard helpers and invariants**

Add this helper:

```powershell
function Require-PathAbsent([string]$name, [string]$relativePath) {
    if (Test-Path (Join-Path $root $relativePath)) {
        $failures.Add("${name}: forbidden path still exists: $relativePath")
    }
}
```

Add guards:

```powershell
Require-PathAbsent 'full traversal snapshot' 'Code/core/pathfinding/AWTraversalSnapshot.cs'
Require-PathAbsent 'full traversal cache' 'Code/core/pathfinding/AWTraversalCache.cs'
Require-PathAbsent 'partial streaming generator' 'Code/core/pathfinding/AWStreamingPathGenerator.cs'
Require-Absent 'fixed path consistency sweep' 'Code/core/pathfinding/AWPathfindingBootstrap.cs' 'ConsistencySweep('
Require-Absent 'eager path startup' 'Code/core/pathfinding/AWPathfindingBootstrap.cs' 'EnsureStarted();'
Require-Present 'reuse before request allocation' 'Code/core/pathfinding/AWPathMovementBridge.cs' 'TryReuseActiveRequest'
Require-Present 'ready path cursor' 'Code/core/pathfinding/AWPathFinder.cs' 'ReadyPathCursor'
Require-Present 'fast Cultiway step' 'Code/core/pathfinding/AWPathMovementBridge.cs' 'FastMoveTo('
Require-Present 'vanilla side effect replay' 'Code/core/pathfinding/AWPathMovementBridge.cs' 'FastMoveToWithMoveToSideEffects'
Require-Present 'dock portal registry' 'Code/core/pathfinding/AWPortalRegistry.cs' 'RegisterOrUpdate'
Require-Present 'boat driver lifecycle' 'Code/patch/AW_CultiwayTransportPatch.cs' 'BehBoatFindRequest'
```

- [ ] **Step 4: Run and prove red state**

Run rule tests and source guards. Expected: missing linked lifecycle/search files and all
old-cache/new-Cultiway guards fail.

- [ ] **Step 5: Commit failing tests**

```powershell
git add Tests
git commit -m "test: define complete Cultiway path invariants"
```

---

### Task 2: Port Lifecycle Types, Request Keys, Streams, and Recovery Rules

**Files:**
- Create: `Code/core/pathfinding/AWPathLifecycleRules.cs`
- Modify: `Code/core/pathfinding/AWPathTypes.cs`
- Modify: `Code/core/pathfinding/AWPathfindingConfig.cs`
- Modify: `Code/core/pathfinding/AWPathRequest.cs`
- Modify: `Code/core/pathfinding/AWPathStream.cs`
- Modify: `Code/core/pathfinding/AWPathRecoveryManager.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs`

- [ ] **Step 1: Implement pure request and retry rules**

Create `AWPathLifecycleRules.cs`:

```csharp
using System;

namespace AncientWarfare3.core.pathfinding
{
    public readonly struct AWPathRequestKey : IEquatable<AWPathRequestKey>
    {
        public AWPathRequestKey(int pTarget, bool pWater, bool pBlocks, bool pLava,
            int pRegionLimit)
        {
            TargetTileId = pTarget; PathOnWater = pWater; WalkOnBlocks = pBlocks;
            WalkOnLava = pLava; RegionLimit = Math.Max(0, pRegionLimit);
        }
        public int TargetTileId { get; }
        public bool PathOnWater { get; }
        public bool WalkOnBlocks { get; }
        public bool WalkOnLava { get; }
        public int RegionLimit { get; }
        public bool Matches(int pTarget, bool pWater, bool pBlocks, bool pLava,
            int pRegionLimit) => Equals(new AWPathRequestKey(pTarget, pWater, pBlocks,
                pLava, pRegionLimit));
        public bool Equals(AWPathRequestKey pOther) =>
            TargetTileId == pOther.TargetTileId && PathOnWater == pOther.PathOnWater &&
            WalkOnBlocks == pOther.WalkOnBlocks && WalkOnLava == pOther.WalkOnLava &&
            RegionLimit == pOther.RegionLimit;
        public override bool Equals(object pObject) =>
            pObject is AWPathRequestKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(TargetTileId, PathOnWater,
            WalkOnBlocks, WalkOnLava, RegionLimit);
    }

    public enum AWPathSlotAction { Enqueue, ReplacePending, StoreAfterRunning }

    public sealed class AWLatestPathSlotRules
    {
        private bool _queued;
        private bool _running;
        public int MaximumQueuedNodes { get; private set; }
        public AWPathSlotAction Submit(bool pHasPending, bool pHasRunning)
        {
            _queued |= pHasPending;
            _running |= pHasRunning;
            if (_running) return AWPathSlotAction.StoreAfterRunning;
            if (_queued) return AWPathSlotAction.ReplacePending;
            _queued = true;
            MaximumQueuedNodes = Math.Max(MaximumQueuedNodes, 1);
            return AWPathSlotAction.Enqueue;
        }
        public void WorkerStarted() { _queued = false; _running = true; }
        public bool WorkerFinished() { _running = false; _queued = true;
            MaximumQueuedNodes = Math.Max(MaximumQueuedNodes, 1); return true; }
    }

    public static class AWPathLifecycleRules
    {
        public static int RetryLimit(AWPathFailureReason pReason) => pReason switch
        {
            AWPathFailureReason.StepBlocked => 4,
            AWPathFailureReason.UnsafeStep => 4,
            AWPathFailureReason.PortalUnavailable => 2,
            AWPathFailureReason.TransportFailed => 2,
            AWPathFailureReason.Timeout => 2,
            AWPathFailureReason.GeneratorException => 1,
            _ => 0
        };
        public static bool AcceptWorldGeneration(int pRequestGeneration,
            int pCurrentGeneration) => pRequestGeneration == pCurrentGeneration;
    }
}
```

If `System.HashCode` is unavailable under net48, replace `HashCode.Combine` with the existing
unchecked `* 397` hash pattern while keeping the same fields.

- [ ] **Step 2: Align lifecycle enums and path step with Cultiway**

Use `Pending`, `Streaming`, `Succeeded`, `Failed`, `Cancelled`; movement methods `Walk`,
`Swim`, `Portal`; and Cultiway hazard flags including `Direct` and `Portal`. A portal step
stores immutable entry/exit definitions, not a taxi request ID.

- [ ] **Step 3: Replace lock-based stream state with Cultiway atomics**

Port `PathStream` semantics: `ConcurrentQueue<AWPathStep>`, integer status with
`Interlocked.CompareExchange`, `TryPeek`, `TryDequeue`, `Complete`, `Cancel`, `Fail`, and
`EnsureCompleted`. No per-step `_stateGate` lock remains.

- [ ] **Step 4: Capture request inputs on the main thread**

`AWPathRequest` stores actor ID, request key, start/target IDs, world generation, immutable
movement snapshot, stream, and cancellation token. It does not retain a full traversal
generation. Keep cultivation/ECS fields absent.

- [ ] **Step 5: Use one recovery rule source**

`AWPathRecoveryManager` calls `AWPathLifecycleRules.RetryLimit`, resets attempts when failure
reason changes, uses Cultiway exponential delay `clamp(0.3 * 2^(n-1), 0.3, 2)`, and removes
state on progress, actor cleanup, or world clear.

- [ ] **Step 6: Run tests and commit**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
git add Code/core/pathfinding/AWPathLifecycleRules.cs Code/core/pathfinding/AWPathTypes.cs Code/core/pathfinding/AWPathfindingConfig.cs Code/core/pathfinding/AWPathRequest.cs Code/core/pathfinding/AWPathStream.cs Code/core/pathfinding/AWPathRecoveryManager.cs Tests
git commit -m "feat: port Cultiway path lifecycle"
```

---

### Task 3: Implement Lazy Workers, Reuse-Before-Allocation, and Latest Actor Slots

**Files:**
- Modify: `Code/core/pathfinding/AWPathFinder.cs`
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`
- Modify: `Code/core/pathfinding/AWPathfindingBootstrap.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs`
- Test: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add a failing allocation-counter test for request decisions**

Warm `AWPathRequestKey`, then execute 10,000 `Matches` calls and assert zero allocated bytes.
Add source-order validation that the text `TryReuseActiveRequest` occurs before
`new AWPathRequest` in the submit method.

- [ ] **Step 2: Replace the unbounded task queue with actor slots**

Each `ActorPathSlot` owns:

```csharp
long ActorId;
object Sync;
AWPathfindingTask Pending;
AWPathfindingTask Running;
bool Queued;
```

Request submission locks only that actor slot. It reuses matching pending/running/streaming
work. A changed request cancels old pending/running work and stores exactly one latest
pending task. It enqueues the actor ID only when no worker is currently running that slot
and no actor-ID node is already queued.

A worker dequeues the actor ID, moves `Pending -> Running`, clears `Queued`, generates, then
clears `Running`. If one newer pending task arrived while it ran, the worker enqueues the
same actor ID once. Remove an empty slot only by exact dictionary key/value comparison.

- [ ] **Step 3: Start workers lazily**

Remove pathfinder startup and traversal initialization from per-frame bootstrap. The first
non-reused request calls `EnsureWorkersStarted` and creates 1-4 background workers. Ownership
pending/Cultiway states never start workers.

- [ ] **Step 4: Add `ReadyPathCursor`**

Port Cultiway's cursor exactly: it retains owner, actor ID, and task identity; `Poll` reads
that stream directly; `Consume` dequeues once and exact-cleans a finished task. It performs
no repeated actor dictionary lookup inside one smooth movement update.

- [ ] **Step 5: Implement deterministic shutdown/restart**

Unlike upstream's process-lifetime workers, AW3 must stop workers on ownership yield.
`StopAndDrain` signals shutdown, cancels all slots, wakes and joins workers, clears pending
actor IDs, and leaves the manager restartable for a later fresh world only. World generation
increments before cancellation so stale output cannot be consumed.

- [ ] **Step 6: Verify queue bounds and commit**

Run tests and a synthetic 10,000-retarget slot test. Expected queued actor nodes never
exceed active actor slots plus workers.

```powershell
git add Code/core/pathfinding/AWPathFinder.cs Code/core/pathfinding/AWPathMovementBridge.cs Code/core/pathfinding/AWPathfindingBootstrap.cs Tests
git commit -m "perf: reuse and bound Cultiway path requests"
```

---

### Task 4: Port Cultiway Search over Lazy Live Traversal with Reusable Workspaces

**Files:**
- Create: `Code/core/pathfinding/AWTraversalCell.cs`
- Create: `Code/core/pathfinding/AWLiveTraversalSource.cs`
- Create: `Code/core/pathfinding/AWPathSearchCore.cs`
- Create: `Code/core/pathfinding/AWPathSearchWorkspace.cs`
- Create: `Code/core/pathfinding/AWPortalDefinition.cs`
- Create: `Code/core/pathfinding/AWPortalRegistry.cs`
- Create: `Code/core/pathfinding/AWPortalAwarePathGenerator.cs`
- Modify: `Code/core/pathfinding/AWPathFinder.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs`

- [ ] **Step 1: Add fake-grid failing search tests**

Create an in-test `FakeTraversalSource` and assert:

```text
5x5 open grid reaches target
blocked center produces a detour
diagonal route has the expected shorter step count
non-water actor avoids lethal ocean when land exists
fire/lava risk loses to a safe detour
stamina/health labels retain a non-dominated route
short search hitting its limit runs the long corridor fallback
cancellation interrupts expansion
```

Run and expect missing search types.

- [ ] **Step 2: Define a pure traversal source**

`AWTraversalCell` contains tile ID, x/y, type flags, damage, walk multiplier, and eight fixed
neighbor IDs. Define:

```csharp
public interface IAWTraversalSource
{
    bool TryGet(int pTileId, out AWTraversalCell pCell);
}
```

The test fake and production live adapter implement the same interface. No full-world array
or generation is part of the contract.

- [ ] **Step 3: Implement the live adapter**

Resolve `World.world.tiles_list[tileId]` on demand and capture only that tile. Read the same
fields as Cultiway `TileTraversalInfo`: block, lava, ocean, liquid, damage, walk multiplier,
type ID, fire, coordinates, and live neighbor IDs. Catch only fire/property exceptions that
upstream treats as optional. Workers never mutate `WorldTile`.

- [ ] **Step 4: Implement reusable search storage**

One workspace per worker owns reusable node list/array, binary heap, tile cache, and a fixed
four-label set per tile. `Reset()` clears counts and dictionaries but retains capacity. Nodes
are structs with parent index rather than one class allocation per expanded label.

- [ ] **Step 5: Port Cultiway route semantics**

Port `PortalAwarePathGenerator` behavior:

```text
direct long local search
best portal estimate
entry and exit local searches
real-cost comparison with direct route
60,000-node corridor fallback after long-limit exhaustion
multi-label dominance on G, stamina, health, and risk
Cultiway hazard/speed/cost constants
cancellation checks during expansion and emission
```

Cultivation power reduction is omitted. Actor snapshots still include vanilla movement,
health, stamina, immunity, water, lava, flying, boat, and fast-swimming properties.

- [ ] **Step 6: Run pure search tests and allocation warm-up test**

Run 1,000 identical fake-grid searches after warm-up and assert workspace capacity stops
growing and per-search allocations are zero except the final returned step buffer/stream
segments. Record expanded nodes and route equality across runs.

- [ ] **Step 7: Commit**

```powershell
git add Code/core/pathfinding/AWTraversalCell.cs Code/core/pathfinding/AWLiveTraversalSource.cs Code/core/pathfinding/AWPathSearchCore.cs Code/core/pathfinding/AWPathSearchWorkspace.cs Code/core/pathfinding/AWPortalDefinition.cs Code/core/pathfinding/AWPortalRegistry.cs Code/core/pathfinding/AWPortalAwarePathGenerator.cs Code/core/pathfinding/AWPathFinder.cs Tests
git commit -m "feat: port Cultiway multi-label search"
```

---

### Task 5: Port Ready-Cursor Smooth Movement and Cultiway Fast Steps

**Files:**
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`
- Modify: `Code/patch/AW_GlobalPathfindingPatch.cs`
- Modify: `Code/core/pathfinding/AWPathDiagnostics.cs`
- Test: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add source guards for fast/slow movement separation**

Require `GetFastMoveBlockReason`, `FastMoveTo`, `FastMoveToWithMoveToSideEffects`,
`SetMoveStepTile`, `ApplyStepActionForCurrentTile`, and one `ReadyPathCursor` local in smooth
movement. Reject an unconditional `pActor.moveTo(tile)` in the ordinary safe-step branch.

- [ ] **Step 2: Port Cultiway poll handling**

Use `OpenReadyCursor` in `updatePathMovement` and retain that cursor across up to 256 smooth
tile boundaries. Consumed steps advance recovery; failed steps cancel exact work and enter
bounded recovery; waiting actors set a short timer; no-request actors request recovery only
when recovery state exists.

- [ ] **Step 3: Port fast movement and side-effect replay**

Use upstream `PatchAboutPathfinding.cs` implementations for:

```text
GetFastMoveBlockReason
FastMoveTo
FastMoveToWithMoveToSideEffects
SetMoveStepTile
ApplyStepActionForCurrentTile
GetBoundaryWalkedDistance
ContinuePathMovementFromSmooth
```

Safe ground steps directly update movement batch, next-step tile, current/dirty tile, and
next-step position. Boats use vanilla `moveTo`. Tile step actions and enabled fungi/flower/
plant laws use selective side-effect replay. Record fast and vanilla step counters.

- [ ] **Step 4: Preserve calibration throttling**

Keep the socialization exception and 0.25-second repeated calibration suppression. Prune
calibration state on actor disposal and world clear.

- [ ] **Step 5: Build and commit**

Run guards and Debug/Release builds. Expected: Harmony transpiler finds exactly one
calibration call and one movement call; no patch error.

```powershell
git add Code/core/pathfinding/AWPathMovementBridge.cs Code/patch/AW_GlobalPathfindingPatch.cs Code/core/pathfinding/AWPathDiagnostics.cs Tests/SourceGuardTests.ps1
git commit -m "perf: port Cultiway fast actor movement"
```

---

### Task 6: Register Vanilla Docks and Rebuild Water Connectivity on Demand

**Files:**
- Create: `Code/core/pathfinding/AWWaterConnectivityService.cs`
- Modify: `Code/core/pathfinding/AWPortalDefinition.cs`
- Modify: `Code/core/pathfinding/AWPortalRegistry.cs`
- Modify: `Code/patch/AW_GlobalPathfindingPatch.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs`

- [ ] **Step 1: Add failing portal graph tests**

Test register/update/remove, same-water-component connection, disconnected ocean rejection,
nearest connection ordering, graph generation change only when dirty, and no rebuild when
no docks or no relevant dirty regions exist.

- [ ] **Step 2: Adapt Cultiway portal definitions to vanilla buildings**

Each immutable definition stores building ID/reference, land/portal tile, ocean tile, wait
time, transfer time, graph generation, and immutable target connections. Registry mutation
occurs on the main thread; workers consume an immutable snapshot.

- [ ] **Step 3: Hook dock lifecycle**

On `Building.setState`, identify `asset.docks`. Normal buildings register/update; ruins or
removed buildings unregister. World load performs one bounded dock enumeration. Building
destruction removes affected portal requests immediately.

- [ ] **Step 4: Rebuild connectivity only after relevant dirtiness**

Adapt Cultiway `WaterConnectivityUpdater`: flood connected ocean `MapRegion` values, group
docks by component, and create ordered connections. Trigger from dirty region/chunk events
or dock state changes, never every frame. Coalesce multiple dirty notifications into one
pending rebuild.

- [ ] **Step 5: Run graph tests and commit**

```powershell
git add Code/core/pathfinding/AWWaterConnectivityService.cs Code/core/pathfinding/AWPortalDefinition.cs Code/core/pathfinding/AWPortalRegistry.cs Code/patch/AW_GlobalPathfindingPatch.cs Tests
git commit -m "feat: index Cultiway dock portals"
```

---

### Task 7: Port Reusable Passenger Requests and Complete Boat AI Lifecycle

**Files:**
- Create: `Code/core/pathfinding/AWPortalRequest.cs`
- Create: `Code/core/pathfinding/AWPortalManager.cs`
- Create: `Code/patch/AW_CultiwayTransportPatch.cs`
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`
- Modify: `Code/core/pathfinding/AWPathfindingBootstrap.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs`

- [ ] **Step 1: Add failing transport lifecycle tests**

Test compatible passenger reuse, route extension without backtracking, one driver per
request, load/unload tile retention, dead passenger removal, dead driver release, destroyed
dock merge/repair, final completion, cancellation, and world clear.

- [ ] **Step 2: Port Cultiway request state**

Use states `WaitingDriver`, `WaitingPassengers`, `Driving`, and `Completed`. Each route leg
stores dock building/tile, load/unload actor IDs, and per-actor load/unload tiles. Index
passenger actor ID and driver actor ID to requests so lookup is O(1), while retaining the
same route reuse/extension semantics as Cultiway.

- [ ] **Step 3: Handle portal steps**

When movement reaches a portal step, create/reuse a portal request and defer consumption.
The ready portal step is consumed exactly once when the passenger is actually loaded. A
completed/invalid request produces `TransportFailed` and bounded recovery.

- [ ] **Step 4: Port the boat behavior patches**

Adapt the upstream patches for these exact original methods:

```text
BehBoatFindRequest.execute
BehBoatTransportFindTilePickUp.execute
BehBoatTransportDoLoading.execute and its common-passenger transpiler
BehBoatTransportFindTileUnload.execute
BehBoatTransportUnloadUnits.execute
Actor.u1_checkInside
MapBox.checkEventUnitsDestroy
MapBox.checkEventBuildingsDestroy
```

Loading marks passengers inside the boat and consumes their ready portal step. Unloading
uses the recorded exit tile, cancels stale path tasks, and requests the remaining land path.
Destroyed docks repair toward the next leg when possible; otherwise cancel cleanly.

- [ ] **Step 5: Bound portal processing**

Maintain direct dirty request IDs. Per-frame portal maintenance processes a fixed maximum
without scanning all requests. Completion removes all passenger/driver indexes and route
state.

- [ ] **Step 6: Build and commit**

```powershell
git add Code/core/pathfinding/AWPortalRequest.cs Code/core/pathfinding/AWPortalManager.cs Code/patch/AW_CultiwayTransportPatch.cs Code/core/pathfinding/AWPathMovementBridge.cs Code/core/pathfinding/AWPathfindingBootstrap.cs Tests
git commit -m "feat: port Cultiway boat transport lifecycle"
```

---

### Task 8: Remove the Partial Traversal System and Harden Ownership Cleanup

**Files:**
- Delete: `Code/core/pathfinding/AWTraversalSnapshot.cs`
- Delete: `Code/core/pathfinding/AWTraversalCache.cs`
- Delete: `Code/core/pathfinding/AWTraversalRules.cs`
- Delete: `Code/core/pathfinding/AWStreamingPathGenerator.cs`
- Modify: `Code/core/pathfinding/AWPathfindingBootstrap.cs`
- Modify: `Code/core/pathfinding/PathfindingOwnershipService.cs`
- Modify: `Code/patch/AW_GlobalPathfindingPatch.cs`
- Modify: `Code/patch/AW_PathfindingSafetyPatch.cs`
- Modify: `Code/patch/AW_DeferredRuntimeWorkPatch.cs`
- Test: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Prove all old-type references are gone**

Run:

```powershell
rg -n "AWTraversalGeneration|AWTraversalCache|AWTraversalRules|AWStreamingPathGenerator" Code Tests
```

Expected before deletion: no references outside the four files being removed. If a live
reference remains, replace it with the new live source/search/portal owner first.

- [ ] **Step 2: Delete the four obsolete files**

Remove them and remove every tile-type/fire/building dirty hook that existed solely to
maintain the full traversal cache. Keep dock/water connectivity dirtiness hooks.

- [ ] **Step 3: Reduce bootstrap to ownership and real pending work**

Per-frame bootstrap processes owner state transitions, bounded portal work, dirty water
connectivity, and bounded diagnostics only when their pending flags are non-zero. It does
not initialize workers or scan tiles.

- [ ] **Step 4: Harden late-owner yield**

When state changes to `Suspending` or `Cultiway`, cancel/stop AW3 slots and workers, release
waiting actors without consuming another step, clear portal requests/registry, and cease
interception. Do not unpatch Cultiway. A fresh world may reevaluate ownership; the same world
does not hot-enable AW3 after yielding.

- [ ] **Step 5: Keep vanilla safety patch dormant under either custom owner**

`AW_PathfindingSafetyPatch` runs only for a vanilla global-path call not owned by AW3 or
Cultiway. It must not hide errors from the custom movement chain.

- [ ] **Step 6: Run path-absence guards and commit**

```powershell
git add -A Code/core/pathfinding Code/patch/AW_GlobalPathfindingPatch.cs Code/patch/AW_PathfindingSafetyPatch.cs Code/patch/AW_DeferredRuntimeWorkPatch.cs Tests/SourceGuardTests.ps1
git commit -m "perf: remove partial AW3 traversal cache"
```

---

### Task 9: Complete Attribution, Diagnostics, Builds, and Static Verification

**Files:**
- Modify: `THIRD_PARTY_NOTICES.md`
- Modify: `Code/core/pathfinding/AWPathDiagnostics.cs`
- Modify: `Tests/SourceGuardTests.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs`

- [ ] **Step 1: Update the third-party description**

State that AW3 now adapts Cultiway request reuse, lazy workers, multi-label search, ready
cursor, fast movement, portal registry, water connectivity, and boat lifecycle. Remove the
obsolete claim that the port uses immutable full-map worker snapshots. Keep the complete MIT
license and copyright.

- [ ] **Step 2: Add final counter and bound assertions**

Expose generated/reused/superseded/cancelled/completed/failed, pending slots, worker use,
first-step latency, expanded/fallback nodes, stale-world rejection, recovery, cursor/fast/
vanilla steps, portal/passenger/driver counts, and workspace capacity. Diagnostics retain a
fixed maximum of 32 exception samples and never log per frame.

- [ ] **Step 3: Run all automated verification**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\SourceGuardTests.ps1
dotnet build AncientWarfare3.csproj -c Debug --no-restore -p:AutomaticallyUseReferenceAssemblyPackages=true
dotnet build AncientWarfare3.csproj -c Release --no-restore -p:AutomaticallyUseReferenceAssemblyPackages=true
git diff --check
```

Expected: all tests/guards pass, both builds have zero errors, and diff check is empty.

- [ ] **Step 4: Inspect Harmony target existence**

For every patched method, confirm an exact signature in
`AssetRipper_export_20260628_163320/ExportedProject/Assets/Scripts/Assembly-CSharp`.
The build alone does not prove a transpiler pattern match; source guards must verify expected
call counts and the live log must verify patch success.

- [ ] **Step 5: Commit**

```powershell
git add THIRD_PARTY_NOTICES.md Code/core/pathfinding/AWPathDiagnostics.cs Tests
git commit -m "docs: complete Cultiway path attribution and guards"
```

---

### Task 10: Run Comparative Path and Combined Long-Run Acceptance

**Files:**
- Create: `docs/performance/2026-07-15-cultiway-path-results.md`
- Modify: `docs/performance/2026-07-15-school-runtime-results.md`
- Modify only production/test files required by observed failing acceptance.

- [ ] **Step 1: Deploy and verify patch ownership**

On a fresh world, verify `Player.log` reports AW3 as owner, workers remain absent until the
first path request, and every Harmony patch succeeds. Repeat with real Cultiway installed and
verify only Cultiway owns movement and AW3 workers/requests remain zero.

- [ ] **Step 2: Run request and movement stress scenarios**

Record exact results for:

```text
10,000 repeated same-target requests after one initial request
10,000 rapid retargets across a fixed actor set
500 concurrent safe-ground walkers
obstacle detours and unreachable targets
fire/lava/ocean hazard alternatives
world clear during active searches
actor death/disposal during pending and streaming work
```

Require no new request/task/token/worker item for repeated same-target calls, queued work no
larger than active actor slots plus workers, no stale-world movement, and no stuck actors
after exhausted recovery.

- [ ] **Step 3: Run dock/boat scenarios**

Verify shared passengers, driver assignment, load, sail, unload, continuation, dead
passenger, dead boat, destroyed entry dock, destroyed exit dock, disconnected water, and
world clear. Inspect both actor state and portal indexes after every terminal case.

- [ ] **Step 4: Compare performance to both baselines**

Under identical seed/save/camera/speed/population, require:

```text
no full-map path allocation
no fixed per-frame tile sweep
>= 90% safe ground steps through fast movement
500-walker Actor movement p95 >= 20% below pre-replacement AW3
500-walker Actor movement p95 no worse than vanilla
no new long GC spike attributable to path search
```

Record p50/p95/max, allocations, request counts, workspace capacity, expanded nodes, and
first-step latency. Do not infer performance from source guards.

- [ ] **Step 5: Repeat combined school checkpoints at years 50/100/200**

Use the same fixed fresh-world protocol from the school plan. Confirm that physical scholar
travel, lectures, debates, Actor/updateAge time, ecology, and cache bounds still pass after
global movement replacement.

- [ ] **Step 6: Fix each failed acceptance through TDD**

Add a focused failing pure/source/integration test before production edits. Repeat the exact
scenario and retain the original threshold. After three failed fixes to one subsystem, stop
and reassess the architecture rather than stacking a fourth patch.

- [ ] **Step 7: Commit measured results**

```powershell
git add docs/performance Code Tests
git commit -m "test: verify Cultiway path performance"
```

- [ ] **Step 8: Final completion audit**

Map every requirement in the design spec to an automated result, source invariant, database
query, live metric, or scenario record. Any missing or indirect evidence remains incomplete.
Only after all requirements have direct evidence may the active goal be marked complete.

# Global Streaming Pathfinding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace every WorldBox `Actor.goTo` route with an AW3-owned, Cultiway-derived, multithreaded streaming pathfinder while preserving movement side effects, vanilla dock/boat transport, deterministic cancellation, and single-owner behavior when Cultiway is loaded.

**Architecture:** Pure path types, streams, recovery rules, and the multi-label A* generator consume only immutable tile IDs and snapshots and are tested outside Unity. Main-thread adapters build copy-on-write world snapshots, capture actor profiles, validate emitted steps, coordinate vanilla boats, and bridge Harmony movement methods; worker threads never touch live WorldBox or Unity objects. `PathfindingOwnershipService` treats Cultiway owner `inmny.cultiway` as authoritative and drains AW3 safely if that owner appears.

**Tech Stack:** C# 11, .NET Framework 4.8, .NET 9 temporary rule harness, Harmony, NeoModLoader, WorldBox publicized API, `System.Collections.Concurrent`, Cultiway-Reborn MIT source.

**Execution constraint:** Work directly on `master` because the user explicitly rejected branches/worktrees. Keep all tests under `F:/tmp/AW3PathfindingRuleTests`; never restore or stage the user-deleted `Tests/` or `Verification/` trees.

---

## File Structure

- `THIRD_PARTY_NOTICES.md`: Cultiway MIT provenance and copied-source notice.
- `Code/core/pathfinding/AWPathfindingConfig.cs`: fixed first-release limits and worker-count calculation.
- `Code/core/pathfinding/AWPathTypes.cs`: lifecycle, failure, movement, hazard, estimate, step, poll, and process value types.
- `Code/core/pathfinding/AWPathRequest.cs`: immutable actor/request input and request identity comparison.
- `Code/core/pathfinding/AWPathStream.cs`: concurrent step stream with exactly-one terminal state.
- `Code/core/pathfinding/AWBinaryHeap.cs`: allocation-bounded min heap derived from Cultiway's queue.
- `Code/core/pathfinding/AWTraversalSnapshot.cs`: immutable tile/chunk/generation data and actor traversal profile.
- `Code/core/pathfinding/AWTraversalRules.cs`: pure passability, cost, dominance, heuristic, and fallback-corridor rules.
- `Code/core/pathfinding/AWStreamingPathGenerator.cs`: streaming multi-label A* over immutable snapshots.
- `Code/core/pathfinding/AWTraversalCache.cs`: main-thread initial snapshot, copy-on-write dirty chunks, pinned generations, and consistency sweep.
- `Code/core/pathfinding/AWPathFinder.cs`: worker queue, one request per actor, reuse/cancel/poll/clear lifecycle.
- `Code/core/pathfinding/AWPathRecoveryManager.cs`: bounded retry state and backoff scheduling.
- `Code/core/pathfinding/AWPathDiagnostics.cs`: thread-safe counters and main-thread aggregate logging.
- `Code/core/pathfinding/PathfindingOwnershipRules.cs`: pure owner state machine and exact-owner decisions.
- `Code/core/pathfinding/PathfindingOwnershipService.cs`: Harmony owner arbitration and late-Cultiway yield transition.
- `Code/core/pathfinding/AWTransportRegistry.cs`: dock snapshots, ocean components, passenger requests, and boat claims.
- `Code/core/pathfinding/AWPathMovementBridge.cs`: main-thread request capture, step revalidation, movement, side effects, and smooth continuation.
- `Code/core/pathfinding/AWPathfindingBootstrap.cs`: initialization, frame pump, world clear, shutdown, and ownership transitions.
- `Code/patch/AW_GlobalPathfindingPatch.cs`: Harmony bridge for actor movement, boats, disposal, and world lifecycle.
- `Code/patch/AW_PathfindingSafetyPatch.cs`: make the old vanilla null-reference finalizer conditional on ownership.
- `Code/ModClass.cs`: initialize pathfinding arbitration before patch registration and bootstrap after patch registration.

### Task 1: Establish provenance, pure lifecycle types, and RED harness

**Files:**
- Create: `THIRD_PARTY_NOTICES.md`
- Create: `Code/core/pathfinding/AWPathfindingConfig.cs`
- Create: `Code/core/pathfinding/AWPathTypes.cs`
- Create temporarily: `F:/tmp/AW3PathfindingRuleTests/AW3PathfindingRuleTests.csproj`
- Create temporarily: `F:/tmp/AW3PathfindingRuleTests/Program.cs`

- [ ] **Step 1: Create the temporary harness with source links**

Create a net9 executable whose project links the pure path files as they are added:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>11</LangVersion>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="F:/WorldBox New Mod/AncientWarfare3.0/Code/core/pathfinding/AWPathfindingConfig.cs" Link="AWPathfindingConfig.cs" />
    <Compile Include="F:/WorldBox New Mod/AncientWarfare3.0/Code/core/pathfinding/AWPathTypes.cs" Link="AWPathTypes.cs" />
  </ItemGroup>
</Project>
```

`Program.cs` starts with an assertion helper and contract checks:

```csharp
using AncientWarfare3.core.pathfinding;

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

Check(AWPathfindingConfig.Default.ShortRangeTiles == 24, "short range");
Check(AWPathfindingConfig.Default.MaxNodesLongFallback == 60000, "fallback nodes");
Check(AWPathfindingConfig.WorkerCount(1) == 1, "single CPU worker");
Check(AWPathfindingConfig.WorkerCount(32) == 4, "worker cap");
Check(AWPathFailureRules.IsTerminal(AWPathFailureReason.Unreachable), "unreachable terminal");
Check(!AWPathFailureRules.IsTerminal(AWPathFailureReason.StepBlocked), "blocked recoverable");
Console.WriteLine("AW3 pathfinding rules passed");
```

- [ ] **Step 2: Run the harness and verify RED**

Run `dotnet run --project F:\tmp\AW3PathfindingRuleTests\AW3PathfindingRuleTests.csproj`.

Expected: compilation fails because `AWPathfindingConfig`, lifecycle enums, and failure rules do not exist.

- [ ] **Step 3: Add config and lifecycle value types**

Implement the Cultiway defaults in `AWPathfindingConfig` and define these public contracts in `AWPathTypes`:

```csharp
public enum AWPathRequestState { Pending, Streaming, Completed, Failed, Cancelled }
public enum AWPathPollKind { NoRequest, Waiting, StepReady, Completed, Failed, Cancelled }
public enum AWPathFailureReason
{
    None, InvalidActor, InvalidStart, InvalidTarget, CancelledByNewRequest, WorldCleared,
    StepBlocked, UnsafeStep, PortalUnavailable, TransportFailed, Timeout,
    GeneratorException, Unreachable, SearchLimitExceeded
}
public enum AWMovementMethod { Walk, Swim, Sail, Transport }

[Flags]
public enum AWHazardFlags
{
    None = 0, Block = 1, Lava = 2, Ocean = 4, Fire = 8, TerrainDamage = 16,
    StaminaDrain = 32, Drowning = 64, LowHealth = 128, Direct = 256, Transport = 512
}
```

Add immutable `AWTraversalEstimate`, `AWPathStep` (tile IDs only), `AWPathPollResult`, and `AWPathProcessResult`. `AWPathFailureRules.IsTerminal` returns true only for invalid input, `Unreachable`, `SearchLimitExceeded`, cancellation, and world clear.

- [ ] **Step 4: Verify GREEN and record MIT attribution**

Run the harness and expect `AW3 pathfinding rules passed`. Add the complete MIT grant from `Cultiway-Reborn-master/LICENSE`, copyright `Inmny 2025`, the exact source directories used, and a note that AW3 changed namespaces and concurrency boundaries. Put the same short derived-source header on every substantially ported path file.

- [ ] **Step 5: Commit the foundation**

```powershell
git add THIRD_PARTY_NOTICES.md Code/core/pathfinding/AWPathfindingConfig.cs Code/core/pathfinding/AWPathTypes.cs
git commit -m "feat: establish streaming path contracts"
```

Do not stage `F:/tmp`.

### Task 2: Implement exactly-once concurrent streams

**Files:**
- Create: `Code/core/pathfinding/AWPathStream.cs`
- Modify temporarily: `F:/tmp/AW3PathfindingRuleTests/AW3PathfindingRuleTests.csproj`
- Modify temporarily: `F:/tmp/AW3PathfindingRuleTests/Program.cs`

- [ ] **Step 1: Add failing stream tests**

Link the stream file and add tests proving that concurrent producers preserve each step once and only the first terminal transition wins:

```csharp
var stream = new AWPathStream();
Parallel.For(0, 256, i => stream.AddStep(new AWPathStep(i, AWMovementMethod.Walk)));
stream.Complete();
stream.Fail(AWPathFailureReason.GeneratorException, null);
var ids = new HashSet<int>();
while (stream.TryTake(out AWPathStep step)) Check(ids.Add(step.TileId), "duplicate stream step");
Check(ids.Count == 256 && stream.State == AWPathRequestState.Completed, "terminal stream state");
```

- [ ] **Step 2: Run and verify RED**

Expected: missing stream type.

- [ ] **Step 3: Implement the concurrent stream**

Use `ConcurrentQueue<AWPathStep>`, `Interlocked.CompareExchange` for terminal transition, one captured exception, and these methods:

```csharp
bool AddStep(AWPathStep pStep);
bool TryPeek(out AWPathStep pStep);
bool TryTake(out AWPathStep pStep);
void Complete();
void Cancel(AWPathFailureReason pReason);
void Fail(AWPathFailureReason pReason, Exception pError);
AWPathPollResult Poll();
```

No method blocks the main thread.

- [ ] **Step 4: Verify concurrency repeatedly and commit**

Run the harness 20 times in PowerShell and require every run to pass:

```powershell
1..20 | ForEach-Object { dotnet run --project F:\tmp\AW3PathfindingRuleTests\AW3PathfindingRuleTests.csproj --no-restore }
```

Then commit the production file with `git commit -m "feat: add cancellable path streams"`.

### Task 3: Build immutable traversal generations and pure cost rules

**Files:**
- Create: `Code/core/pathfinding/AWTraversalSnapshot.cs`
- Create: `Code/core/pathfinding/AWTraversalRules.cs`
- Create: `Code/core/pathfinding/AWTraversalCache.cs`
- Create: `Code/core/pathfinding/AWPathRequest.cs`
- Modify temporarily: `F:/tmp/AW3PathfindingRuleTests/AW3PathfindingRuleTests.csproj`
- Modify temporarily: `F:/tmp/AW3PathfindingRuleTests/Program.cs`

- [ ] **Step 1: Write failing pure traversal tests**

Add grid builders and assertions for diagonal distance, blocked/ocean/lava/fire eligibility, lethal health rejection, stamina depletion, and label dominance:

```csharp
var safe = AWTileTraversalSnapshot.TestTile(0, 0, 0, walkMultiplier: 1f);
var fire = AWTileTraversalSnapshot.TestTile(1, 1, 0, fire: true);
var normal = AWActorTraversalProfile.TestWalker(health: 10f, stamina: 10f);
Check(AWTraversalRules.CanEnter(safe, normal, AWPathRequestOptions.Default), "safe tile");
Check(AWTraversalRules.Estimate(safe, fire, normal, AWPathRequestOptions.Default).RiskCost > 0f,
    "fire risk");
Check(Math.Abs(AWTraversalRules.Distance(0, 0, 1, 1) - 1.41421356f) < .001f,
    "diagonal distance");
Check(AWTraversalRules.Dominates(timeA: 2, staminaA: 1, healthA: 0, riskA: 1,
    timeB: 3, staminaB: 2, healthB: 0, riskB: 2), "label dominance");
```

- [ ] **Step 2: Verify RED**

Expected: missing snapshot/profile/rule APIs.

- [ ] **Step 3: Implement pure snapshots and rules**

`AWTileTraversalSnapshot` stores ID, x/y, eight neighbor IDs, block/liquid/ocean/lava/fire/damage flags, walk multiplier, terrain damage, and ocean component. `AWActorTraversalProfile` stores all captured capabilities and health/stamina/speed values. `AWTraversalRules` ports Cultiway cost constants but removes cultivation/Xian floors.

- [ ] **Step 4: Implement immutable request identity**

`AWPathRequest` contains actor ID, start/target IDs, a value-type `AWPathRequestOptions`, immutable `AWActorTraversalProfile`, pinned `AWTraversalGeneration`, created timestamp, and cancellation token. `Matches` compares target plus all movement/region options; it does not compare start tile so a pending same-target request can continue. Add and pass:

```csharp
var request = AWPathRequest.CreateForTests(7, 10, 99, generation: 3);
Check(request.Matches(99, request.Options), "same request must reuse");
Check(!request.Matches(98, request.Options), "target change must replace");
```

- [ ] **Step 5: Implement runtime copy-on-write cache**

`AWTraversalCache.Initialize(WorldTile[])` runs only on the main thread, divides tiles into fixed 32x32 chunks, and publishes an `AWTraversalGeneration` containing an immutable chunk-reference array. `MarkDirty(WorldTile)` adds a chunk ID to a deduplicated queue. `ProcessDirty(int pChunkBudget)` clones only changed chunks and atomically publishes one complete generation. Requests call `Pin()`/`Dispose()` so old generations remain until no worker references them. A bounded cursor compares live tiles to cached flags to catch third-party edits.

- [ ] **Step 6: Add source-level thread-boundary assertions**

The temporary harness reads `AWTraversalRules.cs`, `AWTraversalSnapshot.cs`, and `AWPathRequest.cs` and rejects the tokens `World.world`, `WorldTile`, `Actor`, `City`, `Kingdom`, `Building`, `UnityEngine`, and `ModClass.Log` from the pure worker inputs.

- [ ] **Step 7: Verify and commit**

Run the harness, build `AncientWarfare3.csproj`, and commit with `git commit -m "feat: snapshot path traversal state"`.

### Task 4: Port streaming multi-label A* and corridor fallback

**Files:**
- Create: `Code/core/pathfinding/AWBinaryHeap.cs`
- Create: `Code/core/pathfinding/AWStreamingPathGenerator.cs`
- Modify temporarily: `F:/tmp/AW3PathfindingRuleTests/Program.cs`
- Reference: `F:/WorldBox New Mod/Cultiway-Reborn-master/Source/Core/Pathfinding/PortalAwarePathGenerator.cs`
- Reference: `F:/WorldBox New Mod/Cultiway-Reborn-master/Source/Utils/PriorityQueuePreview.cs`

- [ ] **Step 1: Add failing route tests**

Cover a straight route, wall detour, diagonal route, safe route preferred over lethal lava, four-label cap, cancellation, unreachable target, long-search node limit, and successful corridor fallback. Each test consumes the emitted stream and asserts adjacent tile IDs plus final target.

```csharp
AWPathTestGrid grid = AWPathTestGrid.FromRows(".....", ".###.", ".....");
AWPathStream route = Generate(grid, startX: 0, startY: 1, targetX: 4, targetY: 1);
Check(route.State == AWPathRequestState.Completed, "detour completes");
Check(route.Steps.AllAdjacent(grid), "detour adjacency");
Check(route.Steps[^1].TileId == grid.Id(4, 1), "detour target");
```

- [ ] **Step 2: Verify RED**

Expected: missing generator/heap/test-grid helper integration.

- [ ] **Step 3: Port the heap and search labels**

Port the Cultiway queue under the AW3 namespace. Generator-local labels contain tile ID, parent label, elapsed time, stamina cost, health cost, risk, f-score, and movement method. Keep at most four non-dominated labels per tile and return discarded labels to local pools.

- [ ] **Step 4: Implement search tiers and streaming emission**

Use Cultiway limits: direct/raycast for short safe lines, 3,000 nodes under 24 tiles, 12,000 nodes under 96 tiles or long routes, then a 60,000-node corridor fallback. `SearchLimitExceeded` is terminal only after fallback exhaustion. Reconstruct tile IDs, emit forward steps incrementally, check cancellation every 64 expansions and every emission, and call exactly one terminal stream method.

- [ ] **Step 5: Enforce the worker thread boundary**

Read `AWStreamingPathGenerator.cs` from the harness and reject `World.world`, `WorldTile`, `Actor`, `City`, `Kingdom`, `Building`, `UnityEngine`, and `ModClass.Log`.

- [ ] **Step 6: Verify deterministic correctness and allocations**

Run each pure route test under ten fixed seeds. Add a 256x256 obstacle-grid test that records `GC.GetAllocatedBytesForCurrentThread()` after warmup and rejects unbounded per-node object allocation; pooled labels and heap arrays may grow during warmup but the second same-size search must allocate less than 10 percent of the first.

- [ ] **Step 7: Commit**

Commit heap/generator/rule changes with `git commit -m "feat: port streaming multi-label path search"`.

### Task 5: Add worker management, reuse, cancellation, diagnostics, and recovery

**Files:**
- Create: `Code/core/pathfinding/AWPathFinder.cs`
- Create: `Code/core/pathfinding/AWPathRecoveryManager.cs`
- Create: `Code/core/pathfinding/AWPathDiagnostics.cs`
- Modify temporarily: `F:/tmp/AW3PathfindingRuleTests/Program.cs`

- [ ] **Step 1: Write failing lifecycle tests**

Use a controllable fake generator to prove one active request per actor, same-request reuse, changed-target cancellation, stale completion rejection, actor/world clear, bounded queue shutdown, and retry counts of 4/2/1/0 for step/transport/generator/terminal failures.

- [ ] **Step 2: Verify RED**

Expected: missing pathfinder, recovery, and diagnostics APIs.

- [ ] **Step 3: Implement workers and actor-keyed tasks**

`AWPathFinder` owns a `BlockingCollection<AWPathfindingTask>`, `ConcurrentDictionary<long, AWPathfindingTask>`, 1-4 long-running worker threads, and a world-generation token. Public main-thread methods are:

```csharp
bool Request(AWPathRequest pRequest, out bool pReused);
AWPathPollResult Poll(long pActorId);
bool Consume(long pActorId);
void Cancel(long pActorId, AWPathFailureReason pReason);
void Clear(AWPathFailureReason pReason);
void Start(int pWorkers);
void StopAndDrain();
```

Worker completion removes only the exact task instance still registered for that actor.

- [ ] **Step 4: Implement Cultiway-compatible recovery**

Store target/options, attempts, and next retry time per actor. Retry `StepBlocked`/`UnsafeStep` four times, transport/portal/timeout twice, generator exceptions once, and terminal failures zero times. Backoff is `0.1, 0.25, 0.5, 1.0` seconds capped at one second. Progress clears attempts. Exhaustion returns false so the bridge cancels current AI behavior.

- [ ] **Step 5: Implement diagnostics without worker logging**

Use `Interlocked` counters and a `ConcurrentQueue<AWPathDiagnosticEvent>`. Main-thread `DrainAndMaybeLog` reports aggregates only when diagnostics are enabled or first-step latency exceeds 100 ms, queue depth exceeds 2,000, or a worker exception occurs. No worker calls Unity/NML logging.

- [ ] **Step 6: Stress and commit**

Submit 10,000 mixed reuse/cancel requests from parallel producers, clear mid-run, and require completion within 10 seconds with no live tasks. Commit with `git commit -m "feat: manage concurrent path requests"`.

### Task 6: Implement ownership arbitration and bootstrap lifecycle

**Files:**
- Create: `Code/core/pathfinding/PathfindingOwnershipRules.cs`
- Create: `Code/core/pathfinding/PathfindingOwnershipService.cs`
- Create: `Code/core/pathfinding/AWPathfindingBootstrap.cs`
- Modify: `Code/ModClass.cs`
- Modify: `Code/patch/AW_PathfindingSafetyPatch.cs`
- Modify temporarily: `F:/tmp/AW3PathfindingRuleTests/Program.cs`

- [ ] **Step 1: Write failing pure owner-transition tests**

Model owner lists and assert: pending prefixes yield; no Cultiway selects AW3; `inmny.cultiway` selects Cultiway; unrelated owner text containing `culti` does not; two stable ticks are required after matching assembly load; late Cultiway transitions AW3 through draining to yielded; removing Cultiway does not re-enable AW3 until world reset.

- [ ] **Step 2: Verify RED**

Expected: missing ownership state machine.

- [ ] **Step 3: Implement the pure state machine plus Harmony adapter**

Define `AWPathOwnerState.Pending`, `Aw3`, `Suspending`, and `Cultiway` in the pure rules file. The runtime service inspects patch owners on `Actor.goTo`, `Actor.updatePathMovement`, `Actor.isUsingPath`, and `Actor.updateMovement`; exact owner `inmny.cultiway` wins. Subscribe to `AppDomain.CurrentDomain.AssemblyLoad`; assembly simple name `Cultiway` moves state to pending/suspending for two main-thread ticks. A low-frequency audit detects dynamic patches from an already-loaded assembly.

- [ ] **Step 4: Bootstrap before and after Harmony registration**

In `ModClass.OnModLoad`, call `AWPathfindingBootstrap.PrepareOwnership()` before `PatchHarmonyByClass()` and `AWPathfindingBootstrap.AfterPatchesRegistered()` immediately after. Prefixes call `ShouldIntercept`; only state `Aw3` returns true. On yield transition, release actor wait state, cancel tasks, stop workers, clear transport, then log the selected owner once.

- [ ] **Step 5: Make the legacy finalizer conditional**

`AW_PathfindingSafetyPatch` returns the original exception unchanged when AW3 owns intercepted actor movement. It retains existing behavior for vanilla callers while AW3 is pending/yielded.

- [ ] **Step 6: Verify Harmony targets and commit**

Add source/reflection tests for all four actor methods and exact signatures from the publicized assembly. Build and commit with `git commit -m "feat: arbitrate global path ownership"`.

### Task 7: Bridge global actor movement and preserve vanilla side effects

**Files:**
- Create: `Code/core/pathfinding/AWPathMovementBridge.cs`
- Create: `Code/patch/AW_GlobalPathfindingPatch.cs`
- Modify temporarily: `F:/tmp/AW3PathfindingRuleTests/Program.cs`
- Reference: `F:/WorldBox New Mod/Cultiway-Reborn-master/Source/Patch/PatchAboutPathfinding.cs`
- Reference: `F:/WorldBox New Mod/AssetRipper_export_20260628_163320/ExportedProject/Assets/Scripts/Assembly-CSharp/Actor.cs`

- [ ] **Step 1: Add failing source-integration tests**

Require Harmony patches for `Actor.goTo`, `updatePathMovement`, `isUsingPath`, private `updateMovement(float,float)`, `Dispose`, and `MapBox.clearWorld`. Require each prefix to call `PathfindingOwnershipService.ShouldIntercept` before touching actor state. Reject direct worker references to live game types.

- [ ] **Step 2: Verify RED**

Expected: missing movement bridge and patch targets.

- [ ] **Step 3: Capture requests on the main thread**

`Submit(Actor, WorldTile, bool, bool, bool, int)` validates actor/tiles, handles same-tile movement, captures `AWActorTraversalProfile`, pins a cache generation, sets `tile_target`, parks `next_step_position` at the current tile, sets not-moving state, and requests/reuses the path. It returns `ExecuteEvent.True` for accepted requests and false only for invalid input or ownership suspension.

- [ ] **Step 4: Consume and revalidate steps**

Before moving, resolve `World.world.tiles_list[step.TileId]`, verify adjacency unless `Direct`/transport, re-run live boat/block/lava/ocean/fire checks, and preserve vanilla `damaged_when_walked`, tile `step_action`, flora law, callback, and fireman behavior. Unsafe live state aborts the stream and enters recovery; no worker mutates actors.

- [ ] **Step 5: Port smooth multi-boundary movement and calibration throttle**

Port Cultiway's 256-boundary loop and exact cardinal/diagonal walked distances. Continue a ready custom stream without recursive calls, stop when waiting, and retain vanilla local/curved paths when present. Throttle same actor/action/target calibration restarts for 0.25 seconds so moving combat targets do not cancel every frame.

- [ ] **Step 6: Wire lifecycle cleanup**

Actor dispose/death cancels its task; `MapBox.clearWorld` cancels with `WorldCleared`, clears cache/recovery/calibration, and drains workers; a new world rebuilds cache before ownership can switch to active.

- [ ] **Step 7: Build and commit**

Run the harness and both net48 builds, then commit with `git commit -m "feat: replace actor movement with streaming paths"`.

### Task 8: Bind transport legs to vanilla docks and boats

**Files:**
- Create: `Code/core/pathfinding/AWTransportRegistry.cs`
- Modify: `Code/core/pathfinding/AWStreamingPathGenerator.cs`
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`
- Modify: `Code/patch/AW_GlobalPathfindingPatch.cs`
- Modify temporarily: `F:/tmp/AW3PathfindingRuleTests/Program.cs`
- Reference: Cultiway `PortalManager.cs`, `PortalRequest.cs`, `PortalSnapshot.cs`, and boat patches in `PatchAboutPathfinding.cs`

- [ ] **Step 1: Write failing transport graph tests**

Build two land components and one ocean component with docks. Assert route shape `Walk -> Transport -> Walk`, same-route passenger grouping, single boat claim, exactly-once passenger step consumption, dead-boat release, destroyed-origin repair, destroyed-final-dock failure, and no teleport/vanilla fallback.

- [ ] **Step 2: Verify RED**

Expected: no transport registry or generator transport candidates.

- [ ] **Step 3: Snapshot vanilla docks and ocean connectivity**

Main-thread registry stores building/city IDs, land/ocean tile IDs, ocean component, graph generation, waiting cost, and sailing estimate. Rebuild affected components on dock state changes and bounded water dirty queues. The worker receives only immutable transport nodes and IDs.

- [ ] **Step 4: Add transport candidates to A***

For land actors whose target is on another reachable land component, evaluate the two cheapest compatible origin/destination dock pairs, emit the origin walk, one `AWMovementMethod.Transport` step carrying request/node IDs, and the destination walk. Boats themselves use `Sail` traversal and never request passenger transport.

- [ ] **Step 5: Patch vanilla boat tasks**

Patch `BehBoatFindRequest.execute`, pickup target, loading, unload target, unloading, and destroy-event cleanup. AW3 handles only boats claimed by `AWTransportRegistry`; unclaimed boats continue vanilla. Loading marks passengers inside the real `Boat`, consumes the transport step once, and unloading cancels stale land streams and requests a fresh path to the original target.

- [ ] **Step 6: Verify and commit**

Run transport tests and net48 build. Commit with `git commit -m "feat: route streamed paths through vanilla boats"`.

### Task 9: Add dirty hooks, frame pumping, and bounded diagnostics

**Files:**
- Modify: `Code/core/pathfinding/AWTraversalCache.cs`
- Modify: `Code/core/pathfinding/AWPathfindingBootstrap.cs`
- Modify: `Code/patch/AW_GlobalPathfindingPatch.cs`
- Modify: `Code/patch/AW_DeferredRuntimeWorkPatch.cs`
- Modify temporarily: `F:/tmp/AW3PathfindingRuleTests/Program.cs`

- [ ] **Step 1: Write failing lifecycle/source tests**

Require dirty notifications for tile type/fire/building state changes, initial world cache construction, bounded per-frame chunk rebuild, once-per-frame diagnostic drain, and world-token checks before applying a completed step.

- [ ] **Step 2: Verify RED**

Expected: lifecycle calls are absent.

- [ ] **Step 3: Wire safe dirty notifications**

Patch `WorldTile.setTileTypes(TileType,TopTileType,bool)`, `WorldTile.setTileType(TileType,bool)`, `WorldTile.setTopTileType(TopTileType,bool)`, `WorldTile.startFire(bool)`, `WorldTile.stopFire()`, and `Building.setState(BuildingState)`. Each patch only enqueues the affected tile/chunk; it never rebuilds inline. Building set/destroy updates transport and traversal. Fire/type changes update traversal. The consistency sweep covers unpatched third-party edits.

- [ ] **Step 4: Pump bounded work**

Call `AWPathfindingBootstrap.ProcessFrame()` from `AW_DeferredRuntimeWorkPatch`: finalize ownership, process at most two dirty chunks, advance recovery timers, process transport claims, drain diagnostic events, and perform a small consistency-sweep slice. Skip all work while Cultiway owns movement.

- [ ] **Step 5: Verify no per-frame log or scan**

Source assertions reject logging inside worker/generator/step loops and reject full `tiles_list` iteration from `ProcessFrame`. Benchmark 10,000 no-op frame pumps in the pure adapter and require stable memory.

- [ ] **Step 6: Commit**

Commit with `git commit -m "perf: bound pathfinding maintenance work"`.

### Task 10: Full pathfinding verification and checkpoint

**Files:**
- Verify: all pathfinding production files
- Inspect: `F:/WorldBox New Mod/Cultiway-Reborn-master/LICENSE`
- Inspect live log: `C:/Users/24908/AppData/LocalLow/mkarpenko/WorldBox/Player.log`

- [ ] **Step 1: Run the complete temporary suite**

```powershell
dotnet run --project F:\tmp\AW3PathfindingRuleTests\AW3PathfindingRuleTests.csproj
```

Expected: `AW3 pathfinding rules passed`, including concurrency, fallback, transport, ownership, and source-boundary checks.

- [ ] **Step 2: Run both production builds**

```powershell
dotnet build AncientWarfare3.csproj -c Debug --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: zero warnings and zero errors.

- [ ] **Step 3: Audit forbidden dependencies and attribution**

Search production path files for `Friflo`, cultivation, Xian, teleport array, train, sect, skill, or Cultiway UI references; expect none. Confirm every derived file header and `THIRD_PARTY_NOTICES.md` contains Inmny 2025 MIT attribution.

- [ ] **Step 4: Run live movement acceptance**

Deploy only after confirming the live checkout has no unexpected user files. In a fresh world observe civilians, rulers, soldiers, animals, flyers, water creatures, boats, combat recalibration, blocked targets, dock boarding/unloading, world clear, and repeated reload. Then load AW3 with Cultiway and confirm the log selects `inmny.cultiway`, AW3 starts zero workers, and actors move once rather than being double-consumed.

- [ ] **Step 5: Inspect diagnostics and commit any acceptance-only corrections**

`Player.log` must contain no Harmony failure, worker exception, stale-world access, invalid tile, repeated per-frame message, or vanilla fallback. Corrections receive focused RED/GREEN tests and a separate commit before starting the historical-school plan.

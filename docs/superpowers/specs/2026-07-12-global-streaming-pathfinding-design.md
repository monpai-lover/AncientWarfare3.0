# Global Streaming Pathfinding Design

## Goal

Replace WorldBox's actor path generation inside AncientWarfare3 with an independent AW3 port of Cultiway-Reborn's multithreaded streaming pathfinding system. The replacement covers every actor that calls `Actor.goTo`, including civilians, rulers, armies, animals, boats, historical school masters, and travelling disciples.

This pathfinding foundation and the historical-school feature form one delivery. Pathfinding is implemented first because school travel depends on it, but neither feature is deferred to a later release.

## Provenance And License

- The source design is taken from `F:/WorldBox New Mod/Cultiway-Reborn-master/Source/Core/Pathfinding` and `Source/Patch/PatchAboutPathfinding.cs` with the author's permission.
- Cultiway's `/Source` tree is MIT licensed. Every copied or substantially derived file must retain an attribution header.
- AW3 must include the Cultiway MIT notice in its distributed license notices.
- The port lives in the AW3 namespace and does not require Cultiway to be installed.

## Scope

The port includes:

- multithreaded request workers;
- streaming path steps;
- multi-label A* with time, stamina, health, and hazard costs;
- short-range, long-range, node-limit, and fallback-corridor searches;
- request reuse and cancellation;
- main-thread path consumption and movement;
- bounded recovery with exponential backoff;
- world and actor lifecycle cleanup;
- vanilla dock and boat transport legs;
- diagnostics, counters, and failure logging;
- safe ownership arbitration when Cultiway is also loaded.

The port excludes:

- cultivation power levels and Xian hazard resistance;
- Friflo ECS systems;
- Cultiway teleport arrays, trains, skills, buildings, and UI;
- Cultiway-specific logging and hotfix attributes;
- any fallback that teleports an ordinary actor or silently switches back to vanilla pathfinding.

## Runtime Ownership

AW3 owns a `PathfindingOwnershipService` that inspects Harmony patch owners before enabling the global replacement.

- If the Cultiway Harmony owner already patches the actor movement chain, AW3 does not start workers, register transport requests, or intercept movement. All actors continue through Cultiway's implementation.
- If Cultiway is absent, AW3 enables its embedded port.
- The initial decision is made at the post-mod-load main-thread checkpoint, before AW3 starts workers, and is repeated on every world load.
- Loading a Cultiway assembly invalidates the cached decision immediately. AW3 enters a temporary yield state while the movement-method owner set stabilizes across two main-thread ticks. If Cultiway's owner appears, AW3 atomically enters a permanent yield state for that world: it releases actor waiting states, cancels and drains its requests, stops its workers, and ceases interception before either pathfinder consumes another request. A low-frequency audit covers the exceptional case where an already loaded assembly adds patches dynamically.
- AW3 does not hot-enable itself after a detected Cultiway owner disappears; ownership can be reconsidered only on the next world load. This avoids changing movement semantics halfway through active AI behavior.
- Detection uses the actual Cultiway Harmony owner and declaring assembly/type identity found during implementation, not a broad substring match that could disable AW3 for an unrelated patch.
- AW3 never unpatches another mod's Harmony patches.
- Every AW3 prefix checks the cached ownership state defensively. Until the initial decision is finalized, every prefix yields.
- The selected owner and every ownership transition are logged once.

## Component Boundaries

### Request Snapshot

`AWPathRequest` captures all mutable actor inputs on the main thread:

- actor ID and start/target tile IDs;
- water, block, lava, and region-limit options;
- boat, water-creature, flying, fire immunity, and block behavior;
- current/max stamina and health;
- movement speed, water damage, and stamina regeneration.
- immutable copies of region, tile-set, or search-bound restrictions referenced by the request.

Worker threads never read the live `Actor`, `WorldTile`, `City`, `Kingdom`, `Building`, or Unity object after request creation.

### Traversal Snapshot

`AWTileTraversalCache` stores immutable per-tile traversal data:

- coordinates and tile ID;
- block, liquid, ocean, lava, and damaging-terrain flags;
- walk multiplier and estimated terrain damage;
- current fire flag;
- eight neighboring tile IDs.

The main thread creates an initial full snapshot, then rebuilds only dirty map chunks. Generations use copy-on-write chunk references rather than copying every world tile for each edit. Each request pins a complete immutable cache generation until it finishes, so workers never observe a mixture of old and new chunks. A completed step is revalidated on the main thread before movement, so a newly built wall, fire, destroyed bridge, or changed tile cannot be crossed from a stale snapshot. Missed third-party dirty notifications can reduce route quality only until the next bounded consistency sweep; they cannot authorize an invalid movement step.

### Generator

`AWStreamingPathGenerator` ports Cultiway's `PortalAwarePathGenerator` search behavior:

- a binary-heap open queue;
- multiple non-dominated labels per tile;
- stamina, health, accumulated risk, elapsed time, and heuristic cost;
- direct long-range search first;
- transport-route estimates where eligible;
- a larger corridor-bounded fallback after a long search reaches its node limit;
- cancellation checks throughout expansion and emission.

The initial configuration matches Cultiway:

- 24 short-range tiles;
- 96 long-range tiles;
- 3,000 short-search nodes;
- 12,000 long-search nodes;
- 60,000 fallback nodes;
- four labels per tile;
- one to four background workers, calculated as `clamp(CPU count - 1, 1, 4)`.

Configuration remains internal for the first release. Runtime diagnostics expose observed queue depth and latency before any user-facing tuning controls are added.

### Request Manager

`AWPathFinder` owns one active task per actor ID.

- A request with the same target and options reuses pending or streaming work.
- A different request cancels and disposes the old task before enqueueing the new one.
- Steps are emitted through a concurrent stream and consumed without waiting for the full route.
- Cancellation tokens stop obsolete searches.
- Actor disposal, death, world clear, and mod shutdown clean active and queued tasks.
- Worker exceptions are placed in a thread-safe diagnostic queue; Unity logging occurs on the main thread.

### Movement Bridge

Harmony integration ports Cultiway's actor movement chain:

- `Actor.goTo` submits or reuses a request and puts the actor into a waiting state;
- `Actor.updatePathMovement` consumes ready steps;
- `Actor.isUsingPath` reports pending or streaming requests;
- smooth movement can consume multiple completed tile boundaries in a frame without losing path state;
- every step preserves relevant vanilla tile and flora side effects;
- boats retain vanilla movement semantics;
- a stale or unsafe step aborts the current stream and enters recovery.

The bridge must not mutate an actor from a worker thread.

## Failure Semantics

Failure behavior follows Cultiway rather than falling back to vanilla:

- `StepBlocked`, `UnsafeStep`, `PortalUnavailable`, `TransportFailed`, `Timeout`, and `GeneratorException` are recoverable;
- retries use bounded exponential delays;
- portal/transport/timeout failures retry at most twice;
- generator exceptions retry once;
- other recoverable step failures retry at most four times;
- `SearchLimitExceeded` is reported only after the internal corridor fallback also exhausts its limit; it and `Unreachable` then end the request without retrying through vanilla;
- exhausted recovery cancels the actor's current AI behavior;
- no ordinary actor is teleported to satisfy a failed path.

Failure logs include actor ID/type, start, target, request options, cache generation, search count, and final reason.

## Vanilla Dock Transport

AW3 retains Cultiway's transport-leg concept but binds it only to vanilla docks and boats.

`AWTransportRegistry` maintains a main-thread snapshot of living docks:

- dock building and city IDs;
- land-side and ocean-side boarding tiles;
- connected ocean component;
- estimated waiting and sailing cost;
- current graph generation.

The route generator can produce:

```text
walk to dock -> wait/board -> sail -> unload -> walk to target
```

Transport behavior requirements:

- passengers with compatible routes may share one request;
- a boat claims one transport request at a time;
- loading consumes the passenger's transport step exactly once;
- unloading revalidates the remaining land route;
- destroyed docks are removed and requests are repaired toward the next valid stop where possible;
- dead boats release their request for another driver;
- missing ocean connectivity cancels the transport leg with `TransportFailed`;
- world clear removes all dock snapshots and requests.

Historical school masters add one higher-level exception: when no physical sea route remains available for a long time, the school travel state may use its separately specified timed-voyage fallback. This does not alter global pathfinding for other actors.

## Existing AW3 Path Safety

`AW_PathfindingSafetyPatch` currently converts a vanilla `RegionPathFinder.getGlobalPath` null reference into `NotFound`. When AW3 owns the new pathfinder, that patch is disabled because the vanilla global path should no longer be called by intercepted actors. When AW3 yields ownership to Cultiway, the patch remains dormant unless vanilla code outside the replaced actor chain invokes it.

## Performance And Diagnostics

Required counters:

- pending and active request count;
- worker utilization;
- generated, reused, cancelled, completed, and failed requests;
- mean and maximum first-step latency;
- expanded nodes and fallback searches;
- stale-step rejections;
- recovery attempts by reason;
- active transport requests and waiting passengers;
- traversal-cache rebuild time and dirty chunk count.

No per-frame log is allowed. Aggregates are emitted only when diagnostics are enabled or a threshold is exceeded.

## Validation

Pure rule tests cover:

- obstacle detours;
- diagonal routes;
- water/lava/fire risk ordering;
- stamina and lethal-health dominance;
- node limits and fallback corridors;
- cancellation and same-request reuse;
- stream completion and failure states;
- bounded recovery counts;
- dock route selection and destroyed-dock repair;
- ownership arbitration with and without a Cultiway patch owner.
- a late Cultiway owner transition that drains AW3 work and releases waiting actors without double consumption.

Integration tests verify all Harmony targets exist and that no Cultiway ECS, cultivation, teleport, or train type remains referenced.

Live acceptance requires:

- civilians, armies, animals, rulers, and boats move normally;
- combat target recalibration does not continuously restart paths;
- mass movement does not create a main-thread pathfinding spike;
- actors stop cleanly after unreachable targets;
- world clear and repeated world loads leave no old tasks;
- dock passengers board, cross, unload, and continue their routes;
- loading Cultiway and AW3 together starts only one pathfinding owner;
- Player.log contains no Harmony failures, worker exceptions, invalid casts, or stale-world accesses.

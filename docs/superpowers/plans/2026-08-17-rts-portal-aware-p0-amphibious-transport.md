# RTS Portal-Aware P0 Amphibious Transport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve dock entry/exit metadata through path generation and execute the complete RTS embark, sail, and landing voyage from military P0.

**Architecture:** Port Cultiway's reusable transport-portal boundaries into the existing AW3 dock stack: revisioned endpoint snapshots, true ocean connectivity, route steps retaining entry/exit endpoints, and one army-level voyage request. `ArmyRtsTransportService` owns the physical state machine and directly drives captain assembly, temporary boat movement, roster embarkation, and roster landing without waiting for vanilla behavior action indices.

**Tech Stack:** C# 10, .NET Framework 4.8, Harmony, WorldBox actor/boat APIs, AW3 streaming pathfinder, .NET 9 rules test runner.

---

## File Structure

- Modify `Code/core/pathfinding/AWDockRouteModels.cs`: add concrete land/sea endpoint data and immutable portal route metadata.
- Modify `Code/core/pathfinding/AWDockRouteRegistry.cs`: publish revisioned endpoint snapshots.
- Modify `Code/core/pathfinding/AWDockTransportRules.cs`: pure route validity and selection rules.
- Modify `Code/core/pathfinding/AWDockTransportService.cs`: rebuild real ocean components and select connected dock portals.
- Modify `Code/patch/AW_DockPathTransportPatch.cs`: invalidate the portal graph when docks change.
- Modify `Code/core/pathfinding/AWPathRequest.cs`: carry the selected physical route into worker generation.
- Modify `Code/core/pathfinding/AWPathTypes.cs`: retain entry/exit portal metadata in transport steps.
- Modify `Code/core/pathfinding/AWStreamingPathGenerator.cs`: emit a portal-aware transport step.
- Modify `Code/core/pathfinding/AWPathMovementBridge.cs`: consume the retained route instead of resolving a final-target-only taxi request.
- Modify `Code/core/lineage/ArmyRtsTransportRules.cs`: define P0 voyage stages and transition rules.
- Modify `Code/core/lineage/ArmyRtsTransportProductionService.cs`: spawn a temporary transport at the selected pickup sea tile without requiring a passenger request.
- Modify `Code/core/lineage/ArmyRtsTransportService.cs`: replace per-member taxi progression with the army-level P0 state machine.
- Modify `Code/core/performance/AWCooperativeActorPostRunner.cs`: let the transport service drive captain assembly before suppressing the roster.
- Modify `Tests/AncientWarfare3.Rules.Tests/AWDockRouteRegistryTests.cs.txt`: cover endpoint metadata and route validity.
- Modify `Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportP0RulesTests.cs.txt`: cover P0 ownership and reject vanilla-task dependencies.

The current worktree already contains related RTS transport edits in several listed files. Preserve them unless a failing test proves that the new state machine supersedes them.

### Task 1: Build Correct Dock Portal Routes

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/AWDockRouteRegistryTests.cs.txt`
- Modify: `Code/core/pathfinding/AWDockRouteModels.cs`
- Modify: `Code/core/pathfinding/AWDockRouteRegistry.cs`
- Modify: `Code/core/pathfinding/AWDockTransportRules.cs`
- Modify: `Code/core/pathfinding/AWDockTransportService.cs`
- Modify: `Code/patch/AW_DockPathTransportPatch.cs`

- [ ] **Step 1: Write failing endpoint and route tests**

Use separate land and sea tiles:

```csharp
var entry = new AWDockEndpoint(7L, pLandTileId: 12,
    pOceanTileId: 13, pWaterComponent: 101);
var exit = new AWDockEndpoint(9L, pLandTileId: 42,
    pOceanTileId: 43, pWaterComponent: 101);
var route = new AWDockRouteCandidate(AWTransportRouteSource.DockPortal,
    entry, exit, 80f);

Assert(route.IsValid, "connected dock portals form a physical route");
Assert(route.Entry.LandTileId == 12 && route.Entry.OceanTileId == 13,
    "the source portal retains assembly and pickup tiles");
Assert(route.Exit.LandTileId == 42 && route.Exit.OceanTileId == 43,
    "the destination portal retains arrival and landing tiles");
Assert(!new AWDockRouteCandidate(AWTransportRouteSource.DockPortal, entry,
        new AWDockEndpoint(10L, 52, 53, 202), 80f).IsValid,
    "different ocean components cannot form a transport edge");
var fallback = new AWDockRouteCandidate(
    AWTransportRouteSource.ShoreFallback,
    new AWDockEndpoint(0L, 62, 63, 303),
    new AWDockEndpoint(0L, 72, 73, 303), 96f);
Assert(fallback.IsValid,
    "stable shoreline endpoints form a virtual transport portal pair");
```

Add a source assertion that `AWDockTransportService.cs` does not assign `WaterComponent` from `ocean.region.island.id`.

- [ ] **Step 2: Run the complete rules suite and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false -p:UseAppHost=false
```

Expected: compilation fails because `AWDockEndpoint` lacks separate land and ocean tile IDs.

- [ ] **Step 3: Implement endpoint snapshots and real water connectivity**

Use this endpoint contract:

```csharp
internal readonly struct AWDockEndpoint
{
    internal AWDockEndpoint(long pId, int pLandTileId,
        int pOceanTileId, int pWaterComponent)
    {
        Id = pId;
        LandTileId = pLandTileId;
        OceanTileId = pOceanTileId;
        WaterComponent = pWaterComponent;
    }

    internal long Id { get; }
    internal int LandTileId { get; }
    internal int OceanTileId { get; }
    internal int WaterComponent { get; }
    internal bool IsValid => Id > 0 && LandTileId >= 0 &&
        OceanTileId >= 0 && WaterComponent >= 0;
}
```

`AWDockTransportService` marks the graph dirty on dock create, recalculate, and dispose. On the next lookup it scans live docks, traverses adjacent ocean `MapRegion` objects with a queue, assigns one component ID per connected water body, and registers one endpoint per dock/component pair. Route selection requires source land to entry land reachability, destination land to exit land reachability, and matching water components.

Add `AWTransportRouteSource.DockPortal` and
`AWTransportRouteSource.ShoreFallback`. Dock routes require positive portal
IDs. Shoreline fallback routes use virtual ID `0` but still require stable land
tiles, adjacent boat-safe sea tiles, and a shared water component.

If no connected dock pair exists, `TryResolveRoute` performs one bounded
shoreline search around the source and destination land components and returns
the lowest-cost valid virtual pair. This search runs only when a route starts
or is invalidated, never per frame.

- [ ] **Step 4: Re-run the suite and verify GREEN**

Expected: `Rule tests passed.`

### Task 2: Preserve Portal Metadata Through Streaming Paths

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/AWDockRouteRegistryTests.cs.txt`
- Modify: `Code/core/pathfinding/AWPathRequest.cs`
- Modify: `Code/core/pathfinding/AWPathTypes.cs`
- Modify: `Code/core/pathfinding/AWStreamingPathGenerator.cs`
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`

- [ ] **Step 1: Write failing route-retention tests and source guards**

Require a transport step to expose these scalar fields:

```csharp
var step = new AWPathStep(99, AWMovementMethod.Transport,
    pEntryPortalId: 7L, pExitPortalId: 9L,
    pEntryLandTileId: 12, pPickupSeaTileId: 13,
    pDestinationSeaTileId: 43, pLandingLandTileId: 42);

Assert(step.EntryPortalId == 7L && step.ExitPortalId == 9L,
    "the transport step retains the selected portal pair");
Assert(step.PickupSeaTileId == 13 && step.DestinationSeaTileId == 43,
    "the transport step retains both boat targets");
```

Reject `TransportResult(target.Id)` and require `TransportResult(pRequest)`.

- [ ] **Step 2: Run the pathfinding slice and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false -p:UseAppHost=false -- --pathfinding-mode-slice
```

Expected: compilation or source guards fail because transport steps contain only the final target.

- [ ] **Step 3: Carry the immutable route through request and result**

Add `AWDockRouteCandidate PhysicalTransportRoute` to `AWPathRequest`. Resolve it once in `AWPathMovementBridge.SubmitCore`. Add scalar endpoint fields to `AWPathStep`; do not expose `Building`, `WorldTile`, or `MapRegion` references to worker threads.

Emit:

```csharp
private static AWPathGenerationResult TransportResult(AWPathRequest pRequest)
{
    AWDockRouteCandidate route = pRequest.PhysicalTransportRoute;
    var estimate = new AWTraversalEstimate(0f, 0f, 0f, 0f,
        AWHazardFlags.Transport);
    return AWPathGenerationResult.Success(pRequest.TargetTileId, true,
        new[] { new AWPathStep(pRequest.TargetTileId,
            AWMovementMethod.Transport, estimate,
            pEntryPortalId: route.Entry.Id,
            pExitPortalId: route.Exit.Id,
            pEntryLandTileId: route.Entry.LandTileId,
            pPickupSeaTileId: route.Entry.OceanTileId,
            pDestinationSeaTileId: route.Exit.OceanTileId,
            pLandingLandTileId: route.Exit.LandTileId) });
}
```

`StartTransport` validates retained endpoint IDs rather than performing a second unrelated route lookup.

- [ ] **Step 4: Re-run pathfinding and complete suites**

Run the pathfinding slice, then the complete rules suite. Expected: both exit successfully.

### Task 3: Define The Army-Level P0 State Machine

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportP0RulesTests.cs.txt`
- Modify: `Code/core/lineage/ArmyRtsTransportRules.cs`

- [ ] **Step 1: Write failing transition tests**

```csharp
Assert(ArmyRtsTransportRules.ResolveP0Stage(true, false, false,
        false, false, false) == ArmyRtsTransportP0Stage.AssembleAtEntry,
    "a selected route first assembles the captain");
Assert(ArmyRtsTransportRules.ResolveP0Stage(true, true, false,
        false, false, false) == ArmyRtsTransportP0Stage.BoatToPickup,
    "the boat must reach the selected pickup sea tile");
Assert(ArmyRtsTransportRules.ResolveP0Stage(true, true, true,
        false, false, false) == ArmyRtsTransportP0Stage.Boarding,
    "P0 boards the manifest when captain and boat are ready");
Assert(ArmyRtsTransportRules.ResolveP0Stage(true, true, true,
        true, false, false) == ArmyRtsTransportP0Stage.Sailing,
    "a fully embarked roster begins sailing");
Assert(ArmyRtsTransportRules.ResolveP0Stage(true, true, true,
        true, true, false) == ArmyRtsTransportP0Stage.Landing,
    "arrival begins landing");
Assert(ArmyRtsTransportRules.ResolveP0Stage(true, true, true,
        true, true, true) == ArmyRtsTransportP0Stage.Complete,
    "all landed members complete the voyage");
```

- [ ] **Step 2: Run `--rts-transport-p0` and verify RED**

Expected: compilation fails because the new stage enum and resolver do not exist.

- [ ] **Step 3: Implement pure stage rules**

```csharp
public enum ArmyRtsTransportP0Stage
{
    RoutePending,
    AssembleAtEntry,
    BoatToPickup,
    Boarding,
    Sailing,
    Landing,
    Complete,
    Failed
}

public static ArmyRtsTransportP0Stage ResolveP0Stage(bool routeValid,
    bool captainAtEntry, bool boatAtPickup, bool allEmbarked,
    bool boatAtDestination, bool allLanded)
{
    if (!routeValid) return ArmyRtsTransportP0Stage.RoutePending;
    if (allLanded) return ArmyRtsTransportP0Stage.Complete;
    if (!captainAtEntry) return ArmyRtsTransportP0Stage.AssembleAtEntry;
    if (!boatAtPickup) return ArmyRtsTransportP0Stage.BoatToPickup;
    if (!allEmbarked) return ArmyRtsTransportP0Stage.Boarding;
    return boatAtDestination
        ? ArmyRtsTransportP0Stage.Landing
        : ArmyRtsTransportP0Stage.Sailing;
}
```

- [ ] **Step 4: Re-run `--rts-transport-p0` and verify GREEN**

Expected: `RTS transport P0 rules passed.`

### Task 4: Execute The Full Voyage In P0

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportP0RulesTests.cs.txt`
- Modify: `Code/core/lineage/ArmyRtsTransportProductionService.cs`
- Modify: `Code/core/lineage/ArmyRtsTransportService.cs`
- Modify: `Code/core/performance/AWCooperativeActorPostRunner.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`

- [ ] **Step 1: Replace old guards with failing P0 ownership guards**

Require:

```text
AWDockTransportService.TryResolveRoute(
ArmyRtsTransportP0Stage.AssembleAtEntry
TryProvisionAtRoute(
DriveActorToTileP0(
DriveBoatToTileP0(
member.embarkInto(boat)
member.disembarkTo(boat, landingTile)
```

Reject voyage dependencies on:

```text
TaxiManager.newRequest(
TaxiRequestState.Loading
VanillaForceEmbarkTaskId
boat_transport_go_load
boat_transport_go_unload
```

- [ ] **Step 2: Run `--rts-transport-p0` and verify RED**

Expected: source guards fail because the current implementation waits for per-member vanilla taxi state.

- [ ] **Step 3: Add route-specific temporary boat provisioning**

Add:

```csharp
internal static bool TryProvisionAtRoute(Kingdom pKingdom,
    AWDockRouteCandidate pRoute, out Actor pBoatActor)
```

Resolve the selected entry dock and pickup sea tile, enter the existing temporary-build scope, invoke `Docks.buildBoatFromHere`, join the kingdom and dock city, record the boat in `TemporaryBoatIds`, and leave `Boat.taxi_request` null.

For `ShoreFallback`, resolve a compatible kingdom transport actor asset and
create the temporary boat directly on `route.Entry.OceanTileId`; attach kingdom
and nearest friendly city ownership, record it in `TemporaryBoatIds`, and use
the same cleanup path. This branch does not require a dock building or stored
boat resource.

- [ ] **Step 4: Replace voyage execution with the P0 state machine**

`TransportState` stores `AWDockRouteCandidate Route`, `ArmyRtsTransportP0Stage Stage`, `Actor Boat`, and the locked strategic target. `Begin` resolves the route once and captures the full army roster.

```csharp
case ArmyRtsTransportP0Stage.AssembleAtEntry:
    DriveActorToTileP0(captain, entryLandTile, pCycleElapsed);
    break;
case ArmyRtsTransportP0Stage.BoatToPickup:
    EnsureTemporaryBoat(state);
    DriveBoatToTileP0(state.Boat, pickupSeaTile, pCycleElapsed);
    break;
case ArmyRtsTransportP0Stage.Boarding:
    foreach (Actor member in state.Members.Values)
        if (IsValidMember(member, army) && !member.is_inside_boat)
            member.embarkInto(boat);
    break;
case ArmyRtsTransportP0Stage.Sailing:
    DriveBoatToTileP0(state.Boat, destinationSeaTile, pCycleElapsed);
    break;
case ArmyRtsTransportP0Stage.Landing:
    foreach (Actor member in state.Members.Values)
        if (IsValidMember(member, army) && member.is_inside_boat)
            member.disembarkTo(boat, landingTile);
    break;
```

Drive helpers submit one locked AW streaming path when the target changes, advance path movement and smooth movement in the same P0 cycle, call `skipBehaviour()`, and do not reset an unchanged route.

In `AWCooperativeActorPostRunner`, call a transport member helper before the generic suppression return so the captain assembly path advances while other passengers remain frozen. Remove the path that advances `force_into_a_boat`.

- [ ] **Step 5: Re-run focused slices**

Run `--rts-transport-p0`, `--three-month-replenishment-slice`, and `--pathfinding-mode-slice`. Expected: all exit successfully.

### Task 5: Recovery, Verification, And Deployment

**Files:**
- Verify all files modified in Tasks 1-4.
- Deploy to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`.

- [ ] **Step 1: Add recovery guards**

Require pre-boarding endpoint invalidation to re-resolve the graph, boat death to return to `BoatToPickup`, destination invalidation after boarding to re-resolve only the destination side, and temporary boats to remain alive while passengers are aboard.

- [ ] **Step 2: Run the complete rules suite**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false -p:UseAppHost=false
```

Expected: `Rule tests passed.`

- [ ] **Step 3: Run the production build**

```powershell
dotnet build AncientWarfare3.csproj -c Release --no-restore -p:UseSharedCompilation=false
```

Expected: 0 warnings and 0 errors.

- [ ] **Step 4: Inspect the scoped diff**

Run `git diff --check` for the pathfinding, RTS transport, scheduler, and test files listed above. Expected: no whitespace errors.

- [ ] **Step 5: Deploy**

```powershell
& .\deploy-local.ps1
```

Expected: deployment succeeds and deployed source/DLL hashes match the build output.

- [ ] **Step 6: Runtime acceptance**

In large-step mode, verify dock and terrain-change scenarios. The log must show:

```text
route_selected -> assembling -> boat_to_pickup -> boarding ->
embarked -> sailing -> landing -> landed -> complete
```

No transition may wait for `force_into_a_boat` or vanilla `Loading`.

# RTS Transport Hot Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove repeated global and roster scans from the new portal-aware RTS transport without changing voyage behavior.

**Architecture:** Add constant-time ownership and dock indexes, replace repeated member predicates with one voyage census, omit embarked passengers from redundant actor P0 admission, and coalesce topology rebuilds until traversal changes settle.

**Tech Stack:** C#, AncientWarfare3 P0 scheduler, AW pathfinding cache, source-based regression tests, .NET Release build.

---

### Task 1: Restore The Transport Test Baseline

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportP0RulesTests.cs.txt`

- [ ] **Step 1: Replace the obsolete native embark assertion**

Remove requirements for `member.embarkInto(boat)`. Require the current batch-independent handoff:

```text
member.data.transportID = boat.actor.data.id
member.is_inside_boat = true
member.inside_boat = boat
boat.addPassenger(member)
```

Keep the native `boat.unloadPassengers(landingTile, false)` assertion.

- [ ] **Step 2: Run the focused test**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false -p:UseAppHost=false -- --rts-transport-p0
```

Expected: the stale embark failure disappears. Any remaining failure must be investigated before adding performance tests.

### Task 2: Add Constant-Time Boat And Dock Ownership Indexes

**Files:**
- Modify: `Code/core/lineage/ArmyRtsTransportService.cs`
- Modify: `Code/core/pathfinding/AWDockTransportService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportP0RulesTests.cs.txt`

- [ ] **Step 1: Add failing source assertions**

Require `OwnedTransportBoats.TryGetValue` inside `OwnsTransportBoat` and reject `foreach (TransportState state in States.Values)` from that method. Require `DockBuildings.TryGetValue` inside `FindDockBuilding` and reject city enumeration from that method.

- [ ] **Step 2: Run the focused test and verify RED**

Expected: failures for linear boat ownership and dock lookup.

- [ ] **Step 3: Implement the indexes**

Add `Dictionary<long, Actor> OwnedTransportBoats`. Register through one helper when provisioning a boat; unregister before replacement, completion, cancellation, disposal, and clear. `OwnsTransportBoat` validates actor identity and liveness after O(1) lookup.

Add `Dictionary<long, Building> DockBuildings`. Clear and rebuild it with the topology registry, and implement `FindDockBuilding` as an O(1) lookup with live-object validation.

- [ ] **Step 4: Run the focused test and verify GREEN**

Expected: ownership and endpoint lookup assertions pass.

### Task 3: Exclude Embarked Passengers From Redundant P0 Admission

**Files:**
- Modify: `Code/core/lineage/ArmyRtsTransportRules.cs`
- Modify: `Code/core/lineage/ArmyRtsTransportService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportP0RulesTests.cs.txt`

- [ ] **Step 1: Add failing rule tests**

Add:

```csharp
Assert(ArmyRtsTransportRules.ShouldAdmitMemberP0(true, false),
    "a valid unembarked member remains P0 work");
Assert(!ArmyRtsTransportRules.ShouldAdmitMemberP0(true, true),
    "an embarked passenger is driven by the boat, not actor P0");
Assert(!ArmyRtsTransportRules.ShouldAdmitMemberP0(false, false),
    "an invalid member is never admitted");
```

- [ ] **Step 2: Run the focused test and verify RED**

Expected: compilation fails because `ShouldAdmitMemberP0` is missing.

- [ ] **Step 3: Implement the minimal rule and use it**

Implement `return pValidMember && !pInsideBoat;`. Update `RefreshMilitaryP0Priority` to skip embarked passengers while retaining unembarked members so waiting armies remain held by transport ownership.

- [ ] **Step 4: Run the focused test and verify GREEN**

Expected: P0 admission tests pass.

### Task 4: Replace Repeated Roster Predicates With One Census

**Files:**
- Modify: `Code/core/lineage/ArmyRtsTransportService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportP0RulesTests.cs.txt`

- [ ] **Step 1: Add failing source assertions**

Require one `BuildRosterCensus` or `RefreshRosterAndBuildCensus` call in the voyage cycle and reject the old independent `AllMembersEmbarked`, `AllMembersLanded`, and `HasAnyEmbarkedMember` method declarations.

- [ ] **Step 2: Run the focused test and verify RED**

Expected: failure because the voyage still rescans the roster through three predicates.

- [ ] **Step 3: Implement the census**

Create a private immutable `RosterCensus` containing valid, embarked, and stable-landed counts plus `HasAnyEmbarked`, `AllEmbarked`, and `AllLanded`. One pass removes invalid IDs and computes the facts after ensuring the captain is present.

Pass `HasAnyEmbarked` into route revalidation. Make `BoardRoster` return whether all valid members are aboard after its mutation pass, and make `LandRoster` return whether all valid members are stably landed after native unload and fallback reconciliation. Remove the old predicate methods.

- [ ] **Step 4: Run the focused test and verify GREEN**

Expected: the state-machine tests pass with one pre-stage census and no old predicates.

### Task 5: Coalesce Full Topology Rebuilds

**Files:**
- Modify: `Code/core/pathfinding/AWDockTransportRules.cs`
- Modify: `Code/core/pathfinding/AWDockTransportService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AWDockRouteRegistryTests.cs.txt`

- [ ] **Step 1: Add failing rebuild-rule tests**

Require no rebuild while traversal dirty tiles remain, no second rebuild in the same render frame, and one rebuild for a stable new revision:

```csharp
False(AWDockTransportRules.ShouldRebuildTopology(true, 4, 5, 8, 10, 11),
    "dirty traversal work coalesces before full rebuild");
False(AWDockTransportRules.ShouldRebuildTopology(true, 4, 5, 0, 11, 11),
    "only one full rebuild may run per render frame");
True(AWDockTransportRules.ShouldRebuildTopology(true, 4, 5, 0, 10, 11),
    "stable changed topology rebuilds once");
```

- [ ] **Step 2: Run the focused transport test and verify RED**

Run the full rules harness if the dock test has no dedicated CLI. Expected: compilation failure on the expanded rule signature.

- [ ] **Step 3: Implement rebuild coalescing**

Track `_lastTopologyRebuildFrame`. `EnsureTopology` reads `AWPathfindingBootstrap.Cache.DirtyTileCount` and `Time.frameCount`, calls the expanded rule, and records the frame only after a completed rebuild. Reset the frame marker on world clear.

- [ ] **Step 4: Run tests and verify GREEN**

Run `--rts-transport-p0`, then the full rules harness.

### Task 6: Verify And Commit The Transport Optimization

**Files:**
- No additional files.

- [ ] **Step 1: Run focused transport tests**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false -p:UseAppHost=false -- --rts-transport-p0
```

- [ ] **Step 2: Run the full rules harness**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false -p:UseAppHost=false
```

- [ ] **Step 3: Run a Release build**

```powershell
dotnet build AncientWarfare3.csproj -c Release --no-restore -p:UseSharedCompilation=false
```

- [ ] **Step 4: Commit only the transport optimization files**

```powershell
git add Code/core/lineage/ArmyRtsTransportRules.cs Code/core/lineage/ArmyRtsTransportService.cs Code/core/pathfinding/AWDockTransportRules.cs Code/core/pathfinding/AWDockTransportService.cs Tests/AncientWarfare3.Rules.Tests/ArmyRtsTransportP0RulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AWDockRouteRegistryTests.cs.txt
git commit -m "perf: remove RTS transport hot path scans"
```

### Task 7: Deploy And Collect Runtime Evidence

**Files:**
- Deployment output only.

- [ ] **Step 1: Deploy locally**

```powershell
.\deploy-local.ps1
```

- [ ] **Step 2: Verify source parity**

```powershell
.\Tests\VerifySourceDeployment.ps1 -SourceRoot 'F:\WorldBox New Mod\AncientWarfare3.0' -DestinationRoot 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
```

- [ ] **Step 3: Runtime acceptance**

Capture a fresh log showing `route_selected`, `assembling`, `boat_to_pickup`, `boarding`, `sailing`, `landing`, and `complete`. Test several simultaneous voyages and confirm no repeated topology rebuild or material FPS loss.

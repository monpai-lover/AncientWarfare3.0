# De Jure Dirty Maintenance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove de jure repair work from normal read paths and process only affected kingdoms and regions through a bounded, retryable authority-cycle queue.

**Architecture:** Keep `DeJureRegionStore` as the persistence/commit boundary. Add `DeJureRegionMaintenanceService` for coalesced dirty identities, retry backoff, and bounded processing; mutators enqueue affected identities and the authority cycle drains them before read-model consumers. Reads return cloned snapshots and never repair or mutate.

**Tech Stack:** C#/.NET, Newtonsoft JSON, existing AW authority-cycle services, source-guard rule tests.

---

### Task 1: Add maintenance queue primitives

**Files:**
- Create: `Code/core/court/DeJureRegionMaintenanceService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionMaintenanceSourceGuardTests.cs.txt`

- [ ] **Step 1: Write source-guard tests for queue semantics**

Add assertions that the service exposes `MarkKingdomDirty`, `MarkRegionDirty`, `ProcessAuthorityCycle`, `Reset`, and `ClearRuntime`; has five retry delays `1,2,4,8,16`; and coalesces IDs in dictionaries rather than appending duplicate list entries.

- [ ] **Step 2: Run the focused rules test and verify it fails**

Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- DeJureRegionMaintenanceSourceGuardTests`.
Expected: FAIL because the service and source file do not exist.

- [ ] **Step 3: Implement the bounded queue**

Create an internal static service with a `[Flags]` reason enum and a private ticket containing ID, reasons, retry count, next cycle, and dormant state. Under a lock, merge marks by ID and wake dormant tickets. `ProcessAuthorityCycle` takes an item budget, snapshots due tickets, invokes a store-owned processing callback, removes successful/no-op tickets, and reschedules exceptions using `1 << retryCount` capped at 16. After five failures leave the ticket dormant. Do not enumerate `World.world.cities` or `World.world.kingdoms` in this service.

- [ ] **Step 4: Run the focused rules test**

Run the command from Step 2. Expected: PASS, including source checks for bounded processing and retry state.

- [ ] **Step 5: Commit the isolated queue**

Run `git add -- Code/core/court/DeJureRegionMaintenanceService.cs Tests/AncientWarfare3.Rules.Tests/DeJureRegionMaintenanceSourceGuardTests.cs.txt` and `git commit -m "perf: add bounded de jure dirty queues"`.

### Task 2: Make store initialization and reads read-only

**Files:**
- Modify: `Code/core/court/DeJureRegionStore.cs:20-160,700-750`
- Test: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionReadPathSourceGuardTests.cs.txt`

- [ ] **Step 1: Add failing guards for read paths**

Assert that `ObserveLoadDirectory` does not call `RepairEmptyRegionsLocked`, `EnsureAllKingdomCapitalSeatsLocked`, `SyncAllRegionNamesLocked`, `MigrateHistoricalMetadataLocked`, or `RepairUnassignedCities`; and that `ActiveRegions`, `TryGetForCity`, `TryGetBySeat`, and `Revision` do not call mutating helpers.

- [ ] **Step 2: Run the guard test and verify it fails**

Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- DeJureRegionReadPathSourceGuardTests`.
Expected: FAIL against the current calls.

- [ ] **Step 3: Move full repair behind world-load completion**

Leave `ObserveLoadDirectory` limited to normalize/read state and reset runtime cursors. Make `RepairAfterWorldLoaded` the only method that invokes empty-region repair, capital-seat repair, historical migration, name synchronization, and `DeJureNewCityAssignmentService.RepairUnassignedCities`, guarded by a one-time boolean for the current load. Add an internal store callback used by the maintenance service to process one dirty ticket under a cloned working store.

- [ ] **Step 4: Make reads snapshot-only**

Remove `SyncAllRegionNamesLocked` from read APIs. Under `Gate`, clone matching regions and return arrays. Keep stronghold rejection as a pure lookup. Ensure `EnsureInitialized` only creates an empty store when no load has occurred; it must not repair or scan.

- [ ] **Step 5: Run source guards and compile**

Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- DeJureRegionReadPathSourceGuardTests` and `dotnet build AncientWarfare3.csproj`. Expected: PASS and a successful build.

- [ ] **Step 6: Commit the read-path isolation**

Run `git add -- Code/core/court/DeJureRegionStore.cs Tests/AncientWarfare3.Rules.Tests/DeJureRegionReadPathSourceGuardTests.cs.txt` and `git commit -m "perf: isolate de jure repair from reads"`.

### Task 3: Enqueue explicit region and city changes

**Files:**
- Modify: `Code/core/court/DeJureRegionStore.cs` at all region mutators
- Modify: `Code/core/court/DeJureNewCityAssignmentService.cs:37-130`
- Test: `Tests/AncientWarfare3.Rules.Tests/DeJureDirtyEventSourceGuardTests.cs.txt`

- [ ] **Step 1: Add event-source guards**

Assert that create, transfer, merge, retirement, seat change, city creation, ownership change, capital change, and explicit seat-name change call the maintenance service with the affected IDs only.

- [ ] **Step 2: Run the guard test and verify it fails**

Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- DeJureDirtyEventSourceGuardTests`.
Expected: FAIL because mutators currently perform derived repair inline.

- [ ] **Step 3: Add dirty marks to mutators**

After each successful in-memory mutation, call `MarkRegionDirty` and/or `MarkKingdomDirty` with a reason. Retirement must set `Active=false`, clear members, and mark the retired ID so maintenance explicitly skips recreation. City assignment must mark only the selected region/kingdom; leave full `RepairUnassignedCities` for world load.

- [ ] **Step 4: Preserve explicit name synchronization**

Keep `SyncSeatName` callable only from the city-name-change path with a known region ID. It updates that region's name and marks it dirty; no read path calls it.

- [ ] **Step 5: Run guards and compile**

Run the focused guard and `dotnet build AncientWarfare3.csproj`. Expected: PASS and successful build.

- [ ] **Step 6: Commit event integration**

Run `git add -- Code/core/court/DeJureRegionStore.cs Code/core/court/DeJureNewCityAssignmentService.cs Tests/AncientWarfare3.Rules.Tests/DeJureDirtyEventSourceGuardTests.cs.txt` and `git commit -m "perf: enqueue de jure changes incrementally"`.

### Task 4: Process dirty work in the authority cycle

**Files:**
- Modify: `Code/core/performance/AWAuthorityCycleService.cs:10-80,250-450,470-510`
- Modify: `Code/core/court/DeJureRegionMaintenanceService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/DeJureAuthorityStageSourceGuardTests.cs.txt`

- [ ] **Step 1: Add the stage guard**

Assert that a `DeJureMaintenance` stage exists once, appears before map/aggregation consumers, uses a finite budget, and calls `ProcessAuthorityCycle` rather than a world-wide repair method.

- [ ] **Step 2: Run the guard test and verify it fails**

Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- DeJureAuthorityStageSourceGuardTests`.
Expected: FAIL because no stage exists.

- [ ] **Step 3: Add the bounded stage**

Add `DeJureMaintenance` to the cooperative enum and phase names. In `ExecuteStage`, call `MeasureAuthority("de_jure_maintenance", () => DeJureRegionMaintenanceService.ProcessAuthorityCycle(2))`. Reset the service in `AWAuthorityCycleService.Reset`.

- [ ] **Step 4: Implement atomic store callback and invalidation**

For each ticket, clone the relevant region/derived state, validate object availability and retirement status, apply existing targeted repair helpers, compare effective values, and swap only on success under `Gate`. Increment `StoreRevision` once if changed. On changed success, call `RegionalGovernmentAggregationService.Clear`, invalidate the hierarchical map snapshot, and invalidate the de jure war-goal cache through existing public clear/invalidate methods; do not do these calls for no-ops or failures.

- [ ] **Step 5: Run tests and build**

Run the focused guard plus `dotnet build AncientWarfare3.csproj`. Expected: PASS and successful build.

- [ ] **Step 6: Commit scheduler integration**

Run `git add -- Code/core/performance/AWAuthorityCycleService.cs Code/core/court/DeJureRegionMaintenanceService.cs Tests/AncientWarfare3.Rules.Tests/DeJureAuthorityStageSourceGuardTests.cs.txt` and `git commit -m "perf: drain de jure work in authority cycles"`.

### Task 5: Add behavioral regression coverage and review

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/DeJureDirtyMaintenanceRulesTests.cs.txt`

- [ ] **Step 1: Add deterministic rule tests**

Cover coalescing, retry delays, dormant wake-up, no-op revision stability, failed-transaction snapshot preservation, explicit-retirement preservation, and one-time cache invalidation. Use the existing source-guard test style and pure test doubles where runtime objects are unavailable.

- [ ] **Step 2: Register the tests**

Add the test class invocation to `Program.cs.txt` without changing unrelated test ordering.

- [ ] **Step 3: Run the complete rules suite**

Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`. Expected: PASS with no new failures.

- [ ] **Step 4: Inspect the final diff**

Run `git diff --check` and `git status --short`; confirm only the planned files from this feature are staged and existing user changes remain unstaged.

- [ ] **Step 5: Commit regression coverage**

Run `git add -- Tests/AncientWarfare3.Rules.Tests/Program.cs.txt Tests/AncientWarfare3.Rules.Tests/DeJureDirtyMaintenanceRulesTests.cs.txt` and `git commit -m "test: cover de jure dirty maintenance recovery"`.

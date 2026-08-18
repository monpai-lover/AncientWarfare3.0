# Cultiway Dock Endpoint Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract snapshot-aware dock water identity, topology refresh, and blocking path-worker wakeups into the integration branch without importing unrelated Cultiway or RTS behavior.

**Architecture:** Keep `AWDockTransportService` as the owner of endpoint registration and route selection. Add one pure `AWDockEndpointRules` helper for component resolution, extend the endpoint value object with a legacy fallback, and expose the existing traversal snapshot component lookup. Change only the worker wait primitive in `AWPathFinder`; all route/task ownership remains unchanged.

**Tech Stack:** C# source-linked .NET rules test project, existing `AWTraversalCache`/`AWTraversalSnapshot`, `SemaphoreSlim`, PowerShell, Git.

---

### Task 1: Add a failing extraction guard

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/PathfindingBaselineSourceGuardTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing test**

  Add a `PathfindingBaselineSourceGuardTests.Run()` method that reads `Code/core/pathfinding/AWDockTransportService.cs` and asserts it contains `AWDockEndpointRules.ResolveWaterComponent`, and reads `Code/core/pathfinding/AWPathFinder.cs` and asserts it does not contain `_queueSignal.Wait(50)`.

- [ ] **Step 2: Register the test and run it**

  Include the `.txt` test in the rules project and invoke it immediately before the path-session tests in `Program.cs.txt`.

  Run:

  ```powershell
  dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
  ```

  Expected: FAIL at the new dock assertion because the current `master` source has no `AWDockEndpointRules.ResolveWaterComponent` reference. Do not proceed until this failure is observed.

- [ ] **Step 3: Commit the red test**

  ```powershell
  git add Tests/AncientWarfare3.Rules.Tests/PathfindingBaselineSourceGuardTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
  git commit -m "test: guard Cultiway dock extraction boundary"
  ```

### Task 2: Implement pure dock component rules and endpoint state

**Files:**
- Create: `Code/core/pathfinding/AWDockEndpointRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/AWDockEndpointRulesTests.cs.txt`
- Modify: `Code/core/pathfinding/AWDockRouteModels.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add focused rule cases**

  The test must assert these exact behaviors:

  ```csharp
  AWDockEndpointRules.ResolveWaterComponent(7, 99) == 7;
  AWDockEndpointRules.ResolveWaterComponent(-1, 99) == 99;
  AWDockEndpointRules.SameWaterComponent(4, 4, 90, 91);
  !AWDockEndpointRules.SameWaterComponent(4, 5, 90, 91);
  !AWDockEndpointRules.SameWaterComponent(-1, -1, -1, -1);
  ```

- [ ] **Step 2: Run the focused test and verify the expected compile failure**

  Run the rules project. Expected: compile failure identifying the missing `AWDockEndpointRules` type. This is the red state for the pure helper.

- [ ] **Step 3: Implement the minimal helper and endpoint fallback**

  Implement `ResolveWaterComponent(snapshot, legacy)` as snapshot-first with legacy fallback, and `SameWaterComponent(firstSnapshot, secondSnapshot, firstLegacy, secondLegacy)` as equality of resolved non-negative components. Extend `AWDockEndpoint` with `LegacyWaterComponent` and an optional constructor argument defaulting to `-1`; leave `IsValid` based on the resolved primary component as it is today.

- [ ] **Step 4: Run focused and staged tests**

  Run the full rules project. The dock assertions should pass after the helper is added; the command is expected to remain red only on the known `_queueSignal.Wait(50)` guard until Task 4. Do not treat that known failure as a green implementation result.

- [ ] **Step 5: Commit the pure rule unit**

  ```powershell
  git add Code/core/pathfinding/AWDockEndpointRules.cs Code/core/pathfinding/AWDockRouteModels.cs Tests/AncientWarfare3.Rules.Tests/AWDockEndpointRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
  git commit -m "feat: resolve dock water components from traversal snapshots"
  ```

### Task 3: Wire snapshot components and generation refresh

**Files:**
- Modify: `Code/core/pathfinding/AWTraversalSnapshot.cs`
- Modify: `Code/core/pathfinding/AWTraversalCache.cs`
- Modify: `Code/core/pathfinding/AWDockTransportService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AWDockEndpointRulesTests.cs.txt`

- [ ] **Step 1: Add the service source assertions**

  Extend the focused test to require `OceanComponentOf`, `GenerationId`, `_registeredGeneration`, `RefreshFromWorld()`, and `AWDockEndpointRules.SameWaterComponent` in the service/cache source. These assertions fail against the pre-extraction baseline.

- [ ] **Step 2: Implement snapshot lookup**

  Add `AWTraversalSnapshot.OceanComponentOf(int tileId)` returning the snapshot tile component or `-1`. Add `AWTraversalCache.OceanComponentOf(int tileId)` that checks the main-thread contract, selects overlay/current generation, and returns `-1` when no generation exists.

- [ ] **Step 3: Implement registration and lookup refresh**

  In `AWDockTransportService.Register`, resolve the ocean tile id through `AWPathfindingBootstrap.Cache.OceanComponentOf`, retain the legacy island id, and construct `AWDockEndpoint` with both values. Reset `_registeredGeneration` in `Clear`. At the start of `TryResolveRoute`, compare `Cache.GenerationId` with `_registeredGeneration`; on change, call the existing `RefreshFromWorld()` and update the marker. Replace direct component equality with `AWDockEndpointRules.SameWaterComponent` while preserving live endpoint and same-island checks.

- [ ] **Step 4: Run full tests**

  ```powershell
  dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
  ```

  Expected: `Rule tests passed.` with no compiler warnings introduced by these files.

- [ ] **Step 5: Commit the wiring**

  ```powershell
  git add Code/core/pathfinding/AWTraversalSnapshot.cs Code/core/pathfinding/AWTraversalCache.cs Code/core/pathfinding/AWDockTransportService.cs Tests/AncientWarfare3.Rules.Tests/AWDockEndpointRulesTests.cs.txt
  git commit -m "fix: refresh dock routes with traversal generations"
  ```

### Task 4: Replace timed path-worker polling

**Files:**
- Modify: `Code/core/pathfinding/AWPathFinder.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingBaselineSourceGuardTests.cs.txt`

- [ ] **Step 1: Add the worker ordering assertion**

  Require `_queueSignal.Wait()` to appear before `TryTakeWork(out AWScheduledPathWork work)` inside `WorkerLoop`, and assert `_queueSignal.Wait(50)` is absent. This test is red until the worker loop changes.

- [ ] **Step 2: Implement the minimal wait change**

  In `WorkerLoop`, wait once on `_queueSignal`, check `_stopping`, then call `TryTakeWork`; retain the existing `continue`, diagnostics, task execution, exception handling, and loop structure. Do not change queue priority or semaphore release sites.

- [ ] **Step 3: Run the full test suite**

  Run the rules project and expect `Rule tests passed.`. Also run `git diff --check` and confirm no whitespace errors.

- [ ] **Step 4: Commit the worker fix**

  ```powershell
  git add Code/core/pathfinding/AWPathFinder.cs Tests/AncientWarfare3.Rules.Tests/PathfindingBaselineSourceGuardTests.cs.txt
  git commit -m "perf: block path workers on queue signals"
  ```

### Task 5: Final extraction verification

**Files:**
- No new production files; review all commits and the retained source worktree.

- [ ] **Step 1: Verify scope against the design**

  ```powershell
  git diff --stat origin/master...HEAD
  git diff --name-only origin/master...HEAD
  ```

  Confirm only the design/plan docs, the endpoint/cache/service/pathfinder files, and the focused rules tests are present. Reject any async authority, UI, RTS lifecycle, scheduler, portal, or train migration files.

- [ ] **Step 2: Run clean verification**

  ```powershell
  dotnet restore Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
  dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
  git diff --check origin/master...HEAD
  git status --short --branch
  ```

  Expected: restore succeeds, `Rule tests passed.`, no diff-check output, and a clean integration worktree.

- [ ] **Step 3: Preserve the source branch**

  Confirm `feature/cultiway-pathfinding-upgrade` still exists and has not been rewritten or deleted.

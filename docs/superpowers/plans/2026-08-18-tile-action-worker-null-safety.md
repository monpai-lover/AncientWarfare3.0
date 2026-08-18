# Tile Action Worker Null-Safety Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent actor-post tile-action classification from dereferencing mutable WorldBox objects on simulation workers and report forced-pause scheduler faults as errors.

**Architecture:** Keep the existing actor-post state machine and bounded per-batch commit cadence, but classify and execute `u5_curTileAction` on the authoritative main thread. Remove the tile-action worker ticket entirely; all other worker-backed actor, pathfinding, timer, and spatial stages remain unchanged.

**Tech Stack:** C# 11, .NET Framework 4.8, Unity/WorldBox, Harmony, PowerShell source guards, .NET 9 rules executable.

---

## File Map

- `Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1`: lock the worker/main-thread boundary and fatal logging severity.
- `Code/core/performance/AWCooperativeActorPostRunner.cs`: remove tile-action worker dispatch and perform null-safe bounded main-thread classification/commit.
- `Code/patch/AW_FramePrioritySchedulerPatch.cs`: emit forced-pause frame, authority, and boundary faults with `LogError`.
- `Code/patch/AW_ArmyRtsSchedulerPatch.cs`: emit forced-pause native RTS scheduling faults with `LogError`.

### Task 1: Add the Failing Safety Guard

**Files:**
- Modify: `Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1`

- [ ] **Step 1: Read the actor-post and scheduler patch sources**

Add these source loads beside the existing runner/path/worker loads:

```powershell
$actorPost = Read-Source 'Code\core\performance\AWCooperativeActorPostRunner.cs'
$frameSchedulerPatch = Read-Source 'Code\patch\AW_FramePrioritySchedulerPatch.cs'
$armySchedulerPatch = Read-Source 'Code\patch\AW_ArmyRtsSchedulerPatch.cs'
```

- [ ] **Step 2: Add worker-boundary assertions**

Append assertions requiring the tile-action path to be main-thread owned:

```powershell
Forbid-Contains $actorPost 'tileActionWorkItemAction' 'Tile-action classification must not retain a worker delegate.'
Forbid-Contains $actorPost 'tileActionTicket' 'Tile-action classification must not retain a worker ticket.'
Forbid-Contains $actorPost 'PostStage.ScheduleTileAction' 'Tile-action work must proceed directly to bounded main-thread commits.'
Forbid-Contains $actorPost 'PostStage.AwaitTileAction' 'Tile-action work must not await a worker that reads live actors.'

$tileCommit = Get-MethodBlock $actorPost 'private void CommitTileActionWorkItem(int index)'
Require-Contains $tileCommit 'CanSkipSafeGroundTileAction(' 'Tile-action classification must occur in the main-thread commit.'

$tileSafety = Get-MethodBlock $actorPost 'private static bool CanSkipSafeGroundTileAction('
Require-Contains $tileSafety 'actor == null' 'Tile-action classification must reject a missing actor.'
Require-Contains $tileSafety 'actor.current_tile' 'Tile-action classification must validate the current tile.'
Require-Contains $tileSafety 'actor.asset' 'Tile-action classification must validate the actor asset.'
Require-Contains $tileSafety 'tile.tile_id < fires.Length' 'Tile-action classification must bounds-check the fire array.'
```

- [ ] **Step 3: Add fatal-log severity assertions**

For each of the following exact messages, inspect the preceding 160 characters and require `ModClass.LogError(`:

```text
AW MapBox.Update failed; scheduler stopped and game paused:
AW native authority cycle failed; game paused:
AW background simulation/presentation boundary failed;
AW native Army RTS scheduling failed; game paused:
```

- [ ] **Step 4: Run the guard and verify RED**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\CultiwayAdvancedPerformanceExtractionSourceGuard.ps1
```

Expected: FAIL for the tile-action worker symbols, missing main-thread classification/null guards, and warning-level forced-pause logs.

- [ ] **Step 5: Commit the failing guard**

```powershell
git add -- Tests/CultiwayAdvancedPerformanceExtractionSourceGuard.ps1
git commit -m "test: lock tile action worker safety"
```

### Task 2: Move Tile-Action Classification to the Main Thread

**Files:**
- Modify: `Code/core/performance/AWCooperativeActorPostRunner.cs`

- [ ] **Step 1: Remove tile-action worker ownership**

Remove `tileActionWorkItemAction`, `tileActionTicket`, tile-action schedule timestamps, their constructor/reset/abort handling, and the `ScheduleTileAction`/`AwaitTileAction` enum cases. Remove tile-action cases from background-work readiness, assist, wait, and phase-name helpers. Keep all enemy-search, path-movement, and smooth-movement tickets unchanged.

- [ ] **Step 2: Route preparation directly into bounded commits**

Replace the current transition with:

```csharp
case PostStage.BeforeTileAction:
    if (TryRunNextPostRange(
            deadCheckJobIndex + 1,
            tileActionJobIndex))
    {
        return false;
    }

    PrepareTileActionWorkItems();
    tileActionCommitIndex = 0;
    stage = PostStage.CommitTileAction;
    continue;
```

- [ ] **Step 3: Classify in `CommitTileActionWorkItem`**

Use the prepared actor array only from the main-thread commit:

```csharp
Actor[] actors = work.Actors;
int actorsChecked = 0;
for (int i = 0; i < work.Count; i++)
{
    Actor actor = actors?[i];
    actorsChecked++;
    if (CanSkipSafeGroundTileAction(actor, work.Fires))
        continue;
    actor.u5_curTileAction();
}
```

Use `actorsChecked` for the existing benchmark counter. Remove `RunTileActionWorkItemAt`, `RecordTileActionBenchmark`, `RunParallel`, `SerialActors`, `Checked`, and `SerialCount`.

- [ ] **Step 4: Make classification reject invalid runtime state**

Start `CanSkipSafeGroundTileAction` with:

```csharp
if (actor == null || actor._update_done)
    return true;

WorldTile tile = actor.current_tile;
ActorAsset asset = actor.asset;
TileTypeBase type = tile?.Type;
if (tile == null || type == null || asset == null)
    return true;

Building building = tile.building;
if (building != null && building.asset == null)
    return true;

bool tileOnFire = fires != null &&
                  tile.tile_id >= 0 &&
                  tile.tile_id < fires.Length &&
                  fires[tile.tile_id];
```

Use `!tileOnFire` in the safe-ground predicate. Invalid actors skip one tick and remain available to normal container cleanup.

- [ ] **Step 5: Verify the focused guard is partially GREEN**

Run the guard from Task 1. Expected: tile-action assertions PASS; only forced-pause `LogError` assertions remain failing.

- [ ] **Step 6: Build both projects**

```powershell
dotnet build AncientWarfare3.csproj -c Release --no-restore
dotnet build Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore
```

Expected: both builds succeed with zero errors.

- [ ] **Step 7: Commit the runtime fix**

```powershell
git add -- Code/core/performance/AWCooperativeActorPostRunner.cs
git commit -m "fix: keep tile action classification on main thread"
```

### Task 3: Promote Forced-Pause Faults to Error Logs

**Files:**
- Modify: `Code/patch/AW_FramePrioritySchedulerPatch.cs`
- Modify: `Code/patch/AW_ArmyRtsSchedulerPatch.cs`

- [ ] **Step 1: Change only fatal pause logs**

Replace `ModClass.LogWarning` with `ModClass.LogError` only at the four forced-pause messages listed in Task 1. Keep cleanup failures, third-party quarantine, and recoverable fallbacks as warnings.

- [ ] **Step 2: Verify the focused guard is GREEN**

Run the guard from Task 1. Expected: `Cultiway advanced extraction source guard passed.`

- [ ] **Step 3: Run focused RTS regressions**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-build -- --rts-war-lifecycle
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-build -- --rts-transport-p0
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-build -- --peacetime-rts-release
```

Expected: all three slices pass.

- [ ] **Step 4: Commit logging severity**

```powershell
git add -- Code/patch/AW_FramePrioritySchedulerPatch.cs Code/patch/AW_ArmyRtsSchedulerPatch.cs
git commit -m "fix: report scheduler pause faults as errors"
```

### Task 4: Full Verification and Deployment

**Files:**
- Verify only: `AncientWarfare3.csproj`
- Verify only: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Run full builds and rules**

```powershell
dotnet build AncientWarfare3.csproj -c Release --no-restore
dotnet build Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-build
```

Expected: both builds succeed with zero errors and the full rules executable passes.

- [ ] **Step 2: Run adversarial RTS simulation**

```powershell
dotnet run --project Tests\ArmyRtsAdversarialSimulation\ArmyRtsAdversarialSimulation.csproj -c Release
```

Expected: continuity and foundation suites pass with no duplicate assignments.

- [ ] **Step 3: Verify diff scope**

```powershell
git diff --check
git status --short
git show --stat --oneline HEAD~3..HEAD
```

Expected: no whitespace errors. Existing restoration-protection working-tree changes remain unstaged.

- [ ] **Step 4: Deploy after the game is closed**

Deploy repository source to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0` with the repository deployment workflow, preserve a backup, and compare source/deployment hashes.

- [ ] **Step 5: Runtime smoke test**

Run play, pause, resume, and at least one large actor update cycle. Confirm no new `TileActionBatchWork.RunParallel` stack trace appears. Any forced-pause scheduler exception must now be emitted at error level.

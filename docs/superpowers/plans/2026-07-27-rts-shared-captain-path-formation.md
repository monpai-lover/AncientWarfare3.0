# RTS Shared Captain Path Formation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every RTS Army follow one captain path with stable formation offsets so soldiers advance with the captain instead of freezing, scattering, or creating actor-scale strategic paths.

**Architecture:** `AWArmyMarchService` owns a bounded sequence-numbered snapshot of the captain's accepted movement steps and one cursor per follower. `ArmyFormationService` resolves a stable lateral slot around a supplied path node, while pure rules govern monotonic cursor advancement, bounded reconnects, and transport pause/rebase. Existing WorldBox combat temporarily preempts formation movement only for a live local military target.

**Tech Stack:** C#/.NET 9 rule slices, WorldBox Actor/Army APIs, Harmony movement patches, PowerShell source guards, MSBuild Debug/Release builds.

---

### Task 1: Shared Path Cursor Rules

**Files:**
- Create: `Code/core/lineage/ArmySharedPathRules.cs`
- Modify: `Code/core/lineage/ArmyMarchRules.cs`
- Modify: `Tests/ArmyRtsRulesSlice/ArmyRtsRulesSlice.csproj`
- Modify: `Tests/ArmyRtsRulesSlice/Program.cs`

- [ ] **Step 1: Write failing rule tests**

Add tests for the wished-for API below. They prove cursor clamping, monotonic
advancement, slot-row lag, transport pause, and bounded local reconnect.

```csharp
Equal(12L, ArmySharedPathRules.ClampCursor(8L, 12L, 20L));
Equal(17L, ArmySharedPathRules.AdvanceCursor(17L, 15L, 20L, true));
Equal(18L, ArmySharedPathRules.AdvanceCursor(17L, 12L, 20L, false));
Equal(17L, ArmySharedPathRules.MaximumSequenceForRow(20L, 3));
True(ArmySharedPathRules.ShouldPauseLandCursor(true));
True(ArmySharedPathRules.ShouldUseLocalReconnect(false, 64f));
False(ArmySharedPathRules.ShouldUseLocalReconnect(true, 64f));
```

- [ ] **Step 2: Run the rule slice and verify RED**

Run `dotnet run --project Tests/ArmyRtsRulesSlice/ArmyRtsRulesSlice.csproj -c Debug`.
Expected: compilation fails because `ArmySharedPathRules` does not exist.

- [ ] **Step 3: Implement the pure rules**

Create constants for a 256-step Army trail, eight-tile reconnect radius, and a
small per-evaluation cursor advance cap. Implement clamping and advancement
with integer sequence values; do not reference WorldBox runtime types.

- [ ] **Step 4: Run the rule slice and verify GREEN**

Run the same command. Expected: exit 0 with the normal Army RTS rules pass summary.

### Task 2: Publish The Captain Trail

**Files:**
- Modify: `Code/core/lineage/AWArmyMarchService.cs`
- Modify: `Code/core/lineage/ArmyMarchRules.cs`
- Modify: `Code/core/pathfinding/AWPathMovementBridge.cs`
- Test: `Tests/ArmySharedCaptainPathSourceGuardTests.ps1`

- [ ] **Step 1: Write a failing source guard**

Require `aw_army_rts_mission` in `IsSupportedLongMarchTask`, a separate
`LeaderTrail` collection in `MarchState`, monotonically increasing trail
sequence values, duplicate-step suppression, and lifecycle cleanup.

- [ ] **Step 2: Run the guard and verify RED**

Run `pwsh -File Tests/ArmySharedCaptainPathSourceGuardTests.ps1`.
Expected: failure reporting the missing RTS mission task/trail integration.

- [ ] **Step 3: Publish accepted captain steps**

Keep provider validation route data separate from the actual captain trail.
Seed the trail from the captain tile on a new supported path submission; append
accepted movement steps from `OnLeaderPathStep` even when the strategic provider
state exists; retain at most 256 nodes and increase the base sequence on trim.
Do not expose or share `Actor.current_path`.

- [ ] **Step 4: Run the guard and verify GREEN**

Run the same command. Expected: exit 0.

### Task 3: Resolve Stable Follower Targets

**Files:**
- Modify: `Code/core/lineage/AWArmyMarchService.cs`
- Modify: `Code/core/lineage/ArmyFormationService.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/ai/behaviours/actor/BehArmyRtsMission.cs`
- Modify: `Code/patch/AW_ArmySafetyPatch.cs`
- Modify: `Code/content/ArmyRtsContent.cs`
- Test: `Tests/ArmyFollowerLocalPathFallbackTests.ps1`
- Test: `Tests/ArmySharedCaptainPathSourceGuardTests.ps1`

- [ ] **Step 1: Extend failing integration guards**

Require the RTS follower behaviour and vanilla follower interception patch to
obtain targets from `AWArmyMarchService`. Require explicit path-anchor formation
resolution, stable slot reuse, lateral-offset validation, collision-free
alternate slots, and local `BehGoToTileTarget` only after direct correction
fails.

- [ ] **Step 2: Run both guards and verify RED**

Run:

```powershell
pwsh -File Tests/ArmySharedCaptainPathSourceGuardTests.ps1
pwsh -File Tests/ArmyFollowerLocalPathFallbackTests.ps1
```

Expected: the shared-path guard fails because the controller and Harmony patch
still call `ArmyFormationService` directly.

- [ ] **Step 3: Implement cursor-driven follower movement**

For each follower, assign one stable slot and trail cursor. Limit the cursor to
the latest sequence minus the slot row, advance it only after reaching its
current path target, and resolve the next target from the path node plus rotated
lateral offset. Use the follower's first safe dedicated alternate when the
preferred offset is unsafe; never use the captain center tile as a follower
slot. Clamp a disconnected target to eight tiles before publishing it to local
path fallback.

- [ ] **Step 4: Run guards and focused RTS slices**

Run the two guards plus the Debug `ArmyRtsRulesSlice` and
`ArmyRtsRuntimeSlice`. Expected: all commands exit 0.

### Task 4: Pause And Rebase Across Transport

**Files:**
- Modify: `Code/core/lineage/AWArmyMarchService.cs`
- Verify: `Code/core/lineage/ArmyRtsTransportService.cs`
- Test: `Tests/ArmySharedCaptainPathSourceGuardTests.ps1`
- Test: `Tests/ArmyRtsTransportSlice/Program.cs`

- [ ] **Step 1: Write failing transport tests**

Prove active transport prevents cursor advancement, repeated transport polls
do not repeatedly clear the trail, and the first post-voyage land query rebases
the trail once at the captain's current landing tile.

- [ ] **Step 2: Run tests and verify RED**

Run the transport slice and shared-path source guard. Expected: the rebase
assertions fail.

- [ ] **Step 3: Implement transport lifecycle handling**

Mark the Army trail paused while `HasActiveVoyage` is true. On the first false
observation after a paused voyage, clear stale land nodes, seed the captain's
landing tile, and reset follower cursors. Mission replacement, Army disposal,
load, and world reset clear both transport and trail state.

- [ ] **Step 4: Run transport and shared-path checks**

Run both commands from Step 2. Expected: exit 0.

### Task 5: Preserve Mission And Captain Ownership

**Files:**
- Verify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Verify: `Code/core/lineage/KingdomWarDirectorRules.cs`
- Verify: `Code/patch/AW_ArmySafetyPatch.cs`
- Modify: `Tests/ArmyRtsSourceGuardTests.ps1`
- Modify: `Tests/ArmyRtsRulesSlice/Program.cs`

- [ ] **Step 1: Add regression assertions**

Cover the existing mission lease: a valid incomplete non-cooling objective is
retained across ordinary director proposals; a completed, invalid, or cooling
target may change; a new homeland emergency may preempt. Retain the captain
lease assertions proving a living captain cannot be replaced or detached.

- [ ] **Step 2: Verify before production edits**

Run the Army RTS rules and source guards. If they pass, make no production edit
for this task. If they fail, make only the minimum change required by the failed
assertion, then rerun to green.

### Task 6: Keep Prewar Formations With The Captain

**Files:**
- Modify: `Code/core/lineage/ArmyDeploymentRules.cs`
- Modify: `Code/core/lineage/ArmyDeploymentService.cs`
- Modify: `Code/core/lineage/ArmyMarchRules.cs`
- Modify: `Code/core/lineage/AWArmyMarchService.cs`
- Modify: `Tests/ArmyRtsRulesSlice/Program.cs`
- Modify: `Tests/ArmyRtsRuntimeSlice/Program.cs`
- Modify: `Tests/ArmySharedCaptainPathSourceGuardTests.ps1`

- [x] **Step 1: Write failing deployment lifecycle tests**

Cover both-side declaration readiness, captain-following anchors while in
transit, frontier anchors only inside the arrival radius, completed vanilla
trail retention for living followers, exact projection identity under
overlapping notices, provider-route target precedence, assignment-close and
last-follower cleanup, and same-target assignment replacement.

- [x] **Step 2: Run focused tests and verify RED**

Verify each assertion fails for the missing production rule rather than a test
setup error.

- [x] **Step 3: Implement the deployment lifecycle fix**

Evaluate attacker and defender projections together. Keep the formation anchor
on the moving captain until arrival, then switch to the final frontier anchor
and observe the deployment quorum. Retain a completed vanilla leader trail
while the assignment and living followers remain, and never bootstrap over a
provider route for the same target. Reject stale provider ownership and rebuild
a completed vanilla trail when its retained assignment key differs from the
current assignment, even when both assignments target the same tile.

- [x] **Step 4: Run focused tests and verify GREEN**

Run the rule/runtime slices and shared-path source guard. Expected: exit 0.

### Task 7: Full Verification And Deployment

**Files:**
- Verify: `Tests/ArmyRtsAdversarialSimulation/ArmyRtsAdversarialSimulation.csproj`
- Verify: `Tests/ArmyRtsTransportSlice/ArmyRtsTransportSlice.csproj`
- Verify: `Tests/ArmyRtsSourceGuardTests.ps1`
- Verify: `AncientWarfare3.csproj`
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`
- Inspect: `C:/Users/24908/AppData/LocalLow/mkarpenko/WorldBox/Player.log`

- [ ] **Step 1: Run focused RTS tests and adversarial simulation**

Run the rules/runtime/transport slices, shared-path and RTS source guards,
occupation continuation and ownership regression tests, and adversarial
simulation. Expected: every command exits 0.

- [ ] **Step 2: Build Debug and Release**

Run `dotnet build AncientWarfare3.csproj -c Debug --no-restore` and the same
command with `-c Release`. Expected: 0 errors and 0 warnings.

- [ ] **Step 3: Deploy the folder payload**

Confirm WorldBox is closed, copy only the mod folder payload with the existing
workflow, preserve runtime save/configuration data, and verify modified-file
hashes against the deployed copy.

- [ ] **Step 4: Run one actual-war acceptance test**

With RTS and route visuals enabled, observe a 64-member Army on land, through a
turning or blocked route, across one transport voyage, and after occupying a
city. Acceptance requires a stable living captain/flag, at least 80 percent of
the roster advancing along the same corridor, no follower idling until
retirement, transport pause/rebase, and no target change before completion.

- [ ] **Step 5: Inspect runtime evidence**

Read `Player.log` for captain changes, director reassignment, route failures,
cursor rebase, watchdog recovery, scheduler faults, and exceptions. Repeated
target oscillation, a living-captain change, or a moving captain with a
stationary roster fails acceptance.

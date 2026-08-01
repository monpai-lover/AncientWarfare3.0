# Rebellion Force Collapse Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make direct-transfer rebellions surrender only after their attacker-side warriors and realm reserve pool are both exhausted, while rebuilding each captured city's reserve pool for its new owner immediately.

**Architecture:** A pure `RebellionForceCollapseRules` decision receives detached runtime facts and fails closed when military facts are unavailable. `RebellionCollapseSettlementService` owns authority gating, coalesced deferred revalidation, and ending the rebellion with a defender victory; the city-capture patch refreshes the transferred city before queueing that check, while `WarScoreRuntimeBridge` queues the same check after combat-driven score updates.

**Tech Stack:** C#/.NET 9 standalone rules tests, Harmony runtime patches, WorldBox war APIs, PowerShell source guards.

---

### Task 1: Pure rebel-collapse decision

**Files:**
- Create: `Code/core/lineage/RebellionForceCollapseRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/RebellionForceCollapseRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write the failing pure-rule tests**

Add tests which assert that `ShouldCollapse(true, true, true, true, 0, 0)` is true, while a living warrior, an available reserve, an inactive or non-rebellion war, a missing attacker, or unreadable warrior facts all return false.

- [ ] **Step 2: Run the focused slice and verify RED**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rebellion-force-collapse-slice`

Expected: compilation fails because `RebellionForceCollapseRules` does not exist.

- [ ] **Step 3: Implement the minimal pure rule**

Create a single stateless method which requires a valid active rebellion, a valid participating main attacker, readable non-negative warrior facts, zero attacker warriors, and zero available reserves.

- [ ] **Step 4: Run the focused slice and verify GREEN**

Run the command from Step 2.

Expected: `AW3 rebellion force collapse rules passed.`

### Task 2: Immediate captured-city reserve refresh

**Files:**
- Modify: `Code/core/lineage/CityReservePoolService.cs`
- Create: `Tests/RebellionForceCollapseSourceGuard.ps1`

- [ ] **Step 1: Write a failing source guard for the refresh contract**

Require an internal `RefreshCapturedCity(City)` entry point which validates the city's current owner, obtains its current `KingdomPoolState` and `CityPool`, computes `CityReservePoolRules.FullReconciliationBudget(city.units?.Count ?? 0, pool.ActorIds.Count)`, and calls `MaintainCity(..., allowFrozenAddition: true)` so a wartime frozen pool can accept the new owner's eligible residents.

- [ ] **Step 2: Run the guard and verify RED**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File Tests/RebellionForceCollapseSourceGuard.ps1`

Expected: failure stating that the captured-city refresh entry point is missing.

- [ ] **Step 3: Implement the bounded complete refresh**

Add `RefreshCapturedCity(City)` beside the existing reconciliation entry points. It must not copy prior-owner membership and must rely on `MaintainCity`/`TemporaryLevyService.CanRegisterReserve` for eligibility and conscription-law reconciliation.

- [ ] **Step 4: Run the guard and reserve-pool slice**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File Tests/RebellionForceCollapseSourceGuard.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --city-reserve-pool-slice
```

Expected: both pass.

### Task 3: Authority-side deferred rebellion settlement

**Files:**
- Create: `Code/core/lineage/RebellionCollapseSettlementService.cs`
- Modify: `Code/core/lineage/WarScoreRuntimeBridge.cs`
- Modify: `Tests/RebellionForceCollapseSourceGuard.ps1`

- [ ] **Step 1: Extend the source guard and verify RED**

Require replica gates both before queueing and inside processing, a coalesced runtime key based on war ID, re-resolution through `WarPeaceSettlementWorld.FindWar`, live `countAttackersWarriors()` and `CityReservePoolService.CountAvailable(mainAttacker)` reads, a `RebellionForceCollapseRules.ShouldCollapse` call, and `World.world?.wars?.endWar(war, WarWinner.Defenders)`. Also require `WarScoreRuntimeBridge.QueueSettlementChecks` to call `RebellionCollapseSettlementService.QueueIfCollapsed(pWar)`.

- [ ] **Step 2: Run the guard and verify RED**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File Tests/RebellionForceCollapseSourceGuard.ps1`

Expected: failure stating that the settlement service or runtime bridge hook is missing.

- [ ] **Step 3: Implement coalesced live revalidation**

Implement `QueueIfCollapsed(War)` with an authority/active-rebellion prefilter and enqueue by war ID. In the callback, re-resolve the war, main attacker, attacker participation, warrior count, and reserve count; catch warrior-count failures as unreadable facts and fail closed through the pure rule.

- [ ] **Step 4: Run focused tests and guard**

Run the pure-rule slice and source guard.

Expected: both pass.

### Task 4: Capture-order integration

**Files:**
- Modify: `Code/patch/AW_CityOccupationAccelerationPatch.cs`
- Modify: `Tests/RebellionForceCollapseSourceGuard.ps1`

- [ ] **Step 1: Extend the source guard and verify RED**

Require the direct rebellion branch of `FinishCapture_Postfix` to call `CityReservePoolService.RefreshCapturedCity(__instance)` before `RebellionCollapseSettlementService.QueueIfCollapsed(war)`, resolving the saved war ID after ownership transfer. Do not run this path for Zhulu or ordinary frozen occupations.

- [ ] **Step 2: Run the guard and verify RED**

Run the source guard.

Expected: failure stating that refresh-before-collapse ordering is missing.

- [ ] **Step 3: Wire refresh then collapse evaluation**

After confirming the direct capture completed for the saved capturer, resolve the war, clear the direct-transfer state, refresh the captured city pool, and queue the deferred collapse check.

- [ ] **Step 4: Run all standalone rules and focused guards**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
pwsh -NoProfile -ExecutionPolicy Bypass -File Tests/RebellionForceCollapseSourceGuard.ps1
```

Expected: full rules suite and source guard pass with no warnings or errors.

### Task 5: Selective deployment and verification

**Files:**
- Deploy: `Code/core/lineage/RebellionForceCollapseRules.cs`
- Deploy: `Code/core/lineage/RebellionCollapseSettlementService.cs`
- Deploy: `Code/core/lineage/CityReservePoolService.cs`
- Deploy: `Code/core/lineage/WarScoreRuntimeBridge.cs`
- Deploy: `Code/patch/AW_CityOccupationAccelerationPatch.cs`

- [ ] **Step 1: Review only task diffs**

Run `git diff --` for the five production files and the three test/project files. Confirm no unrelated hunks were introduced by this task.

- [ ] **Step 2: Copy only production source files to the installed mod**

Preserve relative paths under `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`. Do not compile or deploy a DLL.

- [ ] **Step 3: Verify deployment hashes**

Compare SHA-256 for each workspace production file against its installed counterpart and require exact equality.

- [ ] **Step 4: Commit task files selectively**

Use path-limited staging/commit so the existing dirty worktree and other sessions' changes are preserved. Do not use `git add -A`.


# Actor Death Safety And Xia Restore Boundary Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (or subagent-driven-development) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent stale actors from crashing the large-step death-check job and remove the recurring Xia/name restore scans that mutate kingdom collections across frames.

**Architecture:** Keep native `Actor.die` authoritative for valid actors, but reject a death-check invocation when the actor snapshot is no longer a live world object with a valid tile. Replace cross-frame live `Kingdom` enumerators with stable kingdom-id snapshots resolved at processing time. Remove the obsolete whole-population name migration stage; Xiaization changes future naming policy only, while institutional migration remains a bounded, idempotent kingdom job.

**Tech Stack:** C# 9, Harmony, Unity/WorldBox APIs, existing rule harness and PowerShell source guards.

---

## Root Cause Evidence

### 1. Death null reference

The supplied stack is:

```text
BatchActors.updateDeathCheck
  -> Actor.checkDeath
  -> Actor.die
  -> AWCooperativeActorPostRunner.RunPostJob
```

The first user-code frame is the original `Actor.die` dynamic method, not `FamilyManagerNewFamilyPatch.Postfix`. The previous quarantine therefore correctly does not match. In the current game assembly `Actor.die` dereferences `current_tile.zone` while returning items and can also dereference stale combat state. A stale actor left in the death-check container after removal can reach that method with no valid tile.

### 2. Restore enumerator invalidation

`NameIntegrationMaterializationService` and `KingdomInstitutionalXiaizationService` both keep `World.world.kingdoms.GetEnumerator()` in a static field and resume it in later authority cycles. Kingdom creation/destruction invalidates that enumerator, producing the exact `Collection was modified` messages in the feedback. Resetting only the enumerator after the exception causes the next cycle to start over and repeat the failure.

### 3. Obsolete live-name migration

`LineageService.ApplyNameIntegration` still calls `NameIntegrationMaterializationService.Request`, and `AWAuthorityCycleService` still executes its stage. That contradicts the future-only Xia naming design and reintroduces a pending scan of living kingdom units whenever culture integration is checked.

## Implementation Tasks

### Task 1: Freeze death-check regression behavior

**Files:**
- Create: `Code/core/policy/ActorDeathSafetyRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ActorDeathSafetyRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] Add `ShouldRunDeathCheck(bool hasData, bool isRekt, bool isAlive, bool hasCurrentTile)` returning true only for a live, non-rekt actor with data and a valid current tile.
- [ ] Test normal live actors, dead actors, removed actors, actors with no tile, and actors already rekt.
- [ ] Add a source guard expectation that the check-death Harmony prefix delegates to this predicate and does not patch `Actor.die` to swallow arbitrary exceptions.
- [ ] Run the focused rules harness and verify the new source assertion fails before implementation.

### Task 2: Guard the native death-check entry

**Files:**
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Add a Harmony prefix for `Actor.checkDeath` that returns `false` when `ActorDeathSafetyRules.ShouldRunDeathCheck(...)` is false.
- [ ] Read `current_tile` only on the main/cooperative authority boundary; the prefix must not enumerate actors or query SQLite.
- [ ] Leave valid actors on the original `checkDeath -> die` path so death counts, destruction scheduling, lineage archival, and existing finalizers remain unchanged.
- [ ] Do not add a broad `Actor.die` finalizer that converts unknown null references into success; unknown death faults must still reach the scheduler fault policy.
- [ ] Add a source guard proving the null-tile gate is on `checkDeath`, while the existing exact FunBoost quarantine remains unchanged.

### Task 3: Replace cross-frame kingdom enumerators with stable ids

**Files:**
- Modify: `Code/core/policy/KingdomInstitutionalXiaizationService.cs`
- Modify: `Code/core/lineage/NameIntegrationMaterializationService.cs` only if retained for compatibility cleanup
- Create/modify: focused restore snapshot rule/test files as needed
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Remove static `IEnumerator` and `_restoreWorld` fields from institutional restore.
- [ ] At the beginning of a world restore, copy only live kingdom ids into a private array/list once; advance an integer cursor across authority cycles. Resolve each id with `World.world.kingdoms.get(id)` immediately before processing and skip missing/rekt kingdoms.
- [ ] If kingdom membership changes, do not restart or enumerate the live collection; new Xiaization requests enter through the existing `Request(kingdom)` path.
- [ ] Preserve the one-stage-per-cycle budget and database phase transitions. A failed stage gets the existing persisted retry/backoff instead of restarting a world enumerator.
- [ ] Test that a kingdom add/remove between two restore cycles does not throw, duplicate the first page, or reset the completion cursor.

### Task 4: Remove whole-population name migration from the active pipeline

**Files:**
- Modify: `Code/core/lineage/LineageService.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Code/core/lineage/NameIntegrationMaterializationService.cs`
- Modify: `Tests/SourceGuardTests.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/NameSystemRulesTests.cs.txt`

- [ ] Delete the `NameIntegrationMaterialization` authority stage and its recurring phase name from `AWAuthorityCycleService`.
- [ ] Remove the `NameIntegrationMaterializationService.Request(pKingdom)` call from `ApplyNameIntegration`; retain only the kingdom integrated marker and policy/history projection.
- [ ] Remove the migration service's live candidate list, actor cursor, restore enumerator, and recurring persistence request. Keep only a no-op compatibility reset if an old save schema still needs to be read, and never enqueue actor migration.
- [ ] Preserve future-only naming at actor birth/initialization and preserve existing living/authored names.
- [ ] Add source guards forbidding a `World.world.units` scan and recurring name-migration request after culture integration.

### Task 5: Verify the combined boundaries

**Files:**
- No production files unless a guard requires an integration assertion.

- [ ] Run `dotnet run --project Tests\\AncientWarfare3.Rules.Tests\\AncientWarfare3.Rules.Tests.csproj -c Debug`; expect `Rule tests passed.`
- [ ] Run `powershell -ExecutionPolicy Bypass -File Tests\\SourceGuardTests.ps1`; expect all guards to pass.
- [ ] Run Debug and Release builds; expect zero warnings and errors.
- [ ] Run `git diff --check`.
- [ ] Deploy source files only and reproduce: kill/erase an actor whose tile is concurrently removed; the game must not pause and valid deaths must still complete.
- [ ] Load a world with many kingdoms, trigger Xiaization, create/remove a kingdom during restoration, and confirm no `Collection was modified` warning and no repeated name-migration stage.
- [ ] Compare authority diagnostics before/after; `name_integration_materialization` must be absent and institutional Xiaization must remain bounded to its persisted stage budget.

## Scope Boundary

This plan does not alter pathfinding, RTS commands, succession selection, capital repair, zero-city destruction, school SQLite event-id allocation, or network/version-check warnings. Those are separate plans or external systems.


# Vanilla Recruitment and Integer Manpower Replenishment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore native recruitment while retaining AW3 replenishment backed only by integer city manpower.

**Architecture:** Disconnect all proactive levy scheduling and actor-reserve maintenance. Keep the existing replenishment operation, but let it reserve integer manpower and materialize the reserved amount as synthetic soldier actors. Preserve public compatibility entry points only where old save/runtime callers still require safe cleanup.

**Tech Stack:** C# 9, Harmony patches, WorldBox runtime APIs, deterministic console rules tests and PowerShell source guards.

---

### Task 1: Lock the recruitment ownership boundary

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/VanillaRecruitmentOwnershipSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [x] Add a source guard that fails while `AWAuthorityCycleService` calls `ProcessPreparationMonth`, `KingdomAnnualWorkService` calls `TemporaryLevyService.OnKingdomYear`, or war-notice paths call `OnEmergencyChanged`.
- [x] Run the guard and confirm RED against the current source.
- [x] Disconnect those entry points while leaving garrison annual work intact.
- [x] Run the guard and confirm GREEN.

### Task 2: Make the city manpower pool integer-only

**Files:**
- Modify: `Code/core/lineage/CityReservePoolService.cs`
- Modify: `Code/patch/AW_CityReservePoolPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CityReservePoolRulesTests.cs.txt`

- [x] Add failing source assertions that the runtime city pool contains no `ActorIds`, `EligibleActorIds`, or actor cursors and that actor transition callbacks do not maintain membership.
- [x] Replace actor membership state with city id, capacity, consumed count, emergency id, and synthetic-live count only.
- [x] Preserve integer reserve open/reserve/release, war start/end, save/restore, availability, and mobilization phase APIs.
- [x] Remove the actor transition Harmony patch from active behavior.
- [x] Run focused reserve tests and confirm GREEN.

### Task 3: Preserve spawn-based replenishment

**Files:**
- Modify: `Code/core/lineage/ArmyReplenishmentOperationService.cs`
- Modify: `Code/core/lineage/SyntheticLevyService.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/SyntheticLevyRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyReplenishmentOperationRulesTests.cs.txt`

- [x] Add failing assertions that replenishment reserves integer manpower before spawning and releases unmaterialized reservations.
- [x] Keep bounded soldier creation and direct target-army assignment.
- [x] Remove synthetic replenishment soldiers from temporary-levy registration while retaining history suppression and cleanup metadata.
- [x] Reduce `TemporaryLevyService` to legacy cleanup/exhaustion compatibility or move the remaining exhaustion bookkeeping into replenishment ownership.
- [x] Run focused replenishment tests and confirm GREEN.

### Task 4: Remove legacy levy runtime hooks

**Files:**
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Code/patch/AW_ArmySafetyPatch.cs`
- Modify: `Code/patch/AW_EnlistPatch.cs`
- Modify: `Code/patch/AW_RetirementPatch.cs`
- Modify: `Code/patch/AW_StandingArmyPatch.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`

- [x] Remove levy casualty, enlistment, retirement, standing-army, emergency, and annual recruitment hooks that are no longer reachable.
- [x] Keep synthetic replenishment death cleanup and replenishment operation war-end cleanup.
- [x] Ensure old saves clear legacy runtime queues without rebuilding actor-based pools.
- [x] Run source guards and full rules tests.

### Task 5: Verify performance-facing behavior

**Files:**
- Modify: `Code/core/policy/RecentFeatureBenchmarkRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [x] Remove or retire the monthly preparation levy benchmark label so it cannot imply active work.
- [x] Keep annual garrison and replenishment diagnostics separately visible.
- [x] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj` and require `Rule tests passed.`
- [x] Inspect `git diff --check` and the complete diff for accidental unrelated changes.
- [x] Do not build the main mod DLL.

# AW3 Slavery And Royal Guard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the AW3 slavery design, restore active slave capture, add slave-army and royal-guard naming, and implement one royal guard per kingdom.

**Architecture:** Keep slavery rules centralized in `SlaveService`, add small AI registration and behavior classes for slave catchers, and add `RoyalGuardService` as an independent guard roster instead of using normal `Army` as the guard container. Normal armies remain normal; slave armies are still normal armies with a high slave ratio and special name, while royal guards are persistent actors with a guard job that follows/protects the king.

**Tech Stack:** WorldBox 0.51/NML C# net48, Harmony patches, actor/city AI jobs, SQLite `[TableDef]` persistence, existing AW3 chronicle/history tables.

---

### Task 1: Slave Catcher AI

**Files:**
- Create: `Code/content/SlaveryContent.cs`
- Create: `Code/ai/behaviours/actor/BehFindSlaveCaptureTarget.cs`
- Create: `Code/ai/behaviours/actor/BehCatchTargetAsSlave.cs`
- Modify: `Code/content/XiaContent.cs`
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/patch/AW_RetirementPatch.cs`

- [x] Register `slave_catcher` citizen job, actor job, and two actor tasks during content init.
- [x] Assign at most one slave catcher per eligible city from `CityBehCheckArmy` postfix.
- [x] Find only weak enemy non-important Xia units near the catcher.
- [x] Capture by calling `SlaveService.Enslave(target, "captured", catcher, city, kingdom)` so all history and persistence paths are reused.

### Task 2: Slavery Rule Completion

**Files:**
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Code/core/lineage/ChronicleKeys.cs`

- [x] Add keys for slave catcher state and army name state.
- [x] Ensure `CanBeEnslaved` excludes king, city leader, heir, figures, slaves, retired soldiers, and dead units.
- [x] Record captured slavery events in person, kingdom, and city history.
- [x] Keep the per-kingdom slavery switch as the single gate for capture, city-fall enslavement, slave inheritance, slave army, and slave labor.

### Task 3: Slave Army Naming

**Files:**
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/patch/AW_SlaveryPatch.cs`

- [x] Detect slave armies by warrior composition, with target ratio 80%.
- [x] Rename qualifying armies to `{kingdom} 奴隶军` or `{kingdom} 奴隶军 N` when a kingdom has multiple slave armies.
- [x] Refresh naming after army creation, warrior enlistment, captain replacement, and kingdom-name-sensitive checks.

### Task 4: Royal Guard Persistence And Rules

**Files:**
- Create: `Code/core/db/RoyalGuardStateTableItem.cs`
- Create: `Code/core/lineage/RoyalGuardService.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/content/XiaTraits.cs`

- [x] Persist one active guard roster per kingdom.
- [x] Enforce max 20 members, at least 20% nobles when available, and a noble captain.
- [x] Exclude king, city leaders, heirs, slaves, retired soldiers, figures, babies, and foreign units.
- [x] Candidate score must be above the kingdom's average warrior score.
- [x] Rebalance `禁卫军` trait so it is elite but not AW2-superpowered.

### Task 5: Royal Guard AI And Army Isolation

**Files:**
- Create: `Code/content/GuardContent.cs`
- Create: `Code/ai/behaviours/actor/BehFindRoyalGuardThreat.cs`
- Create: `Code/ai/behaviours/actor/BehRoyalGuardFollowKing.cs`
- Create: `Code/ai/behaviours/actor/BehRoyalGuardAttackThreat.cs`
- Create: `Code/patch/AW_RoyalGuardPatch.cs`
- Modify: `Code/content/XiaContent.cs`

- [x] Register `king_guard` citizen job, actor job, and follow/protect tasks.
- [x] Guards follow the king in peace and war.
- [x] Guards only attack enemies near the king or enemies already targeting the king/guard.
- [x] Guards do not join normal armies and do not become normal army captains.
- [x] Guards are cleaned up on death, retirement, enslavement, promotion to king/leader, or kingdom transfer.

### Task 6: Guard Naming And History

**Files:**
- Modify: `Code/core/lineage/RoyalGuardService.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Code/core/lineage/ChronicleKeys.cs`

- [x] Name the kingdom guard `{kingdom} 禁卫军`.
- [x] Record guard formation, member appointment, captain appointment, dismissal, and death in kingdom/person/city histories where context exists.
- [x] Refresh guard name when the king or kingdom name changes.

### Task 7: Verification

**Files:**
- Build-only verification for this mod batch.

- [x] Run `& "C:\Program Files\dotnet\dotnet.exe" build` from `F:\WorldBox New Mod\AncientWarfare3.0`.
- [x] Confirm 0 errors.
- [x] Check git diff for accidental unrelated reversions.
- [x] Summarize any runtime-only checks that still need in-game validation.

# AW3 War Goals Claims UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persistent cores, active claim/core fabrication projects, targeted war goals, core/claim mapmodes, a war target window, automatic war-goal settlement, and richer history records.

**Architecture:** Keep the existing `WarDecisionService` war gate and `WarClaim` table. Add focused tables/services for permanent city cores, pending fabrication projects, and per-war target snapshots, then expose them through AW3 mapmodes and a compact WorldBox-style target window. First batch uses automatic peace/war-goal settlement on `WarManager.endWar`; full negotiable peace demands remain a future layer.

**Tech Stack:** C# net48, NeoModLoader, Harmony, WorldBox `Kingdom`/`City`/`War`, AW3 SQLite `[TableDef]`, AW3 `HistoryWriter`, AW3 power tab/mapmode helpers, Unity UI.

---

### Task 1: Persistence Tables

**Files:**
- Create: `Code/core/db/KingdomCoreTableItem.cs`
- Create: `Code/core/db/WarProjectTableItem.cs`
- Create: `Code/core/db/WarGoalTableItem.cs`

- [ ] Add a permanent city-core table keyed by `core_id`.
- [ ] Add a fabrication-project table keyed by `project_id`, storing source kingdom, target kingdom, target city, project type, progress, cost, created time, finished time, and active/completed flags.
- [ ] Add a war-goal table keyed by `war_goal_id`, storing `war_id`, attacker, defender, goal type, target city, target kingdom, source claim/core/project, claimant actor, created time, resolved flag, and result.
- [ ] Build after adding the tables.

### Task 2: War Territory Service

**Files:**
- Create: `Code/core/lineage/WarTerritoryService.cs`
- Modify: `Code/core/lineage/WarDecisionService.cs`
- Modify: `Code/core/lineage/WarDecisionAI.cs`

- [ ] Add query helpers for active cores, active claims, pending projects, and mapmode snapshots.
- [ ] Add APIs to create cores, create/update projects, complete projects into cores/claims, and build tooltip text.
- [ ] Add APIs to choose reclaim/claim target city and create a `WarGoal` before starting a war.
- [ ] Extend `WarDecisionService.TryStartWar` with optional goal metadata while preserving existing callers.
- [ ] Keep existing simple `TryStartWar` overloads working.
- [ ] Build after service integration.

### Task 3: War Goal Settlement

**Files:**
- Modify: `Code/core/lineage/WarTerritoryService.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Code/core/lineage/RoyalClaimService.cs`

- [ ] On war start, write goal snapshot when AW3 target metadata exists.
- [ ] On war end, resolve `take_core_city`, `press_claim_city`, `force_vassal`, `independence`, `restore_kingdom`, and no-CB goal records.
- [ ] Reclaim/claim victory should write city transfer intent history even when WorldBox has already transferred or destroyed the city.
- [ ] Defender victory and peace should write failure/peace result without crashing.
- [ ] Build after settlement wiring.

### Task 4: Core And Claim Mapmodes

**Files:**
- Create: `Code/core/policy/WarCoreMapModeService.cs`
- Create: `Code/core/policy/WarClaimMapModeService.cs`
- Create: `Code/patch/AW_WarMapModePatch.cs`
- Modify: `Code/content/GodPowerLibrary.cs`
- Modify: `Code/ui/AW_LineageTab.cs`
- Modify: `Code/patch/AW_MapModeTooltipPatch.cs`

- [ ] Register `aw_core_mapmode` and `aw_claim_mapmode` powers.
- [ ] Let clicking/selecting a kingdom set the focused kingdom for both mapmodes.
- [ ] Core map colors: core green, owned non-core red, pending core cyan.
- [ ] Claim map colors: strong claim green, weak claim yellow, pending claim orange.
- [ ] Cache focused kingdom snapshots and refresh on mapmode click/project completion.
- [ ] Replace default kingdom tooltip in these mapmodes with focused AW3 mapmode details.
- [ ] Build after mapmode wiring.

### Task 5: War Target Window

**Files:**
- Create: `Code/ui/windows/WarDecisionTargetWindow.cs`
- Modify: `Code/ui/AW_LineageWindowIds.cs`
- Modify: `Code/ui/windows/KingdomPolicyWindow.cs`
- Modify: `Code/ui/windows/KingdomWindowAddition.cs`

- [ ] Add a compact draggable window opened from the decision panel and kingdom UI.
- [ ] Show target kingdoms with flag/name, power ratio, available CB icons, core/claim counts, and current war state.
- [ ] Show selected target's available actions: fabricate core, fabricate weak claim, fabricate strong claim, reclaim core city, press claim city, force vassal, restoration, no-CB.
- [ ] Show penalty preview for no-CB and missing-requirement tooltip for locked actions.
- [ ] Execute actions through `WarTerritoryService` and `WarDecisionService`.
- [ ] Build after UI wiring.

### Task 6: History And Localization

**Files:**
- Modify: `Locales/aw3_ancestry_mapmode.csv`
- Modify: `Locales/aw3_policy_ui.csv`
- Modify: `Locales/war.csv`
- Modify: `README.md`
- Modify: `docs/AW3_Roadmap.md`

- [ ] Add zh/en/zht keys for mapmodes, window labels, CB labels, project progress, no-CB penalty, and settlement results.
- [ ] Update roadmap/readme with completed first batch and remaining negotiated-peace TODO.
- [ ] Build after localization/docs updates.

### Task 7: Verification

**Files:**
- No source files.

- [ ] Run baseline build before edits.
- [ ] Run final build with `$env:DOTNET_ROLL_FORWARD='Major'; dotnet build`.
- [ ] Search for missing locale keys introduced by this batch.
- [ ] Inspect git diff to verify only intended war/mapmode/UI/docs files were changed.

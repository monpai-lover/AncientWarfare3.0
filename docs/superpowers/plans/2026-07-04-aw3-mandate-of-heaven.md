# AW3 Mandate Of Heaven Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build AW3's Mandate of Heaven system as a persistent world-order layer with a unique Mandate dynasty, mandate value, imperial authority, legal core territory, vassal integration, special wars, map modes, UI, and history records.

**Architecture:** Add a small persistence model (`MandateState`, `MandatePeriod`, `MandateCoreCity`, `MandateEvent`) and one service (`MandateService`) as the only owner of current Mandate state. Existing systems call this service from kingdom yearly updates, war start/end, title/year-name changes, historical-figure gating, vassal map logic, and AW3 tab buttons.

**Tech Stack:** C# net48, NeoModLoader, Harmony, WorldBox `Kingdom`/`War`/`MapLayer`, AW3 SQLite `[TableDef]`, existing `HistoryWriter`, existing mapmode and window patterns.

---

### Task 1: Persistence And Service Core

**Files:**
- Create: `Code/core/db/MandateStateTableItem.cs`
- Create: `Code/core/db/MandatePeriodTableItem.cs`
- Create: `Code/core/db/MandateCoreCityTableItem.cs`
- Create: `Code/core/db/MandateEventTableItem.cs`
- Create: `Code/core/lineage/MandateService.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`

- [x] Add persistent current-state, dynasty-period, legal-core-city, and event tables.
- [x] Add `MandateService.Exists`, `GetCurrentMandateKingdom`, `TryDeclareMandate`, `ClearMandate`, `OnKingdomYear`, `OnWarStarted`, `OnWarEnded`, `OnKingdomDestroyed`, `HasMandateProtection`, and tooltip/report helpers.
- [x] Persist all current state in SQLite and mirror hot values on `kingdom.data`.

### Task 2: Hook Existing Systems

**Files:**
- Modify: `Code/content/DiplomacyContent.cs`
- Modify: `Code/content/GodPowerLibrary.cs`
- Modify: `Code/content/figures/HistoricalFigureService.cs`
- Modify: `Code/patch/AW_KingdomPolicyPatch.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Code/core/lineage/KingdomTitleService.cs`
- Modify: `Code/core/lineage/YearNameService.cs`

- [x] Register `tianming` and `tianmingrebel` war types.
- [x] Replace historical-figure Mandate stub with `MandateService.Exists`.
- [x] Run Mandate yearly tick after policy/vassal yearly tick.
- [x] Let `tianming` wars transfer Mandate on attacker victory.
- [x] Let low Mandate trigger crisis/collapse unless the emperor has `first`.
- [x] Make Mandate rulers Emperor title and trigger year-name changes.

### Task 3: Map Modes

**Files:**
- Create: `Code/core/policy/MandateDynastyMapModeService.cs`
- Create: `Code/core/policy/MandateDynastyMapLayer.cs`
- Create: `Code/core/policy/MandateCoreMapModeService.cs`
- Create: `Code/core/policy/MandateCoreMapLayer.cs`
- Create: `Code/patch/AW_MandateMapModePatch.cs`
- Modify: `Code/content/GodPowerLibrary.cs`
- Modify: `Code/ui/AW_LineageTab.cs`

- [x] Add Mandate dynasty map mode showing current Mandate country plus direct/recursive vassals.
- [x] Add Mandate legal-core map mode showing controlled, vassal-controlled, lost, and neutral/unknown core territory.
- [x] Add tooltip lines for both map modes.
- [x] Add AW3 tab icon buttons.

### Task 4: Mandate Dynasty Window

**Files:**
- Create: `Code/ui/windows/MandateDynastyWindow.cs`
- Modify: `Code/ui/AW_LineageWindowIds.cs`
- Modify: `Code/ui/AW_LineageTab.cs`

- [x] Add a compact AW3-style window with current emperor, dynasty, era name, Mandate value, imperial authority, legal-core control, vassal count, crisis status, and recent Mandate events.
- [x] Provide a button to open the window from the AW3 tab.

### Task 5: Localization And Documentation

**Files:**
- Create: `Locales/aw3_mandate.csv`
- Modify: `Locales/war.csv`
- Modify: `README.md`
- Modify: `docs/AW3_Roadmap.md`

- [x] Add zh/en/ch keys for buttons, tooltips, map modes, war types, Mandate status, and event labels.
- [x] Document the implemented Mandate layer and remaining future work.

### Task 6: Verification

**Files:**
- No new files.

- [x] Run `$env:DOTNET_ROLL_FORWARD='Major'; dotnet build`.
- [x] Fix compile errors until build exits with `0 warnings, 0 errors`.
- [x] Review `git diff --stat` and touched files.

# AW3 Mandate Stability Pass Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stabilize the current Mandate of Heaven, succession, mandate UI, localization, and ancestry display pass before expanding the next mandate war layer.

**Architecture:** Keep gameplay decisions in focused rule/service classes and keep UI on the existing original-style list-window pattern. Use cached mandate/core state for map and yearly work, and keep old saved internal keys readable through display mappers.

**Tech Stack:** C#/.NET 4.8, Harmony, NeoModLoader, WorldBox 0.51.2, SQLite archive tables, existing AW3 test projects.

---

### Task 1: Localized Labels And Missing Keys

**Files:**
- Modify: `Code/core/lineage/WarDecisionService.cs`
- Modify: `Code/core/lineage/WarTerritoryService.cs`
- Modify: `Code/ui/windows/HistoryListWindow.cs`
- Modify: `Code/ui/windows/MandateDynastyWindow.cs`
- Modify: `Locales/war.csv`
- Modify: `Locales/aw3_war_decisions.csv`

- [ ] Add a single display mapper for war reason and project keys, including old saved keys such as `weak_claim_decision`, `vassal_war`, `core_reclaim`, `claim_war`, `force_vassal`, `tianming`, and `tianmingrebel`.
- [ ] Replace raw event type/reason display in history and mandate tooltips with the mapper.
- [ ] Add missing locale rows, including `aw_war_fabricate_core_desc`.
- [ ] Run the existing war fabrication tests and confirm no regressions.

### Task 2: Mandate Nameplate Marker

**Files:**
- Modify: `Code/core/policy/MandateMapMarkerService.cs`
- Modify: `Code/patch/AW_MandateMapModePatch.cs`

- [ ] Stop using `NameplateText.showSpecial()` for the mandate marker.
- [ ] Reflect `_icon_species` and `_show_icon_species` on `NameplateText`.
- [ ] For the current mandate kingdom, replace the normal species/base icon sprite with the mandate marker sprite.
- [ ] For non-mandate kingdoms, leave the original icon from `showTextKingdom` untouched.
- [ ] Keep rebel and pseudo markers only if they can use the same base-icon replacement safely.

### Task 3: Direct Royal Succession

**Files:**
- Modify: `Code/core/lineage/HeirService.cs`
- Modify: `Code/core/lineage/LineageBranchRules.cs`
- Modify: `Code/core/lineage/LineageService.cs`
- Test: `Tests/WarFabricationRuleTests/Program.cs` or a new focused test project if existing references allow it.

- [x] Split heir eligibility into adult direct sons, recallable adult direct sons, underage direct sons, and collateral candidates.
- [x] Let direct sons remain preferred even if they hold city leader, army leader, or general roles; mark them for recall before succession.
- [x] If no adult direct son exists, allow an underage male direct son as a regency heir.
- [x] Add a branch rule that direct father-to-son succession must not create a new shi branch.
- [ ] Future design item: add collateral restoration succession, where a mandate dynasty can restore inheritance from an older main shi line when the current branch lacks suitable direct heirs, similar to Southern Song returning from the Taizong line to the Taizu line; record it as a lineage-restoration succession event instead of a new dynasty.
- [ ] Preserve existing exclusion rules for madness, dead actors, and current kings.

### Task 4: Mandate Legal Core Sync And Stability Rules

**Files:**
- Modify: `Code/core/lineage/MandateService.cs`
- Modify: `Code/core/lineage/WarTerritoryService.cs`
- Test: `Tests/WarFabricationRuleTests/Program.cs` or `Tests/MandateRulerTitleRuleTests/Program.cs`

- [ ] Add an event-style method `MandateService.OnKingdomCoreCreated(Kingdom, City, string)` that adds the city to the active mandate legal core set only when the kingdom is the active mandate kingdom.
- [ ] Call it after `WarTerritoryService.EnsureCore()` successfully inserts a new kingdom core.
- [ ] Update legal core count and dirty mandate maps only on actual new core insertions.
- [ ] Prevent non-mandate wars from clearing mandate state.
- [ ] Keep historical figure/`first` protection for low-mandate collapse and avoid clearing mandate without a proper mandate-war result.

### Task 5: Mandate Ruler Titles

**Files:**
- Modify: `Code/core/lineage/MandateRulerTitleDefs.cs`
- Modify: `Code/core/lineage/MandateRulerTitleRules.cs`
- Modify: `Code/core/lineage/MandateRulerTitleService.cs`
- Test: `Tests/MandateRulerTitleRuleTests/Program.cs`

- [ ] Replace mojibake title strings with correct Chinese temple names and double posthumous titles.
- [ ] Restrict temple names to mandate rulers only.
- [ ] Make `世宗` rare instead of the default high-reform result.
- [ ] Use stored reign metrics and existing-title guard so a reign title is decided once.
- [ ] Verify founder, low-origin founder, conquest founder, reform successor, and collapse cases with tests.

### Task 6: Mandate Dynasty Window As Original-Style List

**Files:**
- Modify: `Code/ui/windows/MandateDynastyWindow.cs`
- Modify: `Code/ui/windows/WarDecisionTargetWindow.cs`
- Reuse: `Code/ui/items/HistoryListItem.cs`
- Reuse: `Code/ui/items/WarDecisionTargetListItem.cs`
- Reuse: `Code/ui/windows/HistoryListWindow.cs`

- [ ] Convert `MandateDynastyWindow` from manual `AbstractWindow` panel construction to the existing list-window row flow where practical.
- [ ] Keep top rows compact: current mandate status and current mandate decision slot.
- [ ] Display mandate periods as dynasty headers, reigns as nested headers, and events as compact event rows.
- [ ] Default collapsed, with tooltip details and click-to-jump targets.
- [ ] Remove custom scrollbar sizing that caused misplaced scrollbars.
- [x] Convert `WarDecisionTargetWindow` to the same original-style scroll/list-window pattern so war targets, claim/core projects, restoration targets, and no-CB options scroll reliably inside the normal WorldBox window frame.
- [x] Keep only the side-list war target controls; remove duplicate target controls from the policy window body.
- [x] Use compact rows with icon, colored target kingdom/city name, war reason, war goal, and tooltip details. The row tooltip must show the selected target and execution reason that will be consumed by the national decision queue.
- [x] Default the target list to a stable sorted order: reclaim core, press claim, restoration, vassal, independence, no-CB. Disabled rows stay visible only when they explain a useful missing prerequisite; otherwise hide them to keep the window clean.

### Task 7: Noble Ancestors In Ancestry Analysis

**Files:**
- Modify: `Code/core/lineage/LineageDTO.cs`
- Modify: `Code/core/lineage/AncestryAnalysisService.cs`
- Modify: `Code/ui/windows/AncestryAnalysisWindow.cs`
- Modify: `Code/ui/items/AncestryListItem.cs` only if row height needs adjustment.
- Modify: `Locales/aw3_ancestry_mapmode.csv`

- [x] Add `NobleAncestorContribution` rows to `AncestryReport`.
- [x] Search both parent branches up to the existing depth and keep all meaningful noble ancestors instead of only the first evidence row.
- [x] Compute approximate contribution percent from generational distance.
- [x] Display rows like `开封姬氏 25.0% 姬某 周文王` when city, clan, name, and title are available.
- [x] Keep existing molecular-style species/subspecies rows separate.

### Task 8: Verification

**Files:**
- Existing tests under `Tests/`

- [x] Run `dotnet run --project Tests/WarFabricationRuleTests/WarFabricationRuleTests.csproj`.
- [x] Run `dotnet run --project Tests/MandateRulerTitleRuleTests/MandateRulerTitleRuleTests.csproj`.
- [x] Run `dotnet build AncientWarfare3.csproj`.
- [ ] Re-read `Player.log` for missing localization keys and new NRE stacks after the user tests in game.

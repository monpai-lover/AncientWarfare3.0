# Custom Court Navigation, Runtime Defaults, And Viewport Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore editor back navigation, make the built-in civil and military local-government templates active without a custom-court instance, keep workflow nodes in view, remove zoom drift, shift the workflow left by 30 pixels, and complete editor tooltips.

**Architecture:** Keep data rules in `core/court` and UI navigation/viewport behavior in the existing Unity components. Runtime template resolution receives an explicit built-in fallback catalog; the workflow window recenters its workspace only when the active graph changes, while `TreeDragPanHandler` preserves the pointer's world point during zoom.

**Tech Stack:** C#, Unity UI, WorldBox/NeoModLoader APIs, executable .NET rule tests and source guards.

---

### Task 1: Lock The Regressions

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomLocalGovernmentRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`

- [ ] Add assertions that the built-in local catalog contains four-seat civil and military templates and source guards requiring a visible return button, `-530f` root offset, graph focusing, pointer-anchored zoom, and tooltip binding for every toolbar control.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --configuration Release -- --custom-local-government` and confirm failure because the fallback catalog does not exist.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --configuration Release -- --custom-court-multiplayer` and confirm failure on the missing UI behavior.

### Task 2: Activate Built-In Local Governments

**Files:**
- Modify: `Code/core/court/CustomLocalGovernmentPresetRules.cs`
- Modify: `Code/core/court/CustomCourtRuntime.cs`

- [ ] Add a fresh built-in template catalog factory so callers never mutate shared template instances.
- [ ] Make local-template resolution use the applied custom snapshot when present and the built-in catalog otherwise; preserve automatic civil/military classification and persisted manual bindings only when the selected template exists.
- [ ] Run the local-government test target and confirm the new fallback tests pass.

### Task 3: Restore Navigation And Stable Viewport Behavior

**Files:**
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`
- Modify: `Code/ui/items/TreeDragPanHandler.cs`

- [ ] Create an icon-only return button immediately left of `CloseBackground`; return to `CourtWindow.OpenCity` for local entry and `CourtWindow.OpenAndRefresh` for realm entry.
- [ ] Change the workflow root offset from `-500f` to `-530f` without changing window dimensions.
- [ ] Recenter and reset scale around the active offices' bounds after initial load, context changes, local-template changes, import, and whole-preset replacement.
- [ ] Update wheel zoom so the content point under the pointer remains under the pointer after scaling.
- [ ] Run the custom-court source guard and confirm navigation and viewport requirements pass.

### Task 4: Complete Editor Tooltips

**Files:**
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`
- Modify: `Code/ui/components/CourtWorkflowVacancyCard.cs`
- Modify: `locales/aw3_court.csv`

- [ ] Bind descriptions to context/template/default/replacement/import selectors, both name inputs, create/duplicate/delete/save/export/apply commands, and the return button.
- [ ] Add a localized tooltip to the vacancy-card delete command while preserving the existing settings tooltip.
- [ ] Add Chinese, English, and traditional-Chinese localization rows for every new tooltip key.
- [ ] Run localization/source guards and confirm every key resolves.

### Task 5: Verify, Deploy, And Integrate

**Files:**
- Verify all files above.

- [ ] Run the full rules executable, both targeted test modes, the production `AncientWarfare3.csproj` build, and `git diff --check`.
- [ ] Commit the isolated branch, merge it into `master` without touching unrelated dirty files, and deploy from the clean worktree.
- [ ] Launch or reuse WorldBox, inspect `Player.log` for compile/Harmony/runtime errors, then push `master` after runtime verification.

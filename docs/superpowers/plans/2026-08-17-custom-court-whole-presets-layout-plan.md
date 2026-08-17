# Custom Court Whole Presets And Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add profile-backed whole-court presets to the custom court editor and move the canvas and toolbar upward by exactly 50 pixels.

**Architecture:** A Unity-independent rules unit exposes compatible institution choices, unlock state, and deterministic replacement-template generation from `ICourtProfile`. `CustomCourtWorkflowWindow` adapts those rules to the active kingdom, localization, dropdown callbacks, and card rendering without duplicating built-in court data.

**Tech Stack:** C# 11/net48 mod code, net9.0 console rules tests, Unity UI, existing `AWStringDropdown`, CSV localization.

---

### Task 1: Add failing whole-preset rules tests

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWholePresetRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write focused tests for option sets and unlock ordering**

Add assertions that Xia exposes Zhou/Han/Tang/Song, Western exposes bureaucratic/feudal-bureaucratic, and Han unlocks only Zhou/Han.

- [ ] **Step 2: Write focused tests for replacement behavior**

Use a small test profile to assert preservation of template ID/revision/name, replacement of offices and edges, field copying, deterministic duplicate normalization, empty-preset rejection, and deterministic center-near layout.

- [ ] **Step 3: Register the tests and production source in the test project**

Add compile entries for the new test and `Code/core/court/CustomCourtWholePresetRules.cs`, then invoke the tests from `--custom-court-multiplayer`.

- [ ] **Step 4: Run the focused test command and verify RED**

Run:

```powershell
dotnet build .\Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: compilation fails because `CustomCourtWholePresetRules` does not exist.

### Task 2: Implement the whole-preset rules unit

**Files:**
- Create: `Code/core/court/CustomCourtWholePresetRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWholePresetRulesTests.cs.txt`

- [ ] **Step 1: Add profile-compatible institution choices**

Return Xia choices in Zhou/Han/Tang/Song order and Western choices in bureaucratic/feudal-bureaucratic order. Mark an option unlocked when its rank is at most the current institution rank.

- [ ] **Step 2: Add deterministic template replacement**

Resolve office IDs through the supplied profile, discard duplicates and missing definitions, copy definition fields into `CustomCourtOffice`, clear edges, preserve identity/name fields, and place cards in deterministic layer/grade rows around a supplied center.

- [ ] **Step 3: Run the focused tests and verify GREEN**

Run:

```powershell
dotnet build .\Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
dotnet .\Tests\AncientWarfare3.Rules.Tests\bin\Release\net9.0\AncientWarfare3.Rules.Tests.dll --custom-court-multiplayer
```

Expected: build succeeds and the focused command prints `Custom court multiplayer rules tests passed.`

### Task 3: Add failing workflow source guards

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`

- [ ] **Step 1: Change exact layout guards**

Require `_toolPanel.anchoredPosition = new Vector2(-864f, 46f)` and `_canvasRect.anchoredPosition = new Vector2(-480f, 50f)` while retaining unchanged X values and dimensions.

- [ ] **Step 2: Add whole-preset integration guards**

Require `_wholePresetDropdown`, option refresh, enabled replacement callback, unavailable callback, graph-selection clearing, court-name preservation, and localized whole-preset keys.

- [ ] **Step 3: Run the focused source guards and verify RED**

Run:

```powershell
dotnet .\Tests\AncientWarfare3.Rules.Tests\bin\Release\net9.0\AncientWarfare3.Rules.Tests.dll --custom-court-multiplayer
```

Expected: failure identifies the missing dropdown/integration and old `-4`/`0` Y positions.

### Task 4: Integrate the whole-preset dropdown and layout shift

**Files:**
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`

- [ ] **Step 1: Create and lay out the dropdown**

Place a localized whole-preset label and `AWStringDropdown` below the court-name input. Shift office-name and all following toolbar controls down by 26 pixels.

- [ ] **Step 2: Populate options from the active kingdom profile**

Resolve the kingdom, call `CourtProfileRegistry.For(kingdom)` and `CourtInstitutionService.GetInstitution(kingdom)`, localize institution labels, and expose locked options with a localized requirement message.

- [ ] **Step 3: Replace the graph on enabled selection**

Preserve the latest court-name input value, generate the replacement around the canvas center, clear `_edgeSource`, `_edgeTarget`, and office-name input, rerender cards, and show the localized loaded message.

- [ ] **Step 4: Reject unavailable or empty presets without mutation**

Show localized locked/unavailable status and keep the existing template untouched.

- [ ] **Step 5: Apply the exact vertical shift**

Set the tool panel Y to `46f` and canvas Y to `50f`; leave X positions, sizes, and window sizing unchanged.

### Task 5: Add localization and verify integration

**Files:**
- Modify: `Locales/aw3_court.csv`

- [ ] **Step 1: Add Simplified Chinese, English, and Traditional Chinese strings**

Add keys for the whole-preset label, empty selection caption, locked state and requirement, successful replacement, unavailable profile, and empty preset.

- [ ] **Step 2: Rebuild and run custom-court tests**

Run:

```powershell
dotnet build .\Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
dotnet .\Tests\AncientWarfare3.Rules.Tests\bin\Release\net9.0\AncientWarfare3.Rules.Tests.dll --custom-court-template
dotnet .\Tests\AncientWarfare3.Rules.Tests\bin\Release\net9.0\AncientWarfare3.Rules.Tests.dll --custom-court-effect
dotnet .\Tests\AncientWarfare3.Rules.Tests\bin\Release\net9.0\AncientWarfare3.Rules.Tests.dll --custom-court-multiplayer
```

Expected: all commands exit zero.

- [ ] **Step 3: Build the mod and inspect the diff**

Run:

```powershell
dotnet build .\AncientWarfare3.csproj -c Release --no-restore
git diff --check
git status --short
```

Expected: build exits zero, diff check is clean, and only planned files are modified.

### Task 6: Commit, integrate, deploy, and push

**Files:**
- Modify: Git history and deployed mod source only.

- [ ] **Step 1: Commit feature work on the isolated branch**

Stage only the planned code, tests, localization, and forced-added plan; commit with a focused feature message.

- [ ] **Step 2: Rebase onto current master**

Fetch the current master tip locally and rebase the feature branch, resolving only changes in this feature's files.

- [ ] **Step 3: Fast-forward master without disturbing its dirty worktree**

Use Git plumbing/ref update only after confirming master has not diverged and the main worktree's unrelated modifications remain unstaged and unchanged.

- [ ] **Step 4: Deploy and verify the deployed source**

Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy-local.ps1` from the main worktree, then compare the planned source/localization files against `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`.

- [ ] **Step 5: Push `origin/master`**

Push only after the merged build and deployment verification succeed.

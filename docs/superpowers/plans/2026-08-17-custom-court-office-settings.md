# Custom Court Office Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a court-styled office settings popup and make its five preset effects alter live AW3 systems when the office has a living incumbent.

**Architecture:** The editor opens a dedicated settings window with a cloned office draft, then copies validated changes back on confirmation. Pure modifier rules separate flat, percentage, and multiplier composition; a runtime facade resolves active occupied custom offices and supplies identity-safe modifiers to economy, abstract battle, and court influence consumers.

**Tech Stack:** C# 10, .NET Framework 4.8, Unity UI, NeoModLoader window APIs, Newtonsoft.Json, existing AW3 rule-test harness.

---

## File Structure

- Create `Code/core/court/CustomCourtOfficeSettingsRules.cs` for clone,
  effect-row normalization, enum cycling, and settings validation.
- Create `Code/core/court/CustomCourtRuntimeEffectService.cs` for occupied
  office lookup and runtime modifier queries.
- Create `Code/ui/windows/CustomCourtOfficeSettingsWindow.cs` for the two-tab
  settings popup.
- Modify `Code/ui/components/CourtWorkflowVacancyCard.cs` to add the
  upper-left settings button and callback.
- Modify `Code/ui/windows/CustomCourtWorkflowWindow.cs` to open the settings
  popup and refresh the edited card.
- Modify `Code/ui/AW_LineageWindowIds.cs` with the new window ID.
- Modify `Code/core/court/CustomCourtEffectRules.cs` and
  `CustomCourtEffectService.cs` with correct mode-aware modifiers.
- Modify `Code/core/policy/CityEconomyService.cs` for tax, food, and order.
- Modify `Code/core/lineage/ArmyAbstractBattleModels.cs`,
  `ArmyAbstractBattleRules.cs`, and `ArmyAbstractBattleService.cs` for
  morale-adjusted abstract strength.
- Modify `Code/core/court/CourtDirectionService.cs` for office-holder court
  influence.
- Modify `Locales/aw3_court.csv` and focused test sources.

### Task 1: Pure Settings And Effect Modifier Rules

**Files:**
- Create: `Code/core/court/CustomCourtOfficeSettingsRules.cs`
- Modify: `Code/core/court/CustomCourtEffectRules.cs`
- Modify: `Code/core/court/CustomCourtEffectService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CustomCourtEffectRulesTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write failing tests**

Add tests proving that a settings draft is isolated from the original office,
duplicate effect IDs normalize to one row, and mode composition is correct:

```csharp
CustomCourtOffice draft = CustomCourtOfficeSettingsRules.CloneOffice(original);
draft.Grade = 20;
Equal(10, original.Grade, "draft edits do not mutate the card office");

CustomCourtEffectModifier modifier = CustomCourtEffectRules.Compose(new[] {
    Effect(CustomCourtEffectMode.AddFlat, 10f),
    Effect(CustomCourtEffectMode.AddPercent, 20f),
    Effect(CustomCourtEffectMode.Multiply, 1.5f)
});
Equal(198f, modifier.Apply(100f), "flat percent and multiply compose in order");
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project .\Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --custom-court-multiplayer
```

Expected: failure because `CustomCourtOfficeSettingsRules` and
`CustomCourtEffectModifier` do not exist. If the unrelated AWPathStep compile
block is still present, run a standalone source/rules harness and record that
block separately.

- [ ] **Step 3: Implement minimal pure rules**

Add:

```csharp
public readonly struct CustomCourtEffectModifier
{
    public float AdditiveFlat { get; }
    public float AdditivePercent { get; }
    public float MultiplicativeFactor { get; }
    public float Apply(float baseValue) { ... }
}
```

Implement `CloneOffice`, `NormalizeEffects`, `AllowedScopes`,
`NextLayer`, `NextSchool`, `NextScope`, `NextMode`, and validation
delegating to `CustomCourtTemplateRules.ValidateOffice`.

- [ ] **Step 4: Run focused rules and commit**

Expected: new pure tests pass without changing built-in court behavior.

Commit intended files with:

```powershell
git add Code/core/court/CustomCourtOfficeSettingsRules.cs Code/core/court/CustomCourtEffectRules.cs Code/core/court/CustomCourtEffectService.cs Tests/AncientWarfare3.Rules.Tests/CustomCourtEffectRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "feat: add custom court office setting rules"
```

### Task 2: Card Settings Entry And Popup Window

**Files:**
- Create: `Code/ui/windows/CustomCourtOfficeSettingsWindow.cs`
- Modify: `Code/ui/components/CourtWorkflowVacancyCard.cs`
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`
- Modify: `Code/ui/AW_LineageWindowIds.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`
- Modify: `Locales/aw3_court.csv`

- [ ] **Step 1: Add failing source contracts**

Require the following source tokens:

```text
SettingsButton
CustomCourtOfficeSettingsWindow.Open(
BaseAndRequirementsTab
FunctionalEffectsTab
ConfirmOfficeSettings
aw_custom_court_office_settings
```

Run the source guard and confirm it fails on `SettingsButton`.

- [ ] **Step 2: Add the upper-left settings button**

Extend `CourtWorkflowVacancyCard.Create` with an
`Action<CourtWorkflowVacancyCard> settingsRequested` callback. Create an
18 by 18 upper-left button with the existing court button style, settings icon,
fallback text, and tooltip. Keep selection handling on the card background and
stop the settings/delete buttons from selecting or dragging the card.

- [ ] **Step 3: Build the two-tab settings window**

Create an `AbstractWindow<CustomCourtOfficeSettingsWindow>` with:

```csharp
public static void Open(CustomCourtTemplate template,
    CustomCourtOffice office, Action<CustomCourtOffice> confirmed)
```

The window clones the office, renders base/appointment controls on the first
tab, renders five fixed effect rows on the second tab, validates on Confirm,
and invokes the callback only after successful validation. Cancel and window
close discard the draft.

- [ ] **Step 4: Wire editor refresh and localization**

Open the popup from `CustomCourtWorkflowWindow`. On confirmation, copy the
validated draft into the selected office without replacing its stable ID or
layout, call `card.RefreshText()`, and refresh edges/selection. Add simplified
Chinese, English, and traditional Chinese rows for both tabs, fields, effects,
modes, scopes, validation errors, Confirm, and Cancel.

- [ ] **Step 5: Build and commit**

Run the source guard and Release build. Expected: source contracts pass and
build reports zero errors.

Commit intended UI and locale files with:

```powershell
git add Code/ui/windows/CustomCourtOfficeSettingsWindow.cs Code/ui/components/CourtWorkflowVacancyCard.cs Code/ui/windows/CustomCourtWorkflowWindow.cs Code/ui/AW_LineageWindowIds.cs Locales/aw3_court.csv Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt
git commit -m "feat: add custom court office settings window"
```

### Task 3: Occupied Office Runtime Effect Facade

**Files:**
- Create: `Code/core/court/CustomCourtRuntimeEffectService.cs`
- Modify: `Code/core/court/CustomCourtRuntime.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CustomCourtEffectRulesTests.cs.txt`

- [ ] **Step 1: Write failing occupancy tests**

Add pure classification tests proving that effects require a current, living,
matching incumbent and that no custom instance returns the identity modifier.

- [ ] **Step 2: Implement runtime lookup**

Read the resolved snapshot and `CourtService.GetActiveOfficers`, index rows by
office ID, verify the actor is alive and its runtime kingdom/office keys match,
then aggregate only qualifying offices. Cache per kingdom and year plus a dirty
revision keyed by custom instance revision; return identity values on stale
runtime data.

- [ ] **Step 3: Mark the cache dirty on court mutations**

Invalidate runtime effects after template apply, appointment, replacement,
dismissal, death cleanup, and kingdom destruction. Mark
`CourtDirectionService` and city economy refresh dirty when the resolved
effect set changes.

- [ ] **Step 4: Verify and commit**

Run focused effect tests and the Release build.

Commit with:

```powershell
git add Code/core/court/CustomCourtRuntimeEffectService.cs Code/core/court/CustomCourtRuntime.cs Code/core/court/CourtService.cs Tests/AncientWarfare3.Rules.Tests/CustomCourtEffectRulesTests.cs.txt
git commit -m "feat: resolve occupied custom court effects"
```

### Task 4: Economy And Court Influence Consumers

**Files:**
- Modify: `Code/core/policy/CityEconomyService.cs`
- Modify: `Code/core/court/CourtDirectionService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CustomCourtEffectRulesTests.cs.txt`

- [ ] **Step 1: Write failing transformation tests**

Cover:

```csharp
Equal(132f, taxModifier.Apply(100f), "tax modifier changes city tax");
Equal(80f, CustomCourtEffectRules.ApplyCivilOrder(25f, orderModifier),
    "civil order transforms order then returns unrest");
Equal(6f, influenceModifier.Apply(4f),
    "office holder changes court influence weight");
```

- [ ] **Step 2: Integrate city economy**

Read tax, food, and civil-order modifiers once per kingdom annual update.
Apply tax and food after the existing built-in policy/institution calculations.
Apply civil order to `100 - contribution.UnrestRisk`, clamp to 0 through 100,
and convert back to unrest risk. Preserve identity behavior for built-in courts.

- [ ] **Step 3: Integrate court influence**

When `CourtDirectionService` builds an active custom office holder's school
contribution, read that office's court-influence modifier and apply it to the
base weight before ministerial multiplication. Do not apply one office's
influence to unrelated officers.

- [ ] **Step 4: Verify and commit**

Run focused rules, city economy tests, and Release build.

Commit with:

```powershell
git add Code/core/policy/CityEconomyService.cs Code/core/court/CourtDirectionService.cs Tests/AncientWarfare3.Rules.Tests/CustomCourtEffectRulesTests.cs.txt
git commit -m "feat: apply custom court economic effects"
```

### Task 5: Abstract Army Morale Consumer

**Files:**
- Modify: `Code/core/lineage/ArmyAbstractBattleModels.cs`
- Modify: `Code/core/lineage/ArmyAbstractBattleRules.cs`
- Modify: `Code/core/lineage/ArmyAbstractBattleService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/ArmyAbstractBattleRulesTests.cs.txt`

- [ ] **Step 1: Write failing morale tests**

Add a participant morale field and prove that equal armies resolve different
aggregate strength when one side has a positive custom-court modifier. Also
include morale in participant hashing so deterministic battle seeds change
with authoritative strength facts.

- [ ] **Step 2: Implement morale-adjusted strength**

Add an immutable morale modifier value to
`ArmyAbstractBattleParticipant`. Extend `AdjustedCardValue` so morale
modifies unit strength before commander strength. Populate the value from the
participant kingdom through `CustomCourtRuntimeEffectService` for both
attackers and defenders.

- [ ] **Step 3: Verify and commit**

Run abstract battle tests and Release build. Existing battles with identity
morale must produce their previous values and hashes.

Commit with:

```powershell
git add Code/core/lineage/ArmyAbstractBattleModels.cs Code/core/lineage/ArmyAbstractBattleRules.cs Code/core/lineage/ArmyAbstractBattleService.cs Tests/AncientWarfare3.Rules.Tests/ArmyAbstractBattleRulesTests.cs.txt
git commit -m "feat: apply custom court morale to abstract battles"
```

### Task 6: Full Verification And Deployment

- [ ] **Step 1: Run focused checks**

Run custom court source contracts, effect rules, economy rules, and abstract
battle rules. Record any unrelated AWPathStep test compile block separately.

- [ ] **Step 2: Build production**

Run:

```powershell
dotnet build .\AncientWarfare3.csproj -c Release --no-restore
```

Expected: zero warnings and zero errors.

- [ ] **Step 3: Deploy source and DLL**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\deploy-local.ps1
Copy-Item .\bin\Release\net48\AncientWarfare3.dll D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0\bin\Release\net48\AncientWarfare3.dll -Force
```

- [ ] **Step 4: Verify deployment**

Compare SHA-256 hashes for the settings window, vacancy card, runtime effect
service, localization file, and Release DLL between the repository and Mods
deployment. Do not claim completion unless every hash matches.


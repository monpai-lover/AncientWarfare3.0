# School Map Bottom Bar And Capital Safety Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Select real cities from School MapMode, show their complete school composition in a dedicated bottom tab, make influence labels reliably visible, localize the heir-seeking trait, and reject foreign-border capital candidates without changing the rest of AW3's capital algorithm.

**Architecture:** Keep the fixed school system separate from vanilla Religion. School MapMode routes zone and nameplate clicks to a real `City`, while a dedicated `selected_aw_school_city` tab owns the composition element and leaves vanilla `selected_city` untouched. Capital eligibility is centralized in one runtime service backed by a pure rule predicate so policy execution and AI cannot disagree.

**Tech Stack:** C# 11, .NET Framework 4.8, Unity UI, WorldBox publicized API, Harmony, NeoModLoader, PowerShell, temporary .NET 9 rule harnesses.

---

### Task 1: Reject foreign-border capital candidates

**Files:**
- Modify: `Code/core/policy/CapitalMoveRules.cs`
- Create: `Code/core/policy/CapitalMoveCandidateService.cs`
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Modify: `Code/core/policy/KingdomPolicyAI.cs`
- Test: `F:/tmp/AW3CorrectnessRuleTests/Program.cs`
- Test: `F:/tmp/AW3CorrectnessRuleTests/AW3CorrectnessRuleTests.csproj`

- [ ] **Step 1: Write a failing source-level regression test**

First prove the current implementation has no foreign-border input:

```csharp
string capitalRulesSource = File.ReadAllText(Path.Combine(repoRoot, "Code", "core", "policy",
    "CapitalMoveRules.cs"));
Check(capitalRulesSource.Contains("pTouchesForeignBorder", StringComparison.Ordinal),
    "capital candidate rules must explicitly reject a foreign border");
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj
```

Expected: a normal assertion failure stating that the foreign-border rule is missing.

- [ ] **Step 3: Extend the pure predicate**

Change the signature to:

```csharp
public static bool CanConsiderCandidate(bool pCandidateAlive, bool pIsCurrentCapital,
    bool pIsCoreCity, bool pHasOwnNeighbor, bool pTouchesForeignBorder)
{
    return pCandidateAlive && !pIsCurrentCapital && pIsCoreCity &&
           pHasOwnNeighbor && !pTouchesForeignBorder;
}
```

After the API exists, add the two direct assertions for an inland candidate and a foreign-border candidate, then rerun the harness.

- [ ] **Step 4: Centralize runtime neighbor inspection**

Create `CapitalMoveCandidateService.CanConsider(City, Kingdom, City)` and `TouchesForeignBorder(City, Kingdom)`. Count a neighbor as foreign only when it is alive, has a living non-neutral kingdom, and `neighbor.kingdom != pKingdom`.

- [ ] **Step 5: Replace both duplicated candidate checks**

In `KingdomPolicyService.FindNewCapital` and `KingdomPolicyAI.HasClearlyBetterCapital`, replace direct `CapitalMoveRules.CanConsiderCandidate(...)` calls with:

```csharp
if (!CapitalMoveCandidateService.CanConsider(city, pKingdom, current)) continue;
```

Do not change `ScoreCity`, `ShouldMoveCapital`, peace checks, decision costs, or the 30-year AI cooldown.

- [ ] **Step 6: Run the focused rules and commit**

Run the correctness harness and expect `direct-son rules passed`, then commit only the capital files:

```powershell
git add Code/core/policy/CapitalMoveRules.cs Code/core/policy/CapitalMoveCandidateService.cs Code/core/policy/KingdomPolicyService.cs Code/core/policy/KingdomPolicyAI.cs
git commit -m "fix: reject foreign-border capitals"
```

### Task 2: Route School MapMode selection to real cities

**Files:**
- Modify: `Code/core/policy/SchoolMapModeService.cs`
- Modify: `Code/core/policy/AWMapModeMetaLibrary.cs`
- Modify: `Code/content/GodPowerLibrary.cs`
- Test: `F:/tmp/AW3CorrectnessRuleTests/Program.cs`

- [ ] **Step 1: Write failing source-integration assertions**

Require `SchoolMapModeService.SelectCity`, `prepareNext(pAsset, city)`, and a school-specific city configuration method in `AWMapModeMetaLibrary`. Also reject `SchoolAsset` being left with the generic selected-kingdom action.

- [ ] **Step 2: Run the test and verify RED**

Run the correctness harness. Expected: failure stating that School MapMode still selects a school pseudo-meta as a kingdom.

- [ ] **Step 3: Add real-city selection**

Implement:

```csharp
public static bool SelectCity(WorldTile pTile, string pPowerId = null)
{
    City city = pTile?.zone?.city;
    if (city?.data == null || city.isRekt()) return false;
    SelectedUnit.clear();
    SelectedMetas.selected_city = city;
    SelectedObjects.setNanoObject(city);
    SchoolMapBottomBarController.Show(city);
    return true;
}
```

- [ ] **Step 4: Configure the school meta for city selection**

After creating `SchoolAsset`, set its list/get/selected/history delegates to cities, set `power_tab_id = SchoolMapBottomBarController.TabId`, and route both `click_action_zone` and `selected_tab_action_meta` through the same `SelectCity(City)` overload. Preserve the school tile identity getters used for coloring.

- [ ] **Step 5: Make school nameplates click real cities**

In `DrawSchoolNameplates`, call `prepareNext(pAsset, city)` while continuing to call `setupMeta` with the school pseudo-meta for school color and display text.

- [ ] **Step 6: Run the focused rule test**

Expected: the School MapMode source assertions pass and existing nameplate assertions remain green.

### Task 3: Add the dedicated school-city bottom tab

**Files:**
- Create: `Code/ui/items/SchoolCompositionElement.cs`
- Create: `Code/core/policy/SchoolMapBottomBarController.cs`
- Modify: `Code/core/policy/SchoolMapModeService.cs`
- Modify: `Locales/aw3_school.csv`
- Test: `F:/tmp/AW3CorrectnessRuleTests/Program.cs`

- [ ] **Step 1: Write a failing source-integration test**

Require the `selected_aw_school_city` tab asset, an `element_school_composition` object, school icon loading through `definition.IconPath`, raw score and percentage text through `SchoolInfluenceLabelRules.Build`, and a details action calling `SchoolWindow.OpenCity`. Reject references that attach the element to `tab_selected_city` or call `showTabSelectedMeta(MetaTypeLibrary.city)`.

- [ ] **Step 2: Run the test and verify RED**

Expected: failure because the composition element is still attached to vanilla `selected_city` and the dedicated tab is not registered.

- [ ] **Step 3: Build the dedicated tab and composition element**

Register a `PowerTabAsset` named `selected_aw_school_city`, create a sibling `PowersTab` beside `tab_selected_city`, and make one pooled composition element its only content child. Wait until `PowersTab.Start()` has resolved its asset before showing it. Render in the first viewport:

```text
[dominant icon] City / Dominant School | [icon School score percent]... | [Details]
```

Order non-zero schools by score descending and registry order. Reuse up to fourteen cells; hide unused cells rather than destroying them.

- [ ] **Step 4: Add lifecycle control**

`SchoolMapBottomBarController.Show(city)` creates/binds the dedicated tab lazily and requests it after initialization. `ProcessFrame` hides it whenever School MapMode is inactive, the selected object is not the same city, or the city is destroyed. Refresh only when the city snapshot generation changes. If the custom tab is current when School MapMode exits, restore the main toolbar.

- [ ] **Step 5: Add localization**

Add complete `cz,en,ch` rows for the composition heading, dominant school, raw influence, and details action.

- [ ] **Step 6: Run tests and commit the School MapMode batch**

Run the correctness harness and commit the School MapMode service, meta, nameplate, controller, element, and locale files with:

```powershell
git commit -m "fix: show city schools in map bottom bar"
```

### Task 4: Guarantee influence-label visibility and localize heir seeking

**Files:**
- Modify: `Code/ui/items/SchoolInfluenceBar.cs`
- Modify: `Locales/trait.csv`
- Test: `F:/tmp/AW3CorrectnessRuleTests/Program.cs`

- [ ] **Step 1: Write failing assertions**

Require the influence label object to own a nested `Canvas` with `overrideSorting = true`, and require both `trait_aw_heir_urge` locale rows.

- [ ] **Step 2: Run the test and verify RED**

Expected: failure because the Canvas override and trait locale keys are missing.

- [ ] **Step 3: Put the label in a higher render layer**

In `SchoolInfluenceBar.Build`, add a Canvas to the label object:

```csharp
Canvas labelCanvas = labelObject.AddComponent<Canvas>();
labelCanvas.overrideSorting = true;
labelCanvas.sortingOrder = 100;
```

Keep the outline, white text, best-fit settings, explicit active state, and `SetAsLastSibling` defense.

- [ ] **Step 4: Add the trait translations**

Add:

```csv
trait_aw_heir_urge,求嗣,Heir Seeking,求嗣
trait_aw_heir_urge_info,因王室暂无在世男性继承人而积极求嗣，大幅提高生育率；诞下儿子后移除,Seeks a living male heir with greatly increased fertility until a son is born,因王室暫無在世男性繼承人而積極求嗣，大幅提高生育率；誕下兒子後移除
```

- [ ] **Step 5: Verify locale integrity and commit**

Use `Import-Csv -Encoding UTF8` to reject duplicate or incomplete rows, run the correctness harness, then commit the bar and locale files.

### Task 5: Full verification and live deployment

**Files:**
- Verify: all changed files
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`
- Inspect: `C:/Users/24908/AppData/LocalLow/mkarpenko/WorldBox/Player.log`

- [ ] **Step 1: Run all focused suites**

```powershell
dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj
dotnet run --project F:\tmp\AW3CourtLayoutRuleTests\AW3CourtLayoutRuleTests.csproj
dotnet run --project F:\tmp\AW3CourtExpansionRuleTests\AW3CourtExpansionRuleTests.csproj
```

Expected: all three success messages.

- [ ] **Step 2: Build both configurations**

```powershell
dotnet build AncientWarfare3.csproj -c Debug
dotnet build AncientWarfare3.csproj -c Release
```

Expected: zero warnings and zero errors in both builds.

- [ ] **Step 3: Check diffs and locale files**

Run `git diff --check`, verify only intended files are staged, and leave all user-deleted `Tests/` and `Verification/` paths untouched.

- [ ] **Step 4: Fast-forward the live mod checkout**

Stop the current visible WorldBox process, fetch the local master commit into the Steam mod checkout, and use `git merge --ff-only FETCH_HEAD`.

- [ ] **Step 5: Start WorldBox visibly and inspect startup**

Launch `worldbox.exe` without a hidden-window flag. Confirm a visible `WorldBox` main window and scan the current startup log for compilation errors, Harmony failures, null references, invalid casts, missing methods, and missing `trait_aw_heir_urge` localization.

- [ ] **Step 6: Visual acceptance**

Enter School MapMode, click a city zone and a school nameplate, and confirm both select the real city. Verify the bottom tab shows city/school icons and labeled composition, the detailed window bars display labels, and exiting the mode restores the normal city tab. Create a capital-choice scenario with an attractive border city and confirm it is rejected in favor of an eligible inland city.

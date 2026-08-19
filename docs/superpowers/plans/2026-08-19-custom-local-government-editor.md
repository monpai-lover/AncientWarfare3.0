# Custom Local Government Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make local-government editing a contextual sub-editor of one custom court, provide safe four-office civil and military defaults, resolve military templates from real city frontier status, and finish the toolbar layout with a permanent scrollbar.

**Architecture:** Keep `CustomCourtTemplate` and its existing instance/JSON pipeline as the only persistence boundary. Add pure rules/builders for default local templates, migration, and automatic city-role selection; keep Unity/world inspection in runtime adapters. Extend the existing shared workflow window with an entry context rather than creating another window or runtime instance.

**Tech Stack:** C#/.NET Framework 4.8, Unity UI (`RectTransform`, `ScrollRect`, `Scrollbar`), Newtonsoft JSON, SQLite-backed court/economy services, text-based rule/source guards.

---

### Task 1: Add failing pure rules for local template selection and four-office defaults

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/CustomLocalGovernmentRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing tests**

Add a `CustomLocalGovernmentRulesTests.Run()` suite that asserts:

```csharp
Equal(CustomLocalGovernmentDefaultKind.Military,
    CustomLocalGovernmentRules.SelectDefault(false, true, false));
Equal(CustomLocalGovernmentDefaultKind.Military,
    CustomLocalGovernmentRules.SelectDefault(false, false, true));
Equal(CustomLocalGovernmentDefaultKind.Civil,
    CustomLocalGovernmentRules.SelectDefault(false, false, false));
Equal(CustomLocalGovernmentDefaultKind.Manual,
    CustomLocalGovernmentRules.SelectDefault(true, true, true));

CustomLocalCourtTemplate civil =
    CustomLocalGovernmentPresetRules.CreateCivil("minzhou");
CustomLocalCourtTemplate military =
    CustomLocalGovernmentPresetRules.CreateMilitary("junfu");
Equal(4, civil.Offices.Count);
Equal(4, military.Offices.Count);
ContainsOffice(civil, "minzhou_governor");
ContainsOffice(civil, "minzhou_changshi");
ContainsOffice(civil, "minzhou_sihu");
ContainsOffice(civil, "minzhou_sicang");
ContainsOffice(military, "junfu_dudu");
ContainsOffice(military, "junfu_changshi");
ContainsOffice(military, "junfu_sima");
ContainsOffice(military, "junfu_canjun");
Equal(3, civil.Edges.Count);
Equal(3, military.Edges.Count);
```

Also assert `SelectDefault(true, ...)` never returns a non-manual default and
that missing preferred templates fall back to the first valid template.

- [ ] **Step 2: Register and run the focused test to prove RED**

Register the test in `Program.cs.txt` behind `--custom-local-government` and
run:

```powershell
dotnet msbuild Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -target:Compile -target:GenerateBuildDependencyFile -target:GenerateBuildRuntimeConfigurationFiles -target:CopyFilesToOutputDirectory
dotnet run --no-build --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --custom-local-government
```

Expected: compile failure because the new rule types do not exist.

### Task 2: Implement pure defaults, role rules, and idempotent migration

**Files:**
- Create: `Code/core/court/CustomLocalGovernmentRules.cs`
- Create: `Code/core/court/CustomLocalGovernmentPresetRules.cs`
- Modify: `Code/core/court/CustomLocalCourtTemplateRules.cs`
- Modify: `Code/core/court/CustomCourtTemplateJsonCodec.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CustomLocalGovernmentRulesTests.cs.txt`

- [ ] **Step 1: Add stable default-kind and city-role rules**

Implement `CustomLocalGovernmentDefaultKind { Manual, Civil, Military }` and
pure methods:

```csharp
public static CustomLocalGovernmentDefaultKind SelectDefault(
    bool manualBinding, bool hasForeignLandBorder,
    bool frontierMilitaryRole)
{
    if (manualBinding) return CustomLocalGovernmentDefaultKind.Manual;
    return hasForeignLandBorder || frontierMilitaryRole
        ? CustomLocalGovernmentDefaultKind.Military
        : CustomLocalGovernmentDefaultKind.Civil;
}
```

Keep diplomatic relations out of this rule: any live foreign land neighbour
counts as a frontier; water-only contact and dead/owner kingdoms are filtered
by the runtime adapter.

- [ ] **Step 2: Build the two four-office templates**

`CustomLocalGovernmentPresetRules.CreateCivil("minzhou")` must create
`州牧/长史/司户/司仓`; `CreateMilitary("junfu")` must create
`都督/长史/司马/参军`. Use city-layer offices, grades 10/20/30/30, stable
IDs, one root-to-three-child management edges, layouts matching existing
canvas coordinates, and existing valid effect enums/scopes. Mark military and
marshal-capable offices with `MilitaryCapable = true` and preserve the
existing default-kind enum used by serialized templates.

- [ ] **Step 3: Add pristine-template detection and migration**

Add `IsLegacyGeneratedSingleGovernor` that matches only the exact old
generated template: one `<id>_governor`, old localized name, grade 10, one
slot, city layer, center layout, empty requirements/effects, and no edges.
Upgrade only that shape. Add a missing `minzhou` or `junfu` default without
changing other templates. Preserve modified templates and city manual IDs.
Run migration twice and assert serialized output is identical after the first
upgrade.

- [ ] **Step 4: Make normalize/import use the same migration**

Call the migration from both `CustomCourtTemplateJsonCodec.Normalize` and
`TryImport` before validation. Do not create another `CustomCourtInstance` or
local JSON format.

- [ ] **Step 5: Run focused tests to prove GREEN**

Run the command from Task 1. Expected: `Custom local government rules passed.`

### Task 3: Resolve actual city border and military-role facts

**Files:**
- Create: `Code/core/court/CustomLocalGovernmentCityService.cs`
- Modify: `Code/core/court/CustomCourtRuntime.cs`
- Modify: `Code/core/policy/CityEconomyService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CustomLocalGovernmentRulesTests.cs.txt`

- [ ] **Step 1: Add a public economy-role query**

Expose `CityEconomyService.IsFrontierMilitary(Kingdom, City)` using the
existing stored city-economy role. Return false for missing, stale, dead, or
foreign city data; do not rescan all world cities.

- [ ] **Step 2: Add a bounded foreign-land-border adapter**

Implement `CustomLocalGovernmentCityService.HasForeignLandBorder(City,
Kingdom)` by inspecting the city's existing kingdom neighbours. Exclude null,
dead, neutral, owner, and water-only contacts; do not exclude allies,
vassals, or tributaries when they are live foreign territory.

- [ ] **Step 3: Resolve local templates with manual precedence**

Change `CustomCourtRuntime.TryGetLocalTemplate` to compute:

```csharp
bool military = CustomLocalGovernmentRules.SelectDefault(
    manual, HasForeignLandBorder(city, kingdom),
    CityEconomyService.IsFrontierMilitary(kingdom, city)) ==
    CustomLocalGovernmentDefaultKind.Military;
```

Keep manual bindings stable and write automatic IDs with the manual flag
false. A city that loses both facts must resolve back to `CivilDefault`.

- [ ] **Step 4: Add rule coverage**

Cover manual override, foreign border, `FrontierMilitary`, ordinary interior,
border-to-interior recovery, and missing preferred default fallback. Run the
focused test and production build.

### Task 4: Make local office effects actually apply to local incumbents

**Files:**
- Modify: `Code/core/court/CustomCourtRuntimeEffectService.cs`
- Modify: `Code/core/policy/CityEconomyService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/CustomCourtEffectRulesTests.cs.txt`

- [ ] **Step 1: Add a failing city-scoped effect assertion**

Create a local template with an active city office carrying `TaxIncome` or
`FoodProduction` at `City` scope and assert the city modifier includes it;
assert a vacant office contributes identity. Keep central-only aggregation
unchanged.

- [ ] **Step 2: Implement local incumbent filtering**

Add an overload accepting `City` that resolves the city's local template,
filters `CourtService.GetActiveOfficers` by `layer == City`, `city_id`, and
office ID, and composes only offices with living incumbents. Keep kingdom
central modifiers in the existing overload, then combine central and local
modifiers once per city in `CityEconomyService`.

- [ ] **Step 3: Keep military effects bounded**

Use only valid existing scopes. Local army/kingdom effects must be aggregated
through the existing capped modifier rules; do not multiply a military bonus
once per city without the existing clamp. Run focused effect tests and the
production build.

### Task 5: Add contextual central/local editor navigation

**Files:**
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`
- Modify: `Code/ui/windows/CourtWindow.cs`
- Modify: `Code/ui/components/CourtCityGovernmentCard.cs` only if a card-level
  action is needed
- Modify: `Locales/aw3_court.csv`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`

- [ ] **Step 1: Add source-guard assertions for the entry contract**

Require an overload carrying `kingdomId`, `cityId`, and local mode; central
entry calls it with no city; local summary button calls it with the current
city; apply returns through `CourtWindow.OpenCity`; both contextual title keys
exist; no standalone local instance/save service appears.

- [ ] **Step 2: Implement one shared editor context**

Add a private context field and overload:

```csharp
public static void Open(long kingdomId, long cityId, bool localMode)
```

Use the same `_template` draft and existing `SaveTemplate`, `ExportTemplate`,
`ImportTemplate`, and `ApplyCustomCourtTemplate` methods. Local mode selects the
resolved template and hides/locks central controls; central mode remains the
current default. Reset selection and scroll only when context changes.

- [ ] **Step 3: Add the local entry button**

Place a localized `自定义官府` button in the city local summary beside the
existing local-template selector. It opens the same workflow instance with
the city context. Keep the kingdom button labeled `自定义朝廷`.

- [ ] **Step 4: Implement return behavior and localization**

After apply, local context calls `CourtWindow.OpenCity(kingdomId, cityId)`;
central context calls `CourtWindow.OpenAndRefresh(kingdomId)`. Add Simplified,
English, and Traditional rows for button/title/template/office names and the
window framework's ` Title` compatibility key.

### Task 6: Fix toolbar viewport and add permanent scrollbar

**Files:**
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`

- [ ] **Step 1: Add failing source assertions**

Require a computed visible toolbar height that subtracts the `46f` top offset
and bottom inset, `Scrollbar` construction, `handleRect`, `verticalScrollbar`,
`ScrollbarVisibility.Permanent`, and unchanged `_canvasRect.anchoredPosition =
new Vector2(-480f, 50f)`.

- [ ] **Step 2: Correct viewport geometry**

Keep the viewport top coordinate and toolbar scale, but set its height to
`Mathf.Max(1f, viewportHeight - ToolbarTopOffset - ToolbarBottomInset)` so
the bottom stays inside the root. Reserve scrollbar width in the viewport and
leave the content panel width unchanged.

- [ ] **Step 3: Attach the existing pixel-style scrollbar pattern**

Create a narrow track and handle using the same colors and direction as
`CourtAuxiliaryLawWindow`. Set the scrollbar to permanent, clamped, vertical,
and retain mouse-wheel inertia. Reset `verticalNormalizedPosition = 1f` only
when a different editor context is opened.

- [ ] **Step 4: Run focused source guard and build**

Expected: custom workflow guard passes and `dotnet build AncientWarfare3.csproj`
reports 0 warnings and 0 errors.

### Task 7: Integration verification and handoff

**Files:**
- Modify: only focused test/source-guard files if an assertion needs exact
  wording adjustment
- Do not stage: existing worktree changes in the bandit service, custom court
  whitespace work, or Cultiway performance plan

- [ ] **Step 1: Run all focused tests**

Run the custom local government, custom court, effect, office history, and
supporter guards using the clean-worktree msbuild workaround. Record the
pre-existing `RuntimeRegressionSourceGuardTests` failure separately if it
remains.

- [ ] **Step 2: Run complete rules and production build**

Run the full rules suite, `dotnet build AncientWarfare3.csproj`, and
`git diff --check`. Do not claim the full suite passes unless its existing
runtime guard failure has been independently resolved.

- [ ] **Step 3: Commit the feature branch**

```powershell
git add Code/core/court Code/core/policy/CityEconomyService.cs Code/ui/windows/CourtWindow.cs Code/ui/windows/CustomCourtWorkflowWindow.cs Code/ui/components/CourtCityGovernmentCard.cs Locales/aw3_court.csv Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: add contextual custom local governments"
```

- [ ] **Step 4: Merge, deploy, and verify runtime**

Merge into `master` only after focused verification. Deploy with
`.\deploy-local.ps1`, launch a visible WorldBox window, and inspect fresh
`Player.log` output for C# compile errors, Harmony failures, missing
localization, and AW3 exceptions. Visually verify central title versus local
title, resolved civil/military template, all toolbar controls via thumb
dragging, and unchanged canvas dragging.

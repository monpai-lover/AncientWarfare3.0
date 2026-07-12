# AW3 Naming And Court UI Corrections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make official Shi historically strict, Xia alliance names contextual and unique, school UI discoverable, court local officers scalable, and kingdom detail names accurate.

**Architecture:** Keep decisions in pure rule classes and leave Harmony/UI adapters thin. Extend the existing Chinese Name alliance parameter route, split the court read layout into central and local sections, and rebind only the kingdom window's visible input without mutating kingdom data.

**Tech Stack:** C# net48, Harmony, NeoModLoader, Unity UI, Chinese Name optional compile symbol, temporary net9 rule harness.

---

## File Structure

- Modify `Code/core/lineage/OfficialShiRules.cs`: strict attested office-to-Shi map.
- Modify `Code/content/XiaAllianceNamingRules.cs`: meeting-city and uniqueness decisions.
- Modify `Code/content/XiaNaming.cs`: register the AW3 alliance parameter getter.
- Modify `Code/content/XiaNamingRepair.cs`: provide contextual parameters and resolve active-name collisions.
- Modify `name_generators/Xia/alliances.json`: contextual city/founder templates.
- Modify `name_generators/lib/Xia会盟雅称.txt`: remove fixed historical place names.
- Modify `Code/ui/AW_LineageTab.cs`: school map and overview buttons.
- Modify `Code/core/court/CourtPyramidRules.cs`: central/local layout and link boundary.
- Modify `Code/ui/windows/CourtWindow.cs`: pooled section labels and divider.
- Modify `Code/patch/AW_KingdomWindowPatch.cs`: display-only kingdom name rebind.
- Modify `Locales/aw3_school.csv`: tab-button labels and descriptions.
- Modify `F:/tmp/AW3CorrectnessRuleTests/Program.cs`: temporary regression rules and source gates.

### Task 1: Failing Rules And Integration Gates

- [ ] **Step 1: Change official-Shi expectations in the temporary harness**

Assert exact mappings for `史`, `司马`, `司寇`, `司徒`, and `太史`, and assert empty results for chancellor, granary officer, constable, imperial physician, general, governor, and unknown offices.

- [ ] **Step 2: Add failing alliance-name rule tests**

Call the wished-for pure APIs:

```csharp
Check(XiaAllianceNamingRules.ResolveMeetingCity("洛阳", "成周") == "洛阳", "capital wins");
Check(XiaAllianceNamingRules.ResolveMeetingCity("", "成周") == "成周", "first city fallback");
Check(XiaAllianceNamingRules.ResolveUniqueName("洛阳之盟", 7, new[] { "洛阳之盟" }) != "洛阳之盟",
    "active duplicate receives stable disambiguation");
```

- [ ] **Step 3: Add failing court-section tests**

Build more governors than the local column limit and assert they wrap below the
central section. Assert `BuildOrthogonalLinks` emits no segment involving a governor.

- [ ] **Step 4: Add failing source gates**

Assert that the tab source contains `SchoolMapModeService.POWER_ID` and an overview
button, the alliance generator contains `$meeting_city$`, fixed `葵丘`/`践土` are
absent, and the kingdom patch binds a `NameInput` without calling `setName`.

- [ ] **Step 5: Run the harness and verify RED**

Run:

```powershell
dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj
```

Expected: compilation or assertion failures for the not-yet-implemented APIs and gates.

### Task 2: Strict Historical Official Shi

- [ ] **Step 1: Replace the broad switch with the strict whitelist**

Return only the five approved mappings from `HistoricalOfficeShi`; all other office
IDs return an empty string and therefore use the normal Shi word library.

- [ ] **Step 2: Run the harness and verify the official-Shi tests pass**

Run the temporary harness and confirm no official-Shi assertion fails.

- [ ] **Step 3: Commit the isolated rule change**

```powershell
git add Code/core/lineage/OfficialShiRules.cs docs/superpowers/specs/2026-07-12-aw3-naming-and-court-ui-corrections-design.md docs/superpowers/plans/2026-07-12-aw3-naming-and-court-ui-corrections.md
git commit -m "fix: restrict official Shi to attested offices"
```

### Task 3: Contextual Unique Xia Alliance Names

- [ ] **Step 1: Implement pure meeting-city and unique-name rules**

Add `ResolveMeetingCity`, `MeetingName`, and `ResolveUniqueName` to
`XiaAllianceNamingRules`. Use ordinal exact-name comparisons and a stable Chinese
numeral suffix for collisions.

- [ ] **Step 2: Register `aw_xia_alliance` parameters**

Under the Chinese Name compile symbol, register an alliance getter that invokes the
default getter and adds `meeting_city` from founder capital or first valid city.

- [ ] **Step 3: Replace fixed-place templates**

Use `$meeting_city$之盟` and `$k1_short$$k2_short$之盟`; retain only non-place names
such as `诸夏会盟`, `尊王攘夷之盟`, `九州盟誓`, `王畿会盟`, and `诸侯盟誓`.

- [ ] **Step 4: Resolve collisions before `setName`**

Collect names from active alliances excluding the current alliance, resolve a unique
result once at creation, and keep the existing `route=ChineseName` diagnostic.

- [ ] **Step 5: Run the harness and verify GREEN**

Run the temporary harness; all alliance rule and source-gate assertions must pass.

- [ ] **Step 6: Commit alliance naming**

```powershell
git add Code/content/XiaAllianceNamingRules.cs Code/content/XiaNaming.cs Code/content/XiaNamingRepair.cs name_generators/Xia/alliances.json name_generators/lib/Xia会盟雅称.txt
git commit -m "fix: name Xia alliances for real cities"
```

### Task 4: School Tab Entry Points

- [ ] **Step 1: Add the school map toggle**

Create a toggle button with `SchoolMapModeService.POWER_ID` and the registered Ru
school icon, then add it to `GROUP_LINEAGE`.

- [ ] **Step 2: Add the school overview button**

Create a simple `aw_school_overview_btn` button whose click opens
`SchoolWindow.OpenSchool()` and place it adjacent to the map toggle.

- [ ] **Step 3: Add three-language localization**

Add map and overview button keys and descriptions to `Locales/aw3_school.csv`.

- [ ] **Step 4: Run source gates and commit**

```powershell
git add Code/ui/AW_LineageTab.cs Locales/aw3_school.csv
git commit -m "feat: expose school map and overview"
```

### Task 5: Separate Central And Local Court Sections

- [ ] **Step 1: Implement local-section layout rules**

Keep ranks below `GovernorRank` in the centered central pyramid. Place governors
after a fixed section gap using at most six columns and as many rows as required.

- [ ] **Step 2: Stop links at the local boundary**

Filter governor nodes out of `BuildOrthogonalLinks`; keep every generated segment
horizontal or vertical.

- [ ] **Step 3: Add pooled divider UI**

Create one central label, one local label, and one horizontal divider under
`CourtCanvas`; update their positions from the pure local-section boundary and
reuse them on every refresh.

- [ ] **Step 4: Run layout tests and commit**

```powershell
git add Code/core/court/CourtPyramidRules.cs Code/ui/windows/CourtWindow.cs
git commit -m "fix: separate local officers in court layout"
```

### Task 6: Rebind Kingdom Detail Name

- [ ] **Step 1: Add a display-only name helper**

In `AW_KingdomWindowPatch`, locate `NameInputElement` in the existing window and set
its text to `SelectedMetas.selected_kingdom.data.name` in a
`showTopPartInformation` postfix. Set the text color from the kingdom color.

- [ ] **Step 2: Protect state invariants**

Do not call `setName`, do not write `custom_name`, and do not invoke any generator.
Log the old visible value and rebound value only when they differ.

- [ ] **Step 3: Run source gates and commit**

```powershell
git add Code/patch/AW_KingdomWindowPatch.cs
git commit -m "fix: show kingdom data name in detail window"
```

### Task 7: Build, Deploy, And Runtime Verification

- [ ] **Step 1: Run static verification**

```powershell
dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj
git diff --check
dotnet build AncientWarfare3.csproj --no-restore
dotnet build AncientWarfare3.csproj --no-restore -p:DefineConstants=DEBUG%3BTRACE
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: all rule gates pass and every build reports zero warnings and zero errors.

- [ ] **Step 2: Fast-forward the runtime mod copy**

Deploy the committed repository state to
`D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0` without deleting
`.runtime` or unrelated user files, then verify Git blobs for every changed runtime file.

- [ ] **Step 3: Verify startup and Harmony integration**

Restart WorldBox and confirm `Player.log` contains successful AW3 patches, the
`Chinese Name route ready` line, and no new compile, Harmony, or null-reference errors.

- [ ] **Step 4: Verify alliance behavior three times**

Create at least three Xia-involved alliances. Confirm every creation logs
`route=ChineseName`, place-name forms use an actual founder city, and no two active
alliances share an exact name.

- [ ] **Step 5: Verify both school entry points**

Open the AW3 tab, click the school overview and school map buttons, confirm the list
window opens, city colors render, and map clicks open city school detail.

- [ ] **Step 6: Verify the court visually at two realm sizes**

Open a small and a large kingdom court. Confirm central and local labels/divider are
visible, governors wrap below the divider, no line crosses the divider, no right-side
ghost line exists, and default nodes start inside the left-visible canvas area.

- [ ] **Step 7: Verify kingdom and occupation behavior**

Open a newly named Xia kingdom and confirm the title input equals its external map
name rather than `NAME`. Observe an undefended capture reach 100 percent continuously
and a defended capture retain vanilla contest behavior.

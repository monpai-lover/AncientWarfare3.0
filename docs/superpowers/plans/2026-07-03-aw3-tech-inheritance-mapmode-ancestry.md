# AW3 Tech Inheritance Mapmode And Ancestry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix new independent kingdoms inheriting no policy/technology state, add a technology-level map mode, rename the AW3 tab, and add actor ancestry analysis that also detects fallen noble bloodlines.

**Architecture:** Keep policy math in `KingdomPolicyService`, put kingdom-state copying in a focused inheritance service, and make map mode a selected AW3 god power that reuses vanilla `MetaType.Kingdom` rendering with temporary technology colors. Ancestry analysis is read-only over `FamilyEdge`, `ActorArchive`, live actor data, and history snapshots, with one small archival extension to preserve "ever noble blood" for descendants that no longer appear in the lineage tree.

**Tech Stack:** C# net48, HarmonyLib, NeoModLoader/NML UI tabs and windows, UnityEngine.UI, existing AW3 SQLite `[TableDef]` reflection tables, WorldBox `GodPower`/`MetaType.Kingdom` zone rendering.

---

## Scope

Included:

- New breakaway kingdoms inherit technology, social policy, current research progress, and a capped amount of points.
- New kingdoms with no clear parent source use a regional same-species/same-culture fallback instead of staying blank when possible.
- Technology level is computed from completed technology cost plus partial current technology progress.
- AW3 tab title becomes `古代战争3.0(春秋)`.
- AW3 tab gets a technology map mode button.
- Kingdom tooltip shows technology level while the AW3 technology map mode is active.
- Unit inspect right-side button rail gets an ancestry analysis button.
- Ancestry analysis finds current lineage members, Xia/Human mixed descendants, and descendants of nobles who have fallen out of the visible lineage tree.

Excluded:

- Tianming systems.
- Policy tree content redesign.
- A persistent genetic simulation separate from existing family edges.
- Changing family tree folding behavior.

## Existing Facts To Respect

- `KingdomPolicyService.EnsureInitialized(Kingdom)` currently initializes defaults only; it does not copy any progress.
- Policy storage keys live in `LineageKeys`: `POLICY_CURRENT`, `POLICY_PROGRESS`, `TECH_CURRENT`, `TECH_PROGRESS`, `POLICY_COMPLETED`, `TECH_COMPLETED`, `POLICY_POINTS`, `TECH_POINTS`.
- `KingdomPolicyStateTableItem` mirrors the kingdom policy state and is written through private `KingdomPolicyService.UpsertSnapshot`.
- New civilization kingdoms are created by `KingdomManager.makeNewCivKingdom(Actor pActor, string pID = null, bool pLog = true)`.
- City rebellion/split path is `City.makeOwnKingdom(Actor pActor, bool pRebellion = false, bool pFellApart = false)`, where the old kingdom is available before original code runs.
- Vanilla map layers use `GodPower.force_map_mode` and `MetaType.Kingdom`; `ZoneCalculator` draws kingdom zones using `Kingdom.getColor()`.
- Unit right-side buttons are currently added in `AW_UnitTabPatch.ShowMainInfo_Postfix(UnitWindow __instance)` under transform `"Tabs Right"`.
- Family relations are persisted in `FamilyEdgeTableItem`.
- Actor archive currently stores `status`, `lineage_id`, `shi_id`, `parent_id_1`, `parent_id_2`, `noble_distance`, original clan snapshots, city, kingdom, and display name.

## File Map

- Modify `Code/core/policy/KingdomPolicyService.cs`: expose safe policy snapshot helpers and technology-level calculation.
- Create `Code/core/policy/KingdomPolicyInheritanceService.cs`: copy policy/tech state from source kingdom to new kingdom.
- Create `Code/core/policy/TechMapModeService.cs`: selected-power checks, tech color palette, tooltip text.
- Modify `Code/patch/AW_KingdomPolicyPatch.cs`: hook city split and new kingdom creation for inheritance; dirty map after progress changes.
- Create `Code/patch/AW_TechMapModePatch.cs`: patch `Kingdom.getColor()` and tooltip `showKingdom`.
- Modify `Code/content/GodPowerLibrary.cs`: register AW3 technology map mode god power.
- Modify `Code/ui/AW_LineageTab.cs`: add map mode button and rename comments/keys as needed.
- Modify `Code/core/lineage/LineageKeys.cs`: add ancestry/noble-blood keys.
- Modify `Code/core/db/ActorArchiveTableItem.cs`: add noble blood archival fields.
- Modify `Code/core/lineage/LineageArchiveWriter.cs`: write noble-blood snapshots.
- Create `Code/core/lineage/AncestryAnalysisService.cs`: calculate ancestry contributions and noble blood evidence.
- Modify `Code/core/lineage/LineageDTO.cs`: add ancestry DTO classes.
- Create `Code/ui/windows/AncestryAnalysisWindow.cs`: read-only ancestry window.
- Modify `Code/patch/AW_UnitTabPatch.cs`: add ancestry button.
- Modify `Locales/others.csv`: tab title, button names, ancestry labels, map mode text.
- Modify `Locales/aw3_policy_ui.csv`: technology map mode tooltip text if policy locale file is preferred locally.
- Optional modify `README.md`: document technology inheritance, map mode, ancestry analysis.

---

### Task 1: Policy Snapshot API And Technology Level

**Files:**
- Modify: `Code/core/policy/KingdomPolicyService.cs`

- [ ] **Step 1: Add a DTO near `KingdomPolicyService`**

Add this class inside namespace `AncientWarfare3.core.policy`, before `internal static class KingdomPolicyService`:

```csharp
internal sealed class KingdomPolicySnapshot
{
    public string class_state = "";
    public string army_state = "";
    public string name_state = "";
    public string enfeoffment_state = "";
    public float policy_points;
    public float tech_points;
    public string current_policy = "";
    public float policy_progress;
    public string current_tech = "";
    public float tech_progress;
    public string current_decision = "";
    public float decision_progress;
    public string completed_policies = "";
    public string completed_techs = "";
    public string completed_decisions = "";
}

internal sealed class TechLevelReport
{
    public float score;
    public float max_score;
    public int level;
    public int max_level;
    public int completed_count;
    public int total_count;
    public string current_name = "";
    public float current_fraction;
}
```

- [ ] **Step 2: Expose snapshot read/write helpers**

Add these public methods inside `KingdomPolicyService` before `AddYearlyPoints`:

```csharp
public static KingdomPolicySnapshot ReadSnapshot(Kingdom pKingdom)
{
    var snapshot = new KingdomPolicySnapshot();
    if (pKingdom?.data == null) return snapshot;
    EnsureInitialized(pKingdom);
    snapshot.class_state = GetClassId(pKingdom);
    snapshot.army_state = GetArmyState(pKingdom);
    snapshot.name_state = GetNameState(pKingdom);
    snapshot.enfeoffment_state = GetEnfeoffmentState(pKingdom);
    snapshot.policy_points = GetPoliticalPoints(pKingdom);
    snapshot.tech_points = GetTechPoints(pKingdom);
    snapshot.current_policy = GetCurrent(pKingdom, PolicyNodeKind.Social);
    snapshot.policy_progress = GetProgress(pKingdom, PolicyNodeKind.Social);
    snapshot.current_tech = GetCurrent(pKingdom, PolicyNodeKind.Tech);
    snapshot.tech_progress = GetProgress(pKingdom, PolicyNodeKind.Tech);
    snapshot.current_decision = GetCurrent(pKingdom, PolicyNodeKind.Decision);
    snapshot.decision_progress = GetProgress(pKingdom, PolicyNodeKind.Decision);
    snapshot.completed_policies = GetCompletedRaw(pKingdom, PolicyNodeKind.Social);
    snapshot.completed_techs = GetCompletedRaw(pKingdom, PolicyNodeKind.Tech);
    snapshot.completed_decisions = GetCompletedRaw(pKingdom, PolicyNodeKind.Decision);
    return snapshot;
}

public static void ApplySnapshot(Kingdom pKingdom, KingdomPolicySnapshot pSnapshot, bool pIncludeDecision)
{
    if (pKingdom?.data == null || pSnapshot == null) return;
    EnsureInitialized(pKingdom);
    SetState(pKingdom, LineageKeys.POLICY_CLASS_STATE, NonEmpty(pSnapshot.class_state, KingdomPolicyDefs.ClassDefault));
    SetState(pKingdom, LineageKeys.POLICY_ARMY_STATE, NonEmpty(pSnapshot.army_state, KingdomPolicyDefs.ArmyDefault));
    SetState(pKingdom, LineageKeys.POLICY_NAME_STATE, NonEmpty(pSnapshot.name_state, KingdomPolicyDefs.NameDefault));
    SetState(pKingdom, LineageKeys.POLICY_ENFEOFFMENT_STATE, NonEmpty(pSnapshot.enfeoffment_state, KingdomPolicyDefs.EnfeoffmentDefault));
    pKingdom.data.set(LineageKeys.POLICY_POINTS, Mathf.Clamp(pSnapshot.policy_points, 0f, 999f));
    pKingdom.data.set(LineageKeys.TECH_POINTS, Mathf.Clamp(pSnapshot.tech_points, 0f, 999f));
    pKingdom.data.set(LineageKeys.POLICY_CURRENT, pSnapshot.current_policy ?? "");
    pKingdom.data.set(LineageKeys.POLICY_PROGRESS, Mathf.Max(0f, pSnapshot.policy_progress));
    pKingdom.data.set(LineageKeys.TECH_CURRENT, pSnapshot.current_tech ?? "");
    pKingdom.data.set(LineageKeys.TECH_PROGRESS, Mathf.Max(0f, pSnapshot.tech_progress));
    pKingdom.data.set(LineageKeys.POLICY_COMPLETED, pSnapshot.completed_policies ?? "");
    pKingdom.data.set(LineageKeys.TECH_COMPLETED, pSnapshot.completed_techs ?? "");
    if (pIncludeDecision)
    {
        pKingdom.data.set(LineageKeys.DECISION_CURRENT, pSnapshot.current_decision ?? "");
        pKingdom.data.set(LineageKeys.DECISION_PROGRESS, Mathf.Max(0f, pSnapshot.decision_progress));
        pKingdom.data.set(LineageKeys.DECISION_COMPLETED, pSnapshot.completed_decisions ?? "");
    }
    UpsertSnapshot(pKingdom);
}

private static string NonEmpty(string pValue, string pFallback)
{
    return string.IsNullOrEmpty(pValue) ? pFallback : pValue;
}
```

- [ ] **Step 3: Add technology-level calculation**

Add this method inside `KingdomPolicyService`:

```csharp
public static TechLevelReport GetTechLevelReport(Kingdom pKingdom)
{
    var report = new TechLevelReport();
    report.total_count = KingdomPolicyDefs.Techs.Count;
    report.max_level = 5;

    float maxScore = 0f;
    float score = 0f;
    int completed = 0;
    foreach (KingdomPolicyDef tech in KingdomPolicyDefs.Techs)
    {
        maxScore += Mathf.Max(1f, tech.Cost);
        if (!IsCompleted(pKingdom, tech)) continue;
        completed++;
        score += Mathf.Max(1f, tech.Cost);
    }

    KingdomPolicyDef current = KingdomPolicyDefs.Get(GetCurrent(pKingdom, PolicyNodeKind.Tech));
    if (current != null && current.Cost > 0f && !IsCompleted(pKingdom, current))
    {
        float fraction = GetProgressFraction(pKingdom, current);
        score += Mathf.Max(1f, current.Cost) * fraction;
        report.current_name = current.FallbackName;
        report.current_fraction = fraction;
    }

    report.score = score;
    report.max_score = Mathf.Max(1f, maxScore);
    report.completed_count = completed;
    report.level = Mathf.Clamp(1 + Mathf.FloorToInt((score / report.max_score) * report.max_level), 1, report.max_level);
    if (completed >= report.total_count && report.total_count > 0) report.level = report.max_level;
    return report;
}
```

- [ ] **Step 4: Verify compile-level references**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; & 'C:\Program Files\dotnet\dotnet.exe' build
```

Expected: build reaches compilation. If errors appear, they should only be ordinary missing `using UnityEngine`/accessibility errors from this task and must be fixed before continuing.

---

### Task 2: Kingdom Policy And Technology Inheritance

**Files:**
- Create: `Code/core/policy/KingdomPolicyInheritanceService.cs`
- Modify: `Code/patch/AW_KingdomPolicyPatch.cs`

- [ ] **Step 1: Create inheritance service**

Create `Code/core/policy/KingdomPolicyInheritanceService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class KingdomPolicyInheritanceService
    {
        private const float TECH_PROGRESS_FACTOR = 0.75f;
        private const float POLICY_PROGRESS_FACTOR = 0.60f;
        private const float POINT_FACTOR = 0.35f;
        private const float POINT_CAP = 160f;
        private static readonly Dictionary<long, long> PendingSourceByActor = new Dictionary<long, long>();

        public static void RememberSplitSource(Actor pFounder, Kingdom pSource)
        {
            if (pFounder?.data == null || pSource?.data == null || pSource.isRekt()) return;
            PendingSourceByActor[pFounder.data.id] = pSource.id;
        }

        public static void InheritForNewKingdom(Kingdom pNewKingdom, Actor pFounder)
        {
            if (pNewKingdom?.data == null || pNewKingdom.isRekt()) return;
            if (!KingdomPolicyService.CanUsePolicySystem(pNewKingdom)) return;
            KingdomPolicyService.EnsureInitialized(pNewKingdom);

            Kingdom source = ResolveSource(pNewKingdom, pFounder);
            if (source == null || source == pNewKingdom || source.data == null || source.isRekt()) return;
            if (!KingdomPolicyService.CanUsePolicySystem(source)) return;

            KingdomPolicySnapshot src = KingdomPolicyService.ReadSnapshot(source);
            var dst = new KingdomPolicySnapshot
            {
                class_state = src.class_state,
                army_state = src.army_state,
                name_state = src.name_state,
                enfeoffment_state = src.enfeoffment_state,
                policy_points = Mathf.Min(POINT_CAP, src.policy_points * POINT_FACTOR),
                tech_points = Mathf.Min(POINT_CAP, src.tech_points * POINT_FACTOR),
                current_policy = src.current_policy,
                policy_progress = src.policy_progress * POLICY_PROGRESS_FACTOR,
                current_tech = src.current_tech,
                tech_progress = src.tech_progress * TECH_PROGRESS_FACTOR,
                completed_policies = src.completed_policies,
                completed_techs = src.completed_techs,
                current_decision = "",
                decision_progress = 0f,
                completed_decisions = ""
            };

            ClampProgressToDefinition(dst, PolicyNodeKind.Social);
            ClampProgressToDefinition(dst, PolicyNodeKind.Tech);
            KingdomPolicyService.ApplySnapshot(pNewKingdom, dst, pIncludeDecision: false);
            ModClass.LogInfo("[policy inheritance] " + pNewKingdom.name + " inherited policy state from " + source.name);
        }

        private static Kingdom ResolveSource(Kingdom pNewKingdom, Actor pFounder)
        {
            if (pFounder?.data != null && PendingSourceByActor.TryGetValue(pFounder.data.id, out long sourceId))
            {
                PendingSourceByActor.Remove(pFounder.data.id);
                Kingdom source = World.world?.kingdoms?.get(sourceId);
                if (source?.data != null && !source.isRekt()) return source;
            }

            Kingdom citySource = pFounder?.city?.kingdom;
            if (citySource?.data != null && citySource != pNewKingdom && !citySource.isRekt()) return citySource;
            return FindRegionalSource(pNewKingdom, pFounder);
        }

        private static Kingdom FindRegionalSource(Kingdom pNewKingdom, Actor pFounder)
        {
            string species = SafeSpecies(pNewKingdom);
            Culture culture = pFounder?.culture ?? pNewKingdom.culture;
            Kingdom best = null;
            float bestScore = -1f;
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (k?.data == null || k == pNewKingdom || k.isRekt()) continue;
                if (!KingdomPolicyService.CanUsePolicySystem(k)) continue;
                float score = 0f;
                if (!string.IsNullOrEmpty(species) && SafeSpecies(k) == species) score += 100f;
                if (culture != null && k.culture == culture) score += 60f;
                score += Mathf.Min(40f, k.countZones() * 0.01f);
                if (score <= bestScore) continue;
                bestScore = score;
                best = k;
            }
            return bestScore >= 60f ? best : null;
        }

        private static string SafeSpecies(Kingdom pKingdom)
        {
            try { return pKingdom?.getActorAsset()?.id ?? ""; }
            catch { return ""; }
        }

        private static void ClampProgressToDefinition(KingdomPolicySnapshot pSnapshot, PolicyNodeKind pKind)
        {
            string id = pKind == PolicyNodeKind.Tech ? pSnapshot.current_tech : pSnapshot.current_policy;
            KingdomPolicyDef def = KingdomPolicyDefs.Get(id);
            if (def == null)
            {
                if (pKind == PolicyNodeKind.Tech)
                {
                    pSnapshot.current_tech = "";
                    pSnapshot.tech_progress = 0f;
                }
                else
                {
                    pSnapshot.current_policy = "";
                    pSnapshot.policy_progress = 0f;
                }
                return;
            }

            if (pKind == PolicyNodeKind.Tech)
                pSnapshot.tech_progress = Mathf.Clamp(pSnapshot.tech_progress, 0f, Mathf.Max(0f, def.Cost - 0.01f));
            else
                pSnapshot.policy_progress = Mathf.Clamp(pSnapshot.policy_progress, 0f, Mathf.Max(0f, def.Cost - 0.01f));
        }
    }
}
```

- [ ] **Step 2: Patch split source capture**

In `Code/patch/AW_KingdomPolicyPatch.cs`, add `using System.Collections.Generic;` if needed and add these methods inside `AW_KingdomPolicyPatch`:

```csharp
[HarmonyPrefix]
[HarmonyPatch(typeof(City), "makeOwnKingdom")]
public static void MakeOwnKingdom_Prefix(City __instance, Actor pActor)
{
    KingdomPolicyInheritanceService.RememberSplitSource(pActor, __instance?.kingdom);
}

[HarmonyPostfix]
[HarmonyPatch(typeof(City), "makeOwnKingdom")]
public static void MakeOwnKingdom_Postfix(Kingdom __result, Actor pActor)
{
    KingdomPolicyInheritanceService.InheritForNewKingdom(__result, pActor);
}

[HarmonyPostfix]
[HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.makeNewCivKingdom))]
public static void MakeNewCivKingdom_PolicyPostfix(Kingdom __result, Actor pActor)
{
    KingdomPolicyInheritanceService.InheritForNewKingdom(__result, pActor);
}
```

Keep the existing `UpdateAge_Postfix` and `GetMaxCities_Postfix`.

- [ ] **Step 3: Dirty map on policy year**

At the end of `KingdomPolicyService.OnKingdomYear`, after `UpsertSnapshot(pKingdom);`, add:

```csharp
TechMapModeService.DirtyMapIfActive();
```

This method is created in Task 3. If Task 2 is built before Task 3, temporarily skip this line and add it during Task 3.

- [ ] **Step 4: Verify**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; & 'C:\Program Files\dotnet\dotnet.exe' build
```

Expected: 0 errors. Warnings must not be newly introduced by this task.

---

### Task 3: Technology Map Mode Service And God Power

**Files:**
- Create: `Code/core/policy/TechMapModeService.cs`
- Modify: `Code/content/GodPowerLibrary.cs`
- Modify: `Code/ui/AW_LineageTab.cs`

- [ ] **Step 1: Create `TechMapModeService`**

Create `Code/core/policy/TechMapModeService.cs`:

```csharp
using AncientWarfare3.content.policies;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class TechMapModeService
    {
        public const string POWER_ID = "aw_tech_level_mapmode";
        private static ColorAsset[] _colors;

        public static bool IsActive()
        {
            try { return World.world != null && World.world.isSelectedPower(POWER_ID); }
            catch { return false; }
        }

        public static ColorAsset GetColor(Kingdom pKingdom, ColorAsset pFallback)
        {
            if (pKingdom?.data == null || !KingdomPolicyService.CanUsePolicySystem(pKingdom)) return pFallback;
            EnsureColors();
            TechLevelReport report = KingdomPolicyService.GetTechLevelReport(pKingdom);
            int index = Mathf.Clamp(report.level - 1, 0, _colors.Length - 1);
            return _colors[index] ?? pFallback;
        }

        public static string BuildTooltip(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            TechLevelReport report = KingdomPolicyService.GetTechLevelReport(pKingdom);
            string current = string.IsNullOrEmpty(report.current_name)
                ? AW_L10n.Text("aw_tech_mapmode_idle", "无当前科技")
                : report.current_name + " " + Mathf.RoundToInt(report.current_fraction * 100f) + "%";
            return AW_L10n.Text("aw_tech_mapmode_level", "科技等级") + ": " + report.level + "/" + report.max_level +
                   "\n" + AW_L10n.Text("aw_tech_mapmode_completed", "已完成科技") + ": " + report.completed_count + "/" + report.total_count +
                   "\n" + AW_L10n.Text("aw_tech_mapmode_current", "当前研发") + ": " + current +
                   "\n" + AW_L10n.Text("aw_tech_mapmode_points", "科技点") + ": " + Mathf.FloorToInt(KingdomPolicyService.GetTechPoints(pKingdom));
        }

        public static void DirtyMapIfActive()
        {
            if (!IsActive()) return;
            try { World.world?.zone_calculator?.dirtyAndClear(); }
            catch { }
        }

        private static void EnsureColors()
        {
            if (_colors != null) return;
            _colors = new[]
            {
                ColorAsset.tryMakeNewColorAsset("#B33A2E"),
                ColorAsset.tryMakeNewColorAsset("#C96B2C"),
                ColorAsset.tryMakeNewColorAsset("#C9A42C"),
                ColorAsset.tryMakeNewColorAsset("#74A84A"),
                ColorAsset.tryMakeNewColorAsset("#2F9B57")
            };
        }
    }
}
```

- [ ] **Step 2: Register the map mode power**

In `Code/content/GodPowerLibrary.cs`, add:

```csharp
using AncientWarfare3.core.policy;
```

At the end of `Init()`, after Xia spawn power registration, add:

```csharp
            if (AssetManager.powers.get(TechMapModeService.POWER_ID) == null)
            {
                AssetManager.powers.add(new GodPower
                {
                    id = TechMapModeService.POWER_ID,
                    name = TechMapModeService.POWER_ID,
                    path_icon = "ui/icons/iconKnowledge",
                    force_map_mode = MetaType.Kingdom,
                    unselect_when_window = true,
                    ignore_cursor_icon = true,
                    allow_unit_selection = true
                });
            }
```

- [ ] **Step 3: Add button to AW3 tab**

In `Code/ui/AW_LineageTab.cs`, add:

```csharp
using AncientWarfare3.core.policy;
```

After the kingdom roster button is added to `GROUP_LINEAGE`, add:

```csharp
            PowerButton techMapButton = PowerButtonCreator.CreateGodPowerButton(
                TechMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/icons/iconKnowledge")
                ?? SpriteTextureLoader.getSprite("ui/Icons/iconXias"));
            tab.AddPowerButton(GROUP_LINEAGE, techMapButton);
```

- [ ] **Step 4: Verify**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; & 'C:\Program Files\dotnet\dotnet.exe' build
```

Expected: 0 errors. In game, AW3 tab should show an additional icon button; selecting it should force kingdom borders to display.

---

### Task 4: Map Color And Kingdom Tooltip Patch

**Files:**
- Create: `Code/patch/AW_TechMapModePatch.cs`

- [ ] **Step 1: Create patch file**

Create `Code/patch/AW_TechMapModePatch.cs`:

```csharp
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_TechMapModePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.getColor))]
        public static void KingdomGetColor_Postfix(Kingdom __instance, ref ColorAsset __result)
        {
            if (!TechMapModeService.IsActive()) return;
            __result = TechMapModeService.GetColor(__instance, __result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TooltipLibrary), "showKingdom")]
        public static void ShowKingdom_Postfix(Tooltip pTooltip, TooltipData pData)
        {
            if (!TechMapModeService.IsActive()) return;
            Kingdom kingdom = pData?.kingdom;
            if (kingdom?.data == null) return;
            pTooltip.addLineText("aw_tech_mapmode_tooltip", TechMapModeService.BuildTooltip(kingdom), "#D8E889", pLocalize: true);
        }
    }
}
```

If the private `TooltipLibrary.showKingdom` signature differs at compile time, inspect AssetRipper and adjust only the method parameters, keeping the same body. The verified decompiled signature is `private void showKingdom(Tooltip pTooltip, string pType, TooltipData pData)`, so if needed use:

```csharp
public static void ShowKingdom_Postfix(Tooltip pTooltip, string pType, TooltipData pData)
```

- [ ] **Step 2: Verify tooltip patch signature**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; & 'C:\Program Files\dotnet\dotnet.exe' build
```

Expected: 0 errors. If Harmony patch compile fails for signature, use the three-parameter postfix shown above.

---

### Task 5: Tab Title And Localization

**Files:**
- Modify: `Locales/others.csv`
- Modify: `Locales/aw3_policy_ui.csv` if policy-specific keys are kept there.

- [ ] **Step 1: Change AW3 tab title**

In `Locales/others.csv`, replace:

```csv
AW3 Lineage,姓族档案,Lineage Archive,姓族檔案
```

with:

```csv
AW3 Lineage,古代战争3.0(春秋),Ancient Warfare 3.0 (Spring and Autumn),古代戰爭3.0(春秋)
```

Replace the description line with:

```csv
AW3 Lineage Description,历史 / 国策 / 科技 / 姓族,History / Policies / Technology / Lineage,歷史 / 國策 / 科技 / 姓族
```

- [ ] **Step 2: Add map mode localization**

Append these rows to `Locales/others.csv`:

```csv
aw_tech_level_mapmode,科技地图,Technology Map,科技地圖
aw_tech_level_mapmode Description,按国家科技等级显示地图颜色,Show kingdoms by technology level,按國家科技等級顯示地圖顏色
aw_tech_mapmode_tooltip,科技概况,Technology Overview,科技概況
aw_tech_mapmode_level,科技等级,Tech Level,科技等級
aw_tech_mapmode_completed,已完成科技,Completed Techs,已完成科技
aw_tech_mapmode_current,当前研发,Current Research,目前研發
aw_tech_mapmode_points,科技点,Tech Points,科技點
aw_tech_mapmode_idle,无当前科技,No active research,無目前科技
```

- [ ] **Step 3: Verify localization keys**

Run:

```powershell
rg -n "AW3 Lineage|aw_tech_level_mapmode|aw_tech_mapmode_level" Locales
```

Expected: all keys appear exactly once, except `AW3 Lineage` and `AW3 Lineage Description` which already appear as their edited rows.

---

### Task 6: Noble Blood Persistence

**Files:**
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/db/ActorArchiveTableItem.cs`
- Modify: `Code/core/lineage/LineageArchiveWriter.cs`

- [ ] **Step 1: Add actor data keys**

In `LineageKeys`, after `LINEAGE_STATUS`, add:

```csharp
        public const string EVER_NOBLE_BLOOD = "aw_ever_noble_blood";
        public const string NOBLE_ORIGIN_ACTOR_ID = "aw_noble_origin_actor_id";
        public const string NOBLE_ORIGIN_NAME = "aw_noble_origin_name";
        public const string NOBLE_ORIGIN_DISTANCE = "aw_noble_origin_distance";
```

- [ ] **Step 2: Add archive columns**

In `ActorArchiveTableItem`, after `public int noble_distance;`, add:

```csharp
        [TableItemDef(pDefaultValue: "0")] public int ever_noble_blood = 0;
        [TableItemDef(pDefaultValue: "-1")] public long noble_origin_actor_id = -1;
        public string noble_origin_name = "";
        [TableItemDef(pDefaultValue: "99")] public int noble_origin_distance = 99;
```

The table manager auto-adds columns through reflection; no hand migration is needed.

- [ ] **Step 3: Compute noble blood in archive writer**

In `LineageArchiveWriter.Upsert`, after reading `status`, add:

```csharp
            var nobleBlood = ResolveNobleBloodSnapshot(pActor, previous, status, nobleDist);
```

Add these columns to both update and insert value lists:

```csharp
                    ColumnVal.Create("EVER_NOBLE_BLOOD", nobleBlood.ever ? 1 : 0),
                    ColumnVal.Create("NOBLE_ORIGIN_ACTOR_ID", nobleBlood.originId),
                    ColumnVal.Create("NOBLE_ORIGIN_NAME", nobleBlood.originName),
                    ColumnVal.Create("NOBLE_ORIGIN_DISTANCE", nobleBlood.distance),
```

Add this private method inside `LineageArchiveWriter`:

```csharp
private static (bool ever, long originId, string originName, int distance) ResolveNobleBloodSnapshot(
    Actor pActor, ActorArchiveTableItem previous, string pStatus, int pNobleDistance)
{
    bool selfNoble = pStatus == LineageStatus.NOBLE || pNobleDistance < 99 || pActor.hasTrait(LineageKeys.TRAIT_GUIZU) || pActor.hasTrait(LineageKeys.TRAIT_ZHUHOU);
    if (selfNoble)
    {
        pActor.data.set(LineageKeys.EVER_NOBLE_BLOOD, true);
        pActor.data.set(LineageKeys.NOBLE_ORIGIN_ACTOR_ID, pActor.data.id);
        pActor.data.set(LineageKeys.NOBLE_ORIGIN_NAME, pActor.getName());
        pActor.data.set(LineageKeys.NOBLE_ORIGIN_DISTANCE, Mathf.Max(0, pNobleDistance));
        return (true, pActor.data.id, pActor.getName(), Mathf.Max(0, pNobleDistance));
    }

    pActor.data.get(LineageKeys.EVER_NOBLE_BLOOD, out bool liveEver, false);
    pActor.data.get(LineageKeys.NOBLE_ORIGIN_ACTOR_ID, out long liveOriginId, -1L);
    pActor.data.get(LineageKeys.NOBLE_ORIGIN_NAME, out string liveOriginName, "");
    pActor.data.get(LineageKeys.NOBLE_ORIGIN_DISTANCE, out int liveDistance, 99);
    if (liveEver) return (true, liveOriginId, liveOriginName ?? "", liveDistance);

    if (previous != null && previous.ever_noble_blood != 0)
        return (true, previous.noble_origin_actor_id, previous.noble_origin_name ?? "", previous.noble_origin_distance);

    return (false, -1L, "", 99);
}
```

Add `using UnityEngine;` at the top of `LineageArchiveWriter.cs` if it is missing.

- [ ] **Step 4: Propagate noble blood on birth**

In `LineageService` where a child inherits lineage from a parent, after setting `NOBLE_DISTANCE` and `LINEAGE_STATUS`, set:

```csharp
            pChild.data.set(LineageKeys.EVER_NOBLE_BLOOD, true);
            pChild.data.set(LineageKeys.NOBLE_ORIGIN_ACTOR_ID, source.data.id);
            pChild.data.set(LineageKeys.NOBLE_ORIGIN_NAME, source.getName());
            pChild.data.set(LineageKeys.NOBLE_ORIGIN_DISTANCE, dist + 1);
```

Apply this in both inheritance paths in `OnActorBornWithParents` and any helper used for Xia/Human child inheritance. If parent source already has `NOBLE_ORIGIN_ACTOR_ID`, prefer that origin and increment distance by one:

```csharp
            source.data.get(LineageKeys.NOBLE_ORIGIN_ACTOR_ID, out long originId, source.data.id);
            source.data.get(LineageKeys.NOBLE_ORIGIN_NAME, out string originName, source.getName());
            source.data.get(LineageKeys.NOBLE_ORIGIN_DISTANCE, out int originDist, dist);
            pChild.data.set(LineageKeys.NOBLE_ORIGIN_ACTOR_ID, originId);
            pChild.data.set(LineageKeys.NOBLE_ORIGIN_NAME, originName);
            pChild.data.set(LineageKeys.NOBLE_ORIGIN_DISTANCE, originDist + 1);
```

- [ ] **Step 5: Verify**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; & 'C:\Program Files\dotnet\dotnet.exe' build
```

Expected: 0 errors. Existing saves should load because new columns have defaults.

---

### Task 7: Ancestry Analysis Service

**Files:**
- Modify: `Code/core/lineage/LineageDTO.cs`
- Create: `Code/core/lineage/AncestryAnalysisService.cs`

- [ ] **Step 1: Add DTO classes**

Append to `LineageDTO.cs` namespace:

```csharp
internal sealed class AncestryContribution
{
    public string key = "";
    public string label = "";
    public string kind = "";
    public float percent;
    public long source_actor_id = -1;
    public string source_actor_name = "";
    public string color = "";
}

internal sealed class NobleBloodEvidence
{
    public bool has_noble_blood;
    public long origin_actor_id = -1;
    public string origin_name = "";
    public int distance = 99;
    public string reason = "";
}

internal sealed class AncestryReport
{
    public long actor_id = -1;
    public string actor_name = "";
    public string identity = "";
    public int max_depth;
    public int known_ancestors;
    public float unknown_percent;
    public NobleBloodEvidence noble_blood = new NobleBloodEvidence();
    public List<AncestryContribution> contributions = new List<AncestryContribution>();
}
```

- [ ] **Step 2: Create analysis service**

Create `Code/core/lineage/AncestryAnalysisService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.db;
using SQLite;

namespace AncientWarfare3.core.lineage
{
    internal static class AncestryAnalysisService
    {
        private const int MAX_DEPTH = 8;

        public static bool HasAnalyzableAncestry(Actor pActor)
        {
            if (pActor?.data == null) return false;
            if (LineageService.IsXia(pActor) || LineageService.IsHuman(pActor)) return true;
            if (pActor.clan != null) return true;
            if (LineageQuery.GetParentIds(pActor.data.id).Count > 0) return true;
            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActor.data.id);
            if (row == null) return false;
            return row.lineage_id >= 0 || row.shi_id >= 0 || row.ever_noble_blood != 0 ||
                   row.parent_id_1 >= 0 || row.parent_id_2 >= 0 || row.original_clan_id >= 0;
        }

        public static AncestryReport BuildReport(long pActorId)
        {
            var report = new AncestryReport { actor_id = pActorId, max_depth = MAX_DEPTH };
            Actor actor = World.world?.units?.get(pActorId);
            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActorId);
            report.actor_name = actor?.getName() ?? row?.display_name ?? row?.given_name ?? ("#" + pActorId);
            report.identity = ResolveIdentity(actor, row);
            report.noble_blood = ResolveNobleBlood(pActorId, actor, row);

            var acc = new Dictionary<string, AncestryContribution>();
            var visited = new HashSet<long>();
            Accumulate(pActorId, 100f, 0, acc, visited, report);
            report.contributions = acc.Values.OrderByDescending(x => x.percent).ThenBy(x => x.label).ToList();
            float known = report.contributions.Sum(x => x.percent);
            report.unknown_percent = Math.Max(0f, 100f - known);
            return report;
        }

        private static void Accumulate(long pActorId, float pPercent, int pDepth,
            Dictionary<string, AncestryContribution> pAcc, HashSet<long> pVisited, AncestryReport pReport)
        {
            if (pPercent <= 0.05f || pDepth > MAX_DEPTH || pActorId < 0 || !pVisited.Add(pActorId)) return;
            List<long> parents = LineageQuery.GetParentIds(pActorId);
            if (parents.Count == 0 || pDepth == MAX_DEPTH)
            {
                AddContribution(pActorId, pPercent, pAcc);
                if (pActorId != pReport.actor_id) pReport.known_ancestors++;
                return;
            }

            float childShare = pPercent / parents.Count;
            foreach (long parent in parents)
                Accumulate(parent, childShare, pDepth + 1, pAcc, pVisited, pReport);
        }

        private static void AddContribution(long pActorId, float pPercent, Dictionary<string, AncestryContribution> pAcc)
        {
            Actor actor = World.world?.units?.get(pActorId);
            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActorId);
            AncestryContribution c = BuildContribution(actor, row, pActorId);
            if (pAcc.TryGetValue(c.key, out AncestryContribution existing))
            {
                existing.percent += pPercent;
                return;
            }
            c.percent = pPercent;
            pAcc[c.key] = c;
        }

        private static AncestryContribution BuildContribution(Actor pActor, ActorArchiveTableItem pRow, long pActorId)
        {
            string clan = LiveString(pActor, LineageKeys.CLAN_NAME, pRow?.clan_name);
            long shi = LiveLong(pActor, LineageKeys.SHI_ID, pRow?.shi_id ?? -1);
            if (!string.IsNullOrEmpty(clan) && shi >= 0)
                return NewContribution("shi:" + shi, clan + "氏", "shi", pActorId, NameOf(pActor, pRow, pActorId), pRow?.kingdom_color ?? "");

            string family = LiveString(pActor, LineageKeys.FAMILY_NAME, pRow?.family_name);
            long lineage = LiveLong(pActor, LineageKeys.LINEAGE_ID, pRow?.lineage_id ?? -1);
            if (!string.IsNullOrEmpty(family) && lineage >= 0)
                return NewContribution("lineage:" + lineage, family + "姓", "lineage", pActorId, NameOf(pActor, pRow, pActorId), pRow?.kingdom_color ?? "");

            long originalClan = pActor?.clan?.data?.id ?? pRow?.original_clan_id ?? -1;
            if (originalClan >= 0)
                return NewContribution("original_clan:" + originalClan, "原版氏族 " + originalClan, "original_clan", pActorId, NameOf(pActor, pRow, pActorId), pRow?.clan_color_text ?? "");

            string asset = pActor?.asset?.id ?? pRow?.asset_id ?? "";
            if (!string.IsNullOrEmpty(asset))
                return NewContribution("species:" + asset, asset, "species", pActorId, NameOf(pActor, pRow, pActorId), "");

            return NewContribution("unknown", "未知祖源", "unknown", -1, "", "");
        }

        private static NobleBloodEvidence ResolveNobleBlood(long pActorId, Actor pActor, ActorArchiveTableItem pRow)
        {
            if (pActor?.data != null)
            {
                pActor.data.get(LineageKeys.EVER_NOBLE_BLOOD, out bool ever, false);
                if (ever)
                {
                    pActor.data.get(LineageKeys.NOBLE_ORIGIN_ACTOR_ID, out long originId, -1L);
                    pActor.data.get(LineageKeys.NOBLE_ORIGIN_NAME, out string originName, "");
                    pActor.data.get(LineageKeys.NOBLE_ORIGIN_DISTANCE, out int distance, 99);
                    return new NobleBloodEvidence { has_noble_blood = true, origin_actor_id = originId, origin_name = originName, distance = distance, reason = "live_flag" };
                }
            }

            if (pRow != null && pRow.ever_noble_blood != 0)
                return new NobleBloodEvidence { has_noble_blood = true, origin_actor_id = pRow.noble_origin_actor_id, origin_name = pRow.noble_origin_name ?? "", distance = pRow.noble_origin_distance, reason = "archive_flag" };

            return SearchNobleAncestor(pActorId, 0, new HashSet<long>());
        }

        private static NobleBloodEvidence SearchNobleAncestor(long pActorId, int pDepth, HashSet<long> pVisited)
        {
            if (pDepth > MAX_DEPTH || pActorId < 0 || !pVisited.Add(pActorId))
                return new NobleBloodEvidence();

            Actor actor = World.world?.units?.get(pActorId);
            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActorId);
            if (IsNobleEvidence(actor, row))
                return new NobleBloodEvidence { has_noble_blood = true, origin_actor_id = pActorId, origin_name = NameOf(actor, row, pActorId), distance = pDepth, reason = "ancestor_noble" };

            foreach (long parent in LineageQuery.GetParentIds(pActorId))
            {
                NobleBloodEvidence found = SearchNobleAncestor(parent, pDepth + 1, pVisited);
                if (found.has_noble_blood) return found;
            }
            return new NobleBloodEvidence();
        }

        private static bool IsNobleEvidence(Actor pActor, ActorArchiveTableItem pRow)
        {
            if (pActor?.data != null)
            {
                pActor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);
                pActor.data.get(LineageKeys.NOBLE_DISTANCE, out int dist, 99);
                if (status == LineageStatus.NOBLE || dist < 99 || pActor.isKing() || pActor.isCityLeader()) return true;
                if (pActor.hasTrait(LineageKeys.TRAIT_GUIZU) || pActor.hasTrait(LineageKeys.TRAIT_ZHUHOU)) return true;
            }
            if (pRow == null) return false;
            return pRow.status == LineageStatus.NOBLE || pRow.noble_distance < 99 || pRow.lineage_id >= 0 || pRow.shi_id >= 0;
        }

        private static AncestryContribution NewContribution(string pKey, string pLabel, string pKind, long pActorId, string pName, string pColor)
        {
            return new AncestryContribution { key = pKey, label = pLabel, kind = pKind, source_actor_id = pActorId, source_actor_name = pName, color = pColor ?? "" };
        }

        private static string ResolveIdentity(Actor pActor, ActorArchiveTableItem pRow)
        {
            string status = "";
            if (pActor?.data != null) pActor.data.get(LineageKeys.LINEAGE_STATUS, out status, LineageStatus.NONE);
            if (string.IsNullOrEmpty(status) && pRow != null) status = pRow.status;
            return string.IsNullOrEmpty(status) ? LineageStatus.NONE : status;
        }

        private static string NameOf(Actor pActor, ActorArchiveTableItem pRow, long pId)
        {
            return pActor?.getName() ?? pRow?.display_name ?? pRow?.given_name ?? ("#" + pId);
        }

        private static string LiveString(Actor pActor, string pKey, string pFallback)
        {
            if (pActor?.data != null)
            {
                pActor.data.get(pKey, out string value, "");
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return pFallback ?? "";
        }

        private static long LiveLong(Actor pActor, string pKey, long pFallback)
        {
            if (pActor?.data != null)
            {
                pActor.data.get(pKey, out long value, -1L);
                if (value >= 0) return value;
            }
            return pFallback;
        }
    }
}
```

Remove `using SQLite;` if the compiler reports it is unused and warnings are treated as errors.

- [ ] **Step 3: Verify**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; & 'C:\Program Files\dotnet\dotnet.exe' build
```

Expected: 0 errors.

---

### Task 8: Ancestry Analysis Window

**Files:**
- Create: `Code/ui/windows/AncestryAnalysisWindow.cs`
- Modify: `Code/ui/AW_LineageWindowIds.cs`

- [ ] **Step 1: Add window id**

In `AW_LineageWindowIds`, add:

```csharp
public const string ANCESTRY = "aw_ancestry_analysis";
```

- [ ] **Step 2: Create window**

Create `Code/ui/windows/AncestryAnalysisWindow.cs`:

```csharp
using AncientWarfare3.core.lineage;
using NeoModLoader.General.UI.Window;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal class AncestryAnalysisWindow : AbstractWindow<AncestryAnalysisWindow>
    {
        private static long _actorId = -1;

        public static void Open(long pActorId)
        {
            _actorId = pActorId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.ANCESTRY);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.ANCESTRY, RefreshCurrent);
        }

        public override void Init()
        {
        }

        protected override void OnNormalEnable()
        {
            base.OnNormalEnable();
            Refresh();
        }

        private static void RefreshCurrent()
        {
            Instance?.Refresh();
        }

        private void Refresh()
        {
            if (ContentTransform == null) return;
            foreach (Transform child in ContentTransform)
                Destroy(child.gameObject);

            AncestryReport report = AncestryAnalysisService.BuildReport(_actorId);
            AddText(report.actor_name, 18, TextAnchor.MiddleLeft);
            AddText(AW_L10n.Text("aw_ancestry_identity", "身份") + ": " + report.identity, 13, TextAnchor.MiddleLeft);
            if (report.noble_blood.has_noble_blood)
            {
                AddText(AW_L10n.Text("aw_ancestry_noble_blood", "贵族血脉") + ": " +
                        report.noble_blood.origin_name + " +" + report.noble_blood.distance,
                    13, TextAnchor.MiddleLeft);
            }
            else
            {
                AddText(AW_L10n.Text("aw_ancestry_no_noble_blood", "未发现贵族血脉"), 13, TextAnchor.MiddleLeft);
            }

            AddText(AW_L10n.Text("aw_ancestry_depth", "追溯代数") + ": " + report.max_depth, 13, TextAnchor.MiddleLeft);
            AddText(AW_L10n.Text("aw_ancestry_known", "已识别祖先") + ": " + report.known_ancestors, 13, TextAnchor.MiddleLeft);
            AddText(AW_L10n.Text("aw_ancestry_unknown", "未知祖源") + ": " + report.unknown_percent.ToString("0.0") + "%", 13, TextAnchor.MiddleLeft);

            foreach (AncestryContribution c in report.contributions)
                AddText(c.label + "  " + c.percent.ToString("0.0") + "%", 14, TextAnchor.MiddleLeft);
        }

        private void AddText(string pText, int pSize, TextAnchor pAnchor)
        {
            var obj = new GameObject("AncestryText", typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(ContentTransform, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(520f, 28f);
            var text = obj.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.text = pText ?? "";
        }
    }
}
```

This is a minimal readable window. If the project has a preferred reusable list item/window helper, keep this interface and replace only internal layout.

- [ ] **Step 3: Verify window class compiles**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; & 'C:\Program Files\dotnet\dotnet.exe' build
```

Expected: 0 errors. If `AW_LineageWindowIds.SafeShow` requires a different delegate shape, follow the existing `FamilyTreeWindow`/`HistoryListWindow` call pattern.

---

### Task 9: Unit Inspect Button For Ancestry

**Files:**
- Modify: `Code/patch/AW_UnitTabPatch.cs`
- Modify: `Locales/others.csv`

- [ ] **Step 1: Add constants**

In `AW_UnitTabPatch`, add:

```csharp
private const string ANCESTRY_BTN_NAME = "AW_AncestryTabButton";
```

- [ ] **Step 2: Change show condition**

Keep the existing family and biography conditions. Add a separate ancestry condition:

```csharp
bool showAncestry = actor != null && actor.data != null && AncestryAnalysisService.HasAnalyzableAncestry(actor);
```

Do not hide the family/biography buttons when ancestry is available but family tree is not available. Hide/show each button independently.

- [ ] **Step 3: Build ancestry button**

Add a `BuildAncestryButton(Transform pRail)` method copied from `BuildBioButton`, with:

```csharp
var obj = new GameObject(ANCESTRY_BTN_NAME, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TipButton));
...
icon.sprite = SpriteTextureLoader.getSprite("ui/icons/iconFamily")
              ?? SpriteTextureLoader.getSprite("ui/icons/iconClan")
              ?? SpriteTextureLoader.getSprite("ui/Icons/iconXias");
...
tip.hoverAction = () => Tooltip.show(obj, "normal",
    new TooltipData { tip_name = "aw_ancestry_entry", tip_description = "aw_view_ancestry" });
```

Wire click:

```csharp
ancestryBtn.onClick.RemoveAllListeners();
ancestryBtn.onClick.AddListener(() => AncestryAnalysisWindow.Open(centerId));
```

Add imports:

```csharp
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.windows;
```

They may already exist; keep one copy only.

- [ ] **Step 4: Add localization**

Append to `Locales/others.csv`:

```csv
aw_ancestry_entry,祖源分析,Ancestry Analysis,祖源分析
aw_view_ancestry,查看该人物的血脉祖源构成,View this actor's ancestry composition,查看該人物的血脈祖源構成
aw_ancestry_identity,身份,Identity,身份
aw_ancestry_noble_blood,贵族血脉,Noble Blood,貴族血脈
aw_ancestry_no_noble_blood,未发现贵族血脉,No noble blood found,未發現貴族血脈
aw_ancestry_depth,追溯代数,Trace Depth,追溯代數
aw_ancestry_known,已识别祖先,Known Ancestors,已識別祖先
aw_ancestry_unknown,未知祖源,Unknown Ancestry,未知祖源
```

- [ ] **Step 5: Verify**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; & 'C:\Program Files\dotnet\dotnet.exe' build
```

Expected: 0 errors. In game, actors with no visible family tree but with noble ancestors should still show the ancestry button.

---

### Task 10: README Note

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add a concise feature note**

Add a section or bullet under existing AW3 feature notes:

```markdown
- 国策/科技：新独立国家会从来源国或区域上下文继承科技、国策与部分研发进度，避免新国家成为白板。
- 科技地图：AW3 标签页提供科技地图模式，按国家科技等级从红到绿显示，并在国家 tooltip 中显示科技等级和当前研发。
- 祖源分析：人物窗口右侧提供祖源分析，按父母链计算血脉占比，并能追踪已经脱离族谱显示范围的贵族血脉后代。
```

- [ ] **Step 2: Verify markdown only**

Run:

```powershell
rg -n "科技地图|祖源分析|新独立国家" README.md
```

Expected: all three phrases appear.

---

### Task 11: Full Verification

**Files:**
- Check all modified files.

- [ ] **Step 1: Build**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; & 'C:\Program Files\dotnet\dotnet.exe' build
```

Expected: 0 errors. New warnings should be fixed unless they pre-existed and are unrelated.

- [ ] **Step 2: Source checks**

Run:

```powershell
rg -n "aw_tech_level_mapmode|TechMapModeService|KingdomPolicyInheritanceService|AncestryAnalysisService|EVER_NOBLE_BLOOD|aw_ancestry_entry" Code Locales README.md
```

Expected: every symbol appears in the intended files.

- [ ] **Step 3: In-game checks**

Use a Xia or Human kingdom with policy system enabled:

- Start a technology and social policy.
- Trigger a rebellion or city split.
- Confirm new kingdom has completed tech/policy strings and partial current progress instead of blank fields.
- Open AW3 tab and select technology map mode.
- Confirm low-tech countries are red/orange and higher-tech countries green.
- Hover a kingdom while map mode is active and confirm technology level appears.
- Inspect a Xia/Human actor with known parents and confirm ancestry button opens.
- Inspect a descendant whose current status is common/no visible lineage but whose ancestor was noble and confirm `贵族血脉` is shown.

## Self-Review

- Spec coverage: independent kingdom inheritance is covered by Tasks 1-2; map mode by Tasks 3-5; tab rename by Task 5; ancestry and fallen noble blood by Tasks 6-9; docs by Task 10.
- Placeholder scan: no task uses deferred placeholders; fallback paths and exact commands are specified.
- Type consistency: `KingdomPolicySnapshot`, `TechLevelReport`, `TechMapModeService`, `AncestryReport`, and `AncestryAnalysisService` are introduced before use.

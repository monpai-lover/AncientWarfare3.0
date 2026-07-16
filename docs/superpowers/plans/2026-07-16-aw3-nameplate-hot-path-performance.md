# AW3 Nameplate Hot-Path Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove AW3 SQL, repeated component/reflection work, duplicate kingdom text assignment, and off-camera city scans from the per-frame nameplate path without delaying visible state changes.

**Architecture:** Treat `Kingdom.data[LineageKeys.VASSAL_SUZERAIN_ID]` as the live vassal projection, patch the two native `NameplateText` formatting/loading seams, and keep one cached vassal-flag component per nameplate. Build school plates from deduplicated visible-zone cities and pass one cached school snapshot through identity and icon selection.

**Tech Stack:** C# 8 / .NET Framework 4.8, Harmony, Unity UI, WorldBox `NameplateText`, pure-rule console tests, PowerShell source guards.

**Execution note:** The user explicitly selected inline execution directly on `master`. Preserve all unrelated dirty war-mobilization, levy, deployment, slave-vanguard, and asylum work. Do not create a worktree, commit, or push unless the user separately requests it.

---

## File Map

- Create `Code/core/lineage/VassalNameplateFlagStateRules.cs`: pure decision for hide, cached show, or sprite reload.
- Create `Code/core/policy/SchoolNameplateRenderRules.cs`: pure eligibility for rendering a school snapshot.
- Modify `Code/core/lineage/VassalService.cs`: make the runtime vassal getter data-only.
- Modify `Code/ui/components/VassalNameplateSuzerainFlag.cs`: attach once, cache lookup and text binding, and reload only when the direct suzerain changes.
- Modify `Code/patch/AW_VassalNameplatePatch.cs`: attach on `newNameplate`, hide on prepare, apply after kingdom rendering.
- Modify `Code/patch/AW_NameplateTitlePatch.cs`: move suffix insertion to native string generation.
- Modify `Code/core/policy/MandateMapMarkerRules.cs`: keep marker selection pure and testable.
- Modify `Code/core/policy/MandateMapMarkerService.cs`: gather kingdom state only; remove all `NameplateText` reflection.
- Modify `Code/patch/AW_MandateMapModePatch.cs`: replace the native species path before original loading.
- Modify `Code/content/GodPowerLibrary.cs`: enumerate visible-zone cities with reusable deduplication buffers and reuse one snapshot.
- Modify `Code/core/policy/AWMapModeMetaLibrary.cs`: accept the already-read snapshot.
- Modify `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`: link the new and existing pure nameplate rules.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`: cover vassal state, marker selection, and school snapshot eligibility.
- Modify `Tests/SourceGuardTests.ps1`: permanently reject SQL, reflection, duplicate text, component lookup, and world-city scans in these render paths.

### Task 1: Make Vassal Projection And Flag Lifetime Constant-Time

**Files:**
- Create: `Code/core/lineage/VassalNameplateFlagStateRules.cs`
- Modify: `Code/core/lineage/VassalService.cs:172`
- Modify: `Code/ui/components/VassalNameplateSuzerainFlag.cs`
- Modify: `Code/patch/AW_VassalNameplatePatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing vassal flag rule tests**

Link `VassalNameplateFlagStateRules.cs`, then add these assertions before `SchoolRuntimePerformanceTests.Run()`:

```csharp
Equal(VassalNameplateFlagAction.ShowCached,
    VassalNameplateFlagStateRules.Resolve(true, true, 10, 20, true, 20),
    "stable direct suzerain reuses cached flag sprites");
Equal(VassalNameplateFlagAction.Reload,
    VassalNameplateFlagStateRules.Resolve(true, true, 10, 21, true, 20),
    "changed direct suzerain reloads the flag once");
Equal(VassalNameplateFlagAction.Hide,
    VassalNameplateFlagStateRules.Resolve(true, true, 10, -1, false, 20),
    "independent kingdom hides its old suzerain flag");
Equal(VassalNameplateFlagAction.Hide,
    VassalNameplateFlagStateRules.Resolve(true, true, 10, 21, false, 20),
    "invalid direct suzerain never displays a stale flag");
Equal(VassalNameplateFlagAction.Hide,
    VassalNameplateFlagStateRules.Resolve(false, true, 10, 21, true, 21),
    "mini kingdom plate hides the optional suzerain flag");
```

- [ ] **Step 2: Add failing vassal hot-path source guards**

Add guards that inspect the `GetSuzerainId` method region and the whole flag component:

```powershell
$vassalService = Read-Source 'Code/core/lineage/VassalService.cs'
$getSuzerainStart = $vassalService.IndexOf('public static long GetSuzerainId(')
$getSuzerainEnd = $vassalService.IndexOf('public static Kingdom GetSuzerain(', $getSuzerainStart)
if ($getSuzerainStart -lt 0 -or $getSuzerainEnd -lt 0 -or
    $vassalService.Substring($getSuzerainStart,
        $getSuzerainEnd - $getSuzerainStart).Contains('ReadActiveSuzerainId(')) {
    $failures.Add('runtime suzerain lookup must not query archived vassal relations')
}
Require-Absent 'stable vassal plate component lookup' `
    'Code/ui/components/VassalNameplateSuzerainFlag.cs' '.GetComponent<'
Require-Present 'nameplates receive one cached vassal flag component' `
    'Code/patch/AW_VassalNameplatePatch.cs' 'VassalNameplateSuzerainFlag.Attach(__instance);'
```

- [ ] **Step 3: Run RED and verify the expected failures**

Run:

```powershell
dotnet run --project '.\Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj' -c Release --no-restore
& '.\Tests\SourceGuardTests.ps1'
```

Expected: rule compilation fails because `VassalNameplateFlagStateRules` is absent; source guards report the SQL fallback, `GetComponent`, and missing attach hook.

- [ ] **Step 4: Add the pure flag transition rule**

Create:

```csharp
namespace AncientWarfare3.core.lineage
{
    public enum VassalNameplateFlagAction
    {
        Hide,
        ShowCached,
        Reload
    }

    public static class VassalNameplateFlagStateRules
    {
        public static VassalNameplateFlagAction Resolve(bool pFullPlate,
            bool pKingdomValid, long pKingdomId, long pSuzerainId,
            bool pSuzerainValid, long pShownSuzerainId)
        {
            if (!pFullPlate || !pKingdomValid || !pSuzerainValid ||
                pSuzerainId < 0 || pSuzerainId == pKingdomId)
                return VassalNameplateFlagAction.Hide;
            return pSuzerainId == pShownSuzerainId
                ? VassalNameplateFlagAction.ShowCached
                : VassalNameplateFlagAction.Reload;
        }
    }
}
```

- [ ] **Step 5: Remove the runtime SQL fallback**

Reduce `GetSuzerainId` to the live projection:

```csharp
public static long GetSuzerainId(Kingdom pKingdom)
{
    if (pKingdom?.data == null) return -1L;
    pKingdom.data.get(LineageKeys.VASSAL_SUZERAIN_ID, out long dataId, -1L);
    return dataId;
}
```

Keep `ReadActiveSuzerainId` only if another archival/repair path still uses it; the normal getter must not call it.

- [ ] **Step 6: Replace per-frame component discovery with attach-on-create state**

Implement `Attach`, a bounded static `Dictionary<NameplateText, VassalNameplateSuzerainFlag>`, and `OnDestroy` removal. `Attach` uses `AddComponent` exactly once and passes the nameplate into `Initialize`; `Initialize` resolves `_text_name` once. Build child objects with `AddComponent`/`transform` references, move the root before the text only during creation, and never clear `_shownSuzerainId` in `Hide()`.

In `Apply`, read `VassalService.GetSuzerainId`, resolve the suzerain with `World.world.kingdoms.get(id)`, call `VassalNameplateFlagStateRules.Resolve`, and only call `LoadFlag` for `Reload`. `ShowCached` only activates the existing root. `Hide(NameplateText)` performs one dictionary lookup and deactivates the root.

- [ ] **Step 7: Attach at the original creation boundary**

Add to `AW_VassalNameplatePatch`:

```csharp
[HarmonyPostfix]
[HarmonyPatch(typeof(NameplateText), nameof(NameplateText.newNameplate))]
public static void NameplateTextNewNameplate_Postfix(NameplateText __instance)
{
    VassalNameplateSuzerainFlag.Attach(__instance);
}
```

Keep prepare-hide and kingdom-apply hooks so relation changes appear on the next frame.

- [ ] **Step 8: Run GREEN for the vassal slice**

Run the two commands from Step 3. Expected: the new rule assertions pass and the vassal source guards no longer report failures.

### Task 2: Use Native Kingdom Text And Species Loading

**Files:**
- Modify: `Code/patch/AW_NameplateTitlePatch.cs`
- Modify: `Code/core/policy/MandateMapMarkerRules.cs`
- Modify: `Code/core/policy/MandateMapMarkerService.cs`
- Modify: `Code/patch/AW_MandateMapModePatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing marker-selection tests**

Link `MandateMapMarkerRules.cs` and add:

```csharp
Equal("moh_nameplate",
    MandateMapMarkerRules.ResolveIcon(true, true, "orthodox", false, "", ""),
    "orthodox mandate replaces the kingdom species icon");
Equal("ui/Icons/traits/iconrebel",
    MandateMapMarkerRules.ResolveIcon(true, true, "rebel_claimant", false, "", ""),
    "rebel mandate uses the rebel marker");
Equal("ui/wars/Mandate_of_Heaven",
    MandateMapMarkerRules.ResolveIcon(true, false, "", false,
        "pseudo_foreign", "foreign_pseudo"),
    "pseudo claimant uses the pseudo mandate marker");
Equal("ui/Icons/traits/iconrebel",
    MandateMapMarkerRules.ResolveIcon(true, false, "", true, "", ""),
    "ordinary mandate rebel keeps its rebel marker");
Equal("", MandateMapMarkerRules.ResolveIcon(true, false, "", false, "", ""),
    "ordinary kingdom keeps its original species icon");
```

- [ ] **Step 2: Add failing native-seam source guards**

```powershell
Require-Present 'kingdom suffix uses native nameplate string generation' `
    'Code/patch/AW_NameplateTitlePatch.cs' 'getStringForNameplate'
Require-Absent 'kingdom suffix does not recalculate population' `
    'Code/patch/AW_NameplateTitlePatch.cs' 'getPopulationPeople'
Require-Absent 'kingdom suffix does not assign nameplate text twice' `
    'Code/patch/AW_NameplateTitlePatch.cs' '.setText('
Require-Absent 'mandate marker service has no field reflection' `
    'Code/core/policy/MandateMapMarkerService.cs' 'FieldInfo'
Require-Absent 'mandate marker service does not mutate nameplate fields' `
    'Code/core/policy/MandateMapMarkerService.cs' '.SetValue('
Require-Present 'mandate marker uses native species loader' `
    'Code/patch/AW_MandateMapModePatch.cs' '"showSpecies"'
Require-Absent 'mandate marker no longer post-processes kingdom plates' `
    'Code/patch/AW_MandateMapModePatch.cs' 'ApplyNameplate('
```

- [ ] **Step 3: Run RED**

Run the rule executable and source guards. Expected: `ResolveIcon` is missing and the existing postfix/reflection/duplicate-text guards fail.

- [ ] **Step 4: Move suffix insertion into `getStringForNameplate`**

Replace the `showTextKingdom` postfix with a prefix on the private native formatter:

```csharp
[HarmonyPrefix]
[HarmonyPatch(typeof(NameplateText), "getStringForNameplate")]
public static void GetStringForNameplate_Prefix(NameplateText __instance, ref string pName)
{
    if (__instance?.is_mini != false || !(__instance.nano_object is Kingdom kingdom) ||
        kingdom.data == null) return;
    bool rebel = MandateRebelService.IsRebelKingdom(kingdom);
    bool republic = RepublicGovernmentService.IsRepublic(kingdom);
    if (kingdom.data.original_actor_asset != LineageService.XIA_ASSET_ID && !rebel && !republic) return;
    string suffix = KingdomTitleDisplayRules.GetNameplateTitleSuffix(
        (int)KingdomTitleService.GetTitle(kingdom), MandateService.IsMandateKingdom(kingdom),
        rebel, republic);
    if (!string.IsNullOrEmpty(suffix)) pName += suffix;
}
```

The original `showTextKingdom` now owns population calculation and its single `setText` call.

- [ ] **Step 5: Make marker selection pure**

Move the three icon paths into `MandateMapMarkerRules` and implement `ResolveIcon(valid, currentMandate, markerKind, rebel, origin, claimant)`. Invalid returns empty; active mandate marker kind takes priority, then rebel, then pseudo origin/claimant, then ordinary empty.

Update `MandateMapMarkerService.GetMarkerIcon` to gather kingdom/report/data state and delegate to `ResolveIcon`. Delete `ApplyNameplate`, `ReplaceSpeciesIcon`, `ClearSpecialIcon`, all `FieldInfo`, `AccessTools`, `Image`, and reflective reads/writes.

- [ ] **Step 6: Prefix the native string species loader**

Patch the string overload of `NameplateText.showSpecies`. Inspect `__instance.nano_object as Kingdom`, ask `GetMarkerIcon`, and replace `pPath` only if the marker path is nonempty and its sprite was found. Maintain two bounded sets for available and unavailable marker paths so `SpriteTextureLoader.getSprite` is used only on the first encounter for each of the three fixed paths. Leave the original loader responsible for setting visibility and sprite state.

- [ ] **Step 7: Run GREEN for native text and marker loading**

Run the rule executable and source guards. Expected: marker tests and all native-seam guards pass.

### Task 3: Restrict School Nameplates To Visible Cities And One Snapshot

**Files:**
- Create: `Code/core/policy/SchoolNameplateRenderRules.cs`
- Modify: `Code/content/GodPowerLibrary.cs:355`
- Modify: `Code/core/policy/AWMapModeMetaLibrary.cs:347`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing school snapshot eligibility tests**

Link the new rules file and add:

```csharp
True(SchoolNameplateRenderRules.CanRender("confucian", 12f, true),
    "one valid cached snapshot can render a school plate");
Equal(false, SchoolNameplateRenderRules.CanRender("confucian", 0f, true),
    "zero influence snapshot has no plate");
Equal(false, SchoolNameplateRenderRules.CanRender("none", 12f, false),
    "unknown school definition has no plate");
```

- [ ] **Step 2: Add failing visible-zone and snapshot-count guards**

Extract the `DrawSchoolNameplates` method region and require:

```powershell
if (-not $schoolDrawRegion.Contains('zone_camera.getVisibleZones()')) {
    $failures.Add('school nameplates must enumerate visible zones')
}
if ($schoolDrawRegion.Contains('foreach (City city in World.world.cities)')) {
    $failures.Add('school nameplates must not scan every world city')
}
if ([regex]::Matches($schoolDrawRegion,
        'CitySchoolSnapshotService\.GetSnapshot\(').Count -ne 1) {
    $failures.Add('school nameplate candidate must read exactly one snapshot')
}
Require-Present 'school identity accepts cached snapshot' `
    'Code/core/policy/AWMapModeMetaLibrary.cs' `
    'GetSchoolIdentityMetaForCity(City pCity, CitySchoolSnapshot pSnapshot)'
```

- [ ] **Step 3: Run RED**

Run the rule executable and source guards. Expected: missing pure rule and current whole-world/two-snapshot implementation fail.

- [ ] **Step 4: Add pure render eligibility**

Create:

```csharp
namespace AncientWarfare3.core.policy
{
    public static class SchoolNameplateRenderRules
    {
        public static bool CanRender(string pDominantSchool, float pTotalScore,
            bool pDefinitionExists)
        {
            return pDefinitionExists && pTotalScore > 0f &&
                   !string.IsNullOrEmpty(pDominantSchool);
        }
    }
}
```

- [ ] **Step 5: Add the cached-snapshot meta overload**

Keep the existing one-argument method as a compatibility wrapper that calls `GetSnapshot` once. Add a two-argument overload, apply `SchoolNameplateRenderRules.CanRender`, and create the same stable school meta using the supplied snapshot and definition.

- [ ] **Step 6: Build and sort visible-zone candidates**

Add reusable static `List<City>`, `HashSet<long>`, and cached `Comparison<City>` fields. Each draw clears them, visits `World.world.zone_camera.getVisibleZones()` by index, rejects invalid cities, deduplicates by `city.data.id`, sorts ascending by ID, then applies the existing camera-center check and 100-plate cap.

For each remaining city, execute this order exactly once:

```csharp
CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(city);
AWMapModeMetaObject meta =
    AWMapModeMetaLibrary.GetSchoolIdentityMetaForCity(city, snapshot);
if (meta == null) continue;
CourtSchoolDefinition definition = CourtSchoolRegistry.Find(snapshot.DominantSchool);
```

Then retain existing text, priority, color, icon, and count behavior.

- [ ] **Step 7: Run GREEN for the school slice**

Run the rule executable and source guards. Expected: all new school assertions and source contracts pass.

### Task 4: Full Static Verification And Diff Review

**Files:**
- Verify: all files above
- Preserve: every unrelated modified/untracked file from the initial `git status --short`

- [ ] **Step 1: Run focused verification from a clean process**

```powershell
& '.\Tests\SourceGuardTests.ps1'
dotnet run --project '.\Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj' -c Release --no-restore
```

Expected: `Source guards passed.` and `Rule tests passed.`

- [ ] **Step 2: Rebuild both configurations**

```powershell
dotnet build '.\AncientWarfare3.csproj' -c Debug -t:Rebuild --no-incremental --no-restore -p:AutomaticallyUseReferenceAssemblyPackages=true
dotnet build '.\AncientWarfare3.csproj' -c Release -t:Rebuild --no-incremental --no-restore -p:AutomaticallyUseReferenceAssemblyPackages=true
```

Expected: both builds complete with zero errors. Record warnings separately if they predate this slice.

- [ ] **Step 3: Check whitespace and scope**

```powershell
git diff --check
git diff -- Code/core/lineage/VassalService.cs Code/ui/components/VassalNameplateSuzerainFlag.cs Code/patch/AW_VassalNameplatePatch.cs Code/patch/AW_NameplateTitlePatch.cs Code/core/policy/MandateMapMarkerRules.cs Code/core/policy/MandateMapMarkerService.cs Code/patch/AW_MandateMapModePatch.cs Code/content/GodPowerLibrary.cs Code/core/policy/AWMapModeMetaLibrary.cs Tests/AncientWarfare3.Rules.Tests Tests/SourceGuardTests.ps1
git status --short
```

Confirm the nameplate slice did not rewrite or remove unrelated war-mobilization changes.

### Task 5: Deploy Without Touching Runtime Data And Verify In Game

**Files:**
- Deploy source: repository tracked mod files plus rebuilt assembly
- Deploy target: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`
- Preserve: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0/.runtime/`
- Inspect: `C:/Users/24908/AppData/LocalLow/mkarpenko/WorldBox/Player.log`

- [ ] **Step 1: Record runtime archive and assembly hashes**

Record SHA-256 for the installed `.runtime/aw3_lineage_archive.db` when present and for the newly built Release DLL.

- [ ] **Step 2: Deploy tracked runtime files without deleting the target**

Copy only runtime mod paths and the Release assembly. Do not mirror-delete, do not copy `Tests`, `docs`, `.git`, `bin`, or `obj`, and do not alter `.runtime`. Compare the runtime archive hash before/after and compare installed/source DLL hashes.

- [ ] **Step 3: Run visual state checks**

Launch WorldBox and verify independent, vassal, Xia-title, republic, rebel, pseudo-mandate, and active-mandate kingdoms in normal, mini, selected, mandate, vassal, and school map modes. Change a direct vassal relation and mandate state and confirm the next rendered frame updates without stale flags or icons. Confirm school mode shows no more than 100 visible city plates with correct name, color, and school icon.

- [ ] **Step 4: Compare the nameplate benchmark and log**

At the same camera position and world state, record vanilla `Benchmark Nameplates` total and `set_nameplates` values. Confirm the latest `Player.log` has no post-load AW3 Harmony or nameplate exceptions. Runtime benchmark evidence is required before claiming the performance issue fully resolved.

---

## Self-Review

- Spec coverage: runtime vassal projection, next-frame title/marker/flag updates, one native text assignment, reflection removal, stable flag caching, visible-zone school enumeration, one snapshot per city, static tests, builds, deployment, benchmarks, and log review are each mapped to a task.
- Placeholder scan: no `TODO`, `TBD`, deferred implementation, or unspecified error-handling step remains.
- Type consistency: production and test names consistently use `VassalNameplateFlagAction`, `VassalNameplateFlagStateRules.Resolve`, `MandateMapMarkerRules.ResolveIcon`, `SchoolNameplateRenderRules.CanRender`, and `GetSchoolIdentityMetaForCity(City, CitySchoolSnapshot)`.
- Scope: no vanilla overlap algorithm, building simulation, tower behavior, old-save repair, or unrelated missing-localization work is included.

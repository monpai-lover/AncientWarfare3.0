# Formal Bandit Government Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the peasant-rebel bandit route a formal policy government with full entry effects, fixed national-border walls, entry-territory recovery, staged exit, save migration, and authoritative multiplayer switching.

**Architecture:** Add `peasant_bandit` to the policy class catalog while keeping the route key for compatibility. A focused transition coordinator owns class changes and delegates territory and wall capture to separate services; the existing `SetPolicyClass` host command remains the only UI entry point. Runtime restore reconciles class and route without replaying entry effects.

**Tech Stack:** C#/.NET Framework 4.8.1, Harmony, WorldBox 0.51 APIs, Newtonsoft.Json, AW3 multiplayer commands, standalone rules tests, PowerShell source guards, CSV localization.

---

## Safety Contract

Reuse original `WarManager.endWar`, `City.recalculateNeighbourZones`,
`City.border_zones`, `WorldTile.neighboursAll`, `TopTileLibrary.wall_wild`,
`WorldTile.setTopTileType`, and `City.joinAnotherKingdom`. Never register a
custom wall, construct `War`, delete a kingdom from route code, or remove
recorded wall tiles.

## File Map

Create:

- `Code/core/lineage/PeasantRebelBanditTerritoryService.cs`
- `Code/core/lineage/PeasantRebelGovernmentTransitionService.cs`

Modify:

- `Code/content/policies/KingdomPolicyDefs.cs`
- `Code/core/lineage/LineageKeys.cs`
- `Code/core/lineage/PeasantRebelRouteRules.cs`
- `Code/core/lineage/PeasantRebelRouteService.cs`
- `Code/core/lineage/PeasantRebelBanditRoute.cs`
- `Code/core/lineage/PeasantRebelBanditWallService.cs`
- `Code/core/lineage/MandateRebelService.cs`
- `Code/core/lineage/MandateRebelStateRules.cs`
- `Code/core/policy/KingdomPolicyService.cs`
- `Code/ui/windows/KingdomPolicyWindow.cs`
- `Code/ui/windows/KingdomWindowAddition.cs`
- `Locales/aw3_policy_ui.csv`
- `Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt`
- `Tests/PeasantRebelRouteRuntimeSourceGuard.ps1`

## Task 1: Define Government And Territory Rules

**Files:**

- Modify: `Code/content/policies/KingdomPolicyDefs.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/lineage/PeasantRebelRouteRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt`

- [ ] **Step 1: Write failing detached tests**

Add to `PeasantRebelRouteRulesTests.Run`:

```csharp
True(PeasantRebelRouteRules.CanSwitchGovernment(
        "peasant_rebel", "peasant_bandit"),
    "peasant rebels may become bandits");
True(PeasantRebelRouteRules.CanSwitchGovernment(
        "peasant_bandit", "peasant_rebel"),
    "bandits may return to peasant rebels");
False(PeasantRebelRouteRules.CanSwitchGovernment(
        "default", "peasant_bandit"),
    "ordinary governments cannot become bandits directly");
False(PeasantRebelRouteRules.CanSwitchGovernment(
        "peasant_bandit", "default"),
    "bandits cannot skip the peasant rebel exit");
True(PeasantRebelRouteRules.CanAcquireWhitelistedCity(
        true, false, true), "entry territory may be recovered");
False(PeasantRebelRouteRules.CanAcquireWhitelistedCity(
        true, false, false), "new territory is forbidden");
True(PeasantRebelRouteRules.CanAcquireWhitelistedCity(
        false, false, false), "non-bandits remain unrestricted");
```

- [ ] **Step 2: Observe RED**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
dotnet run --project `
  'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' `
  -- --peasant-rebel-routes
```

Expected: compile failure for the two missing rule methods.

- [ ] **Step 3: Implement the pure rules**

Add to `PeasantRebelRouteRules`:

```csharp
public static bool CanSwitchGovernment(string currentClass,
    string targetClass)
{
    string current = (currentClass ?? "").Trim();
    string target = (targetClass ?? "").Trim();
    if (target == "peasant_bandit")
        return current == "peasant_rebel";
    if (current == "peasant_bandit")
        return target == "peasant_rebel";
    return current != target;
}

public static bool CanAcquireWhitelistedCity(bool bandit,
    bool alreadyOwned, bool whitelisted)
{
    return !bandit || alreadyOwned || whitelisted;
}
```

- [ ] **Step 4: Register class and key**

Add `public const string ClassBandit = "peasant_bandit";` to
`KingdomPolicyDefs`, append it after `ClassRebel` in `ClassStates`, and add:

```csharp
public const string MANDATE_REBEL_BANDIT_ENTRY_CITY_IDS =
    "aw_mandate_rebel_bandit_entry_city_ids";
```

beside the existing bandit wall keys in `LineageKeys`.

- [ ] **Step 5: Verify and commit**

Run the focused test and:

```powershell
dotnet build AncientWarfare3.csproj --no-restore `
  -p:TargetFrameworkVersion=v4.8.1
```

Expected: focused tests pass and build has zero errors.

```powershell
git add Code/content/policies/KingdomPolicyDefs.cs `
  Code/core/lineage/LineageKeys.cs `
  Code/core/lineage/PeasantRebelRouteRules.cs `
  Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt
git commit -m "feat: define formal bandit government rules"
```

## Task 2: Persist Entry Territory And Enforce Recovery Only

**Files:**

- Create: `Code/core/lineage/PeasantRebelBanditTerritoryService.cs`
- Modify: `Code/core/lineage/PeasantRebelRouteService.cs`
- Test: `Tests/PeasantRebelRouteRuntimeSourceGuard.ps1`

- [ ] **Step 1: Write failing source guards**

Load the new file or an empty string, then require:

```powershell
Require $territory 'CaptureCurrentCities(' `
    'Bandit entry must persist all current city IDs.'
Require $territory 'MANDATE_REBEL_BANDIT_ENTRY_CITY_IDS' `
    'Bandit territory must use its persisted whitelist.'
Require $territory 'JsonConvert.SerializeObject' `
    'Bandit territory persistence must be structured JSON.'
Require $route 'PeasantRebelBanditTerritoryService.CanAcquire(' `
    'Acquisition boundaries must query the whitelist service.'
Forbid $route 'currentCityCount == 0' `
    'The single-city invariant must be removed.'
```

- [ ] **Step 2: Observe RED**

Run `& './Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'`.

Expected: failure for missing current-city capture.

- [ ] **Step 3: Implement the territory service**

Create a service with this surface:

```csharp
internal static bool CaptureCurrentCities(Kingdom pKingdom);
internal static bool EnsureLegacyWhitelist(Kingdom pKingdom);
internal static bool HasValidWhitelist(Kingdom pKingdom);
internal static bool IsWhitelistMissing(Kingdom pKingdom);
internal static bool CanAcquire(Kingdom pKingdom, City pCity,
    bool pBandit);
```

`CaptureCurrentCities` must check replica authority first, gather every valid
currently owned city ID, sort it, and persist
`JsonConvert.SerializeObject(ids)`. It returns false without writing when the
kingdom has no valid owned city. `EnsureLegacyWhitelist` returns true for a
valid non-empty JSON list, calls `CaptureCurrentCities` only when the key is
absent for an old save, and returns false for malformed or empty JSON.
`HasValidWhitelist` and `IsWhitelistMissing` must use the same structured JSON
reader so restore can distinguish migration from corruption. `CanAcquire`
must call:

```csharp
return PeasantRebelRouteRules.CanAcquireWhitelistedCity(
    pBandit, pCity?.kingdom == pKingdom,
    ReadIds(pKingdom).Contains(pCity?.getID() ?? -1L));
```

`ReadIds` catches malformed JSON and returns an empty `HashSet<long>`, so a
corrupt list cannot authorize acquisition. Do not treat malformed JSON as a
legacy save and do not rebuild it from current ownership.

- [ ] **Step 4: Replace the route boundary**

Replace `PeasantRebelRouteService.CanAcquireCity` with:

```csharp
internal static bool CanAcquireCity(Kingdom pRecipient, City pCity)
{
    return PeasantRebelBanditTerritoryService.CanAcquire(
        pRecipient, pCity, IsBanditOrEntering(pRecipient));
}
```

Keep the existing occupation, direct transfer, and peace-settlement callers.

- [ ] **Step 5: Verify and commit**

Run the source guard, focused route tests, and net48 build. Expected: all exit
zero.

```powershell
git add Code/core/lineage/PeasantRebelBanditTerritoryService.cs `
  Code/core/lineage/PeasantRebelRouteService.cs `
  Tests/PeasantRebelRouteRuntimeSourceGuard.ps1
git commit -m "feat: persist bandit entry territory"
```

## Task 3: Capture A Fixed National-Border Wall

**Files:**

- Modify: `Code/core/lineage/PeasantRebelBanditWallService.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditRoute.cs`
- Test: `Tests/PeasantRebelRouteRuntimeSourceGuard.ps1`

- [ ] **Step 1: Write failing national-border guards**

Replace the founding-city requirements with:

```powershell
Require $wall 'CaptureAndBuild(Kingdom pKingdom)' `
    'Wall capture must cover the complete kingdom border.'
Require $wall 'foreach (City city in pKingdom.getCities())' `
    'National border capture must scan every retained city.'
Require $wall 'city.recalculateNeighbourZones()' `
    'Each city must use the original boundary refresh.'
Require $wall 'city.border_zones' `
    'Capture must start from original border zones.'
Require $wall 'IsInsideKingdom(neighbour, pKingdom)' `
    'Same-kingdom city borders must be excluded.'
Forbid $wall 'CaptureAndBuild(Kingdom pKingdom, City pCity)' `
    'Walls must not remain tied to one city.'
```

- [ ] **Step 2: Observe RED**

Run `& './Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'`.

Expected: failure for missing kingdom-wide capture.

- [ ] **Step 3: Refactor wall capture**

Change the entry point to `CaptureAndBuild(Kingdom pKingdom)`. After the
existing authority checks, use:

```csharp
var points = new List<WallPoint>();
var seen = new HashSet<string>(StringComparer.Ordinal);
foreach (City city in pKingdom.getCities())
{
    if (city?.data == null || city.isRekt() ||
        city.kingdom != pKingdom) continue;
    city.recalculateNeighbourZones();
    foreach (TileZone zone in city.border_zones)
    {
        if (zone?.tiles == null) continue;
        foreach (WorldTile tile in zone.tiles)
        {
            if (!IsInsideKingdom(tile, pKingdom) ||
                !TouchesOutsideKingdom(tile, pKingdom) ||
                !IsTerrainEligible(tile)) continue;
            string key = tile.x + ":" + tile.y;
            if (seen.Add(key))
                points.Add(new WallPoint { x = tile.x, y = tile.y });
        }
    }
}
PersistAndBuild(pKingdom, points);
```

Extract the existing sort, JSON writes, and original top-tile placement loop
into `PersistAndBuild`.

- [ ] **Step 4: Add kingdom membership helpers**

```csharp
private static bool IsInsideKingdom(WorldTile pTile, Kingdom pKingdom)
{
    if (pTile == null || pKingdom?.data == null) return false;
    try
    {
        City city = pTile.zone_city ?? pTile.zone?.city;
        return city?.kingdom == pKingdom;
    }
    catch { return false; }
}

private static bool TouchesOutsideKingdom(WorldTile pTile,
    Kingdom pKingdom)
{
    try
    {
        foreach (WorldTile neighbour in pTile.neighboursAll)
            if (!IsInsideKingdom(neighbour, pKingdom)) return true;
    }
    catch { }
    return false;
}
```

Keep current road, building, water, lava, block, wall, and top-tile filtering.

- [ ] **Step 5: Update and verify the route call**

Change the call to:

```csharp
PeasantRebelBanditWallService.CaptureAndBuild(pContext.Rebel);
```

Run source guard, build, and `git diff --check`. Expected: guard/build pass
and diff check is silent.

- [ ] **Step 6: Commit**

```powershell
git add Code/core/lineage/PeasantRebelBanditWallService.cs `
  Code/core/lineage/PeasantRebelBanditRoute.cs `
  Tests/PeasantRebelRouteRuntimeSourceGuard.ps1
git commit -m "feat: wall the fixed bandit national border"
```

## Task 4: Coordinate Full Government Transitions

**Files:**

- Create: `Code/core/lineage/PeasantRebelGovernmentTransitionService.cs`
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditRoute.cs`
- Modify: `Code/core/lineage/PeasantRebelRouteService.cs`
- Test: `Tests/PeasantRebelRouteRuntimeSourceGuard.ps1`

- [ ] **Step 1: Write failing coordinator guards**

Load the new coordinator or an empty string, then require:

```powershell
Require $government 'TrySetClassState(' `
    'Special government changes need one coordinator.'
Require $government 'CanSwitchGovernment(' `
    'Authority must share detached transition rules with UI.'
Require $government 'CaptureCurrentCities(' `
    'Bandit entry must capture retained territory.'
RequireOrder $government 'CanMutateAuthority(' 'EnterBandit(' `
    'Authority must be checked before full bandit entry.'
Require $route 'PeasantRebelGovernmentTransitionService.TryEnterBandit(' `
    'AI and manual bandit entry must share the transition coordinator.'
Forbid $bandit 'city.joinAnotherKingdom(' `
    'Formal bandit entry must retain every current city.'
```

- [ ] **Step 2: Observe RED**

Run the source guard. Expected: missing transition coordinator.

- [ ] **Step 3: Add a direct policy write boundary**

Change `KingdomPolicyService.ForceSetClassState` to call the coordinator and
keep player-command eligibility at that public boundary:

```csharp
public static bool ForceSetClassState(Kingdom pKingdom, string pClassId)
{
    if (!IsPolicyEnabledForKingdom(pKingdom)) return false;
    return PeasantRebelGovernmentTransitionService.TrySetClassState(
        pKingdom, pClassId);
}
```

Move the authoritative internal write into a separate boundary that does not
depend on the player-facing `POLICY_ENABLED` toggle:

```csharp
internal static bool ApplyClassStateDirect(Kingdom pKingdom,
    string pClassId)
{
    if (!PeasantRebelRouteRules.CanMutateAuthority(
            AW3MultiplayerReplicaScope.IsReplicaSession) ||
        AW3MultiplayerReplicaScope.IsApplying ||
        pKingdom?.data == null ||
        pKingdom.isRekt() ||
        !KingdomPolicyDefs.ClassStates.Contains(pClassId)) return false;
    if (!KingdomPolicyProfileRules.IsResolvableKingdomProfile(
            KingdomPolicyProfileService.EnsureAssigned(pKingdom)))
        return false;
    EnsureInitialized(pKingdom);
    pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, pClassId);
    ApplyClassStateEffects(pKingdom, pClassId);
    UpsertSnapshot(pKingdom);
    return true;
}
```

Add `ClassBandit` beside `ClassRebel` in `ApplyClassStateEffects`.

- [ ] **Step 4: Implement coordinator dispatch**

Create:

```csharp
internal static bool TrySetClassState(Kingdom pKingdom,
    string pTargetClass)
{
    if (!PeasantRebelRouteRules.CanMutateAuthority(
            AW3MultiplayerReplicaScope.IsReplicaSession) ||
        AW3MultiplayerReplicaScope.IsApplying ||
        pKingdom?.data == null) return false;
    string current = KingdomPolicyService.GetClassId(pKingdom);
    if (!PeasantRebelRouteRules.CanSwitchGovernment(
            current, pTargetClass)) return false;
    if (pTargetClass == KingdomPolicyDefs.ClassBandit)
        return EnterBandit(pKingdom);
    if (current == KingdomPolicyDefs.ClassBandit)
        return PeasantRebelRouteService.ConvertBanditToFounding(
            pKingdom, PeasantRebelRouteService.ResolveOrigin(pKingdom));
    if (current == KingdomPolicyDefs.ClassRebel &&
        pTargetClass != KingdomPolicyDefs.ClassRebel)
        return MandateRebelService.SettleRebelGovernment(
            pKingdom, "manual_government_change", pTargetClass);
    return KingdomPolicyService.ApplyClassStateDirect(
        pKingdom, pTargetClass);
}
```

`EnterBandit` must resolve the persisted origin, founding city or capital, and
king or city leader. Before any class, route, war, wall, or territory write,
preflight all four, at least one valid currently owned city,
`World.world.wars`, `World.world.cities`, `World.world.kingdoms`, and
`TopTileLibrary.wall_wild`, plus a non-empty persisted name root, then call
`PeasantRebelRouteService.EnterExistingBanditGovernment`. This preserves the
UI policy-enabled check while allowing AI entry, save repair, and settlement
to use the internal transition even when the UI toggle is off.

Put that shared preflight and call behind this coordinator surface:

```csharp
internal static bool TryEnterBandit(Kingdom pRebel, Kingdom pOrigin,
    City pFoundingCity, Actor pFounder);
```

The private `EnterBandit(Kingdom pKingdom)` resolver used by
`TrySetClassState` must call `TryEnterBandit`. Replace the bandit branch in
`PeasantRebelRouteService.InitializeAndEnter` with the same call:

```csharp
entered = PeasantRebelGovernmentTransitionService.TryEnterBandit(
    pRebel, pOrigin, pFoundingCity, pFounder);
```

- [ ] **Step 5: Make entry retain all cities**

Delete the `city.joinAnotherKingdom` loop and the one-city validation from
`PeasantRebelBanditRoute.Enter`. Capture a fresh whitelist for every new entry
instead of accepting stale persisted data, then apply the class before wall
capture:

```csharp
if (!PeasantRebelBanditTerritoryService.CaptureCurrentCities(
        pContext.Rebel) ||
    !KingdomPolicyService.ApplyClassStateDirect(
        pContext.Rebel, KingdomPolicyDefs.ClassBandit)) return false;
```

Keep original war ending, rename, wall, title, and history behavior.

Remove the annual `SafeCityCount(pKingdom) > 1` rejection. Resolve the
founding city for transition scoring when it remains owned; otherwise use the
capital or first valid currently owned city. A multi-city bandit must continue
annual conversion evaluation and bounded fixed-wall repair, and losing the
original founding city must not remove its runtime route while another
whitelisted city survives.

```csharp
City transitionCity = ResolveTransitionCity(pKingdom);
MandateRebelService.RunBanditRouteYear(pKingdom);
if (transitionCity?.data != null &&
    TryConvertToFounding(pKingdom, transitionCity))
{
    MandateRebelService.RunFoundingRouteYear(pKingdom);
    return;
}
PeasantRebelBanditWallService.RepairYear(pKingdom,
    IsOriginSuppressionActive(pKingdom));
```

Replace the old annual source guard with:

```powershell
Require $banditYearBody 'ResolveTransitionCity(pKingdom)' `
    'Annual bandit work must survive loss of the original founding city.'
Forbid $banditYearBody 'SafeCityCount(pKingdom) > 1' `
    'Multi-city formal bandits must continue annual route work.'
```

Add the helper used above:

```csharp
private static City ResolveTransitionCity(Kingdom pKingdom)
{
    if (pKingdom?.data == null) return null;
    if (TryResolveFoundingCity(pKingdom, out City founding))
        return founding;
    City capital = pKingdom?.capital;
    if (capital?.data != null && !capital.isRekt() &&
        capital.kingdom == pKingdom) return capital;
    foreach (City city in pKingdom.getCities())
        if (city?.data != null && !city.isRekt() &&
            city.kingdom == pKingdom) return city;
    return null;
}
```

- [ ] **Step 6: Reuse one entry and exit path**

Add `EnterExistingBanditGovernment` to the route service. It must use the
current `BanditEntryScope`, call the registered bandit behavior, persist route
`bandit`, and refresh the route/title caches. Expose read-only
`ResolveOrigin` and `ResolveFoundingCity` ID lookups for the coordinator.

```csharp
internal static bool EnterExistingBanditGovernment(Kingdom pRebel,
    Kingdom pOrigin, City pFoundingCity, Actor pFounder);
internal static Kingdom ResolveOrigin(Kingdom pKingdom);
internal static City ResolveFoundingCity(Kingdom pKingdom);
```

In `ConvertBanditToFounding`, require:

```csharp
KingdomPolicyService.ApplyClassStateDirect(
    pKingdom, KingdomPolicyDefs.ClassRebel)
```

before persisting route `founding`. Do not clear walls or entry-city IDs.

- [ ] **Step 7: Verify and commit**

Run source guard, focused route tests, and net48 build. Expected: all pass.

```powershell
git add Code/core/lineage/PeasantRebelGovernmentTransitionService.cs `
  Code/core/lineage/PeasantRebelBanditRoute.cs `
  Code/core/lineage/PeasantRebelRouteService.cs `
  Code/core/policy/KingdomPolicyService.cs `
  Tests/PeasantRebelRouteRuntimeSourceGuard.ps1
git commit -m "feat: coordinate formal bandit government transitions"
```

## Task 5: Reconcile Saves And Complete The Staged Exit

**Files:**

- Modify: `Code/core/lineage/MandateRebelStateRules.cs`
- Modify: `Code/core/lineage/MandateRebelService.cs`
- Modify: `Code/core/lineage/PeasantRebelRouteRules.cs`
- Modify: `Code/core/lineage/PeasantRebelRouteService.cs`
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt`
- Test: `Tests/PeasantRebelRouteRuntimeSourceGuard.ps1`

- [ ] **Step 1: Write failing reconciliation tests**

```csharp
Equal("peasant_bandit",
    PeasantRebelRouteRules.ResolveGovernmentClass(
        "peasant_rebel", "bandit", true, true),
    "old bandit routes migrate to formal government");
Equal("peasant_rebel",
    PeasantRebelRouteRules.ResolveGovernmentClass(
        "peasant_bandit", "founding", true, true),
    "founding route exits bandit government");
Equal("default",
    PeasantRebelRouteRules.ResolveGovernmentClass(
        "default", "founding", false, false),
    "ordinary governments do not reactivate routes");
Equal("peasant_rebel",
    PeasantRebelRouteRules.ResolveGovernmentClass(
        "peasant_bandit", "bandit", true, false),
    "invalid bandit metadata fails closed to peasant rebel");
Equal("peasant_rebel",
    PeasantRebelRouteRules.ResolveGovernmentClass(
        "peasant_bandit", "", true, false),
    "orphaned formal bandit state cannot remain active");
```

Run the focused test. Expected: compile failure for
`ResolveGovernmentClass`.

- [ ] **Step 2: Implement pure reconciliation**

```csharp
public static string ResolveGovernmentClass(string storedClass,
    string route, bool currentRebel, bool validBanditMetadata)
{
    string value = (storedClass ?? "").Trim();
    string routeId = (route ?? "").Trim();
    if (!currentRebel && value != "peasant_bandit") return value;
    if (routeId == PeasantRebelRouteIds.Bandit)
        return validBanditMetadata
            ? "peasant_bandit"
            : "peasant_rebel";
    if (routeId == PeasantRebelRouteIds.Founding)
        return "peasant_rebel";
    if (value == "peasant_bandit")
        return validBanditMetadata
            ? "peasant_bandit"
            : "peasant_rebel";
    return value;
}
```

- [ ] **Step 3: Recognize both rebel classes correctly**

Change the final class check in
`MandateRebelStateRules.IsCurrentRebelGovernment` to:

```csharp
return pClassState == KingdomPolicyDefs.ClassRebel ||
       pClassState == KingdomPolicyDefs.ClassBandit;
```

In `KingdomPolicyService.GetClassId`, read the stored class before the current
rebel projection. Return `ClassBandit` unchanged; only other current rebels
project as `ClassRebel`.

- [ ] **Step 4: Reconcile without replaying effects**

In `PeasantRebelRouteService.RebuildRuntime`, read stored class and route. For
a bandit candidate, require a valid surviving origin and a valid non-empty
entry whitelist. On authority only, a genuinely missing whitelist from an old
save may call `EnsureLegacyWhitelist`; malformed JSON must remain invalid.
Call `ResolveGovernmentClass` with that metadata result. If invalid bandit
metadata resolves to `ClassRebel`, resolve its runtime route to `founding` so
the class remains the source of truth. Then on authority only:

```csharp
string resolvedRoute = resolvedClass == KingdomPolicyDefs.ClassBandit
    ? PeasantRebelRouteIds.Bandit
    : resolvedClass == KingdomPolicyDefs.ClassRebel
        ? PeasantRebelRouteIds.Founding
        : "";
if (resolvedClass != storedClass)
    kingdom.data.set(LineageKeys.POLICY_CLASS_STATE, resolvedClass);
if (resolvedRoute != storedRoute)
    kingdom.data.set(LineageKeys.MANDATE_REBEL_ROUTE, resolvedRoute);
if (resolvedClass != KingdomPolicyDefs.ClassBandit &&
    resolvedClass != KingdomPolicyDefs.ClassRebel &&
    storedRoute.Length > 0)
    kingdom.data.set(LineageKeys.MANDATE_REBEL_ROUTE, "");
```

Populate `RuntimeByKingdom` from `resolvedRoute`, not the stale stored route.
Reconciliation may write class, route, and a missing legacy whitelist only;
it must not end wars, rename realms, change territory, or capture/build
walls. The existing presentation refresh may project the reconciled title.

Extend the source guard to forbid `EnterExistingBanditGovernment`, `endWar`,
`RenameForRoute`, and `CaptureAndBuild` inside the isolated rebuild body.

- [ ] **Step 5: Settle to an explicit ordinary class**

Change the method to:

```csharp
public static bool SettleRebelGovernment(Kingdom pKingdom,
    string pReason, string pTargetClass = null)
```

Make the first executable check reject replica/apply scopes before any rebel
flag, route, name, class, trait, or history write:

```csharp
if (!PeasantRebelRouteRules.CanMutateAuthority(
        AW3MultiplayerReplicaScope.IsReplicaSession) ||
    AW3MultiplayerReplicaScope.IsApplying ||
    pKingdom?.data == null || pKingdom.isRekt()) return false;
```

Use default settlement for existing callers, or the supplied target:

```csharp
string target = string.IsNullOrEmpty(pTargetClass)
    ? MandateRebelStateRules.SettledClassAfterRebellion(classState)
    : pTargetClass;
```

Keep current flag/trait cleanup. Before projection refresh, clear the active
route, restore the persisted root as visible name, and apply the target:

```csharp
pKingdom.data.set(LineageKeys.MANDATE_REBEL_ROUTE, "");
pKingdom.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
    out string root, "");
if (!string.IsNullOrWhiteSpace(root) &&
    !PeasantRebelRouteService.TryApplyRouteName(
        pKingdom, root.Trim())) return false;
if (!KingdomPolicyService.ApplyClassStateDirect(
        pKingdom, target)) return false;
```

Return `true` after the existing history and projection work. Update existing
void callers to ignore the returned result explicitly.

- [ ] **Step 6: Verify and commit**

Run route rules, source guard, net48 build, and `git diff --check`. Expected:
all pass.

```powershell
git add Code/core/lineage/MandateRebelStateRules.cs `
  Code/core/lineage/MandateRebelService.cs `
  Code/core/lineage/PeasantRebelRouteRules.cs `
  Code/core/lineage/PeasantRebelRouteService.cs `
  Code/core/policy/KingdomPolicyService.cs `
  Tests/AncientWarfare3.Rules.Tests/PeasantRebelRouteRulesTests.cs.txt `
  Tests/PeasantRebelRouteRuntimeSourceGuard.ps1
git commit -m "fix: reconcile bandit government save state"
```

## Task 6: Expose Localized State-Aware Policy Controls

**Files:**

- Modify: `Code/ui/windows/KingdomPolicyWindow.cs`
- Modify: `Code/ui/windows/KingdomWindowAddition.cs`
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Modify: `Locales/aw3_policy_ui.csv`
- Test: `Tests/PeasantRebelRouteRuntimeSourceGuard.ps1`

- [ ] **Step 1: Write failing UI guards**

Load the policy window, kingdom-window addition, and policy locale into
`$policyUi`, `$kingdomUi`, and `$policyLocale`, then require:

```powershell
Require $policyUi 'CanSwitchGovernment(current, classId)' `
    'UI must share transition availability with authority.'
Require $policyUi 'button.interactable = !active && canSwitch;' `
    'Invalid transitions must be disabled.'
Require $policyUi 'KingdomPolicyDefs.ClassBandit' `
    'The formal bandit class must render.'
Require $policyUi 'AddClassStateIcon(box.transform, classId)' `
    'Every government choice must render its mapped icon.'
Require $kingdomUi 'GetClassIconPath(classId)' `
    'The kingdom summary must share the formal class icon mapping.'
Require $policyLocale 'aw_policy_class_peasant_bandit,' `
    'Bandit class name must be localized.'
Require $policyLocale 'aw_policy_class_peasant_bandit_desc,' `
    'Bandit class description must be localized.'
```

- [ ] **Step 2: Observe RED**

Run the source guard. Expected: state-aware UI condition is missing.

- [ ] **Step 3: Disable invalid choices**

Inside `BuildClassStateChooser`, add:

```csharp
bool canSwitch = PeasantRebelRouteRules.CanSwitchGovernment(
    current, classId);
```

After creating each box:

```csharp
Button button = box.GetComponent<Button>();
if (button != null)
    button.interactable = !active && canSwitch;
```

Keep the click path as `AW3CommandRequest.SetPolicyClass`. When locked, append
localized `aw_policy_class_transition_locked` to the tooltip.

- [ ] **Step 4: Add display branches**

Add `ClassBandit => "\u571F\u532A"` to `ClassName` and fallback naming. In
`ClassDesc`, add:

```csharp
if (pClassId == KingdomPolicyDefs.ClassBandit)
    return AW_L10n.Text("aw_policy_class_peasant_bandit_desc",
        "\u4E0E\u6BCD\u56FD\u548C\u5E73\uFF0C\u56FA\u5B88\u843D\u8349\u65F6\u9886\u5730\uFF0C\u5E76\u5728\u56FD\u754C\u4FEE\u7B51\u6728\u5899\u3002");
```

Move the existing class icon mapping from `KingdomWindowAddition` into one
shared `KingdomPolicyService.GetClassIconPath` method. Map both `ClassRebel`
and `ClassBandit` to the existing
`"ui/Icons/traits/iconrebel"` asset, retain every other current mapping and
fallback, and make `KingdomWindowAddition.ClassIconPath` delegate to it.

```csharp
internal static string GetClassIconPath(string pClassId)
{
    if (pClassId == KingdomPolicyDefs.ClassSlaveOwner)
        return "ui/policy/start_slaves";
    if (pClassId == KingdomPolicyDefs.ClassHalfAristocrat)
        return "ui/policy/start_halfaristocrat";
    if (pClassId == KingdomPolicyDefs.ClassAristocrat)
        return "ui/policy/base_enfeoffment";
    if (pClassId == KingdomPolicyDefs.ClassReform)
        return "ui/icons/iconPeace";
    if (pClassId == KingdomPolicyDefs.ClassRepublic)
        return "ui/icons/iconDiplomacy";
    if (pClassId == KingdomPolicyDefs.ClassRebel ||
        pClassId == KingdomPolicyDefs.ClassBandit)
        return "ui/Icons/traits/iconrebel";
    return "ui/icons/iconDiplomacy";
}
```

After each class chooser box is created, call:

```csharp
AddClassStateIcon(box.transform, classId);
```

Implement the helper as an 18x18 left-anchored image and shift the existing
label so the icon and localized text never overlap:

```csharp
private static void AddClassStateIcon(Transform pParent,
    string pClassId)
{
    var iconObject = new GameObject("ClassIcon",
        typeof(RectTransform), typeof(Image));
    iconObject.transform.SetParent(pParent, false);
    RectTransform rect = iconObject.GetComponent<RectTransform>();
    rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
    rect.pivot = new Vector2(0.5f, 0.5f);
    rect.anchoredPosition = new Vector2(15f, 0f);
    rect.sizeDelta = new Vector2(18f, 18f);
    Image image = iconObject.GetComponent<Image>();
    image.sprite = SpriteTextureLoader.getSprite(
                       KingdomPolicyService.GetClassIconPath(pClassId)) ??
                   SpriteTextureLoader.getSprite(
                       "ui/icons/iconKnowledge");
    image.preserveAspect = true;
    image.raycastTarget = false;

    RectTransform textRect = pParent.Find("Text") as RectTransform;
    if (textRect == null) return;
    textRect.offsetMin = new Vector2(28f, textRect.offsetMin.y);
    textRect.offsetMax = new Vector2(-4f, textRect.offsetMax.y);
}
```

- [ ] **Step 5: Add localization**

Append:

```csv
aw_policy_class_peasant_bandit,土匪,Bandits,土匪
aw_policy_class_peasant_bandit_desc,与母国和平，固守落草时领地，并在国界修筑木墙,At peace with the origin realm; holds only its entry territory and builds wooden walls on the national border,與母國和平，固守落草時領地，並在國界修築木牆
aw_policy_class_transition_locked,土匪只能与农民义军政体互相转换,Bandit government can transition only to or from peasant rebels,土匪只能與農民義軍政體互相轉換
```

- [ ] **Step 6: Verify and commit**

Run source guard, name-system slice, war-return/display slice, and net48 build.
Expected: all pass.

```powershell
git add Code/ui/windows/KingdomPolicyWindow.cs `
  Code/ui/windows/KingdomWindowAddition.cs `
  Code/core/policy/KingdomPolicyService.cs `
  Locales/aw3_policy_ui.csv `
  Tests/PeasantRebelRouteRuntimeSourceGuard.ps1
git commit -m "feat: expose localized bandit government switching"
```

## Task 7: Verify, Deploy, And Test The Full Chain

**Files:**

- Modify only for test-driven corrections: files listed in Tasks 1-6
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run automated gates**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
& './Tests/PeasantRebelRouteRuntimeSourceGuard.ps1'
dotnet run --project `
  'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' `
  -- --peasant-rebel-routes
dotnet run --project `
  'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' `
  -- --name-system-slice
dotnet run --project `
  'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj' `
  -- --war-return-display-slice
dotnet build AncientWarfare3.csproj --no-restore `
  -p:TargetFrameworkVersion=v4.8.1
git diff --check master...HEAD
```

Expected: focused checks exit zero, build has zero errors, and diff check is
silent. Run the full rules suite separately; if it still stops only at the
authorized pre-existing `ArmyRtsCaptainCombatRulesTests.cs.txt:146` assertion,
record that exact residual failure.

- [ ] **Step 2: Audit original API reuse**

```powershell
rg -n -g 'PeasantRebel*' `
  'endWar|recalculateNeighbourZones|border_zones|neighboursAll|wall_wild|setTopTileType' `
  Code/core/lineage
$bad = rg -n -g 'PeasantRebel*' `
  'new TopTileType|top_tiles.*add|new War\(|KingdomManager.removeObject|setTopTileType\(null\)' `
  Code/core/lineage
if ($LASTEXITCODE -eq 1) {
    'NO_FORBIDDEN_ROUTE_IMPLEMENTATIONS'
} else {
    $bad
    throw 'Forbidden route implementation found.'
}
```

Expected: original APIs appear and forbidden scan reports none.

- [ ] **Step 3: Deploy the explicit worktree**

Close WorldBox, then run:

```powershell
$source = (Resolve-Path '.').Path
$target = 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
& "$source\deploy-local.ps1" `
  -SourceRoot $source -DestinationRoot $target
```

Expected: timestamped backup followed by `DEPLOY-DONE`.

- [ ] **Step 4: Verify deployment and compile**

Hash every file below source `Code` against deployed `Code`; throw on any
mismatch. In the deployment directory run:

```powershell
dotnet restore AncientWarfare3.csproj `
  -p:TargetFrameworkVersion=v4.8.1
dotnet build AncientWarfare3.csproj --no-restore `
  -p:TargetFrameworkVersion=v4.8.1
```

Expected: parity succeeds and deployed build has zero errors.

- [ ] **Step 5: Validate multi-city manual entry**

Use a controlled active multi-city peasant-rebel kingdom:

1. Confirm ordinary governments cannot select bandit.
2. Select bandit government for the rebel.
3. Confirm every existing city remains owned and is whitelisted.
4. Confirm all wars end and only the origin may later suppress it.
5. Confirm `<root>贼`, `大当家`, and `少当家` everywhere.
6. Confirm one fixed wall follows the external national border and no wall
   divides same-kingdom cities.
7. Grow zones and confirm the wall does not move.
8. Confirm new cities are rejected and a lost whitelisted city is recoverable.

- [ ] **Step 6: Validate both exit stages**

1. In bandit government, confirm only peasant rebel is selectable.
2. Switch to peasant rebel and confirm `<root>义军`, retained walls, stopped
   repair, unrestricted expansion, and restored origin rebellion war.
3. Switch to an ordinary class and confirm active route, rebel flags, rebel
   traits, special titles, and `义军` suffix are cleared; visible name is root.

- [ ] **Step 7: Validate AI, save/load, and replica behavior**

1. Trigger AI bandit selection and compare it with manual entry.
2. Save/reload a formal bandit; ensure class, whitelist, origin, walls, titles,
   and restrictions survive without replaying entry effects.
3. Load a route-only old bandit; ensure class/current-city whitelist migrate
   without moving old wall coordinates.
4. In a LAN replica, ensure only the host changes class, wars, territory, and
   walls.

- [ ] **Step 8: Commit only regression-tested acceptance corrections**

For each acceptance defect, add a focused failing rule assertion or guard,
observe RED, apply the smallest correction, rerun Steps 1-7, then commit:

```powershell
git add Code Tests Locales
git commit -m "fix: address bandit government acceptance defect"
```

Do not create an empty commit when no correction is needed.

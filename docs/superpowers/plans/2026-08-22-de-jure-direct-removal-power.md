# Direct De Jure Removal Power Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the existing de jure removal power delete an entire region when its capital is clicked and only unassign one city when an ordinary member is clicked.

**Architecture:** Put the context decision in `DeJureRegionRetirementRules`, keep persistence mutations in `DeJureRegionStore`, and let `DeJureRegionPowerService` perform one decision and one mutation per click. Reuse the existing hierarchical map invalidation path and unified CSV localization.

**Tech Stack:** C#/.NET 9 rules harness, WorldBox mod APIs, Newtonsoft.Json persistence, CSV localization.

---

### Task 1: Lock the context decision with tests

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionRetirementRulesTests.cs.txt`
- Modify: `Code/core/court/DeJureRegionRetirementRules.cs`

- [ ] **Step 1: Write the failing decision tests**

Add assertions for a new `ResolveRemovalAction` API:

```csharp
Equal(DeJureRegionRemovalAction.RetireRegion,
    DeJureRegionRetirementRules.ResolveRemovalAction(
        true, true, true, true, false),
    "regional capital retires the entire region");
Equal(DeJureRegionRemovalAction.UnassignCity,
    DeJureRegionRetirementRules.ResolveRemovalAction(
        true, true, true, false, false),
    "ordinary member only loses its assignment");
Equal(DeJureRegionRemovalAction.None,
    DeJureRegionRetirementRules.ResolveRemovalAction(
        true, false, false, false, false),
    "unassigned city is unchanged");
Equal(DeJureRegionRemovalAction.None,
    DeJureRegionRetirementRules.ResolveRemovalAction(
        true, true, true, false, true),
    "bandit stronghold is unchanged");
```

- [ ] **Step 2: Run the focused harness and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --court-de-jure-city
```

Expected: compilation fails because `DeJureRegionRemovalAction` and
`ResolveRemovalAction` do not exist.

- [ ] **Step 3: Add the minimal decision rule**

Add:

```csharp
internal enum DeJureRegionRemovalAction
{
    None = 0,
    UnassignCity = 1,
    RetireRegion = 2
}

internal static DeJureRegionRemovalAction ResolveRemovalAction(
    bool liveCity, bool activeRegion, bool memberCity,
    bool isRegionCapital, bool banditStronghold)
{
    if (!CanRetire(liveCity, activeRegion, memberCity,
            banditStronghold))
        return DeJureRegionRemovalAction.None;
    return isRegionCapital
        ? DeJureRegionRemovalAction.RetireRegion
        : DeJureRegionRemovalAction.UnassignCity;
}
```

- [ ] **Step 4: Run the focused harness and verify GREEN**

Run the same command. Expected: exit code 0.

### Task 2: Implement single-click persistence and service flow

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionRetirementRulesTests.cs.txt`
- Modify: `Code/core/court/DeJureRegionStore.cs`
- Modify: `Code/core/court/DeJureRegionPowerService.cs`

- [ ] **Step 1: Add failing source guards**

Require the store and service to contain these contracts:

```csharp
Contains(store, "UnassignCity");
Contains(store, "DeJureCityUnassigned");
Contains(service, "DeJureRegionRemovalAction.RetireRegion");
Contains(service, "DeJureRegionRemovalAction.UnassignCity");
DoesNotContain(service, "aw_de_jure_region_retire_selected");
DoesNotContain(service, "aw_de_jure_region_retire_target_mismatch");
```

- [ ] **Step 2: Run the focused harness and verify RED**

Expected: source guard failure for `UnassignCity`.

- [ ] **Step 3: Add city-only unassignment**

Implement `DeJureRegionStore.UnassignCity(City, out string)`:

```csharp
internal static bool UnassignCity(City pCity, out string pError)
{
    pError = string.Empty;
    if (!IsDeJureEligibleCity(pCity))
    {
        pError = "invalid_city";
        return false;
    }
    EnsureInitialized();
    lock (Gate)
    {
        DeJureAdministrationStore snapshot = CloneStore(_store);
        try
        {
            DeJureRegion region = _store.Regions.FirstOrDefault(p =>
                p != null && p.Active && p.MemberCityIds != null &&
                p.MemberCityIds.Contains(pCity.data.id));
            if (region == null)
            {
                pError = "region_missing";
                return false;
            }
            if (region.SeatCityId == pCity.data.id)
            {
                pError = "region_capital";
                return false;
            }
            region.MemberCityIds.Remove(pCity.data.id);
            region.Version++;
            AddChange(region.RegionId, pCity.data.id, region.RegionId,
                -1L, "DeJureCityUnassigned");
            _store.StoreRevision++;
            RegionalGovernmentAggregationService.Clear();
            return true;
        }
        catch (Exception error)
        {
            _store = snapshot;
            pError = error.Message;
            ModClass.LogError("De jure city unassignment failed: " +
                              error.Message);
            return false;
        }
    }
}
```

- [ ] **Step 4: Replace the RetireMode branch**

Resolve the clicked region and call the pure decision rule. Use this flow:

```csharp
if (!DeJureRegionStore.TryGetForCity(city.data.id,
        out DeJureRegion region))
    return "aw_de_jure_region_no_assignment";
DeJureRegionRemovalAction action =
    DeJureRegionRetirementRules.ResolveRemovalAction(
        true, region.Active, region.MemberCityIds.Contains(city.data.id),
        region.SeatCityId == city.data.id, false);
bool changed = action == DeJureRegionRemovalAction.RetireRegion
    ? DeJureRegionStore.RetireState(city, out string error)
    : action == DeJureRegionRemovalAction.UnassignCity
        ? DeJureRegionStore.UnassignCity(city, out error)
        : false;
if (!changed) return "aw_de_jure_region_retire_failed";
_targetRegionId = -1L;
pSuccess = true;
HierarchicalVassalMapModeService.MarkHierarchyDirty(city.kingdom);
HierarchicalVassalMapModeService.RefreshAfterDeJureMutation();
return action == DeJureRegionRemovalAction.RetireRegion
    ? "aw_de_jure_region_retired"
    : "aw_de_jure_region_city_unassigned";
```

- [ ] **Step 5: Run the focused harness and verify GREEN**

Expected: exit code 0.

### Task 3: Localize, verify, deploy, and push

**Files:**
- Modify: `Locales/aw3_court.csv`
- Modify: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionRetirementRulesTests.cs.txt`

- [ ] **Step 1: Add failing localization guards**

Require these keys:

```text
aw_de_jure_region_city_unassigned
aw_de_jure_region_no_assignment
aw_de_jure_region_stronghold_ineligible
```

- [ ] **Step 2: Run the focused harness and verify RED**

Expected: source guard failure for the first missing key.

- [ ] **Step 3: Add unified CSV rows**

Add Simplified Chinese, English, and Traditional Chinese text for city-only
removal, no assignment, and stronghold ineligibility. Remove obsolete
two-click prompt rows only if no code references remain.

- [ ] **Step 4: Run complete verification**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --court-de-jure-city
dotnet build AncientWarfare3.csproj --no-restore
git diff --check
```

Expected: all commands exit 0 with no test failure or compiler error.

- [ ] **Step 5: Commit only task files**

Stage the rules, store, service, test, CSV, and this plan. Do not stage existing
actor visuals, null-safety patches, or bandit sprite work.

- [ ] **Step 6: Deploy validated files**

Use `deploy-local.ps1` if it supports selective safe deployment; otherwise copy
only the changed source and CSV paths into
`D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`, then compare
hashes.

- [ ] **Step 7: Push master**

Run `git push origin master` and verify the remote accepts the new commits.

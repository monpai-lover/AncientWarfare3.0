# AW3 Initial Monarchy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep newly founded managed-lineage kingdoms monarchies while preserving republic conversion after an established monarchy truly runs out of hereditary heirs.

**Architecture:** Persist one new-game-only boolean on kingdom data to distinguish an initial founder vacancy from an extinct established monarchy. Keep the decision logic pure in succession/republic rule classes, route the initial vacancy through the existing city-leader fallback, and leave republican ranking unchanged.

**Tech Stack:** C# 11, .NET Framework 4.8 rule-test executables, Harmony patches, WorldBox kingdom data persistence.

---

### Task 1: Add failing state-machine regression tests

**Files:**
- Modify: `Tests/SuccessionGovernmentRuleTests/Program.cs`
- Modify: `Tests/WarFabricationRuleTests/Program.cs`

- [ ] **Step 1: Write the failing focused tests**

Add assertions for the desired API and update established-monarchy cases to pass the new state explicitly:

```csharp
Expect(SuccessionTransitionRules.ShouldUseInitialFounderFallback(
        pIsRepublic: false, pMonarchyEstablished: false),
    "A new managed kingdom must select its first monarch from city leaders.");
Expect(!SuccessionTransitionRules.ShouldUseInitialFounderFallback(
        pIsRepublic: false, pMonarchyEstablished: true),
    "An extinct established monarchy must not reuse founder selection.");
Expect(!RepublicGovernmentRules.ShouldEnterRepublic(
        pSuccessionPending: false, pHasMonarchyHeir: false,
        pElectableCount: 2, pMonarchyEstablished: false),
    "A kingdom that has never had a king must not become a republic.");
Expect(RepublicGovernmentRules.ShouldEnterRepublic(
        pSuccessionPending: false, pHasMonarchyHeir: false,
        pElectableCount: 2, pMonarchyEstablished: true),
    "An established monarchy with no hereditary heir may become a republic.");
```

Mirror the new `pMonarchyEstablished` argument in the broad rule-test executable so both suites describe the same rule.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj
```

Expected: compilation fails because `ShouldUseInitialFounderFallback` and the four-argument `ShouldEnterRepublic` contract do not exist yet.

### Task 2: Implement explicit monarchy establishment and founder routing

**Files:**
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/lineage/SuccessionTransitionRules.cs`
- Modify: `Code/core/lineage/RepublicGovernmentRules.cs`
- Modify: `Code/core/lineage/RepublicGovernmentService.cs`
- Modify: `Code/patch/AW_HeirPatch.cs`

- [ ] **Step 1: Add the persisted kingdom key**

Add beside the succession keys:

```csharp
public const string KINGDOM_MONARCHY_ESTABLISHED = "aw_monarchy_established";
```

- [ ] **Step 2: Add the pure founder decision**

Add to `SuccessionTransitionRules`:

```csharp
public static bool ShouldUseInitialFounderFallback(bool pIsRepublic,
    bool pMonarchyEstablished)
{
    return !pIsRepublic && !pMonarchyEstablished;
}
```

- [ ] **Step 3: Require an established monarchy before republic conversion**

Change the rule to:

```csharp
public static bool ShouldEnterRepublic(bool pSuccessionPending, bool pHasMonarchyHeir,
    int pElectableCount, bool pMonarchyEstablished)
{
    return pMonarchyEstablished && !pSuccessionPending &&
           !pHasMonarchyHeir && pElectableCount > 0;
}
```

- [ ] **Step 4: Encapsulate the persisted state in the government service**

Add methods that read an absent key as `false` and only ever persist `true`:

```csharp
public static bool HasEstablishedMonarchy(Kingdom pKingdom)
{
    if (pKingdom?.data == null) return false;
    pKingdom.data.get(LineageKeys.KINGDOM_MONARCHY_ESTABLISHED,
        out bool established, false);
    return established;
}

public static void MarkMonarchyEstablished(Kingdom pKingdom)
{
    if (pKingdom?.data == null) return;
    pKingdom.data.set(LineageKeys.KINGDOM_MONARCHY_ESTABLISHED, true);
}
```

Pass `HasEstablishedMonarchy(pKingdom)` into `ShouldEnterRepublic` inside `ElectLeaderForVacancy`.

- [ ] **Step 5: Route the first king through the existing leader fallback**

In `GetKingFromLeaders_Prefix`, after hereditary lookup and before republican election:

```csharp
if (SuccessionTransitionRules.ShouldUseInitialFounderFallback(
        RepublicGovernmentService.IsRepublic(pKingdom),
        RepublicGovernmentService.HasEstablishedMonarchy(pKingdom)))
{
    Actor founder = HeirService.GetLeaderSuccessionCandidate(pKingdom);
    HeirService.MarkLeaderFallbackSuccession(pKingdom, founder);
    __result = founder;
    return false;
}
```

In the managed `setKing` postfix, after validating that `king` is the actual new king, mark the monarchy when the kingdom is not a republic and the actor is not a republican leader:

```csharp
if (!RepublicGovernmentService.IsRepublic(__instance) &&
    !RepublicGovernmentService.IsRepublicLeader(king))
    RepublicGovernmentService.MarkMonarchyEstablished(__instance);
```

- [ ] **Step 6: Run the focused test and verify GREEN**

Run:

```powershell
dotnet run --project Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj
```

Expected: exit code 0 and `Succession/government rule tests passed.`

### Task 3: Verify integration and commit

**Files:**
- Verify: `Tests/WarFabricationRuleTests/WarFabricationRuleTests.csproj`
- Verify: `AncientWarfare3.csproj`

- [ ] **Step 1: Run the broad rule suite**

Run:

```powershell
dotnet run --project Tests/WarFabricationRuleTests/WarFabricationRuleTests.csproj
```

Expected: exit code 0 and the suite's success message.

- [ ] **Step 2: Build the mod**

Run:

```powershell
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: exit code 0 with no compile errors.

- [ ] **Step 3: Inspect the final diff**

Run:

```powershell
git diff --check
git diff -- Code/core/lineage/LineageKeys.cs Code/core/lineage/SuccessionTransitionRules.cs Code/core/lineage/RepublicGovernmentRules.cs Code/core/lineage/RepublicGovernmentService.cs Code/patch/AW_HeirPatch.cs Tests/SuccessionGovernmentRuleTests/Program.cs Tests/WarFabricationRuleTests/Program.cs
```

Expected: no whitespace errors; changes are limited to the explicit state, routing, and regression tests.

- [ ] **Step 4: Commit the implementation**

```powershell
git add -- Code/core/lineage/LineageKeys.cs Code/core/lineage/SuccessionTransitionRules.cs Code/core/lineage/RepublicGovernmentRules.cs Code/core/lineage/RepublicGovernmentService.cs Code/patch/AW_HeirPatch.cs Tests/SuccessionGovernmentRuleTests/Program.cs Tests/WarFabricationRuleTests/Program.cs docs/superpowers/plans/2026-07-10-aw3-initial-monarchy.md
git commit -m "fix: 防止新国家首次立王误转共和国"
```

- [ ] **Step 5: Push master**

```powershell
git push origin master
```

Expected: `origin/master` advances to the implementation commit.

# Matrilocal Lineage And Native Sinitic Genealogy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persistent species-agnostic matrilocal inheritance for female leaders and kings, preserve inherited dictionary surnames, and connect all five native Sinitic species to the common genealogy system.

**Architecture:** Keep policy in pure rules and runtime writes in the existing lineage/household boundary. Birth processing establishes the matrilocal marker once, selects a single authoritative parent, and then reuses existing surname, lineage, Clan, and atomic archive services. Native Sinitic species retain their current name generators while adopting the same full genealogy admission lifecycle as civilized monkeys.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony birth patches, WorldBox actor data, SQLite lineage archive, custom executable rules test project.

---

### Task 1: Matrilocal Source Rules

**Files:**
- Modify: `Code/core/lineage/RulerHouseholdRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt`

- [ ] **Step 1: Write failing establishment and source-selection tests**

Add assertions proving that a woman with a strictly higher authority tier establishes matrilocal marriage, that invalid/same-rank pairs do not, and that a marked father causes the mother slot to beat a complete father lineage:

```csharp
True(RulerHouseholdRules.ShouldEstablishMatrilocal(
    womanValid: true, womanAuthorityTier: 1,
    manValid: true, manAuthorityTier: 0),
    "a female city leader receives an ordinary matrilocal husband");
Equal(2, RulerHouseholdRules.SelectBirthLineageSourceSlot(
    parent1Male: true, parent1Complete: true,
    parent1MatrilocalToParent2: true,
    parent2Male: false, parent2Complete: true,
    parent2MatrilocalToParent1: false),
    "a marked husband makes the reigning mother authoritative");
```

- [ ] **Step 2: Run the rule suite and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: compilation fails because both new rule methods are missing.

- [ ] **Step 3: Implement pure matrilocal rules**

Add:

```csharp
public static bool ShouldEstablishMatrilocal(bool womanValid,
    int womanAuthorityTier, bool manValid, int manAuthorityTier)
{
    return womanValid && manValid && womanAuthorityTier > 0 &&
           womanAuthorityTier > manAuthorityTier;
}

public static int SelectBirthLineageSourceSlot(bool parent1Male,
    bool parent1Complete, bool parent1MatrilocalToParent2,
    bool parent2Male, bool parent2Complete,
    bool parent2MatrilocalToParent1)
{
    if (parent1MatrilocalToParent2 && parent2Complete) return 2;
    if (parent2MatrilocalToParent1 && parent1Complete) return 1;
    if (parent1Male && parent1Complete) return 1;
    if (parent2Male && parent2Complete) return 2;
    if (parent1Complete) return 1;
    return parent2Complete ? 2 : -1;
}
```

- [ ] **Step 4: Run the suite and verify GREEN**

Run the command from Step 2. Expected: `Rule tests passed.`

- [ ] **Step 5: Commit Task 1**

```powershell
git add Code/core/lineage/RulerHouseholdRules.cs Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt
git commit -m "feat: define matrilocal lineage source rules"
```

### Task 2: Birth Runtime And Western Dictionary Surname Inheritance

**Files:**
- Create: `Code/core/lineage/MatrilocalLineageService.cs`
- Modify: `Code/core/lineage/LineageService.cs`
- Modify: `Code/patch/AW_BirthPatch.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/WesternSurnameInheritanceRulesTests.cs.txt`

- [ ] **Step 1: Add source guards for the lightweight birth call and persistent marker writer**

Read `AW_BirthPatch.cs` and `MatrilocalLineageService.cs` as text. Assert that the lightweight branch calls `LineageService.OnLightweightActorBornWithParents` and that the service writes both `MATRILOCAL_IN_LAW` and `MATRILOCAL_WIFE_ID`.

- [ ] **Step 2: Run the rule suite and verify RED**

Expected: the source guard fails because the service and birth call do not exist.

- [ ] **Step 3: Implement idempotent marker reconciliation**

Create a species-agnostic service which identifies the female and male parent, resolves authority as `king=2`, `city leader=1`, `ordinary=0`, applies `ShouldEstablishMatrilocal`, and writes:

```csharp
man.data.set(LineageKeys.MATRILOCAL_IN_LAW, true);
man.data.set(LineageKeys.MATRILOCAL_WIFE_ID, woman.data.id);
```

Expose a read helper that returns true only when the marked wife ID equals the other parent ID.

- [ ] **Step 4: Connect both birth paths**

At the start of full birth processing, reconcile the parent pair before `InheritFromParents`. Add `OnLightweightActorBornWithParents` which uses the same universal reconciliation, invokes the existing `TryInheritLightweightWesternSurname`, and does not admit commoners into a full lineage.

In `AW_BirthPatch`, call the new lightweight method immediately after `WesternLineageParentEdgeService.RecordBirth`.

- [ ] **Step 5: Use the matrilocal source in full lineage selection**

Replace the hard-coded male-first complete source selection with `SelectBirthLineageSourceSlot`, passing marker facts from `MatrilocalLineageService.IsMatrilocalTo`. Keep ordinary father-first behavior unchanged.

- [ ] **Step 6: Run focused and full tests**

Run the rule suite. Expected: `Rule tests passed.`

- [ ] **Step 7: Commit Task 2**

```powershell
git add Code/core/lineage/MatrilocalLineageService.cs Code/core/lineage/LineageService.cs Code/patch/AW_BirthPatch.cs Tests/AncientWarfare3.Rules.Tests/WesternSurnameInheritanceRulesTests.cs.txt
git commit -m "fix: inherit western surnames through matrilocal births"
```

### Task 3: Civilized Monkey Matrilocal Naming

**Files:**
- Modify: `Code/content/CivMonkeyNamingRules.cs`
- Modify: `Code/content/CivMonkeyNamingContent.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/XiaExpansionAndCivMonkeyNamingRulesTests.cs.txt`

- [ ] **Step 1: Write a failing parent-slot test**

Add a pure monkey rule that selects the mother for a marked matrilocal pair, the father for an ordinary pair, and the mother only when no valid paternal surname exists.

- [ ] **Step 2: Run the rule suite and verify RED**

Expected: compilation fails because `CivMonkeyNamingRules.SelectFamilySourceSlot` is missing.

- [ ] **Step 3: Implement and use the selector**

Implement the selector as an adapter over the universal Task 1 precedence. Update `ResolveInheritedFamily` to capture both parents, test the father's universal matrilocal wife ID, select one source, and return that source's existing structured family. Only call the monkey surname word library when neither parent supplies a surname.

- [ ] **Step 4: Run the rule suite and verify GREEN**

Expected: `Rule tests passed.`

- [ ] **Step 5: Commit Task 3**

```powershell
git add Code/content/CivMonkeyNamingRules.cs Code/content/CivMonkeyNamingContent.cs Tests/AncientWarfare3.Rules.Tests/XiaExpansionAndCivMonkeyNamingRulesTests.cs.txt
git commit -m "fix: let monkey royal children follow matrilocal mothers"
```

### Task 4: Common Genealogy For Five Native Sinitic Species

**Files:**
- Modify: `Code/core/lineage/WesternLineageEligibilityRules.cs`
- Modify: `Code/core/lineage/LineageService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/NativeSiniticNamingRulesTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/WesternLineageEligibilityRulesTests.cs.txt`

- [ ] **Step 1: Change existing exclusion tests to the approved genealogy contract**

Assert that `NativeSinitic` with a stable lineage uses `UsesAwLineageSystem`, that births use the full path, and that lightweight western edges remain disabled. Add a loop over all five exact species IDs to retain generator routing while checking genealogy eligibility.

- [ ] **Step 2: Run the rule suite and verify RED**

Expected: assertions fail because `NativeSinitic` is currently explicitly excluded.

- [ ] **Step 3: Admit Native Sinitic profiles to full genealogy**

Update `UsesAwLineageSystem` and `ShouldUseFullBirthPath` to include `NamingProfileId.NativeSinitic`. Do not add it to `UsesLightweightParentEdges`.

- [ ] **Step 4: Reuse native structured surname during noble admission**

Allow `CanUseXiaizedLineageGovernment` for the native profile. In `EnsureForeignPseudoOfficialLineage`, build family/Clan identity from existing `FAMILY_NAME` or `CHINESE_FAMILY_NAME`; do not invoke western city particles or monkey word libraries. Keep `NAME_INTEGRATED=true` so display remains surname-first.

- [ ] **Step 5: Run tests and build**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: rules pass; build has 0 warnings and 0 errors.

- [ ] **Step 6: Commit Task 4**

```powershell
git add Code/core/lineage/WesternLineageEligibilityRules.cs Code/core/lineage/LineageService.cs Tests/AncientWarfare3.Rules.Tests/NativeSiniticNamingRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/WesternLineageEligibilityRulesTests.cs.txt
git commit -m "feat: add native Sinitic species to common genealogy"
```

### Task 5: Integration, Verification, And Source Deployment

**Files:**
- Verify all files from Tasks 1-4
- Deploy production `.cs` files only to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`

- [ ] **Step 1: Review branch diff against the design**

Confirm ordinary marriages remain paternal, every species uses the same authority-tier matrilocal rule, dictionary surnames are copied rather than regenerated, all five species retain their current generator IDs, and no periodic world scan was added.

- [ ] **Step 2: Run fresh verification**

Run full rules, Release build, and `git diff --check`. Expected: all exit 0, 0 warnings, 0 errors.

- [ ] **Step 3: Merge the feature commits into master without staging unrelated dirty files**

Use non-interactive Git integration and resolve only overlapping lineage files. Re-run verification on `master` after integration.

- [ ] **Step 4: Deploy source files and compare SHA-256**

Copy only changed production `.cs` files. Verify each workspace/deployment pair has the same SHA-256; verify deployed `Assemblies` and `.runtime` still exist. Do not copy `bin/Release/AncientWarfare3.dll`.

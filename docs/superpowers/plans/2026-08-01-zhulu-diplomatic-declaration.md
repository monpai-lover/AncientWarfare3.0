# Zhulu Diplomatic Declaration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Zhulu-era AI prefer unification at a fixed 70 percent decision chance while requiring every Zhulu war to pass through the existing diplomatic notice and 1-3 year preparation pipeline.

**Architecture:** `ZhuluAgeDirectorService` remains the monthly authority for realm scoring and forced Mandate grants only. `WarDecisionAI` owns target selection and probability, while `DiplomaticWarDeclarationService` owns notice persistence, preparation, final revalidation, and creation of `zhulu_war`. Pure rules and source guards lock the 70 percent boundary and prohibit direct declaration calls from the age director or AI.

**Tech Stack:** C# mod sources loaded by WorldBox/NeoModLoader, AW3 rule-test console project, PowerShell source guards, Harmony-integrated runtime.

---

## File Map

- Modify `Code/core/lineage/ZhuluWarRules.cs`: fixed 70 percent intent rule.
- Modify `Code/core/lineage/WarDecisionQueueRules.cs`: admit `zhulu_annexation` when its intrinsic casus belli is valid.
- Modify `Code/core/lineage/DiplomaticWarDeclarationService.cs`: queue and execute Zhulu declarations through the normal notice ledger.
- Modify `Code/core/lineage/WarDecisionAI.cs`: use 70 percent for Zhulu and call the diplomatic helper in synchronous and asynchronous paths.
- Modify `Code/core/lineage/ZhuluAgeDirectorService.cs`: remove target selection and direct war creation; retain scoring and Mandate work.
- Modify `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`: pure boundary and integration source assertions.
- Modify `Tests/ZhuluWorldAgeSourceGuardTests.ps1`: forbid declaration ownership in the age director and require diplomatic ownership.

### Task 1: Lock the 70 Percent Rule and Diplomatic Queue Contract

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`
- Modify: `Code/core/lineage/ZhuluWarRules.cs`
- Modify: `Code/core/lineage/WarDecisionQueueRules.cs`

- [ ] **Step 1: Write failing pure-rule tests**

Add these assertions before `RuntimeRegistrationIsComplete()`:

```csharp
True(ZhuluWarRules.ShouldIssueDiplomaticDeclaration(0d),
    "the first roll issues a Zhulu declaration");
True(ZhuluWarRules.ShouldIssueDiplomaticDeclaration(.699999d),
    "rolls below 70 percent issue a Zhulu declaration");
False(ZhuluWarRules.ShouldIssueDiplomaticDeclaration(.70d),
    "the 70 percent boundary does not issue");
False(ZhuluWarRules.ShouldIssueDiplomaticDeclaration(1d),
    "rolls above the boundary do not issue");
False(ZhuluWarRules.ShouldIssueDiplomaticDeclaration(-.01d),
    "invalid negative rolls do not issue");

True(WarDecisionQueueRules.CanQueueGoal(
        ZhuluWarRules.GoalTypeId, pBasicAllowed: true,
        pHasNormalCb: true, pCanForceNoCb: false,
        pHasCoreTarget: false, pHasClaimTarget: false,
        pCanForceVassal: false, pCanForceTributary: false,
        pIsIndependenceTarget: false, pHasRestorationTarget: false,
        pCanReunifySuccession: false, out string zhuluReason),
    "a valid intrinsic Zhulu casus belli can enter the queue");
Equal("", zhuluReason,
    "an eligible Zhulu declaration has no queue failure");
False(WarDecisionQueueRules.CanQueueGoal(
        ZhuluWarRules.GoalTypeId, pBasicAllowed: true,
        pHasNormalCb: false, pCanForceNoCb: false,
        pHasCoreTarget: false, pHasClaimTarget: false,
        pCanForceVassal: false, pCanForceTributary: false,
        pIsIndependenceTarget: false, pHasRestorationTarget: false,
        pCanReunifySuccession: false, out zhuluReason),
    "Zhulu cannot queue without its intrinsic casus belli");
Equal("missing_zhulu_cb", zhuluReason,
    "Zhulu reports its own missing-casus-belli reason");
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --zhulu-war-slice
```

Expected: compilation fails because `ShouldIssueDiplomaticDeclaration` does not exist, or the queue assertions fail with `unknown_goal`.

- [ ] **Step 3: Implement the minimal pure rules**

Add to `ZhuluWarRules`:

```csharp
public const double DiplomaticDeclarationChance = .70d;

public static bool ShouldIssueDiplomaticDeclaration(double pRoll)
{
    return pRoll >= 0d && pRoll < DiplomaticDeclarationChance;
}
```

Add this switch arm to `WarDecisionQueueRules.CanQueueGoal`:

```csharp
case ZhuluWarRules.GoalTypeId:
    return Check(pHasNormalCb, "missing_zhulu_cb", out pReason);
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the command from Step 2. Expected: `Zhulu war rules passed.`

### Task 2: Add Zhulu to the Existing Diplomatic Notice Pipeline

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`
- Modify: `Code/core/lineage/DiplomaticWarDeclarationService.cs`

- [ ] **Step 1: Write failing integration source assertions**

In `RuntimeRegistrationIsComplete()`, read `DiplomaticWarDeclarationService.cs` and assert:

```csharp
True(declaration.Contains("public static bool IssueZhulu(") &&
     declaration.Contains("ZhuluWarRules.GoalTypeId") &&
     declaration.Contains("ZhuluWarRules.WarTypeId"),
    "Zhulu exposes a normal diplomatic notice entry point");
True(declaration.Contains("case ZhuluWarRules.GoalTypeId:") &&
     declaration.Contains("ZhuluWarService.CanDeclare("),
    "Zhulu is revalidated when preparation completes");
True(declaration.Contains(
         "ZhuluWarRules.GoalTypeId => ZhuluWarRules.WarTypeId") &&
     declaration.Contains(
         "ZhuluWarRules.GoalTypeId => ZhuluWarRules.GoalTypeId"),
    "Zhulu goal maps to its war type and reason");
False(declaration.Contains(
        "pGoalType == ZhuluWarRules.GoalTypeId"),
    "Zhulu remains a normal diplomatic goal rather than a system goal");
```

- [ ] **Step 2: Run the focused test and verify RED**

Run the Task 1 focused command. Expected: the new diplomatic source assertions fail.

- [ ] **Step 3: Add the diplomatic entry point and execution mapping**

Add the following public helper next to the existing `Issue` overloads:

```csharp
public static bool IssueZhulu(Kingdom pAttacker, Kingdom pDefender)
{
    City target = FindDisplayCity(pAttacker, pDefender,
        ZhuluWarRules.GoalTypeId);
    return Issue(pAttacker, pDefender, ZhuluWarRules.GoalTypeId,
        target, ZhuluWarRules.WarTypeId, ZhuluWarRules.GoalTypeId,
        HistoryLocalizationRules.Text("aw_war_goal_zhulu_annexation"));
}
```

Add this branch to `TryBuildExecutionPlan`:

```csharp
case ZhuluWarRules.GoalTypeId:
    if (!ZhuluWarService.CanDeclare(pAttacker, pDefender,
            out pFailureReason))
        return false;
    city = pDefender.capital ??
           WarTerritoryService.FindFirstTargetCity(pDefender);
    break;
```

Add these mappings without changing `IsSystemGoal`:

```csharp
ZhuluWarRules.GoalTypeId => ZhuluWarRules.WarTypeId,
```

in `WarTypeForGoal`, and:

```csharp
ZhuluWarRules.GoalTypeId => ZhuluWarRules.GoalTypeId,
```

in `ReasonKeyForGoal`.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Task 1 focused command. Expected: `Zhulu war rules passed.`

### Task 3: Remove Direct Declaration Ownership and Reroute Both AI Paths

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`
- Modify: `Tests/ZhuluWorldAgeSourceGuardTests.ps1`
- Modify: `Code/core/lineage/WarDecisionAI.cs`
- Modify: `Code/core/lineage/ZhuluAgeDirectorService.cs`

- [ ] **Step 1: Replace obsolete source expectations with failing guards**

Change the Zhulu AI assertion in `ZhuluWarRulesTests` to:

```csharp
True(warAi.Contains("WarStrategyCandidateKind.Zhulu") &&
     warAi.Contains("ShouldIssueDiplomaticDeclaration(") &&
     warAi.Contains("DiplomaticWarDeclarationService.IssueZhulu("),
    "sync and async Zhulu decisions use the diplomatic pipeline");
False(warAi.Contains("ZhuluWarService.TryDeclare("),
    "war AI never starts a Zhulu war directly");
```

In `ZhuluWorldAgeSourceGuardTests.ps1`, remove assertions requiring `pZhuluAgeOverride: true`, alliance withdrawal, target building, and per-realm declaration. Add:

```powershell
function Assert-NotContains([string] $Text, [string] $Needle,
    [string] $Message) {
    if ($Text.Contains($Needle)) { throw $Message }
}

Assert-NotContains $director 'ZhuluWarService.TryDeclare(' `
    'Zhulu age director starts wars directly instead of using diplomacy.'
Assert-NotContains $director 'DiplomaticWarDeclarationService.Issue' `
    'Zhulu age director owns declarations instead of scoring only.'
Assert-NotContains $director 'alliance.leave(' `
    'Zhulu age director changes alliances to force a war.'
Assert-Contains $director 'TryForceGrantMandateForZhuluAge' `
    'Zhulu age director no longer enforces the 2:1 Mandate result.'
```

- [ ] **Step 2: Run both focused guards and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --zhulu-war-slice
pwsh -NoProfile -File Tests/ZhuluWorldAgeSourceGuardTests.ps1
```

Expected: both fail because the AI and age director still call `ZhuluWarService.TryDeclare`.

- [ ] **Step 3: Apply fixed 70 percent logic in synchronous AI**

Move `selectedKind` before the chance check and use:

```csharp
bool shouldIssue = selectedKind == WarStrategyCandidateKind.Zhulu
    ? ZhuluWarRules.ShouldIssueDiplomaticDeclaration(Rng.NextDouble())
    : Chance(0.28f * WarMultiplier(pKingdom, target, court));
if (!shouldIssue) return AsyncStrategyAuthorityTrace.Planned(trace);
if (selectedKind == WarStrategyCandidateKind.Zhulu)
{
    if (DiplomaticWarDeclarationService.IssueZhulu(pKingdom, target))
        pKingdom.data.set(LAST_ACTION_YEAR, year);
    return AsyncStrategyAuthorityTrace.Planned(trace);
}
```

- [ ] **Step 4: Apply the same rule in asynchronous commit**

Replace the shared `.28f * WarMultiplier` chance with a branch:

```csharp
bool shouldIssue = pPlan.WarKind == WarStrategyCandidateKind.Zhulu
    ? ZhuluWarRules.ShouldIssueDiplomaticDeclaration(pPlan.Roll)
    : pPlan.Roll < Math.Max(0f, Math.Min(1f,
        .28f * WarMultiplier(source, target, court)));
if (!shouldIssue) return false;

if (pPlan.WarKind == WarStrategyCandidateKind.Zhulu)
{
    if (!DiplomaticWarDeclarationService.IssueZhulu(source, target))
        return false;
    source.data.set(LAST_ACTION_YEAR, pPlan.CaptureYear);
    KingdomStrategyRevisionService.MarkChanged(source.id, target.id);
    return true;
}
```

- [ ] **Step 5: Reduce the age director to scoring and Mandate work**

Make `ProcessMonth()` end after:

```csharp
List<RealmSnapshot> realms = BuildRealmSnapshots();
if (realms.Count == 0) return;
realms.Sort(CompareRealmRank);
TryGrantMandate(realms);
```

Delete `TargetCandidate`, `TargetSelection`, `ProcessRealmSafely`, `TryDeclareNext`, `BuildTargetCandidates`, alliance withdrawal, active-war checks, adjacency checks, and representative-distance helpers. Keep realm construction, recursive vassal scoring, ranking, lifecycle state, and bounded Mandate failure logging.

- [ ] **Step 6: Run both focused guards and verify GREEN**

Run the commands from Step 2. Expected:

```text
Zhulu war rules passed.
ZhuluWorldAgeSourceGuardTests: PASS
```

### Task 4: Full Verification and Source-Only Deployment

**Files:**
- Verify all files listed in the File Map.
- Deploy only changed source files to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`.

- [ ] **Step 1: Run the complete rule suite**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: `Rule tests passed.` with no Zhulu assertion failures.

- [ ] **Step 2: Run source guards and whitespace validation**

```powershell
pwsh -NoProfile -File Tests/ZhuluWorldAgeSourceGuardTests.ps1
git diff --check -- Code/core/lineage/ZhuluWarRules.cs Code/core/lineage/WarDecisionQueueRules.cs Code/core/lineage/DiplomaticWarDeclarationService.cs Code/core/lineage/WarDecisionAI.cs Code/core/lineage/ZhuluAgeDirectorService.cs Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt Tests/ZhuluWorldAgeSourceGuardTests.ps1
```

Expected: source guard PASS and `git diff --check` exit code 0.

- [ ] **Step 3: Audit direct declaration paths**

```powershell
rg -n "ZhuluWarService\.TryDeclare|DiplomaticWarDeclarationService\.IssueZhulu|ShouldIssueDiplomaticDeclaration" Code/core/lineage Tests
```

Expected: no `TryDeclare` call in `ZhuluAgeDirectorService.cs` or `WarDecisionAI.cs`; both AI paths call `IssueZhulu`; direct `TryDeclare` remains only as the lower-level war service API for non-AI callers and tests.

- [ ] **Step 4: Deploy source without DLL output**

Copy the five changed production `.cs` files into their matching paths under the game mod directory. Do not run the AW3 DLL build and do not copy `AncientWarfare3.dll`.

- [ ] **Step 5: Verify deployed source equality**

```powershell
$root = 'F:\WorldBox New Mod\AncientWarfare3.0'
$mod = 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
$files = @(
  'Code\core\lineage\ZhuluWarRules.cs',
  'Code\core\lineage\WarDecisionQueueRules.cs',
  'Code\core\lineage\DiplomaticWarDeclarationService.cs',
  'Code\core\lineage\WarDecisionAI.cs',
  'Code\core\lineage\ZhuluAgeDirectorService.cs'
)
$files | ForEach-Object {
  if ((Get-FileHash (Join-Path $root $_)).Hash -ne
      (Get-FileHash (Join-Path $mod $_)).Hash) {
    throw "Deployment mismatch: $_"
  }
}
```

Expected: exit code 0 and no mismatch.

## Runtime Acceptance Check

After launching WorldBox, switch to `age_zhulu` with at least four independent realms. Confirm that no war is created on era entry or by the next monthly score pass; later AI decisions create diplomatic notices, preparation lasts 1-3 years, and only then does `zhulu_war` appear. Repeated observations should show a strong preference rather than every eligible realm declaring every time.

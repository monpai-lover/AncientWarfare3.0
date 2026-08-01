# Consort Frequency And Alliance Distance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make eligible AI rulers obtain consorts often enough to be observable and reject new alliances between realms that are neither adjacent nor geographically close.

**Architecture:** Add pure household-priority and alliance-distance rules to the existing rules layer, then feed them with household preview and map adjacency facts in `DiplomacyProposalService`. Reuse the proposal availability and final execution checks so player, AI, asynchronous, and stale proposals share one policy.

**Tech Stack:** C# 9/.NET 9 standalone rules tests, WorldBox runtime API, CSV localization, PowerShell source guards.

---

### Task 1: Alliance distance rule

**Files:**
- Modify: `Code/core/lineage/DiplomacyProposalRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing boundary tests**

Add assertions proving adjacency overrides distance, 120 tiles is accepted, more than 120 is rejected with `alliance_too_distant`, and a missing capital returns `alliance_unavailable`:

```csharp
Equal("", DiplomacyProposalRules.AllianceDistanceFailure(
    sharesBorder: true, hasBothCapitals: true, capitalDistance: 500f),
    "bordering realms may ally regardless of capital distance");
Equal("", DiplomacyProposalRules.AllianceDistanceFailure(
    sharesBorder: false, hasBothCapitals: true, capitalDistance: 120f),
    "nearby non-bordering realms may ally across a narrow sea");
Equal("alliance_too_distant",
    DiplomacyProposalRules.AllianceDistanceFailure(
        sharesBorder: false, hasBothCapitals: true,
        capitalDistance: 120.01f),
    "remote realms cannot form a new alliance");
Equal("alliance_unavailable",
    DiplomacyProposalRules.AllianceDistanceFailure(
        sharesBorder: false, hasBothCapitals: false,
        capitalDistance: 0f),
    "alliance distance fails closed when a capital is missing");
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: compilation fails because `AllianceDistanceFailure` does not exist.

- [ ] **Step 3: Implement the pure rule**

Add:

```csharp
public const float MaximumNonBorderAllianceCapitalDistance = 120f;

public static string AllianceDistanceFailure(bool sharesBorder,
    bool hasBothCapitals, float capitalDistance)
{
    if (sharesBorder) return "";
    if (!hasBothCapitals) return "alliance_unavailable";
    return !float.IsNaN(capitalDistance) &&
           !float.IsInfinity(capitalDistance) && capitalDistance >= 0f &&
           capitalDistance <= MaximumNonBorderAllianceCapitalDistance
        ? ""
        : "alliance_too_distant";
}
```

- [ ] **Step 4: Run tests and verify GREEN**

Run the same `dotnet run` command. Expected: `Rule tests passed.`

### Task 2: Alliance runtime enforcement and localization

**Files:**
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Modify: `Code/ui/windows/DiplomacyConversationWindow.cs`
- Modify: `Locales/aw3_diplomacy.csv`
- Create: `Tests/AllianceDistanceSourceGuard.ps1`

- [ ] **Step 1: Write a failing source guard**

Create a PowerShell guard that asserts `AllianceExecutionFailure` calls both `KingdomAdjacency.AreDirectNeighbors` and `DiplomacyProposalRules.AllianceDistanceFailure`, and that the UI and CSV include `alliance_too_distant`.

```powershell
$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw "$root/Code/core/lineage/DiplomacyProposalService.cs"
$window = Get-Content -Raw "$root/Code/ui/windows/DiplomacyConversationWindow.cs"
$locale = Get-Content -Raw "$root/Locales/aw3_diplomacy.csv"
if ($service -notmatch 'KingdomAdjacency\.AreDirectNeighbors' -or
    $service -notmatch 'DiplomacyProposalRules\.AllianceDistanceFailure') {
    throw 'Alliance execution must enforce the shared distance rule.'
}
if ($window -notmatch '"alliance_too_distant"' -or
    $locale -notmatch '(?m)^aw_diplomacy_failure_alliance_too_distant,') {
    throw 'Alliance distance failure must be localized.'
}
```

- [ ] **Step 2: Run the guard and verify RED**

Run:

```powershell
pwsh -NoProfile -File Tests/AllianceDistanceSourceGuard.ps1
```

Expected: failure stating that runtime enforcement or localization is missing.

- [ ] **Step 3: Enforce distance at the common boundary**

At the start of `AllianceExecutionFailure`, after validating the kingdoms and before alliance membership checks, resolve both capitals, call `KingdomAdjacency.AreDirectNeighbors`, calculate `Toolbox.DistTile` only when both capitals exist, and return:

```csharp
string distanceFailure = DiplomacyProposalRules.AllianceDistanceFailure(
    sharesBorder, requesterCapital != null && responderCapital != null,
    capitalDistance);
if (!string.IsNullOrEmpty(distanceFailure)) return distanceFailure;
```

Keep the existing same-alliance fast path before this rule so an already-valid alliance remains usable even if geography later changes. Because `ReadContextCore`, `Execute`, and `TryFormOrJoinAlliance` already call `AllianceExecutionFailure`, no other alliance entry point is added.

- [ ] **Step 4: Add the UI reason and localization**

Add this switch arm:

```csharp
"alliance_too_distant" => AW_L10n.Text(
    "aw_diplomacy_failure_alliance_too_distant",
    "The realms are too distant to form an alliance"),
```

Add this CSV row:

```csv
aw_diplomacy_failure_alliance_too_distant,两国相距过远 无法建立盟约,The realms are too distant to form an alliance,兩國相距過遠 無法建立盟約
```

- [ ] **Step 5: Run guard and rules tests**

Expected: the PowerShell guard exits 0 and standalone rules end with `Rule tests passed.`

### Task 3: Household vacancy priority

**Files:**
- Modify: `Code/core/lineage/RulerHouseholdRules.cs`
- Modify: `Code/core/lineage/DiplomacyProposalAiRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing household rule tests**

Add tests for the relaxed relationship threshold and vacancy urgency:

```csharp
Equal(30, RulerHouseholdRules.MinimumConsortRequestOpinion,
    "friendly realms may request a consort without requiring an alliance-level opinion");
Equal(30, RulerHouseholdRules.AiProposalUrgency(
        hasPrincipalWife: true, activeConsorts: 0),
    "a ruler with no consort receives urgent household diplomacy");
Equal(30, RulerHouseholdRules.AiProposalUrgency(
        hasPrincipalWife: false, activeConsorts: 0),
    "a ruler without a principal spouse receives urgent household diplomacy");
Equal(0, RulerHouseholdRules.AiProposalUrgency(
        hasPrincipalWife: true, activeConsorts: 1),
    "household diplomacy returns to normal priority after the first consort");
```

Add AI scoring tests that construct an urgent household candidate and prove it outranks an equal-opinion alliance, while a non-urgent consort remains below it:

```csharp
var urgentHousehold = new DiplomacyProposalAiCandidate(
    DiplomacyProposalType.HouseholdOffering, true, 60, 1f, false,
    0f, false, urgency: 30);
var routineHousehold = new DiplomacyProposalAiCandidate(
    DiplomacyProposalType.HouseholdOffering, true, 60, 1f, false,
    0f, false, urgency: 0);
True(DiplomacyProposalAiRules.Score(urgentHousehold) >
     DiplomacyProposalAiRules.Score(allianceAction),
    "an empty ruler household outranks a routine alliance");
True(DiplomacyProposalAiRules.Score(routineHousehold) <=
     DiplomacyProposalAiRules.Score(allianceAction),
    "a populated household does not dominate routine diplomacy");
```

- [ ] **Step 2: Run tests and verify RED**

Expected: the threshold assertion and missing `AiProposalUrgency` fail.

- [ ] **Step 3: Implement minimum opinion and urgency**

Change the threshold to `30`, add the pure urgency method, and include `pCandidate.Urgency` in only the household score:

```csharp
public static int AiProposalUrgency(bool hasPrincipalWife,
    int activeConsorts)
{
    return !hasPrincipalWife || activeConsorts <= 0 ? 30 : 0;
}

DiplomacyProposalType.HouseholdOffering => 65 + opinion +
    (pCandidate.PrincipalHouseholdOffer ? 15 : 0) +
    Math.Max(0, pCandidate.Urgency),
```

- [ ] **Step 4: Run tests and verify GREEN**

Run the complete standalone rules project. Expected: `Rule tests passed.`

### Task 4: Feed household urgency into both planners

**Files:**
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Create: `Tests/HouseholdDiplomacyPrioritySourceGuard.ps1`

- [ ] **Step 1: Write a failing source guard**

Assert that all six candidate builders for ordinary offers, upper-realm offers, and consort requests in both synchronous and read-only paths assign urgency via `RulerHouseholdRules.AiProposalUrgency` using their preview's `HasPrincipalWife` and `ActiveConsorts` values.

- [ ] **Step 2: Run the guard and verify RED**

Run:

```powershell
pwsh -NoProfile -File Tests/HouseholdDiplomacyPrioritySourceGuard.ps1
```

Expected: failure because household candidates currently leave urgency at zero.

- [ ] **Step 3: Populate urgency in all candidate paths**

Pass this named constructor argument from each household preview:

```csharp
urgency: RulerHouseholdRules.AiProposalUrgency(
    preview.HasPrincipalWife, preview.ActiveConsorts)
```

For consort-request previews, pass `hasPrincipalWife: true` and the existing `ActiveConsorts` value. A request is always for a consort, so zero active consorts alone supplies the intended urgency without another database query or model extension.

- [ ] **Step 4: Run source guard and complete rules suite**

Expected: both commands exit 0 and rules print `Rule tests passed.`

### Task 5: Final verification and selective deployment

**Files:**
- Deploy only changed production files to `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run whitespace and focused diff checks**

```powershell
git diff --check -- Code/core/lineage/DiplomacyProposalRules.cs Code/core/lineage/DiplomacyProposalAiRules.cs Code/core/lineage/RulerHouseholdRules.cs Code/core/lineage/DiplomacyProposalService.cs Code/ui/windows/DiplomacyConversationWindow.cs Locales/aw3_diplomacy.csv Tests/AncientWarfare3.Rules.Tests/Program.cs.txt Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt Tests/AllianceDistanceSourceGuard.ps1 Tests/HouseholdDiplomacyPrioritySourceGuard.ps1
```

Expected: no output.

- [ ] **Step 2: Run all verification commands once more**

Run the complete standalone rules test project and both source guards. Do not build `AncientWarfare3.csproj` and do not produce a mod DLL.

- [ ] **Step 3: Copy only production files**

Copy the six changed production/localization files, preserving relative paths. Do not delete or mirror the whole mod directory.

- [ ] **Step 4: Verify deployment hashes**

For every copied file, compare `Get-FileHash -Algorithm SHA256` between workspace and game mod directory. Expected: every pair is equal.

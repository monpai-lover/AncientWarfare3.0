# Succession Split Opinion And War-Goal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a native `宗统分裂 -100` opinion modifier between the two courts of one live succession split and give their legal reunification goal a bounded AI preference.

**Architecture:** Add pure pair/opinion rules to `SuccessionDisputeRules`, expose them through the existing hot succession projection, and register a native WorldBox `OpinionAsset` so UI and AI consume one value. Extend the existing war-goal context with a split-pair fact and apply a bounded bonus only to an already legal `reunify_succession` option; the three-generation claim gate remains authoritative.

**Tech Stack:** C# 9 / .NET Framework 4.8, WorldBox opinion assets, existing AW3 succession projection, PowerShell source guards, .NET 9 rules harness.

---

### Task 1: Pure Opposed-Court Opinion Rules

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/SuccessionDisputeRulesTests.cs.txt`
- Modify: `Code/core/lineage/SuccessionDisputeRules.cs`

- [ ] **Step 1: Add failing opposed-court tests**

Append these assertions after the existing materialization cases:

```csharp
Equal(-100, SuccessionDisputeRules.OpposedCourtOpinion(
        SuccessionDisputeStatus.Active, pFirstKingdomId: 10,
        pSecondKingdomId: 20, pOriginalKingdomId: 10,
        pRivalKingdomId: 20, pOriginalCityCount: 3,
        pRivalCityCount: 2),
    "active materialized rival courts receive the dynastic split penalty");
Equal(-100, SuccessionDisputeRules.OpposedCourtOpinion(
        SuccessionDisputeStatus.PermanentSplit, pFirstKingdomId: 20,
        pSecondKingdomId: 10, pOriginalKingdomId: 10,
        pRivalKingdomId: 20, pOriginalCityCount: 3,
        pRivalCityCount: 2),
    "the permanent split penalty is symmetric and does not expire");
Equal(0, SuccessionDisputeRules.OpposedCourtOpinion(
        SuccessionDisputeStatus.Closed, 10, 20, 10, 20, 3, 2),
    "a closed dispute removes the penalty");
Equal(0, SuccessionDisputeRules.OpposedCourtOpinion(
        SuccessionDisputeStatus.Active, 10, 20, 10, 20, 3, 0),
    "a destroyed rival court is not a materialized split");
Equal(0, SuccessionDisputeRules.OpposedCourtOpinion(
        SuccessionDisputeStatus.Active, 10, 30, 10, 20, 3, 2),
    "an unrelated kingdom receives no split penalty");
Equal(0, SuccessionDisputeRules.OpposedCourtOpinion(
        SuccessionDisputeStatus.Active, 10, 10, 10, 20, 3, 2),
    "a kingdom never receives the penalty against itself");
```

- [ ] **Step 2: Run the inheritance slice and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Debug -- --inheritance-slice
```

Expected: compilation fails because `OpposedCourtOpinion` does not exist.

- [ ] **Step 3: Implement the pure pair and opinion rules**

Add beside `IsMaterialized`:

```csharp
public const int OpposedCourtOpinionPenalty = -100;

public static bool AreOpposedCourts(SuccessionDisputeStatus pStatus,
    long pFirstKingdomId, long pSecondKingdomId,
    long pOriginalKingdomId, long pRivalKingdomId,
    int pOriginalCityCount, int pRivalCityCount)
{
    if (pFirstKingdomId < 0 || pSecondKingdomId < 0 ||
        pFirstKingdomId == pSecondKingdomId ||
        pOriginalKingdomId < 0 || pRivalKingdomId < 0 ||
        !IsMaterialized(pStatus, pRivalKingdomId,
            pOriginalCityCount, pRivalCityCount)) return false;
    return pFirstKingdomId == pOriginalKingdomId &&
           pSecondKingdomId == pRivalKingdomId ||
           pFirstKingdomId == pRivalKingdomId &&
           pSecondKingdomId == pOriginalKingdomId;
}

public static int OpposedCourtOpinion(SuccessionDisputeStatus pStatus,
    long pFirstKingdomId, long pSecondKingdomId,
    long pOriginalKingdomId, long pRivalKingdomId,
    int pOriginalCityCount, int pRivalCityCount)
{
    return AreOpposedCourts(pStatus, pFirstKingdomId, pSecondKingdomId,
        pOriginalKingdomId, pRivalKingdomId, pOriginalCityCount,
        pRivalCityCount) ? OpposedCourtOpinionPenalty : 0;
}
```

- [ ] **Step 4: Re-run the inheritance slice and verify GREEN**

Expected: `AW3 inheritance and succession rules passed.`

- [ ] **Step 5: Commit the pure rule**

```powershell
git add Code/core/lineage/SuccessionDisputeRules.cs Tests/AncientWarfare3.Rules.Tests/SuccessionDisputeRulesTests.cs.txt
git commit -m "feat: define succession split opinion penalty"
```

### Task 2: Native Opinion Asset And Runtime Projection

**Files:**
- Modify: `Code/core/lineage/SuccessionDisputeService.cs`
- Modify: `Code/core/lineage/RitualDiplomacyOpinionCallbacks.cs`
- Modify: `Code/core/lineage/RitualDiplomacyOpinionService.cs`
- Modify: `Locales/others.csv`
- Create: `Tests/SuccessionSplitOpinionSourceGuard.ps1`

- [ ] **Step 1: Write a failing source guard**

Create a guard that loads the five production files and requires all of these
literal integration points:

```powershell
$service = Get-Content -Raw 'Code/core/lineage/SuccessionDisputeService.cs'
$callbacks = Get-Content -Raw 'Code/core/lineage/RitualDiplomacyOpinionCallbacks.cs'
$assets = Get-Content -Raw 'Code/core/lineage/RitualDiplomacyOpinionService.cs'
$locale = Get-Content -Raw -Encoding utf8 'Locales/others.csv'

$required = @(
  @($service, 'ReadOpposedCourtOpinion'),
  @($service, 'OpposedCourtOpinion'),
  @($callbacks, 'SuccessionSplit'),
  @($assets, 'aw_opinion_succession_split'),
  @($assets, 'opinion_aw_succession_split'),
  @($locale, 'opinion_aw_succession_split,宗统分裂,Dynastic split,宗統分裂')
)
foreach ($entry in $required) {
  if (-not $entry[0].Contains($entry[1])) {
    throw "Missing succession split opinion integration: $($entry[1])"
  }
}
if ($service.Contains('DiplomaticRelationModifierService.Upsert')) {
  throw 'Succession split opinion must not create a duplicated DB modifier.'
}
Write-Host 'Succession split opinion source guard passed.'
```

- [ ] **Step 2: Run the guard and verify RED**

Run:

```powershell
& .\Tests\SuccessionSplitOpinionSourceGuard.ps1
```

Expected: failure names `ReadOpposedCourtOpinion`.

- [ ] **Step 3: Expose the runtime opinion through the hot dispute projection**

Add to `SuccessionDisputeService` near `TryGetMaterializedByKingdom`:

```csharp
public static int ReadOpposedCourtOpinion(Kingdom pFirst,
    Kingdom pSecond)
{
    if (pFirst?.data == null || pSecond?.data == null ||
        pFirst == pSecond ||
        !TryGetCachedByKingdom(pFirst.id,
            out SuccessionDisputeSnapshot row)) return 0;
    return SuccessionDisputeRules.OpposedCourtOpinion(row.Status,
        pFirst.id, pSecond.id, row.OriginalKingdomId,
        row.RivalKingdomId,
        CountLiveCities(FindKingdom(row.OriginalKingdomId)),
        CountLiveCities(FindKingdom(row.RivalKingdomId)));
}
```

This rechecks live territorial materialization and performs no write.

- [ ] **Step 4: Register the native opinion callback**

Add to `RitualDiplomacyOpinionCallbacks`:

```csharp
public static int SuccessionSplit(Kingdom pMain, Kingdom pTarget)
{
    return SuccessionDisputeService.ReadOpposedCourtOpinion(
        pMain, pTarget);
}
```

Add to `RegisterAssets`:

```csharp
AddOpinion("aw_opinion_succession_split",
    "opinion_aw_succession_split", "opinion_aw_succession_split",
    RitualDiplomacyOpinionCallbacks.SuccessionSplit);
```

- [ ] **Step 5: Add the localization row**

Append to `Locales/others.csv`:

```csv
opinion_aw_succession_split,宗统分裂,Dynastic split,宗統分裂
```

- [ ] **Step 6: Run the source guard and build**

```powershell
& .\Tests\SuccessionSplitOpinionSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Debug --no-restore
```

Expected: guard passes; build completes with zero warnings and zero errors.

- [ ] **Step 7: Commit native opinion integration**

```powershell
git add Code/core/lineage/SuccessionDisputeService.cs Code/core/lineage/RitualDiplomacyOpinionCallbacks.cs Code/core/lineage/RitualDiplomacyOpinionService.cs Locales/others.csv
git add -f Tests/SuccessionSplitOpinionSourceGuard.ps1
git commit -m "feat: show succession split diplomatic hostility"
```

### Task 3: Bounded Reunification War-Goal Preference

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarAiGoalSelectionRulesTests.cs.txt`
- Modify: `Code/core/lineage/WarAiGoalSelectionRules.cs`
- Modify: `Code/core/lineage/WarDecisionAI.cs`
- Modify: `Tests/SuccessionSplitOpinionSourceGuard.ps1`

- [ ] **Step 1: Add failing bounded-preference tests**

Call a new `SuccessionSplitPrefersReunificationWithoutForcingIt()` method from
`Run`, then add:

```csharp
private static void SuccessionSplitPrefersReunificationWithoutForcingIt()
{
    var split = new WarAiGoalContext(
        directlyAdjacent: true, attackerIsSubject: false,
        targetIsIndependent: true, diplomaticBlocked: false,
        attackerToTargetPowerRatio: 1.5f, targetCityCount: 3,
        attackerCentralization: 2, attackerExpansionism: .7f,
        courtWar: .7f, courtPeace: .2f,
        currentSubjectCount: 1, subjectSoftCap: 6,
        opposedSuccessionBranches: true);
    Equal("reunify_succession", WarAiGoalSelectionRules.SelectBestGoal(
        new[]
        {
            new WarAiGoalCandidate("reunify_succession", 90),
            new WarAiGoalCandidate("force_vassal", 130)
        }, WarAiPeopleRelation.SameCulture, split),
        "opposed courts prefer whole-realm reunification over a nearby indirect rule score");
    Equal("take_mandate", WarAiGoalSelectionRules.SelectBestGoal(
        new[]
        {
            new WarAiGoalCandidate("reunify_succession", 90),
            new WarAiGoalCandidate("take_mandate", 260)
        }, WarAiPeopleRelation.SameCulture, split),
        "the succession preference does not override a materially stronger legal objective");
}
```

- [ ] **Step 2: Run the war-AI slice and verify RED**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Debug -- --war-ai-slice
```

Expected: compilation fails because `opposedSuccessionBranches` is absent.

- [ ] **Step 3: Add the context fact and bounded bonus**

Append `bool opposedSuccessionBranches = false` to the `WarAiGoalContext`
constructor, assign it, and expose:

```csharp
public bool OpposedSuccessionBranches { get; }
```

Add to `WarAiGoalSelectionRules`:

```csharp
public const int SuccessionReunificationPreference = 60;

private static int SuccessionAdjustment(string pGoalType,
    WarAiGoalContext pContext)
{
    return pContext.OpposedSuccessionBranches &&
           pGoalType == "reunify_succession"
        ? SuccessionReunificationPreference
        : 0;
}
```

In the context overload of `StrategicScore`, add this adjustment to the score
before the existing subjugation adjustment. Do not change `IsEligible`,
`CanUseReunificationClaim`, or any declaration gate.

- [ ] **Step 4: Wire the runtime fact**

Append this named argument in `WarDecisionAI.BuildGoalContext`:

```csharp
opposedSuccessionBranches:
    SuccessionDisputeService.ReadOpposedCourtOpinion(
        pSource, pTarget) < 0
```

- [ ] **Step 5: Extend the source guard**

Require `OpposedSuccessionBranches`,
`SuccessionReunificationPreference`, and the named runtime argument. Reject
changes that make `reunify_succession` unconditionally eligible.

- [ ] **Step 6: Run both focused slices and the source guard**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Debug -- --war-ai-slice
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Debug -- --inheritance-slice
& .\Tests\SuccessionSplitOpinionSourceGuard.ps1
```

Expected: both slices and the guard pass.

- [ ] **Step 7: Commit the AI preference**

```powershell
git add Code/core/lineage/WarAiGoalSelectionRules.cs Code/core/lineage/WarDecisionAI.cs Tests/AncientWarfare3.Rules.Tests/WarAiGoalSelectionRulesTests.cs.txt
git add -f Tests/SuccessionSplitOpinionSourceGuard.ps1
git commit -m "feat: prefer bounded succession reunification wars"
```

### Task 4: Full Verification, Scoped Deployment, And Runtime Acceptance

**Files:**
- Create: `docs/superpowers/deploy/2026-07-28-succession-split-opinion-and-war-goal.txt`
- Deploy only the production files changed in Tasks 1-3 to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`

- [ ] **Step 1: Run fresh focused and full verification**

```powershell
& .\Tests\SuccessionSplitOpinionSourceGuard.ps1
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Debug -- --inheritance-slice
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Debug -- --war-ai-slice
dotnet build AncientWarfare3.csproj -c Debug --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: all commands exit zero with zero build warnings and errors. Run the
complete Rules harness separately; if an unrelated RTS/war test still fails,
record its exact expected/actual values without changing that subsystem.

- [ ] **Step 2: Record and preserve installed runtime state**

Before copying, calculate the installed `.runtime` recursive file count and a
stable aggregate SHA-256 digest. Never copy, delete, recreate, or clean
`.runtime`, and never use `save8`.

- [ ] **Step 3: Perform scoped deployment**

Copy only:

```text
Code/core/lineage/SuccessionDisputeRules.cs
Code/core/lineage/SuccessionDisputeService.cs
Code/core/lineage/RitualDiplomacyOpinionCallbacks.cs
Code/core/lineage/RitualDiplomacyOpinionService.cs
Code/core/lineage/WarAiGoalSelectionRules.cs
Code/core/lineage/WarDecisionAI.cs
Locales/others.csv
```

Recalculate `.runtime` and require exact count and digest equality. Run Debug
and Release builds in the installed mod.

- [ ] **Step 4: Verify the existing split after restart**

Launch the latest approved autosave, not `save8`. For the currently visible
North/South split, require the original diplomacy opinion breakdown to show:

```text
宗统分裂  -100
```

Verify the total opinion includes the penalty, both directions are symmetric,
and an unrelated kingdom has no such row.

- [ ] **Step 5: Verify closure and AI behavior**

Use a disposable test save or automated rule evidence to confirm that closing
the dispute removes the modifier. Inspect a legal three-generation split pair
and confirm `reunify_succession` is selected over nearby vassal/claim choices,
while the expired fourth generation receives no free reunification option.

- [ ] **Step 6: Check logs and scoped diff**

Require no new AW3 compilation, opinion callback, succession projection,
SQLite, or async-write error in `Player.log`. Run:

```powershell
git diff --check -- Code/core/lineage/SuccessionDisputeRules.cs Code/core/lineage/SuccessionDisputeService.cs Code/core/lineage/RitualDiplomacyOpinionCallbacks.cs Code/core/lineage/RitualDiplomacyOpinionService.cs Code/core/lineage/WarAiGoalSelectionRules.cs Code/core/lineage/WarDecisionAI.cs Locales/others.csv Tests/AncientWarfare3.Rules.Tests/SuccessionDisputeRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/WarAiGoalSelectionRulesTests.cs.txt Tests/SuccessionSplitOpinionSourceGuard.ps1
```

- [ ] **Step 7: Record deployment evidence**

Write exact test outputs, build results, deployed paths, runtime digest before
and after, autosave path, process ID, and runtime acceptance result to the
deployment manifest. Commit only that manifest:

```powershell
git add -f docs/superpowers/deploy/2026-07-28-succession-split-opinion-and-war-goal.txt
git commit -m "test: record succession split rivalry deployment"
```

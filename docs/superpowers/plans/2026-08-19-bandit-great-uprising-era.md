# Bandit Great Uprising Era Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert all bandit kingdoms originating from a realm into independent peasant-rebel kingdoms when bandit population reaches 5% of the realm population during long-term corruption or famine.

**Architecture:** Add a pure rule layer for ratios, streaks and bounded cursors; add a persisted realm coordinator that builds one annual bandit-origin index, tracks corruption/famine streaks and calls the existing `PeasantRebelRouteService.ConvertBanditToFounding` one kingdom at a time. Wire the coordinator after mandate state is evaluated, preserving current bandit, restoration and extinction ownership boundaries.

**Tech Stack:** C#/.NET Framework 4.8, Unity runtime types, `Kingdom.data` persistence, existing annual kingdom work queue, existing rule-test executable.

---

### Task 1: Add failing pure-rule coverage

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Create: `Tests/AncientWarfare3.Rules.Tests/BanditGreatUprisingRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add the test source before the production rule file is linked**

Add tests for these exact contracts:

```csharp
True(BanditGreatUprisingRules.MeetsBanditRatio(5, 100), "5 percent activates");
False(BanditGreatUprisingRules.MeetsBanditRatio(4, 100), "below threshold stays inactive");
True(BanditGreatUprisingRules.MeetsBanditRatio(1, 0), "zero origin population is safe");
True(BanditGreatUprisingRules.ShouldActivate(5, 100, 5, 0), "corruption streak activates");
True(BanditGreatUprisingRules.ShouldActivate(5, 100, 0, 2), "famine streak activates");
False(BanditGreatUprisingRules.ShouldActivate(5, 100, 4, 1), "short streaks do not activate");
Equal(5, BanditGreatUprisingRules.AdvanceStreak(4, true, 5), "streak caps");
Equal(0, BanditGreatUprisingRules.AdvanceStreak(4, false, 5), "broken streak resets");
Equal(0, BanditGreatUprisingRules.AdvanceCursor(3, 2, 5), "cursor wraps");
True(BanditGreatUprisingRules.CanConvert(true, true, true), "active origin converts bandit");
False(BanditGreatUprisingRules.CanConvert(false, true, true), "inactive origin does not convert");
```

- [ ] **Step 2: Link the new test and production file, then register `Run()`**

Add the test file to the existing rules-test project and invoke
`BanditGreatUprisingRulesTests.Run()` from `Program.cs.txt`. Do not add the
production rules link yet; the first run must fail because the production
type is intentionally absent.

- [ ] **Step 3: Run the rules executable and verify the intended failure**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected: compilation fails because `BanditGreatUprisingRules` is not yet defined.

- [ ] **Step 4: Commit the failing test scaffold**

```powershell
git add -- Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/BanditGreatUprisingRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "test: define bandit uprising rule contracts"
```

### Task 2: Implement pure rules and persistence keys

**Files:**
- Modify: `Code/core/lineage/BanditGreatUprisingRules.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Implement the minimal rule API**

Implement constants and methods with no Unity or world access:

```csharp
public const float BanditPopulationRatioThreshold = 0.05f;
public const int CorruptionStreakYears = 5;
public const int FamineStreakYears = 2;
public const int ConversionBudgetPerYear = 4;

public static bool MeetsBanditRatio(int banditPopulation, int originPopulation);
public static bool ShouldActivate(int banditPopulation, int originPopulation,
    int corruptionStreak, int famineStreak);
public static int AdvanceStreak(int current, bool condition, int cap);
public static int AdvanceCursor(int current, int processed, int count);
public static bool CanConvert(bool uprisingActive, bool banditRoute,
    bool originValid);
```

Use `double` for the ratio comparison, clamp negative inputs to zero, and
use `max(1, originPopulation)` to avoid division by zero.

- [ ] **Step 2: Add persisted key constants**

Add these `LineageKeys` constants:

```csharp
MANDATE_REBEL_GREAT_UPRISING_ACTIVE
MANDATE_REBEL_GREAT_UPRISING_STARTED_YEAR
MANDATE_REBEL_GREAT_UPRISING_LAST_YEAR
MANDATE_REBEL_GREAT_UPRISING_CORRUPTION_STREAK
MANDATE_REBEL_GREAT_UPRISING_FAMINE_STREAK
MANDATE_REBEL_GREAT_UPRISING_CONVERSION_CURSOR
MANDATE_REBEL_GREAT_UPRISING_LAST_CONVERSION_YEAR
```

Add `Code/core/lineage/BanditGreatUprisingRules.cs` as the linked production
compile item in the rules-test project.

- [ ] **Step 3: Run tests and production build**

Expected: all rules pass and `AncientWarfare3.csproj` builds with zero
warnings and errors.

- [ ] **Step 4: Commit**

```powershell
git add -- Code/core/lineage/BanditGreatUprisingRules.cs Code/core/lineage/LineageKeys.cs Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/BanditGreatUprisingRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: add bandit uprising activation rules"
```

### Task 3: Add annual bandit-origin index and realm state coordinator

**Files:**
- Create: `Code/core/lineage/BanditGreatUprisingService.cs`
- Modify: `Code/core/policy/KingdomAnnualWorkService.cs`

- [ ] **Step 1: Add a once-per-year origin index**

Implement a private runtime cache keyed by origin kingdom ID:

```csharp
private static int _indexYear = int.MinValue;
private static readonly Dictionary<long, List<long>> _banditsByOrigin =
    new Dictionary<long, List<long>>();
```

`RebuildIndexIfNeeded(int year)` must iterate `World.world.kingdoms` once,
select `PeasantRebelRouteService.IsBandit(kingdom)`, resolve the origin with
`PeasantRebelRouteService.ResolveOrigin(kingdom)`, and append the bandit ID.
It must skip disposed kingdoms and invalid origins. `ClearRuntime()` resets
the cache for world reloads.

- [ ] **Step 2: Implement defensive population and condition snapshots**

Add private helpers that sum:

- origin city population via `getPopulationPeople()`;
- bandit live non-boat actors via `getUnits()`;
- hungry population via `city.status.hungry`.

Use the current `MandateService.ReadReport()` and
`MandatePhaseService.CurrentPhase` for the approved corruption proxy. A
corruption condition is true when mandate value or authority is <= 30, or
the phase is Decline/Chaos. Famine is true at >= 30% hungry population.

- [ ] **Step 3: Implement persisted annual evaluation**

`OnKingdomYear(Kingdom pKingdom)` must:

1. return for replicas, non-civilizations, neutral, disposed or duplicate
   years;
2. load streaks and active state from `Kingdom.data`;
3. update streaks with `AdvanceStreak`;
4. activate once when `ShouldActivate` returns true;
5. process at most `ConversionBudgetPerYear` bandit IDs using a cursor;
6. persist the updated year, streaks, active flag, cursor and last conversion
   year.

The method must not mutate `World.world.kingdoms` directly.

- [ ] **Step 4: Wire after mandate annual work**

Call `BanditGreatUprisingService.OnKingdomYear(pKingdom)` immediately after
`MandateRebelService.OnKingdomYear(pKingdom)` and before ordinary bandit
spawning in `RunStrategyMandate`.

- [ ] **Step 5: Commit coordinator wiring**

```powershell
git add -- Code/core/lineage/BanditGreatUprisingService.cs Code/core/policy/KingdomAnnualWorkService.cs
git commit -m "feat: evaluate realm bandit uprising eras annually"
```

### Task 4: Convert each bandit independently through the existing route

**Files:**
- Modify: `Code/core/lineage/BanditGreatUprisingService.cs`
- Modify: `Code/core/lineage/PeasantRebelRouteService.cs` only if a narrow
  internal helper is required for origin validation

- [ ] **Step 1: Add candidate validation**

For each candidate ID, resolve the kingdom and origin again, require
`PeasantRebelRouteService.IsBandit(candidate)`, require the origin to match
the currently evaluated kingdom, and skip disposed or neutral candidates.

- [ ] **Step 2: Reuse the conversion entry point**

Call:

```csharp
PeasantRebelRouteService.ConvertBanditToFounding(candidate, pKingdom)
```

Do not set `POLICY_CLASS_STATE` directly and do not call
`World.world.kingdoms.removeObject` from this coordinator. Catch exceptions
per candidate, log the candidate ID and reason, then advance the cursor.

- [ ] **Step 3: Make repeated annual passes idempotent**

A successful conversion is no longer returned by
`IsBandit(candidate)` on the next pass. The cursor must still advance across
failed candidates so later candidates are attempted.

- [ ] **Step 4: Commit**

```powershell
git add -- Code/core/lineage/BanditGreatUprisingService.cs Code/core/lineage/PeasantRebelRouteService.cs
git commit -m "feat: convert origin bandits into independent rebel kingdoms"
```

### Task 5: Add source guards and regression tests

**Files:**
- Create: `Tests/BanditGreatUprisingSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/BanditGreatUprisingRulesTests.cs.txt`

- [ ] **Step 1: Add source guard assertions**

The guard must assert that the coordinator contains:

- the replica and duplicate-year gates;
- `PeasantRebelRouteService.ConvertBanditToFounding`;
- `ConversionBudgetPerYear`;
- no `World.world.kingdoms.removeObject` call;
- annual wiring in `KingdomAnnualWorkService`.

- [ ] **Step 2: Add pure regression cases**

Cover ratio boundaries, streak resets, activation idempotence, invalid-origin
filtering, cursor progress and independent-candidate eligibility.

- [ ] **Step 3: Run all focused checks**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
powershell -ExecutionPolicy Bypass -File Tests\BanditGreatUprisingSourceGuard.ps1
dotnet build AncientWarfare3.csproj --no-restore
git diff --check
```

Expected: all tests and guards pass; production build reports zero warnings
and zero errors.

- [ ] **Step 4: Review uncommitted user changes and commit only feature files**

```powershell
git status --short
git add -- Code/core/lineage/BanditGreatUprisingRules.cs Code/core/lineage/BanditGreatUprisingService.cs Code/core/lineage/LineageKeys.cs Code/core/policy/KingdomAnnualWorkService.cs Code/core/lineage/PeasantRebelRouteService.cs Tests/AncientWarfare3.Rules.Tests/BanditGreatUprisingRulesTests.cs.txt Tests/BanditGreatUprisingSourceGuard.ps1
git commit -m "test: guard bandit great uprising integration"
```

Do not stage the pre-existing performance plan or unrelated bandit survivor
fix files unless they are explicitly part of the current feature diff.

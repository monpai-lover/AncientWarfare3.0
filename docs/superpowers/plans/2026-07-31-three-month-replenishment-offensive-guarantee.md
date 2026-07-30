# Three-Month Replenishment and Offensive Guarantee Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add four actor-backed conscription laws, guarantee that each approved ordinary-army shortage is processed within three game months, and keep at least one viable ordinary army attacking whenever national ordinary manpower permits.

**Architecture:** The existing auxiliary-law service owns the selected conscription tier, while a focused pure rules file defines capacity and AI preference. `CityReservePoolService` adds a deterministic eligible-civilian index and remains the sole owner of reserve membership. A new `ArmyReplenishmentOperationService` persists one immutable operation on each army, consumes indexed actors progressively, and delegates completion to a small ordinary-army consolidation service; the RTS controller only opens requests and the war director only restores strategic assignments.

**Tech Stack:** C#, Harmony patches over WorldBox runtime APIs, AW3 authority-cycle services, WorldBox actor/army custom data, .NET 9 isolated rules tests, PowerShell source guards, and the .NET Framework 4.8 mod build.

---

## File Map

New focused production units:

- `Code/core/court/CourtConscriptionLawRules.cs`: four tiers, percentages, and pure AI scoring.
- `Code/core/lineage/ArmyReplenishmentOperationRules.cs`: immutable shortage, three-month progress, restore clamping, and completion decisions.
- `Code/core/lineage/ArmyReplenishmentOperationService.cs`: persistent per-army operation orchestration and indexed actor conversion.
- `Code/core/lineage/ArmyReplenishmentCompletionService.cs`: ordinary-army merge selection and offensive continuity.

Existing owners to extend:

- `CourtAuxiliaryLawRules/Service/Window`: law kind, persistence, AI evaluation, and UI.
- `CityReservePoolRules/Service`: eligible-civilian capacity and law reconciliation.
- `TemporaryLevyService`: preparation enlistment and one shared real-actor conversion entry point.
- `AW_WarPatch`: final refill, freeze, and formal mobilization ordering.
- `AWAuthorityCycleService` and restore pipeline: authoritative processing and recovery.
- `ArmyMapInformationRules/Service`: separate shortage and reserve-supply display.
- multiplayer strategic projection: read-only client display of authoritative shortage/supply.

### Task 1: Define Conscription-Law Rules

**Files:**
- Create: `Code/core/court/CourtConscriptionLawRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/CourtConscriptionLawRulesTests.cs.txt`
- Modify: `Code/core/court/CourtAuxiliaryLawRules.cs:20-32,58-64,174-177`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing conscription-law rules tests**

Create `CourtConscriptionLawRulesTests.cs.txt`:

```csharp
using System;

internal static class CourtConscriptionLawRulesTests
{
    internal static void Run()
    {
        Equal(CourtConscriptionLaw.Standard,
            CourtConscriptionLawRules.DefaultLaw,
            "missing saves use the fifty-percent tier");
        Equal(30, CourtConscriptionLawRules.ReservePercent(
            CourtConscriptionLaw.Limited), "limited share");
        Equal(50, CourtConscriptionLawRules.ReservePercent(
            CourtConscriptionLaw.Standard), "standard share");
        Equal(70, CourtConscriptionLawRules.ReservePercent(
            CourtConscriptionLaw.Expanded), "expanded share");
        Equal(100, CourtConscriptionLawRules.ReservePercent(
            CourtConscriptionLaw.FullMobilization), "full share");
        Equal(0, CourtConscriptionLawRules.Capacity(-4, 70),
            "negative eligibility cannot create capacity");
        Equal(21, CourtConscriptionLawRules.Capacity(31, 70),
            "capacity floors the selected share");

        Greater(
            Score(CourtConscriptionLaw.Limited, "nong", 0.9f, 0.8f,
                0.1f, 0.1f, false, false, false),
            Score(CourtConscriptionLaw.Expanded, "nong", 0.9f, 0.8f,
                0.1f, 0.1f, false, false, false),
            "livelihood agrarian courts prefer limited service");
        Greater(
            Score(CourtConscriptionLaw.Expanded, "bing", 0.2f, 0.1f,
                0.9f, 0.9f, false, false, false),
            Score(CourtConscriptionLaw.Standard, "bing", 0.2f, 0.1f,
                0.9f, 0.9f, false, false, false),
            "military aggressive courts prefer expanded service");
        Greater(
            Score(CourtConscriptionLaw.FullMobilization, "fa", 0.2f,
                0.1f, 0.9f, 0.9f, true, true, true),
            Score(CourtConscriptionLaw.Expanded, "fa", 0.2f,
                0.1f, 0.9f, 0.9f, true, true, true),
            "existential capital defense permits full mobilization");
        Greater(
            Score(CourtConscriptionLaw.Standard, "", 0.5f, 0.5f,
                0.5f, 0.5f, false, false, false),
            Score(CourtConscriptionLaw.FullMobilization, "", 0.5f, 0.5f,
                0.5f, 0.5f, false, false, false),
            "full mobilization is not a peacetime default");
        Equal(4, CourtAuxiliaryLawRules.OptionCount(
            CourtAuxiliaryLawKind.Conscription),
            "conscription exposes four options");
    }

    private static int Score(CourtConscriptionLaw law, string school,
        float livelihood, float peace, float war, float aggression,
        bool existential, bool capitalThreat, bool severeDisadvantage)
    {
        return CourtConscriptionLawRules.Score(law, school, livelihood,
            peace, war, aggression, existential, capitalThreat,
            severeDisadvantage);
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(name + ": expected " +
                expected + ", got " + actual);
    }

    private static void Greater(int left, int right, string name)
    {
        if (left <= right)
            throw new InvalidOperationException(name + ": " + left +
                " <= " + right);
    }
}
```

Link the production and test files in the rules-test project. Add a
`--conscription-law-slice` branch in `Program.cs.txt` that calls `Run()` and
prints `AW3 conscription law rules passed.`.

- [ ] **Step 2: Run the slice and verify RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --conscription-law-slice
```

Expected: compilation fails because `CourtConscriptionLawRules` and the new
law kind do not exist.

- [ ] **Step 3: Implement the minimal pure law model**

Create this public surface in `CourtConscriptionLawRules.cs`:

```csharp
namespace AncientWarfare3.core.court
{
    public enum CourtConscriptionLaw
    {
        Limited,
        Standard,
        Expanded,
        FullMobilization
    }

    public static class CourtConscriptionLawRules
    {
        public const CourtConscriptionLaw DefaultLaw =
            CourtConscriptionLaw.Standard;

        public static int ReservePercent(CourtConscriptionLaw law)
        {
            return law switch
            {
                CourtConscriptionLaw.Limited => 30,
                CourtConscriptionLaw.Expanded => 70,
                CourtConscriptionLaw.FullMobilization => 100,
                _ => 50
            };
        }

        public static int Capacity(int eligibleCivilians, int percent)
        {
            long eligible = System.Math.Max(0, eligibleCivilians);
            long share = System.Math.Max(0, System.Math.Min(100, percent));
            return (int)System.Math.Min(int.MaxValue,
                eligible * share / 100L);
        }

        public static int Score(CourtConscriptionLaw law,
            string dominantSchool, float livelihood, float peace,
            float war, float aggression, bool existentialDefense,
            bool capitalThreat, bool severeDisadvantage)
        {
            float life = Clamp01(livelihood);
            float calm = Clamp01(peace);
            float martial = Clamp01(war) + Clamp01(aggression);
            bool restraint = dominantSchool == CourtSchoolId.Agrarian ||
                dominantSchool == CourtSchoolId.Dao ||
                dominantSchool == CourtSchoolId.Medical;
            bool hardLine = dominantSchool == CourtSchoolId.Military ||
                dominantSchool == CourtSchoolId.Legalist;
            bool emergency = existentialDefense || capitalThreat ||
                severeDisadvantage;
            return law switch
            {
                CourtConscriptionLaw.Limited => 30 +
                    Round((life + calm) * 35f) + (restraint ? 35 : 0) -
                    (emergency ? 100 : 0),
                CourtConscriptionLaw.Expanded => 35 +
                    Round(martial * 35f) + (hardLine ? 35 : 0) +
                    (emergency ? 20 : 0),
                CourtConscriptionLaw.FullMobilization => emergency
                    ? 145 + (hardLine ? 20 : 0)
                    : -120,
                _ => 75
            };
        }

        private static float Clamp01(float value) =>
            System.Math.Max(0f, System.Math.Min(1f, value));
        private static int Round(float value) => (int)System.Math.Round(
            value, System.MidpointRounding.AwayFromZero);
    }
}
```

Add `Conscription` to `CourtAuxiliaryLawKind`. Change `OptionCount` so Term
and Conscription return four while the other kinds return three.

- [ ] **Step 4: Run GREEN and commit**

Run the focused slice again. Expected: the success line and exit code 0.

```powershell
git add Code/core/court/CourtConscriptionLawRules.cs Code/core/court/CourtAuxiliaryLawRules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: define conscription law tiers"
```

### Task 2: Persist and Present the Fourth Auxiliary Law

**Files:**
- Modify: `Code/core/lineage/LineageKeys.cs:380-405`
- Modify: `Code/core/court/CourtAuxiliaryLawService.cs:9-135,196-375`
- Modify: `Code/ui/windows/CourtAuxiliaryLawWindow.cs:16-23,87-173,535-620`
- Modify: `Code/core/lineage/ChronicleEvents.cs:850-885`
- Modify: `Code/core/lineage/HistoryLocalizationRules.cs:375-390`
- Modify: `locales/aw3_court.csv:466-506`
- Create: `Tests/ConscriptionLawSourceGuardTests.ps1`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add a failing persistence/UI source guard**

Require the exact ownership boundaries:

```powershell
Require $keys 'COURT_CONSCRIPTION_LAW = "aw_court_conscription_law"' `
    'the selected tier must survive save/load'
Require $keys 'COURT_CONSCRIPTION_LAW_LAST_CHANGE_YEAR' `
    'conscription has an independent cooldown'
Require $service 'public static CourtConscriptionLaw GetConscriptionLaw' `
    'the service must expose the missing-key default'
Require $service 'CityReservePoolService.OnConscriptionLawChanged(' `
    'a successful law change must reconcile uncommitted reserves'
Require $window 'CreateLawSection(CourtAuxiliaryLawKind.Conscription, 4)' `
    'the auxiliary window must contain the fourth section'
Require $history 'CourtAuxiliaryLawKind.Conscription =>' `
    'history must name the conscription section'
Require $locales 'aw_court_aux_law_conscription,' `
    'the section title must be localized'
Require $locales 'aw_court_conscription_full_desc,' `
    'full mobilization semantics must be localized'
```

Register the guard near the other court guards in `Tests/SourceGuardTests.ps1`.

- [ ] **Step 2: Run the guard and verify RED**

```powershell
powershell -ExecutionPolicy Bypass -File Tests/ConscriptionLawSourceGuardTests.ps1
```

Expected: failures for all new keys and UI/localization entries.

- [ ] **Step 3: Extend persistence, change handling, and AI evaluation**

Add kingdom keys for value and last-change year. Extend every switch in
`CourtAuxiliaryLawService` (`Get`, `IsValidChoice`, `CurrentValue`, `ValueKey`,
`LastChangeKey`) with the fourth kind. Missing or invalid values resolve to
`CourtConscriptionLawRules.DefaultLaw`.

After the law data write succeeds, call:

```csharp
if (pKind == CourtAuxiliaryLawKind.Conscription)
    CityReservePoolService.OnConscriptionLawChanged(
        pKingdom, (CourtConscriptionLaw)previousValue,
        (CourtConscriptionLaw)pDesiredValue);
```

Add conscription to `TryEvaluateAi`. Read `court.dominant_school`,
`court.livelihood`, `court.peace`, `court.war`, and `court.aggression`.
Set `existentialDefense` from an active military emergency, set
`capitalThreat` when any active war's `WarMilitaryFacts.CapitalThreatened` is
true, and set `severeDisadvantage` when current ordinary military potential is
less than half the enemy-side potential. Score all four candidates through
`CourtConscriptionLawRules.Score` and use the existing `Consider` path, cost,
cooldown, and political reserve.

- [ ] **Step 4: Add the fourth UI section and localization**

Increase `_sections` capacity to four, create the Conscription section with
four buttons, and derive fixed content height from section count instead of
the old `390f` constant:

```csharp
float fixedHeight = 66f + _sections.Count * 108f;
```

Extend `CurrentValue`, `KindName`, `ValueName`, and `ValueDescription` for
Limited, Standard, Expanded, and Full mobilization. Add Simplified Chinese,
English, and Traditional Chinese CSV values. The full-mobilization description
must explicitly say it registers all eligible civilians but does not
immediately make all of them soldiers. Update the auxiliary-entry description
to mention conscription.

Extend `ChronicleEvents.OnCourtAuxiliaryLawChanged` and
`HistoryLocalizationRules` with the new law kind and four values.

- [ ] **Step 5: Run GREEN and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --conscription-law-slice
powershell -ExecutionPolicy Bypass -File Tests/ConscriptionLawSourceGuardTests.ps1
git add Code/core/court Code/core/lineage/LineageKeys.cs Code/core/lineage/ChronicleEvents.cs Code/core/lineage/HistoryLocalizationRules.cs Code/ui/windows/CourtAuxiliaryLawWindow.cs locales/aw3_court.csv Tests
git commit -m "feat: add conscription auxiliary law"
```

Expected: both focused commands pass before committing.

### Task 3: Make Reserve Capacity Actor-Backed and Law-Driven

**Files:**
- Modify: `Code/core/lineage/CityReservePoolRules.cs`
- Modify: `Code/core/lineage/CityReservePoolService.cs`
- Modify: `Code/patch/AW_CityReservePoolPatch.cs`
- Modify: `Code/patch/AW_EnlistPatch.cs`
- Modify: `Code/core/lineage/TemporaryMilitaryDemobilizationService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CityReservePoolRulesTests.cs.txt`
- Modify: `Tests/CityReservePoolLifecycleSourceGuardTests.ps1`

- [ ] **Step 1: Replace the old capacity tests with failing law-capacity tests**

Replace assertions tied to `effectiveWarriorSlots` with:

```csharp
Equal(15, CityReservePoolRules.Capacity(50, 30),
    "limited law registers thirty percent of eligible civilians");
Equal(25, CityReservePoolRules.Capacity(50, 50),
    "standard law registers half of eligible civilians");
Equal(35, CityReservePoolRules.Capacity(50, 70),
    "expanded law registers seventy percent of eligible civilians");
Equal(50, CityReservePoolRules.Capacity(50, 100),
    "full law registers every eligible civilian");
Equal(true, CityReservePoolRules.ShouldAddForLawChange(
    frozen: true, oldPercent: 50, newPercent: 70),
    "raising the law is the only frozen-pool addition path");
Equal(false, CityReservePoolRules.ShouldAddForLawChange(
    frozen: true, oldPercent: 70, newPercent: 30),
    "lowering a wartime law cannot add candidates");
Equal(20, CityReservePoolRules.RequiredRemovalCount(
    memberCount: 35, capacity: 15),
    "lowering removes only excess registrations");
```

- [ ] **Step 2: Extend the lifecycle guard and verify RED**

Require `SortedSet<long> EligibleActorIds`,
`CourtAuxiliaryLawService.GetConscriptionLaw`,
`OnActorReturnedToCivilian`, and deterministic removal from the end of the
member set. Reject the old `EffectiveWarriorSlots(city, kingdom)` capacity
call. Run the reserve slice and lifecycle guard; expect failures.

- [ ] **Step 3: Add the eligible-civilian index**

Extend each `CityPool`:

```csharp
internal readonly SortedSet<long> EligibleActorIds =
    new SortedSet<long>();
internal readonly SortedSet<long> ActorIds = new SortedSet<long>();
```

`OnActorBecameAdult` validates the actor once, adds the ID to
`EligibleActorIds`, and then reconciles membership to:

```csharp
int percent = CourtConscriptionLawRules.ReservePercent(
    CourtAuxiliaryLawService.GetConscriptionLaw(kingdom));
int capacity = CityReservePoolRules.Capacity(
    pool.EligibleActorIds.Count, percent);
```

Death, migration, kingdom transfer, profession change, and enlistment remove
the ID from both indexes. Add `OnActorReturnedToCivilian` and call it from the
shared demobilization service after profession and city are restored.

When lowering capacity, remove highest actor IDs until `ActorIds.Count <=
capacity`; clear only reserve-membership fields. Active warriors are absent
from `EligibleActorIds`, so no active soldier is dismissed.

During `RebuildRuntime`, reconstruct eligible IDs from living city residents
and reconstruct reserve members from persisted membership fields. Validate in
bounded chunks after load; no presentation frame owns this work.

- [ ] **Step 4: Implement explicit wartime law reconciliation**

Add:

```csharp
internal static void OnConscriptionLawChanged(Kingdom kingdom,
    CourtConscriptionLaw previous, CourtConscriptionLaw current);
```

For a decrease, shrink all city pools immediately using indexed IDs. For an
increase, enqueue each city for authority-cycle reconciliation; this explicit
queue may add eligible IDs while frozen, but ordinary adulthood/maintenance
still cannot add members during war. Keep city and actor work budgets.

- [ ] **Step 5: Run GREEN and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --city-reserve-pool-slice
powershell -ExecutionPolicy Bypass -File Tests/CityReservePoolLifecycleSourceGuardTests.ps1
git add Code/core/lineage/CityReservePoolRules.cs Code/core/lineage/CityReservePoolService.cs Code/core/lineage/TemporaryMilitaryDemobilizationService.cs Code/patch/AW_CityReservePoolPatch.cs Code/patch/AW_EnlistPatch.cs Tests
git commit -m "feat: size reserve pools by conscription law"
```

### Task 4: Recruit During Preparation and Finalize Before Freeze

**Files:**
- Modify: `Code/core/lineage/CityReservePoolService.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs:477-932,1372-1525`
- Modify: `Code/patch/AW_WarPatch.cs:80-105`
- Modify: `Tests/CityReserveRecruitmentSourceGuardTests.ps1`
- Modify: `Tests/CityReservePoolLifecycleSourceGuardTests.ps1`

- [ ] **Step 1: Rewrite the obsolete failing source contract**

Remove the assertion that preparation must not convert civilians. Require:

```powershell
Require $preparationRegion 'CityReservePoolService.TryConsumePreparationBatch(' `
    'preparation must consume registered actor IDs'
Require $preparationRegion 'ApprovedTargetShortage(' `
    'preparation cannot exceed an approved establishment shortage'
Reject $preparationRegion 'ScanCity(' `
    'preparation cannot rescan arbitrary residents for soldiers'
Require $warPatch 'CityReservePoolService.CompletePreWarReconciliation(__result)' `
    'formal war creation performs the final indexed refill'
```

Assert source order:

```text
CompletePreWarReconciliation
OnWarStarted
TemporaryLevyService.OnWarStarted
```

- [ ] **Step 2: Run both guards and verify RED**

```powershell
powershell -ExecutionPolicy Bypass -File Tests/CityReserveRecruitmentSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/CityReservePoolLifecycleSourceGuardTests.ps1
```

Expected: the preparation and pre-war reconciliation requirements fail.

- [ ] **Step 3: Add indexed preparation consumption**

Add `TryConsumePreparationBatch` beside `TryConsumeBatch`. It accepts only a
live active war notice, an unfrozen pool, a controlled donor city, a positive
approved shortage, and a live ordinary target army. It removes member fields
atomically and returns real actors ordered by preferred city, distance, city
ID, and actor ID. It never scans `city.units`.

Change `ProcessPreparationRecruitment` so each approved deployment target
consumes at most its current `ApprovedTargetShortage`. Extract the conversion
currently used by casualty recovery into:

```csharp
internal static int EnlistReserveActors(Kingdom kingdom, City source,
    Army targetArmy, IReadOnlyList<Actor> candidates,
    bool preparationRecruitment);
```

Count only successful `makeWarrior`/army assignments. The enlist patch removes
any stale membership; biography and temporary-levy fields remain unchanged.

- [ ] **Step 4: Add the final pre-war indexed refill**

`CompletePreWarReconciliation(War)` visits all participant city-pool indexes,
computes current law capacity from `EligibleActorIds`, and fills membership
from those indexed IDs before freeze. It does not scan residents. Call it in
`AW_WarPatch` immediately before `OnWarStarted`; preserve the current rule that
freeze precedes formal levy conversion.

- [ ] **Step 5: Run GREEN and commit**

Run both guards plus the reserve slice. Then commit:

```powershell
git add Code/core/lineage/CityReservePoolService.cs Code/core/lineage/TemporaryLevyService.cs Code/patch/AW_WarPatch.cs Tests
git commit -m "feat: recruit reserves during war preparation"
```

### Task 5: Define and Persist a Three-Month Army Operation

**Files:**
- Create: `Code/core/lineage/ArmyReplenishmentOperationRules.cs`
- Create: `Code/core/lineage/ArmyReplenishmentOperationService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyReplenishmentOperationRulesTests.cs.txt`
- Modify: `Code/core/lineage/LineageKeys.cs:160-190`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing operation rules tests**

Create the test file:

```csharp
using System;

internal static class ArmyReplenishmentOperationRulesTests
{
    internal static void Run()
    {
        Equal(15d, ArmyReplenishmentOperationRules.DurationWorldSeconds,
            "three game months equal fifteen world seconds");
        Equal(9, ArmyReplenishmentOperationRules.ResolveApprovedShortage(
            existingApproved: 9, requestedShortage: 20),
            "a repeated request cannot enlarge approval");
        Equal(20, ArmyReplenishmentOperationRules.ResolveApprovedShortage(
            existingApproved: 0, requestedShortage: 20),
            "the first request records its full shortage");
        Equal(3, ArmyReplenishmentOperationRules.AllowedCumulative(
            approved: 9, start: 100d, now: 105d),
            "one month allows one third");
        Equal(6, ArmyReplenishmentOperationRules.AllowedCumulative(
            approved: 9, start: 100d, now: 110d),
            "two months allow two thirds");
        Equal(9, ArmyReplenishmentOperationRules.AllowedCumulative(
            approved: 9, start: 100d, now: 115d),
            "three months force all approved actors");
        Equal(2, ArmyReplenishmentOperationRules.BatchRequest(
            approved: 9, enlisted: 4, liveShortage: 7,
            start: 100d, now: 110d),
            "only accrued unfilled approval is consumed");
        Equal(true, ArmyReplenishmentOperationRules.ShouldFinishEarly(
            liveShortage: 0), "a filled shortage finishes immediately");
        Equal(9, ArmyReplenishmentOperationRules.ClampEnlisted(
            approved: 9, persistedEnlisted: 40),
            "restore cannot overstate progress");
        Equal(115d, ArmyReplenishmentOperationRules.ResolveDeadline(
            start: 100d, persistedDeadline: 120d),
            "restore cannot extend the three-month deadline");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(name + ": expected " +
                expected + ", got " + actual);
    }
}
```

Link the production/test files and add `--three-month-replenishment-slice`.

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --three-month-replenishment-slice
```

Expected: missing-type compilation failure.

- [ ] **Step 3: Implement pure progress rules**

Create the rules with this surface:

```csharp
public static class ArmyReplenishmentOperationRules
{
    public const int SchemaVersion = 1;
    public const double DurationWorldSeconds = 15d;
    public const int MaximumOperationsPerCycle = 8;

    public static int ResolveApprovedShortage(int existingApproved,
        int requestedShortage) => existingApproved > 0
        ? existingApproved : System.Math.Max(0, requestedShortage);

    public static double ResolveDeadline(double start,
        double persistedDeadline) => System.Math.Min(
            start + DurationWorldSeconds,
            persistedDeadline < start ? start + DurationWorldSeconds :
                persistedDeadline);

    public static int AllowedCumulative(int approved, double start,
        double now)
    {
        if (approved <= 0 || now <= start) return 0;
        double progress = System.Math.Min(1d,
            (now - start) / DurationWorldSeconds);
        return System.Math.Min(approved,
            (int)System.Math.Floor(approved * progress + 0.000001d));
    }

    public static int BatchRequest(int approved, int enlisted,
        int liveShortage, double start, double now) =>
        System.Math.Min(System.Math.Max(0, liveShortage),
            System.Math.Max(0, AllowedCumulative(approved, start, now) -
                System.Math.Max(0, enlisted)));

    public static bool ShouldFinishEarly(int liveShortage) =>
        liveShortage <= 0;
    public static int ClampEnlisted(int approved, int persistedEnlisted) =>
        System.Math.Max(0, System.Math.Min(
            System.Math.Max(0, approved), persistedEnlisted));
}
```

Use an explicit deadline branch in `AllowedCumulative` so floating-point
rounding at or after `start + 15` always returns full approval.

- [ ] **Step 4: Add versioned army-data keys and operation persistence**

Add keys for version, kingdom ID, source city ID, approved shortage, enlisted
count, start world time, and deadline world time. Store times as invariant
strings to retain double precision because WorldBox custom float data loses
precision in old worlds. `ArmyReplenishmentOperationService.TryRead` parses
with `CultureInfo.InvariantCulture`, validates schema/army/kingdom/war, clamps
counts, and never moves the deadline later. `Clear` removes every operation
key.

`Ensure` creates state only when no valid operation exists. A repeated call
returns the existing immutable approval and times. At this task, do not yet
consume actors.

- [ ] **Step 5: Run GREEN and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --three-month-replenishment-slice
git add Code/core/lineage/ArmyReplenishmentOperationRules.cs Code/core/lineage/ArmyReplenishmentOperationService.cs Code/core/lineage/LineageKeys.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: persist bounded army replenishment"
```

### Task 6: Convert Indexed Actors Progressively Within Three Months

**Files:**
- Modify: `Code/core/lineage/ArmyReplenishmentOperationService.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs:253-330,1240-1525`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs:2453-2491,2591-2621`
- Modify: `Code/core/lineage/ArmyStrategicIndexService.cs:1-45`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs:35-50,60-82`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs:184-210,350-370`
- Modify: `Code/patch/AW_WarPatch.cs:210-240`
- Create: `Tests/ArmyReplenishmentOperationSourceGuardTests.ps1`
- Modify: `Tests/CityReserveRecruitmentSourceGuardTests.ps1`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add a failing orchestration guard**

Require:

```powershell
Require $controller 'ArmyReplenishmentOperationService.Ensure(' `
    'RTS replenishment opens one durable operation'
Reject $controller 'TemporaryLevyService.RequestOffensiveRecovery(' `
    'RTS cannot bypass the three-month operation'
Require $authority 'ArmyReplenishmentOperationService.ProcessAuthorityCycle' `
    'conversion must run on authoritative simulation cycles'
Require $restore 'new AW3RestoreStage("army_replenishment_operations"' `
    'save/load must restore immutable operations'
Require $operation 'CityReservePoolService.TryConsumeBatch(' `
    'operations consume indexed real actors'
Require $operation 'TemporaryLevyService.EnlistReserveActors(' `
    'operations reuse one actor conversion path'
Require $operation 'ArmyReplenishmentOperationRules.BatchRequest(' `
    'conversion is proportional and bounded'
Require $operation 'ArmyRtsControllerService.TryTeleportReinforcementMember' `
    'successful recruits join the formation immediately'
Require $strategicIndex 'ArmyReplenishmentOperationService.OnArmyDisposed(' `
    'army disposal must close its operation'
Require $strategicIndex 'ArmyReplenishmentOperationService.OnArmyKingdomChanged(' `
    'ownership changes must invalidate foreign approval'
Require $warPatch 'ArmyReplenishmentOperationService.OnWarEnded(' `
    'war end must close participant operations'
Reject $operation 'foreach (Actor actor in city.units)' `
    'wartime replenishment cannot scan live residents'
```

- [ ] **Step 2: Run RED**

Run the new source guard, city reserve recruitment guard, and three-month rule
slice. Expected: source guards fail while the pure slice remains green.

- [ ] **Step 3: Route RTS requests into durable operations**

In `UpdateReplenishmentRequest`, replace the direct offensive-recovery call
with:

```csharp
ArmyReplenishmentOperationService.Ensure(pArmy, kingdom, preferredCity,
    missingStrength, CurrentWorldTime());
```

Remove the old readiness-stall reset as an operation deadline. RTS may retain
its visual progress watchdog, but it must query `IsDepartureReleased(army)` so
it cannot reset or enlarge the authoritative three-month operation.

- [ ] **Step 4: Implement bounded authority-cycle conversion**

`ProcessAuthorityCycle` rotates at most eight active army IDs. For each valid
operation:

1. Calculate live target shortage from the persisted RTS mission target.
2. Clear immediately when shortage is zero.
3. Compute `BatchRequest` from current world time.
4. Consume at most that request through `TryConsumeBatch`.
5. Convert with `EnlistReserveActors` and increment only successful actors.
6. Teleport each successful recruit and queue `KingdomWarDirectorService`.
7. At or after the deadline, request all remaining approved shortage in the
   same settlement; no further operation tick is allowed afterward.

If indexed supply is insufficient, record confirmed exhaustion but close the
operation at deadline. Invalid actors are removed by the pool service and do
not increment enlisted count.

Add the authority call under `KingdomMobilizationIndex`, reset its runtime
cursor with the other military services, and add a restore stage after city
reserve pools and RTS mission restore. Replica sessions do not process the
service because the existing authority gate rejects them.

Wire `OnArmyDisposed` and `OnArmyKingdomChanged` through
`ArmyStrategicIndexService`, and call `OnWarEnded` from `AW_WarPatch` before
temporary levies demobilize. These paths clear operation keys and runtime
indexes without consuming another actor. The authority cycle also drops an
operation when its army, kingdom, or referenced active war can no longer be
validated.

- [ ] **Step 5: Run GREEN and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --three-month-replenishment-slice
powershell -ExecutionPolicy Bypass -File Tests/ArmyReplenishmentOperationSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/CityReserveRecruitmentSourceGuardTests.ps1
git add Code/core/lineage/ArmyReplenishmentOperationService.cs Code/core/lineage/TemporaryLevyService.cs Code/core/lineage/ArmyRtsControllerService.cs Code/core/lineage/ArmyStrategicIndexService.cs Code/core/performance/AWAuthorityCycleService.cs Code/core/multiplayer/AW3RuntimeRestorePipeline.cs Code/patch/AW_WarPatch.cs Tests
git commit -m "feat: replenish armies over three months"
```

### Task 7: Merge Weak Ordinary Armies and Guarantee an Attack

**Files:**
- Create: `Code/core/lineage/ArmyReplenishmentCompletionService.cs`
- Modify: `Code/core/lineage/ArmyReplenishmentOperationRules.cs`
- Modify: `Code/core/lineage/ArmyReplenishmentOperationService.cs`
- Modify: `Code/core/lineage/AWArmyService.cs:830-875`
- Modify: `Code/core/lineage/KingdomWarDirectorRules.cs:400-470,610-640`
- Modify: `Code/core/lineage/KingdomWarDirectorService.cs:620-850`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyReplenishmentOperationRulesTests.cs.txt`
- Create: `Tests/ArmyReplenishmentCompletionSourceGuardTests.ps1`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing completion rules**

Add:

```csharp
Equal(true, ArmyReplenishmentOperationRules.ShouldResumeAttack(
    living: 2, minimum: 2),
    "a viable army leaves at the deadline");
Equal(true, ArmyReplenishmentOperationRules.ShouldMergeSecondary(
    living: 1, minimum: 2, ordinary: true, primaryExists: true),
    "a weak ordinary secondary merges");
Equal(false, ArmyReplenishmentOperationRules.ShouldMergeSecondary(
    living: 1, minimum: 2, ordinary: false, primaryExists: true),
    "special forces never merge");
Equal(true, ArmyReplenishmentOperationRules.MustMaintainAttack(
    totalOrdinary: 2, minimum: 2, validEnemyTarget: true),
    "viable national strength preserves one attack");
Equal(false, ArmyReplenishmentOperationRules.MustMaintainAttack(
    totalOrdinary: 1, minimum: 2, validEnemyTarget: true),
    "one actor is not sent alone");
```

- [ ] **Step 2: Add the failing completion source guard and run RED**

Require the completion service to reject `AWArmyService.IsSpecialArmy`,
royal guards, slave vanguards, dedicated garrisons, and restoration armies;
require stable selection ordered by living count descending then army ID;
require `QueueArmyChanged`; require the director to call
`EnsureOffensiveContinuity`. Run the rule slice and guard; expect failures.

- [ ] **Step 3: Expose a safe ordinary-army merge**

Refactor the private duplicate merge into a shared internal method:

```csharp
internal static bool TryMergeOrdinaryArmyInto(Army source, Army target)
```

Reject null/dead armies, different kingdoms, identical IDs, and any special,
royal-guard, dedicated-garrison, slave-vanguard, or restoration marker. Move
real actors through `AddToArmy` under `ArmyCaptainDisposalScope`, preserve a
valid target captain, invalidate the source operation/mission, dispose the
empty source without requesting a replacement, and queue the target kingdom.

- [ ] **Step 4: Implement deterministic completion and offensive continuity**

At early completion or deadline, `ArmyReplenishmentCompletionService`:

- resumes a viable army's saved attack mission immediately;
- otherwise chooses the largest viable ordinary assault army, tie by ID;
- if no viable primary exists but national ordinary total is viable, merges
  weak ordinary armies into the largest stable candidate until it is viable;
- never merges excluded special armies;
- never dispatches a one-actor army.

Add `EnsureOffensiveContinuity(Kingdom)` to the war director. If the kingdom
has a live enemy city and ordinary living force is at least
`MinimumOperationalForce`, but no viable ordinary army has an Attack proposal,
assign the highest-force eligible army to the best currently open enemy-city
target. Preserve player orders and existing capital-defense assignments when
another viable ordinary army can attack. Call this after each normal director
plan and after replenishment completion.

- [ ] **Step 5: Run GREEN and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --three-month-replenishment-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-command-slice
powershell -ExecutionPolicy Bypass -File Tests/ArmyReplenishmentCompletionSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/ReplacementArmyCommandSourceGuardTests.ps1
git add Code/core/lineage/ArmyReplenishmentOperationRules.cs Code/core/lineage/ArmyReplenishmentOperationService.cs Code/core/lineage/ArmyReplenishmentCompletionService.cs Code/core/lineage/AWArmyService.cs Code/core/lineage/KingdomWarDirectorRules.cs Code/core/lineage/KingdomWarDirectorService.cs Tests
git commit -m "fix: keep viable ordinary armies attacking"
```

### Task 8: Show Shortage and Reserve Supply Separately

**Files:**
- Modify: `Code/core/lineage/ArmyMapInformationRules.cs:35-65`
- Modify: `Code/core/presentation/ArmyMapInformationService.cs:300-350`
- Modify: `Code/api/multiplayer/AW3MultiplayerStrategicStateModels.cs`
- Modify: `Code/core/multiplayer/AW3MultiplayerStrategicStateCoordinator.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyMapInformationRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AW3MultiplayerStrategicStateRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/ArmyMapInformationMinimapSourceGuardTests.ps1`
- Modify: `locales/aw3_army_rts.csv`

- [ ] **Step 1: Add failing presentation tests**

Add pure assertions:

```csharp
Equal("12 / 27", ArmyMapInformationRules.ComposeManpowerText(
    shortage: 12, reserveSupply: 27),
    "shortage and supply remain distinct");
Equal("0 / 5", ArmyMapInformationRules.ComposeManpowerText(
    shortage: -3, reserveSupply: 5),
    "display clamps invalid counts");
```

Extend strategic projection tests so `ReplenishmentShortage` and
`KingdomReserveAvailable` reject negatives and survive capture/apply. Add a
`--multiplayer-strategic-state-slice` branch in `Program.cs.txt` that runs
`AW3MultiplayerStrategicStateRulesTests.Run()` and prints
`AW3 multiplayer strategic state rules passed.`.

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --army-map-information-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --multiplayer-strategic-state-slice
powershell -ExecutionPolicy Bypass -File Tests/ArmyMapInformationMinimapSourceGuardTests.ps1
```

Expected: missing new projection properties and compose method.

- [ ] **Step 3: Add authoritative and replica read models**

Add the two non-negative integer properties to
`AW3MultiplayerArmyProjection`. The authoritative store captures live shortage
from mission target minus living members and supply from
`CityReservePoolService.CountAvailable(kingdom)`. Replica apply writes both to
dedicated army-data read keys; it does not mutate reserve membership or start
an operation.

In local authoritative play, `ArmyMapInformationService` reads the live
operation shortage and kingdom reserve count. In replica play, it reads the
projected army-data values. Pass both into the pure formatter. Preserve the
existing 24-army pool and eight-entry-per-frame refresh budget.

- [ ] **Step 4: Localize the two labels and run GREEN**

Add `aw_army_replenishment_shortage` and `aw_army_reserve_supply` in all three
CSV languages. Compose the line as localized label/value pairs; do not call
localization from the pure rules type.

Run the three commands from Step 2 and commit:

```powershell
git add Code/core/lineage/ArmyMapInformationRules.cs Code/core/presentation/ArmyMapInformationService.cs Code/api/multiplayer/AW3MultiplayerStrategicStateModels.cs Code/core/multiplayer/AW3MultiplayerStrategicStateCoordinator.cs locales/aw3_army_rts.csv Tests
git commit -m "feat: display army shortage and reserve supply"
```

### Task 9: Full Verification, Integration, Deployment, and Autosave Acceptance

**Files:**
- Verify: `docs/superpowers/specs/2026-07-31-three-month-replenishment-offensive-guarantee-design.md`
- Verify: `AncientWarfare3.csproj`
- Deploy changed runtime files to: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run every focused rule slice**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --conscription-law-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --city-reserve-pool-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --three-month-replenishment-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-replenishment-arrival-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-wartime-lifecycle-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-command-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --army-map-information-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --multiplayer-strategic-state-slice
```

Expected: every command exits 0 with its slice success message.

- [ ] **Step 2: Run focused and complete source guards**

Run the three new guards, both existing reserve guards, replacement-army guard,
RTS scheduling guard, army map guard, then the aggregate:

```powershell
powershell -ExecutionPolicy Bypass -File Tests/ConscriptionLawSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/ArmyReplenishmentOperationSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/ArmyReplenishmentCompletionSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/CityReservePoolLifecycleSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/CityReserveRecruitmentSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/ReplacementArmyCommandSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/ArmyRtsSchedulingSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/ArmyMapInformationMinimapSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/SourceGuardTests.ps1
```

Expected: every guard exits 0.

- [ ] **Step 3: Run simulations, full rules, and both builds**

```powershell
dotnet run --project Tests/ArmyRtsAdversarialSimulation/ArmyRtsAdversarialSimulation.csproj -c Release
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Debug --no-restore --nologo
dotnet build AncientWarfare3.csproj -c Release --no-restore --nologo
```

Expected: all tests pass and both builds report zero errors. Record warnings.

- [ ] **Step 4: Review the complete diff against the approved spec**

Run `git diff master...HEAD --check` and inspect every changed production and
test file. Explicitly verify: Standard defaults to 50%; full mobilization only
registers civilians; law decreases never dismiss active soldiers; preparation
only consumes approved shortages; final refill precedes freeze; operations do
not reset or enlarge; sufficient indexed actors join by 15 world seconds;
filled shortages end early; special armies do not merge; no one-actor attack
is sent; shortage and supply remain separate; replicas never author state.

- [ ] **Step 5: Merge into the current master safely**

In the main worktree, inspect `git status` and recent concurrent commits. Merge
this branch non-interactively without overwriting unrelated user edits. Rerun
Steps 1 through 3 from merged master. Resolve only overlapping changes that
belong to this feature.

- [ ] **Step 6: Deploy exact changed runtime files**

Copy only changed production/localization files to matching paths under
`D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`. Remove no
installed file unless it is proven obsolete and tracked by this feature.
Compare SHA-256 for every copied source/destination pair and require equality.

- [ ] **Step 7: Test with an autosave, never `save8`**

Verify in game:

1. The auxiliary-law window shows four conscription tiers without clipping.
2. A 30/50/70/100 law change produces the matching indexed pool capacity.
3. A wartime increase adds uncommitted candidates; a decrease does not dismiss
   active temporary soldiers.
4. Preparation consumes real pooled actors only up to approved shortages.
5. Formal war performs final indexed refill, then freezes, then enlists.
6. Army information shows live shortage and national reserve supply as two
   different values for attackers and defenders.
7. A replenishment operation fills progressively and ends immediately if its
   shortage reaches zero.
8. Save in month one or two, reload, and verify approval/deadline do not reset.
9. By the end of month three, every still-valid approved pooled actor has
   joined the formation.
10. A viable under-strength ordinary army attacks after the deadline; a weak
    secondary merges; special forces remain separate.
11. When national ordinary force is at least the minimum, one ordinary army
    keeps attacking; below the minimum, no one-actor army departs.
12. War end demobilizes temporary soldiers and reconciles them to the current
    law without violating the city population floor.

- [ ] **Step 8: Correct acceptance defects through RED then GREEN**

For every observed defect, add the smallest failing rule/integration/source
guard first, reproduce RED, make the minimal production change, rerun the
affected slice plus Steps 1 through 3, and commit the correction with the
observed behavior in the message. Do not patch the installed copy directly.

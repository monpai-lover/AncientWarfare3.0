# Bandit Uprising Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make famine and corruption increase annual bandit recruitment, expand strongholds from 2x2 to 3x3 and 4x4, and give leaderless rebel realms a safe historical-figure leader fallback.

**Architecture:** Keep all pure decisions in existing `*Rules` classes and keep world/database mutation in services. Reuse `CorruptionService`, the existing city food check, `FigureStateStore`, and native actor/city creation APIs. Stronghold growth is transactional: a larger candidate is planned first, then wall/zone state is committed only after the plan succeeds.

**Tech Stack:** C#, Unity/WorldBox runtime APIs, Newtonsoft.Json state serialization, the repository's `.cs.txt` rule-test harness, `dotnet build`.

---

### Task 1: Add pure recruitment-pressure rules

**Files:**
- Modify: `Code/core/lineage/PeasantRebelBanditSpawnRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditRecruitmentRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing tests**

Add tests for the approved additive model:

```csharp
Equal(0, PeasantRebelBanditSpawnRules.CalculateAnnualRecruitment(
    adultPopulation: 20, famine: false, highCorruption: false,
    currentPopulation: 20), "normal city has no forced recruitment");
True(PeasantRebelBanditSpawnRules.CalculateAnnualRecruitment(
    adultPopulation: 100, famine: true, highCorruption: false,
    currentPopulation: 100) > 0, "famine increases recruitment");
True(PeasantRebelBanditSpawnRules.CalculateAnnualRecruitment(
    adultPopulation: 100, famine: true, highCorruption: true,
    currentPopulation: 100) >
    PeasantRebelBanditSpawnRules.CalculateAnnualRecruitment(
        100, true, false, 100), "famine and corruption stack");
Equal(PeasantRebelBanditSpawnRules.AnnualRecruitmentCap,
    PeasantRebelBanditSpawnRules.CalculateAnnualRecruitment(
        100000, true, true, 100000), "recruitment has a hard yearly cap");
Equal(0, PeasantRebelBanditSpawnRules.CalculateAnnualRecruitment(
    adultPopulation: 100, famine: true, highCorruption: true,
    currentPopulation: PeasantRebelBanditSpawnRules.MinimumCityPopulation),
    "minimum city population is protected");
```

- [ ] **Step 2: Run the focused harness and verify failure**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected: compilation failure because `CalculateAnnualRecruitment`, `AnnualRecruitmentCap`, and `MinimumCityPopulation` do not exist.

- [ ] **Step 3: Implement the deterministic calculation**

Add constants and a pure method to `PeasantRebelBanditSpawnRules`:

```csharp
internal const int AnnualRecruitmentCap = 12;
internal const int MinimumCityPopulation = 6;
private const float BaseRate = 0.01f;
private const float FamineBonus = 0.02f;
private const float CorruptionBonus = 0.02f;

internal static int CalculateAnnualRecruitment(int adultPopulation,
    bool famine, bool highCorruption, int currentPopulation)
{
    int population = Math.Max(0, adultPopulation);
    int available = Math.Max(0, currentPopulation - MinimumCityPopulation);
    float rate = (famine || highCorruption) ? BaseRate : 0f;
    if (famine) rate += FamineBonus;
    if (highCorruption) rate += CorruptionBonus;
    int result = (int)Math.Floor(population * rate);
    if ((famine || highCorruption) && result < 1 && available > 0)
        result = 1;
    return Math.Min(AnnualRecruitmentCap, Math.Min(result, available));
}
```

Add `using System;` and register the test class in `Program.cs.txt`.

- [ ] **Step 4: Run the focused harness and verify it passes**

Run the same `dotnet run` command; expected output is success for the new recruitment tests and no regression in existing rules.

- [ ] **Step 5: Commit the isolated rules change**

```powershell
git add Code/core/lineage/PeasantRebelBanditSpawnRules.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditRecruitmentRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: model famine and corruption bandit recruitment"
```

### Task 2: Apply annual recruitment to eligible residents

**Files:**
- Modify: `Code/core/lineage/PeasantRebelBanditSpawnService.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditSpawnRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditRecruitmentRulesTests.cs.txt`

- [ ] **Step 1: Add candidate eligibility tests**

Cover adult civilian eligibility and rejection of king, heir, city leader, official, royal guard, slave, boat, existing warrior, and non-local actors using the existing `CanRelocateOrdinaryResident`-style boolean rules.

- [ ] **Step 2: Run tests and confirm the new eligibility API is missing**

Run the focused harness and expect failure on the new eligibility method.

- [ ] **Step 3: Implement service integration**

In `OnKingdomYear`, preserve the once-per-year guard, read `city.hasAnyFood()` for famine, and read the existing `CorruptionService.ReadCity(city)` snapshot for the high-corruption threshold. Calculate each city's quota and iterate the city's existing actor collection once. Convert only eligible actors with the existing profession and bandit identity APIs. Stop after the quota; never mutate the city population count directly. Keep the current kingdom/year key as the only scheduling guard so the workflow remains one bounded pass per kingdom per year.

Use the existing `LineageKeys.MANDATE_REBEL_BANDIT_SPAWN_LAST_YEAR` guard and add a second per-city/year key only if the current workflow cannot distinguish cities. Log one aggregate diagnostic per city/year, not one line per actor.

- [ ] **Step 4: Run rules and compile checks**

Run the rules harness, then:

```powershell
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: build succeeds and existing spawn behavior remains unchanged for normal cities.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/PeasantRebelBanditSpawnService.cs Code/core/lineage/PeasantRebelBanditSpawnRules.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditRecruitmentRulesTests.cs.txt
git commit -m "feat: recruit more bandits during famine and corruption"
```

### Task 3: Generalize stronghold size rules and persistence

**Files:**
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdState.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditStateStore.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt`

- [ ] **Step 1: Add failing size tests**

Add tests for enum progression, legacy default, and cap:

```csharp
Equal(BanditStrongholdSize.Medium3x3,
    PeasantRebelBanditStrongholdRules.ResolveNextSize(
        BanditStrongholdSize.Small2x2, pressure: 60,
        famine: false, highCorruption: true), "small expands to medium");
Equal(BanditStrongholdSize.Large4x4,
    PeasantRebelBanditStrongholdRules.ResolveNextSize(
        BanditStrongholdSize.Medium3x3, pressure: 100,
        famine: true, highCorruption: true), "medium expands to large");
Equal(BanditStrongholdSize.Large4x4,
    PeasantRebelBanditStrongholdRules.ResolveNextSize(
        BanditStrongholdSize.Large4x4, pressure: 1000,
        famine: true, highCorruption: true), "large is capped");
Equal(BanditStrongholdSize.Small2x2,
    PeasantRebelBanditStrongholdRules.NormalizeSize(0), "legacy state defaults to 2x2");
```

- [ ] **Step 2: Run the harness and verify failure**

Run the rules harness; expected failure because the enum and methods are not present.

- [ ] **Step 3: Add the persisted size model**

Add `BanditStrongholdSize { Small2x2 = 2, Medium3x3 = 3, Large4x4 = 4 }`, add `Size` to `PeasantRebelBanditStrongholdState`, increment `CurrentSchemaVersion`, and normalize invalid/zero values to `Small2x2`. Keep `FixedZoneKeys` unchanged for old states.

- [ ] **Step 4: Implement pure size progression**

Add `ResolveNextSize` with explicit thresholds (`pressure >= 45` for 3x3 and `pressure >= 85` for 4x4), allowing famine or high corruption to add 15 pressure-equivalent points, and clamp to `Large4x4`. The method must never return a smaller size.

- [ ] **Step 5: Run tests and commit persistence/rules**

Run the harness, confirm pass, then commit:

```powershell
git add Code/core/lineage/PeasantRebelBanditStrongholdState.cs Code/core/lineage/PeasantRebelBanditStateStore.cs Code/core/lineage/PeasantRebelBanditStrongholdRules.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt
git commit -m "feat: persist expandable bandit stronghold sizes"
```

### Task 4: Plan and commit 3x3/4x4 zone candidates

**Files:**
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdRules.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditZoneWallService.cs` only where its existing candidate API requires a size parameter
- Modify: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt`

- [ ] **Step 1: Add candidate-grid tests**

Test `RankZoneCandidates(zones, centerKey, 2)`, `RankZoneCandidates(..., 3)`, and `RankZoneCandidates(..., 4)` on complete grids; assert candidate counts are 4, 9, and 16 and incomplete grids return no candidate. Assert size 5 is rejected.

- [ ] **Step 2: Run tests and verify failure**

Run the harness and confirm the generalized candidate API is missing.

- [ ] **Step 3: Implement generalized candidate ranking**

Refactor the current fixed `RankFourZoneCandidates` internals into `RankZoneCandidates(..., int sideLength)`. Preserve center-first, distance, and canonical ordering. Keep `RankFourZoneCandidates` as a compatibility wrapper calling side length 2 so existing callers and old tests remain valid.

- [ ] **Step 4: Replace hard-coded four-zone planning**

In `TryPlan`, derive the requested size from the context/state and call the generalized candidate API. Replace `candidateKeys.Count != 4`, `InteriorZones.Count != 4`, and `motherZones.Count < 4` checks with the requested area (`sideLength * sideLength`). Preserve the fallback wall plan behavior and require a complete candidate before mutating the transaction.

- [ ] **Step 5: Add growth transaction**

Add `TryExpandActiveStronghold(Kingdom, bool famine, bool highCorruption)` that reads the active state, resolves the next size, plans a larger candidate using the existing wall service, applies wall/tower changes through the existing transaction path, updates `FixedZoneKeys` and `Size`, and rolls back all snapshots on failure. Call it once from the annual pressure service after pressure is written.

- [ ] **Step 6: Run tests and build**

Run the rules harness and `dotnet build AncientWarfare3.csproj --no-restore`; expected pass with old 2x2 strongholds still loading and new sizes available for new/growing strongholds.

- [ ] **Step 7: Commit**

```powershell
git add Code/core/lineage/PeasantRebelBanditStrongholdRules.cs Code/core/lineage/PeasantRebelBanditStrongholdService.cs Code/core/lineage/PeasantRebelBanditZoneWallService.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt
git commit -m "feat: expand bandit strongholds to four by four"
```

### Task 5: Add historical-figure leader fallback rules

**Files:**
- Modify: `Code/content/figures/HistoricalFigureSpawnRules.cs`
- Modify: `Code/core/lineage/MandateRebelService.cs`
- Modify: `Code/core/lineage/MandateDeclineRebellionService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/MandateHistoricalLeaderRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing fallback tests**

Add pure tests asserting ordinary founder priority, historical fallback only when no ordinary founder exists, available/dead candidate acceptance, pending/alive candidate rejection, and no-candidate failure.

- [ ] **Step 2: Run tests and verify failure**

Run the rules harness and expect missing selector APIs.

- [ ] **Step 3: Implement candidate-selection rules**

Add a small `HistoricalLeaderFallbackRules` or equivalent pure method that consumes booleans/ordered registry indices and returns the first valid candidate. Do not change normal historical-figure spawn ordering or the single-alive-figure invariant.

- [ ] **Step 4: Expose a safe historical spawn entry point**

Add an internal method to `HistoricalFigureService` that accepts a destination city/kingdom and a leader role. It must reserve through `FigureStateStore.TryReserveSpawn`, create the native actor using the same asset/name/trait initialization path as `TrySpawnOn`, commit with `TryCommitSpawn`, and abort the reservation if actor creation or kingdom assignment fails. Existing `TrySpawnOn` remains unchanged.

- [ ] **Step 5: Integrate into rebel creation and annual reconciliation**

In `MandateRebelService` and `MandateDeclineRebellionService`, retain ordinary founder/city-leader selection first. If `king` is missing/dead after creation or during annual reconciliation, call the fallback entry point against the rebel capital, set `MANDATE_REBEL_LEADER` and the existing rebel trait/identity, and record a person/kingdom history entry. If fallback fails, leave the existing no-leader failure/settlement path intact.

- [ ] **Step 6: Run tests and build**

Run the rules harness and `dotnet build AncientWarfare3.csproj --no-restore`; expected pass with historical state rollback on failed actor creation.

- [ ] **Step 7: Commit**

```powershell
git add Code/content/figures/HistoricalFigureSpawnRules.cs Code/content/figures/HistoricalFigureService.cs Code/core/lineage/MandateRebelService.cs Code/core/lineage/MandateDeclineRebellionService.cs Tests/AncientWarfare3.Rules.Tests/MandateHistoricalLeaderRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: summon historical leaders for leaderless rebels"
```

### Task 6: Full regression verification

**Files:**
- Test only: `Tests/AncientWarfare3.Rules.Tests/*`

- [ ] **Step 1: Run all pure rules tests**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected: all existing and new tests pass. If the known unrelated `DynasticMaleLineContinuityRules` fixture failure remains, report it separately and do not weaken these tests.

- [ ] **Step 2: Build the main project**

```powershell
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: exit code 0.

- [ ] **Step 3: Review diff and status**

```powershell
git diff --check
git status --short --branch
```

Confirm no unrelated user changes were staged and no runtime DLL was overwritten.

- [ ] **Step 4: Commit final integration if needed**

```powershell
git add Code Tests
git commit -m "test: verify bandit uprising expansion"
```

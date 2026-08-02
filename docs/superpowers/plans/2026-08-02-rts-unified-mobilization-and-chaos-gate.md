# RTS Unified Mobilization And Chaos Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make diplomatic-notice recruitment, wartime replenishment, reserve persistence, and post-Mandate Zhulu eligibility use coherent authoritative state so armies receive soldiers and cannot remain stuck in replenishment.

**Architecture:** Add a pure mobilization-phase contract and make the city reserve ledger expose one phase-aware, city-local consumption path. The preparation coordinator handles both existing and newly created armies, while wartime operations use the same ledger contract. Chaos entry and Zhulu declarations share a persisted Mandate-history gate; school death persistence retains immutable membership identity as its transaction boundary.

**Tech Stack:** C# 9/Unity WorldBox mod sources, Harmony patches, System.Data.SQLite, .NET 9 rules test harness, adversarial RTS console simulation.

---

### Task 1: Pure Mobilization Phase Contract

**Files:**
- Create: `Code/core/lineage/ArmyMobilizationRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyMobilizationRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing phase and readiness tests**

```csharp
Equal(ArmyMobilizationPhase.Notice,
    ArmyMobilizationRules.Resolve(liveKingdom: true,
        activeNotice: true, activeWarCount: 0));
Equal(ArmyMobilizationPhase.War,
    ArmyMobilizationRules.Resolve(true, true, 1));
True(ArmyMobilizationRules.CanConsume(ArmyMobilizationPhase.Notice));
True(ArmyMobilizationRules.CanConsume(ArmyMobilizationPhase.War));
False(ArmyMobilizationRules.CanConsume(ArmyMobilizationPhase.Peace));
False(ArmyMobilizationRules.IsDeploymentReady(79, 100));
True(ArmyMobilizationRules.IsDeploymentReady(80, 100));
True(ArmyMobilizationRules.ShouldConfirmExhausted(
    reconciliationComplete: true, available: 0));
False(ArmyMobilizationRules.ShouldConfirmExhausted(false, 0));
```

- [ ] **Step 2: Run the focused harness and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --army-rts
```

Expected: compilation fails because `ArmyMobilizationRules` does not exist.

- [ ] **Step 3: Implement the pure contract**

```csharp
public enum ArmyMobilizationPhase { Inactive, Peace, Notice, War }

public static ArmyMobilizationPhase Resolve(bool liveKingdom,
    bool activeNotice, int activeWarCount)
{
    if (!liveKingdom) return ArmyMobilizationPhase.Inactive;
    if (activeWarCount > 0) return ArmyMobilizationPhase.War;
    return activeNotice
        ? ArmyMobilizationPhase.Notice
        : ArmyMobilizationPhase.Peace;
}

public static bool CanConsume(ArmyMobilizationPhase phase) =>
    phase == ArmyMobilizationPhase.Notice ||
    phase == ArmyMobilizationPhase.War;

public static bool IsDeploymentReady(int living, int target) =>
    target > 0 && (long)Math.Max(0, living) * 100L >=
    (long)target * ArmyRtsRules.DeploymentQuorumPercent;
```

- [ ] **Step 4: Run the focused harness and verify GREEN**

Expected: `AW3 army RTS rules passed.`

### Task 2: One City-Ledger Consumption Contract

**Files:**
- Modify: `Code/core/lineage/CityReservePoolRules.cs`
- Modify: `Code/core/lineage/CityReservePoolService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CityReservePoolRulesTests.cs.txt`

- [ ] **Step 1: Write failing consumption-state tests**

```csharp
True(CityReservePoolRules.CanConsumeForMobilization(
    ArmyMobilizationPhase.Notice, realmControlled: true,
    population: 21));
True(CityReservePoolRules.CanConsumeForMobilization(
    ArmyMobilizationPhase.War, true, 21));
False(CityReservePoolRules.CanConsumeForMobilization(
    ArmyMobilizationPhase.Peace, true, 21));
False(CityReservePoolRules.CanConfirmExhausted(
    reconciliationComplete: false, availableActorCount: 0));
True(CityReservePoolRules.CanConfirmExhausted(
    reconciliationComplete: true, availableActorCount: 0));
```

- [ ] **Step 2: Run `--army-rts` and verify RED**

Expected: missing `CanConsumeForMobilization` or wrong exhaustion behavior.

- [ ] **Step 3: Implement a phase-aware city consumption API**

Add `ResolveMobilizationPhase(Kingdom)` and replace the separate frozen-only
and notice-only consumption bodies with:

```csharp
internal static int TryConsumeForMobilization(Kingdom kingdom,
    City sourceCity, int requested, Army targetArmy,
    bool allowArmyCreation, List<Actor> destination,
    out bool confirmedExhausted)
```

The method must:

```csharp
ArmyMobilizationPhase phase = ResolveMobilizationPhase(kingdom);
if (!CityReservePoolRules.CanConsumeForMobilization(
        phase, IsControlledCity(sourceCity, kingdom),
        SafePopulation(sourceCity))) return 0;
bool complete = ReconcileSourceCityForMobilization(kingdom, sourceCity);
// `targetArmy == null` is legal only for notice-phase army creation.
// Remove only valid actors from this source city's ActorIds.
confirmedExhausted = CityReservePoolRules.CanConfirmExhausted(
    complete, pool.ActorIds.Count);
```

Keep the old public/internal wrappers temporarily, but delegate both to this
method so there is only one mutation implementation.

- [ ] **Step 4: Run `--army-rts` and verify GREEN**

### Task 3: Recruit During Every Notice Month, Including Army Creation

**Files:**
- Modify: `Code/core/lineage/TemporaryLevyRules.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Modify: `Code/core/lineage/WarNoticeService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/TemporaryLevyRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/TemporaryLevyRecoveryRulesTests.cs.txt`

- [ ] **Step 1: Write failing create/replenish preparation tests**

```csharp
Equal(8, TemporaryLevyRules.PreparationRequest(
    ArmyRecruitmentDisposition.Create, living: 0,
    target: 20, workItemLimit: 8));
Equal(8, TemporaryLevyRules.PreparationRequest(
    ArmyRecruitmentDisposition.Replenish, living: 5,
    target: 20, workItemLimit: 8));
True(TemporaryLevyRules.CanRecruitBeforeFrontierReady(
    activeNotice: true, sourceCityValid: true));
False(TemporaryLevyRules.ShouldKeepPreparationRecruitmentCity(
    cityWorkComplete: true, recruited: 0));
```

- [ ] **Step 2: Run `--army-rts` and verify RED**

- [ ] **Step 3: Refactor preparation recruitment**

In `ProcessPreparationRecruitment`:

```csharp
int living = SafeArmyCount(targetArmy);
int target = targetArmy?.data != null
    ? StandingArmyService.TargetStrength(targetArmy, kingdom)
    : CityArmyReinforcementService.ApprovedTargetForCity(city, kingdom);
int request = TemporaryLevyRules.PreparationRequest(
    disposition, living, target,
    TemporaryLevyRules.MaxRecruitsPerWorkItem);
CityReservePoolService.TryConsumeForMobilization(kingdom, city,
    request, targetArmy,
    allowArmyCreation: disposition == ArmyRecruitmentDisposition.Create,
    candidates, out confirmedExhausted);
int recruited = EnlistPreparationActors(kingdom, city,
    disposition, ref targetArmy, candidates);
```

`EnlistPreparationActors` passes `Create` for the first candidate, which creates
the army through existing `EnsureArmyMembership`, then passes `Replenish` for
the rest. It updates the army anchor, mission target, deployment index, and
roster notification exactly once.

Remove preferred-frontier readiness as a recruitment gate. It remains a gate
only for choosing the deployment destination.

- [ ] **Step 4: Verify one monthly pass revisits unfinished cities**

Persist `CurrentCityId` until it reaches 100 percent or confirmed exhaustion.
At 80 percent call the deployment readiness update without marking recruitment
complete. At a new month, start another idempotent pass over all controlled
cities.

- [ ] **Step 5: Run `--army-rts` and verify GREEN**

### Task 4: Make Wartime Replenishment Use The Same Ledger

**Files:**
- Modify: `Code/core/lineage/ArmyReplenishmentOperationRules.cs`
- Modify: `Code/core/lineage/ArmyReplenishmentOperationService.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyReplenishmentOperationRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsPreDepartureDeadlineRulesTests.cs.txt`

- [ ] **Step 1: Replace frozen-pool tests with phase tests and verify RED**

```csharp
True(ArmyReplenishmentOperationRules.HasConsumableSourceReserve(
    ArmyMobilizationPhase.Notice, availableCount: 1));
True(ArmyReplenishmentOperationRules.HasConsumableSourceReserve(
    ArmyMobilizationPhase.War, availableCount: 1));
False(ArmyReplenishmentOperationRules.HasConsumableSourceReserve(
    ArmyMobilizationPhase.Peace, availableCount: 1));
True(ArmyReplenishmentOperationRules.ShouldReleaseDeparture(
    living: 80, target: 100));
```

- [ ] **Step 2: Implement phase-aware operation creation and processing**

Replace all `IsFrozen` authority checks with the derived mobilization phase.
Call `TryConsumeForMobilization` from `ProcessOne`. Keep the 20-second bounded
operation, but distinguish deployment release at 80 percent from full
replenishment completion. Confirm exhaustion only after the ledger reports a
complete empty reconciliation.

- [ ] **Step 3: Clear sticky replenishment state**

On full, confirmed exhaustion, deadline, invalid mission, or ended emergency,
call both `Clear(army)` and `ArmyReplenishmentCompletionService.Complete(army)`.
Ensure `ArmyRtsControllerService` does not immediately recreate an operation
when the same source city is confirmed exhausted in the current monthly epoch.

- [ ] **Step 4: Run `--army-rts` and verify GREEN**

### Task 5: Repair Illegal Chaos And Gate Zhulu At The Source

**Files:**
- Modify: `Code/core/lineage/MandatePhaseRules.cs`
- Modify: `Code/core/lineage/MandatePhaseService.cs`
- Modify: `Code/core/lineage/ZhuluWarRules.cs`
- Modify: `Code/core/lineage/ZhuluWarService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`

- [ ] **Step 1: Write failing Mandate-history tests**

```csharp
False(MandatePhaseRules.CanForceChaos(hasMandateHistory: false));
True(MandatePhaseRules.CanForceChaos(hasMandateHistory: true));
Equal(MandatePhase.Golden,
    MandatePhaseRules.NormalizeLoadedPhase(MandatePhase.Chaos,
        hasMandateHistory: false));
Equal(MandatePhase.Chaos,
    MandatePhaseRules.NormalizeLoadedPhase(MandatePhase.Chaos,
        hasMandateHistory: true));
False(ZhuluWarRules.CanStart(new ZhuluEligibilityFacts(
    MandatePhase.Chaos, true, true, true, true, false, false,
    false, false, false, ageOverride: false,
    hasMandateHistory: false)));
```

- [ ] **Step 2: Run `--army-rts` and verify RED**

- [ ] **Step 3: Add the source gates and load migration**

`MandatePhaseService.ForceChaos` reads `MandateService.ReadReport().period_id`
and returns without mutation when it is negative. `EnsureLoaded` normalizes a
persisted illegal Chaos phase to Golden and writes the repaired phase once.
`ZhuluWarService.CanDeclare` passes `report.period_id >= 0` into
`ZhuluEligibilityFacts`; `ZhuluWarRules.CanStart` requires that flag for
ordinary Chaos, while `AgeOverride` remains valid.

- [ ] **Step 4: Run `--army-rts` and verify GREEN**

### Task 6: Prove The School Root Fix

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/SchoolRuntimePerformanceTests.cs.txt`
- Verify: `Code/core/schools/SchoolMembershipPersistenceRules.cs`
- Verify: `Code/core/schools/HistoricalSchoolStore.cs`

- [ ] **Step 1: Extend identity tests**

Construct two `SchoolMembershipStableIdentity` values with identical immutable
fields and separately vary reputation, standing, and loyalty in the surrounding
membership snapshots. Confirm identity remains equal. Change school, source,
teacher, city, generation, or start year and confirm inequality.

- [ ] **Step 2: Add source guards**

Verify `LoadMembershipTimeForDeath`, authoritative death readback, and rollback
readback all call `MatchesMembershipIdentity`, while final end-state checks
still validate active/end-year/end-reason/update-time.

- [ ] **Step 3: Run the focused school suite**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --actor-runtime
```

Expected: `AW3 actor runtime rules passed.`

### Task 7: Simulation, Compilation, And Source Deployment

**Files:**
- Modify as required: `Tests/ArmyRtsAdversarialSimulation/*`
- Deploy source files only to `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Add notice mobilization to the adversarial simulation**

Create a ten-city, twenty-army scenario with initially empty armies and
city-local reserves. Assert that preparation creates/replenishes armies,
releases them at 80 percent, consumes no foreign-city reserve, replaces battle
losses, exits on true exhaustion, and reaches war completion without a
permanently replenishing army.

- [ ] **Step 2: Run focused and full verification**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --army-rts
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --actor-runtime
dotnet run --project Tests/ArmyRtsAdversarialSimulation/ArmyRtsAdversarialSimulation.csproj -c Release
dotnet build AncientWarfare3.csproj -c Release
```

Expected: focused suites and simulation pass; production build reports zero
errors. Record any unrelated pre-existing full-suite failures separately.

- [ ] **Step 3: Audit every requirement against authoritative evidence**

Search all reserve consumption and Zhulu declaration call sites. Confirm they
route through the new shared contracts. Inspect the diff to ensure no cross-city
fallback, DLL deployment, unrelated file rewrite, or swallowed persistence
conflict was introduced.

- [ ] **Step 4: Deploy source only and verify hashes**

Copy only changed production source files, preserving their relative paths.
For every deployed file compare source and destination SHA-256. Do not copy
`AncientWarfare3.dll` or any test artifact.

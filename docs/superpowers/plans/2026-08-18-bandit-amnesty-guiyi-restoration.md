# Bandit Amnesty and Guiyi Restoration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reliably destroy empty bandit strongholds, add reward-bearing amnesty settlements, and create one restoration-oriented Guiyi bandit force per non-Xia occupier when an integrated-Xia city falls below -50 loyalty.

**Architecture:** Keep stronghold world mutation behind the existing deferred city-manager boundary. Add pure rules for population, amnesty offers, and Guiyi eligibility; keep runtime orchestration in focused services that reuse court appointment, virtual-title, foreign-occupation, and kingdom-continuity APIs. Persist resumable amnesty and Guiyi state so authority cycles and save/load recovery remain idempotent.

**Tech Stack:** C#/.NET, Harmony patches, Unity UI, Newtonsoft.Json kingdom state, SQLite lineage archive, AW3 rule-test console, and PowerShell source guards.

---

## File Map

- `PeasantRebelBanditStrongholdPopulationRules.cs`: pure living-population decisions.
- `PeasantRebelBanditStrongholdPopulationService.cs`: coalesced deferred empty-stronghold observation.
- `BanditStrongholdCityDisposalService.cs`: pending-disposal ownership and completion.
- `PeasantRebelBanditAmnestyOffer.cs`: reward DTO and settlement phase.
- `BanditAmnestySettlementTableItem.cs`: durable amnesty transaction row.
- `PeasantRebelBanditAmnestyService.cs`: authority-side resumable settlement.
- `BanditAmnestySettlementWindow.cs`: wide draggable reward selection.
- `PeasantRebelGuiyiRules.cs`: trigger, uniqueness, and restoration decisions.
- `PeasantRebelGuiyiService.cs`: trigger scheduling, active index, creation, restoration, and cleanup.

### Task 1: Deferred Empty-Stronghold Rules

**Files:**
- Create: `Code/core/lineage/PeasantRebelBanditStrongholdPopulationRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write failing rules tests**

```csharp
True(PeasantRebelBanditStrongholdPopulationRules.IsLivingResident(
    actorExists: true, alive: true, rekt: false, boat: false,
    belongsToCity: true), "living resident counts");
False(PeasantRebelBanditStrongholdPopulationRules.IsLivingResident(
    actorExists: true, alive: false, rekt: false, boat: false,
    belongsToCity: true), "dead actor retained by native list does not count");
True(PeasantRebelBanditStrongholdPopulationRules.ShouldQueueFall(
    activeStronghold: true, livingResidents: 0),
    "true zero population queues fall");
```

- [ ] **Step 2: Run tests and verify failure**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
```

Expected: compilation fails because `PeasantRebelBanditStrongholdPopulationRules` does not exist.

- [ ] **Step 3: Implement the pure rules**

```csharp
namespace AncientWarfare3.core.lineage
{
    public static class PeasantRebelBanditStrongholdPopulationRules
    {
        public static bool IsLivingResident(bool actorExists, bool alive,
            bool rekt, bool boat, bool belongsToCity)
        {
            return actorExists && alive && !rekt && !boat && belongsToCity;
        }

        public static bool ShouldQueueFall(bool activeStronghold,
            int livingResidents)
        {
            return activeStronghold && livingResidents <= 0;
        }
    }
}
```

- [ ] **Step 4: Run the rules suite**

Expected: all rule tests pass.

- [ ] **Step 5: Commit the isolated rules**

```powershell
git add Code/core/lineage/PeasantRebelBanditStrongholdPopulationRules.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditStrongholdRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "test: define empty bandit stronghold lifecycle"
```

### Task 2: Live Empty-Stronghold Observation and Forced Disposal

**Files:**
- Create: `Code/core/lineage/PeasantRebelBanditStrongholdPopulationService.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Modify: `Code/core/lineage/BanditStrongholdCityDisposalService.cs`
- Modify: `Code/core/lineage/EmptyCitySurvivalService.cs`
- Modify: `Code/core/lineage/EmptyCityResettlementService.cs`
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Code/patch/AW_EmptyCitySurvivalPatch.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Create: `Tests/BanditStrongholdZeroPopulationSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write a failing source guard**

```powershell
Require-Text $population 'EnqueueStronghold'
Require-Text $population 'CountLivingResidents'
Require-Text $death 'PeasantRebelBanditStrongholdPopulationService'
Require-Text $emptyCity 'BanditStrongholdCityDisposalService.IsPending'
Require-Text $disposal 'Pending.Remove(cityId)'
Reject-Text $stronghold 'getPopulationPeople()'
```

- [ ] **Step 2: Run the guard and verify failure**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\BanditStrongholdZeroPopulationSourceGuard.ps1
```

Expected: failure on the missing population service.

- [ ] **Step 3: Implement deferred observation**

The service exposes these exact methods:

```csharp
internal static void EnqueueStronghold(long pCityId);
internal static void ProcessAuthorityCycle();
internal static int CountLivingResidents(City pCity);
internal static void Clear();
```

`ProcessAuthorityCycle` re-resolves city and persisted state, counts only living residents with Task 1 rules, and calls `PeasantRebelBanditStrongholdService.QueuePopulationFall(cityId)` at zero. Actor death and `City.eventUnitRemoved` only enqueue IDs.

- [ ] **Step 4: Make pending disposal explicit**

Add:

```csharp
internal static bool IsPending(long pCityId)
{
    return pCityId > 0L && Pending.ContainsKey(pCityId);
}
```

Empty-city survival does not preserve a pending-disposal city, and resettlement cancels it. Pending entries survive transient errors and are removed only after the city is absent/rekt or `removeObject` succeeds.

- [ ] **Step 5: Run focused and broad verification**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\BanditStrongholdZeroPopulationSourceGuard.ps1
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: guard and rules pass; build reports zero errors.

- [ ] **Step 6: Commit lifecycle repair**

```powershell
git add Code/core/lineage/PeasantRebelBanditStrongholdPopulationService.cs Code/core/lineage/PeasantRebelBanditStrongholdService.cs Code/core/lineage/BanditStrongholdCityDisposalService.cs Code/core/lineage/EmptyCitySurvivalService.cs Code/core/lineage/EmptyCityResettlementService.cs Code/patch/AW_ActorDeathPatch.cs Code/patch/AW_EmptyCitySurvivalPatch.cs Code/core/performance/AWAuthorityCycleService.cs Tests/BanditStrongholdZeroPopulationSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "fix: force empty bandit stronghold disposal"
```

### Task 3: Amnesty Offer Model and Resumable Settlement

**Files:**
- Create: `Code/core/lineage/PeasantRebelBanditAmnestyOffer.cs`
- Create: `Code/core/db/BanditAmnestySettlementTableItem.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditAmnestyRules.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditAmnestyService.cs`
- Modify: `Code/core/lineage/VirtualNobleTitleService.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditAmnestyRulesTests.cs.txt`
- Create: `Tests/BanditAmnestySettlementSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/VirtualNobleTitleSourceGuard.ps1`

- [ ] **Step 1: Write failing offer-rule tests**

```csharp
Assert(PeasantRebelBanditAmnestyRules.CanSelectOffice(
    officeExists: true, officeVacant: true, leaderEligible: true));
Assert(!PeasantRebelBanditAmnestyRules.CanSelectOffice(
    officeExists: true, officeVacant: false, leaderEligible: true));
Assert(PeasantRebelBanditAmnestyRules.CanAdvance(
    BanditAmnestySettlementPhase.Prepared,
    BanditAmnestySettlementPhase.TerritorialSettlement));
Assert(!PeasantRebelBanditAmnestyRules.CanAdvance(
    BanditAmnestySettlementPhase.Completed,
    BanditAmnestySettlementPhase.RewardPending));
```

- [ ] **Step 2: Run rules and verify failure**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
```

Expected: compilation fails on missing reward and settlement types.

- [ ] **Step 3: Add persisted settlement types**

```csharp
internal enum BanditAmnestyRewardKind { None, Office, VirtualTitle }
internal enum BanditAmnestySettlementPhase
{ Prepared, TerritorialSettlement, RewardPending, Completed, Failed }

internal sealed class PeasantRebelBanditAmnestyOffer
{
    public BanditAmnestyRewardKind RewardKind;
    public string OfficeId = "";
    public string TitleText = "";
    public bool Hereditary = true;
}
```

The table row stores settlement ID, bandit/origin/leader/stronghold/mother IDs, serialized offer, phase, retry count, years/times, and failure key.

- [ ] **Step 4: Add non-mutating preflight APIs**

Add `VirtualNobleTitleService.ValidateGrant(...)` using the same target, title, and duplicate rules as `TryGrant`. Extend the title source guard to require `OnKingdomDestroying`, `END_REASON='kingdom_destroyed'`, and `ACTIVE=0`, proving that an issuer's destruction closes rather than transfers titles. Add `CourtService.CanPromiseAmnestyOffice(...)` that checks current-tier office existence, vacancy, and intrinsic leader constraints without requiring domestic affiliation before naturalization.

- [ ] **Step 5: Refactor amnesty into staged execution**

Keep compatibility for no-reward callers:

```csharp
internal static bool TryAmnesty(Kingdom bandit, Kingdom origin,
    out string failureKey)
{
    return TryAmnesty(bandit, origin,
        new PeasantRebelBanditAmnestyOffer(), out failureKey);
}
```

The new overload preflights and writes `Prepared`, ends wars, invokes common logical fall, naturalizes residents, restores government, persists `RewardPending`, grants the reward, writes chronicles, then marks `Completed`. Authority-cycle recovery processes nonterminal rows without replaying completed stages.

- [ ] **Step 6: Verify and commit**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\BanditAmnestySettlementSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\AncientWarfare3.Rules.Tests\VirtualNobleTitleSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release --no-restore
git add Code/core/lineage/PeasantRebelBanditAmnestyOffer.cs Code/core/db/BanditAmnestySettlementTableItem.cs Code/core/lineage/PeasantRebelBanditAmnestyRules.cs Code/core/lineage/PeasantRebelBanditAmnestyService.cs Code/core/lineage/VirtualNobleTitleService.cs Code/core/court/CourtService.cs Code/core/performance/AWAuthorityCycleService.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditAmnestyRulesTests.cs.txt Tests/BanditAmnestySettlementSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests/VirtualNobleTitleSourceGuard.ps1
git commit -m "feat: add reward-bearing bandit amnesty settlements"
```

### Task 4: Amnesty Settlement Window and Localization

**Files:**
- Create: `Code/ui/windows/BanditAmnestySettlementWindow.cs`
- Modify: `Code/ui/AW_LineageWindowIds.cs`
- Modify: `Code/content/GodPowerLibrary.cs`
- Modify: `Code/core/lineage/HistoryLocalizationRules.cs`
- Modify: `Locales/aw3/en.json`
- Modify: `Locales/aw3/zh.json`
- Modify: `Locales/aw3/ch.json`
- Create: `Tests/BanditAmnestySettlementUiSourceGuard.ps1`

- [ ] **Step 1: Write a failing UI source guard**

Require the power to call `BanditAmnestySettlementWindow.Open`, the window to call `WideWindowChrome.Attach`, and source references for `BanditAmnestyRewardKind.None`, `Office`, `VirtualTitle`, `characterLimit`, `Hereditary`, and localized feedback keys.

- [ ] **Step 2: Implement the wide settlement window**

Expose:

```csharp
internal static void Open(long pBanditKingdomId,
    long pOriginKingdomId);
```

Use a segmented reward selector, vacant-office selector, title input, hereditary toggle, confirm, and cancel. Confirm re-resolves IDs and calls Task 3. The original divine-power click only validates the selected city and opens this window.

- [ ] **Step 3: Add localized UI and history strings**

Add exact keys for the window title, three reward labels, vacant-office empty state, title validation, settlement phases, completion messages, and chronicles for no reward, office, virtual title, and reward fulfillment in all three locale files and `HistoryLocalizationRules`.

- [ ] **Step 4: Verify and commit**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\BanditAmnestySettlementUiSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\ReportedLocalizationCoverageSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release --no-restore
git add Code/ui/windows/BanditAmnestySettlementWindow.cs Code/ui/AW_LineageWindowIds.cs Code/content/GodPowerLibrary.cs Code/core/lineage/HistoryLocalizationRules.cs Locales/aw3/en.json Locales/aw3/zh.json Locales/aw3/ch.json Tests/BanditAmnestySettlementUiSourceGuard.ps1
git commit -m "feat: add bandit amnesty settlement window"
```

### Task 5: Guiyi Eligibility, State, and One-Per-Occupier Index

**Files:**
- Create: `Code/core/lineage/PeasantRebelGuiyiRules.cs`
- Create: `Code/core/lineage/PeasantRebelGuiyiService.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdState.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditStateStore.cs`
- Modify: `Code/core/lineage/ForeignOccupationService.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelGuiyiRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write failing Guiyi rules tests**

```csharp
True(PeasantRebelGuiyiRules.CanSpawn(new GuiyiSpawnFacts(
    cityAlive: true, cityIntegrated: true, occupierIntegrated: false,
    foreignOccupation: true, loyalty: -51, occupierHasGuiyi: false,
    strongholdAvailable: true, residentAvailable: true)));
False(PeasantRebelGuiyiRules.CanSpawn(new GuiyiSpawnFacts(
    true, true, false, true, -50, false, true, true)));
False(PeasantRebelGuiyiRules.CanSpawn(new GuiyiSpawnFacts(
    true, true, false, true, -80, true, true, true)));
Equal(GuiyiRestorationObjective.ReturnToLivingKingdom,
    PeasantRebelGuiyiRules.ResolveObjective(true, true));
Equal(GuiyiRestorationObjective.RestoreExtinctKingdom,
    PeasantRebelGuiyiRules.ResolveObjective(false, true));
```

- [ ] **Step 2: Run rules and verify failure**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
```

Expected: missing Guiyi rule types.

- [ ] **Step 3: Extend persisted stronghold state**

Bump the schema and normalize these fields:

```csharp
public string RouteSubtype = "";
public long GuiyiOccupierKingdomId = -1L;
public long GuiyiOriginalKingdomId = -1L;
public long GuiyiOriginalCityId = -1L;
public long GuiyiRestorationClaimId = -1L;
public int GuiyiCreatedYear = -1;
public string GuiyiStage = "";
```

Old states normalize to an empty subtype and retain ordinary bandit behavior.

- [ ] **Step 4: Implement trigger and active index**

`ForeignOccupationService.OnCityTransferred` schedules a deferred check. Its annual `TickCity` schedules another after loyalty changes. The service checks `XiaCultureIntegrationService.IsIntegrated` for city and occupier cultures, reads `city.getLoyalty()`, checks its per-occupier active index, and calls a specialized `TryCreateGuiyi` only after normal four-zone planning succeeds.

Rebuild the index from active persisted states after load. Release the slot when state is cleared, the stronghold falls, restoration completes, or the Guiyi kingdom is destroyed.

- [ ] **Step 5: Verify and commit**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
dotnet build AncientWarfare3.csproj -c Release --no-restore
git add Code/core/lineage/PeasantRebelGuiyiRules.cs Code/core/lineage/PeasantRebelGuiyiService.cs Code/core/lineage/PeasantRebelBanditStrongholdState.cs Code/core/lineage/PeasantRebelBanditStateStore.cs Code/core/lineage/ForeignOccupationService.cs Code/core/performance/AWAuthorityCycleService.cs Tests/AncientWarfare3.Rules.Tests/PeasantRebelGuiyiRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "feat: create Guiyi bandits under foreign occupation"
```

### Task 6: Guiyi Restoration Objective, History, and Presentation

**Files:**
- Modify: `Code/core/lineage/PeasantRebelGuiyiService.cs`
- Modify: `Code/core/lineage/PeasantRebelRouteService.cs`
- Modify: `Code/core/lineage/AutonomousRestorationService.cs`
- Modify: `Code/core/lineage/KingdomIdentityContinuityService.cs`
- Modify: `Code/core/lineage/HistoryLocalizationRules.cs`
- Modify: `Code/core/lineage/KingdomRenameProjectionService.cs`
- Modify: `Code/core/presentation/RulerAppellationService.cs`
- Modify: `Locales/aw3/en.json`
- Modify: `Locales/aw3/zh.json`
- Modify: `Locales/aw3/ch.json`
- Create: `Tests/GuiyiRestorationSourceGuard.ps1`

- [ ] **Step 1: Write a failing restoration source guard**

Require references to `PeasantRebelRouteService.RealmStrength`, distinct `ReturnToLivingKingdom` and `RestoreExtinctKingdom` branches, `KingdomIdentityContinuityService.RestoreFromCity`, common stronghold cleanup, active-slot release, and Guiyi history keys.

- [ ] **Step 2: Implement strength and activation**

The annual update compares Guiyi and occupier strength through `PeasantRebelRouteService.RealmStrength` and begins restoration only when existing restoration rules accept the target. It never invokes ordinary bandit pressure conversion.

- [ ] **Step 3: Implement both restoration outcomes**

For a living original kingdom, transfer eligible restored occupied cities back through existing war-territory settlement APIs. For an extinct original, build the existing continuity request and call `KingdomIdentityContinuityService.RestoreFromCity` with the Guiyi leader. On success, run common stronghold logical fall, release the slot, and clear temporary bandit markers.

- [ ] **Step 4: Add names and chronicles**

Use localized `Guiyi Army`/`归义军` presentation without replacing the bandit government during the stronghold phase. Record establishment, objective, suppression, return to a living original kingdom, and extinct-identity restoration in the Guiyi kingdom, occupier, original kingdom when live, and city chronicle.

- [ ] **Step 5: Verify and commit**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\GuiyiRestorationSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\ReportedLocalizationCoverageSourceGuard.ps1
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
dotnet build AncientWarfare3.csproj -c Release --no-restore
git add Code/core/lineage/PeasantRebelGuiyiService.cs Code/core/lineage/PeasantRebelRouteService.cs Code/core/lineage/AutonomousRestorationService.cs Code/core/lineage/KingdomIdentityContinuityService.cs Code/core/lineage/HistoryLocalizationRules.cs Code/core/lineage/KingdomRenameProjectionService.cs Code/core/presentation/RulerAppellationService.cs Locales/aw3/en.json Locales/aw3/zh.json Locales/aw3/ch.json Tests/GuiyiRestorationSourceGuard.ps1
git commit -m "feat: complete Guiyi restoration objective"
```

### Task 7: Regression Verification and Deployment

**Files:**
- Verify all files changed in Tasks 1-6; do not modify unrelated dirty-worktree files.

- [ ] **Step 1: Run focused guards**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\BanditStrongholdZeroPopulationSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\BanditAmnestySettlementSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\BanditAmnestySettlementUiSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\GuiyiRestorationSourceGuard.ps1
```

Expected: all four guards pass.

- [ ] **Step 2: Run full automated verification**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: rules pass and build completes with zero errors.

- [ ] **Step 3: Deploy source and release DLL**

Run the repository deployment script, which creates a timestamped backup and mirrors production directories:

```powershell
.\deploy-local.ps1 -SourceRoot 'F:\WorldBox New Mod\AncientWarfare3.0' -DestinationRoot 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
```

Then verify relative file counts plus SHA256 hashes. Copy the newly built release DLL only after the source mirror matches.

- [ ] **Step 4: Run runtime scenarios**

1. Kill the final stronghold resident by starvation and combat; zones return to the mother and the stronghold disappears.
2. Amnesty with no reward, a vacant office, and a virtual title.
3. Destroy the title issuer; the title becomes extinct. Grant a new title from a later kingdom.
4. Occupy an integrated-Xia city with a non-integrated culture, lower loyalty below `-50`, and confirm one Guiyi force appears.
5. Confirm a second eligible city is blocked while the first Guiyi force exists.
6. Suppress Guiyi and confirm cleanup plus slot release.
7. Complete restoration with both a living and an extinct original kingdom.

- [ ] **Step 5: Inspect final scope**

```powershell
git status --short
git diff --check
```

Expected: no whitespace errors; only plan and in-scope implementation/test/localization files are part of this work.

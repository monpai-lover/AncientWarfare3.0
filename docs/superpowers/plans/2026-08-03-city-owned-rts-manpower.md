# City-Owned RTS Manpower Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the actor-ID reserve pool with a city-owned half-population manpower ledger that recruits real residents during notice preparation, generates Warrior replacements from a fixed abstract reserve during formal war, and safely removes synthetic soldiers without military merit after war.

**Architecture:** Pure rules define capacity, wartime reserve snapshots, consumption, demobilization, and history suppression. `CityReservePoolService` owns count-only city ledgers; `SyntheticLevyService` is the sole runtime boundary for creating already-Warrior, marking, promoting, and removing synthetic Actors. `TemporaryLevyService` recruits only authentic residents in `Notice`, while `ArmyReplenishmentOperationService` consumes only the fixed synthetic reserve in `War`; both route into the city's canonical ordinary army.

**Tech Stack:** C# 9, Unity/WorldBox runtime APIs, Harmony patches, .NET 9 rule harness, adversarial RTS console simulation.

---

## File Structure

- Create `Code/core/lineage/CityManpowerRules.cs`: pure half-population ledger arithmetic.
- Create `Code/core/lineage/SyntheticLevyRules.cs`: pure provenance, fallback, demobilization, history, and task decisions.
- Create `Code/core/lineage/SyntheticLevySpawnScope.cs`: scoped suppression during Actor creation and removal.
- Create `Code/core/lineage/SyntheticLevyService.cs`: main-thread Actor materialization, promotion, and cleanup.
- Modify `Code/core/lineage/CityReservePoolService.cs`: replace selected Actor-ID pools with count ledgers and bounded authentic-resident selection.
- Modify `Code/core/lineage/TemporaryLevyService.cs`: authentic-only notice recruitment and provenance-aware demobilization.
- Modify `Code/core/lineage/ArmyReplenishmentOperationService.cs`: synthetic-only formal-war replenishment from the fixed reserve snapshot.
- Modify `Code/core/lineage/ArmyReplenishmentOperationService.cs`: consume the same city ledger and clear sticky replenishment states.
- Modify `Code/core/lineage/ArmyFieldIndexService.cs`, `StandingArmyService.cs`, and `AW_StandingArmyPatch.cs`: enforce one canonical ordinary army per city.
- Modify `Code/patch/AW_BirthPatch.cs` and `AW_ActorDeathPatch.cs`: suppress personal persistence for synthetic soldiers.
- Modify `Code/patch/AW_ArmySafetyPatch.cs`: prevent synthetic soldiers from receiving civilian jobs while preserving military, food, healing, transport, and retreat behavior.
- Modify `Code/core/lineage/LineageKeys.cs`: add synthetic provenance fields.
- Modify `Code/core/lineage/ZhuluWarService.cs`, `ZhuluWarRules.cs`, `MandatePhaseService.cs`, and `MandatePhaseRules.cs`: re-audit the shared Mandate-history declaration gate.
- Modify focused rule tests and `Tests/ArmyRtsAdversarialSimulation`: prove lifecycle, performance bounds, and full battle behavior.

### Task 1: Pure City Manpower Arithmetic

**Files:**
- Create: `Code/core/lineage/CityManpowerRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/CityManpowerRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing rule tests**

```csharp
internal static class CityManpowerRulesTests
{
    internal static void Run()
    {
        Equal(0, CityManpowerRules.Capacity(0));
        Equal(5, CityManpowerRules.Capacity(10));
        Equal(5, CityManpowerRules.Capacity(11));
        Equal(3, CityManpowerRules.NoticeHeadroom(10, 2));
        Equal(0, CityManpowerRules.NoticeHeadroom(10, 9));
        Equal(4, CityManpowerRules.AuthenticPopulation(
            authenticResidents: 3, authenticMobilized: 1));
        Equal(3, CityManpowerRules.OpenWarReserve(
            authenticPopulation: 10, livingCitySoldiers: 2));
        Equal(2, CityManpowerRules.WarReserveAvailable(
            reserveCapacity: 3, consumed: 1));
        Equal(2, CityManpowerRules.RequiredSynthetic(
            approvedShortage: 5, availableWarReserve: 2));
        Equal(0, CityManpowerRules.RequiredSynthetic(5, 0));
    }
}
```

Register `CityManpowerRulesTests.Run()` in the `--army-rts` branch and include
both new files in the test project.

- [ ] **Step 2: Run the focused harness and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --army-rts
```

Expected: compilation fails because `CityManpowerRules` does not exist.

- [ ] **Step 3: Implement the minimal arithmetic**

```csharp
using System;

namespace AncientWarfare3.core.lineage
{
    public static class CityManpowerRules
    {
        public static int Capacity(int authenticPopulation)
        {
            return Math.Max(0, authenticPopulation) / 2;
        }

        public static int AuthenticPopulation(int authenticResidents,
            int authenticMobilized)
        {
            long total = (long)Math.Max(0, authenticResidents) +
                         Math.Max(0, authenticMobilized);
            return (int)Math.Min(int.MaxValue, total);
        }

        public static int NoticeHeadroom(int authenticPopulation,
            int activeCitySourcedMilitary)
        {
            return Math.Max(0, Capacity(authenticPopulation) -
                               Math.Max(0, activeCitySourcedMilitary));
        }

        public static int OpenWarReserve(int authenticPopulation,
            int livingCitySoldiers)
        {
            return NoticeHeadroom(authenticPopulation,
                livingCitySoldiers);
        }

        public static int WarReserveAvailable(int reserveCapacity,
            int consumed)
        {
            return Math.Max(0, Math.Max(0, reserveCapacity) -
                               Math.Max(0, consumed));
        }

        public static int RequiredSynthetic(int approvedShortage,
            int availableWarReserve)
        {
            return Math.Min(Math.Max(0, approvedShortage),
                Math.Max(0, availableWarReserve));
        }
    }
}
```

- [ ] **Step 4: Run the focused harness and verify GREEN**

Expected: `AW3 army RTS rules passed.`

- [ ] **Step 5: Commit the rule slice**

```powershell
git add Code/core/lineage/CityManpowerRules.cs Tests/AncientWarfare3.Rules.Tests/CityManpowerRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: define city manpower ledger rules"
```

### Task 2: Synthetic Soldier Lifecycle Rules

**Files:**
- Create: `Code/core/lineage/SyntheticLevyRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/SyntheticLevyRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing provenance and disposal tests**

```csharp
Equal(SyntheticLevyDisposition.RestoreCivilian,
    SyntheticLevyRules.ResolveDemobilization(
        synthetic: false, alive: true, militaryMerit: 0));
Equal(SyntheticLevyDisposition.RemoveActor,
    SyntheticLevyRules.ResolveDemobilization(
        synthetic: true, alive: true, militaryMerit: 0));
Equal(SyntheticLevyDisposition.PromotePermanent,
    SyntheticLevyRules.ResolveDemobilization(
        synthetic: true, alive: true, militaryMerit: 1));
True(SyntheticLevyRules.SuppressPersonalHistory(
    synthetic: true, promoted: false));
False(SyntheticLevyRules.SuppressPersonalHistory(
    synthetic: true, promoted: true));
True(SyntheticLevyRules.AllowTask(true, SyntheticLevyTask.Military));
True(SyntheticLevyRules.AllowTask(true, SyntheticLevyTask.Healing));
False(SyntheticLevyRules.AllowTask(true, SyntheticLevyTask.Social));
False(SyntheticLevyRules.AllowTask(true, SyntheticLevyTask.CivilianWork));
```

- [ ] **Step 2: Run `--army-rts` and verify RED**

Expected: missing `SyntheticLevyRules` and related enum types.

- [ ] **Step 3: Implement the pure lifecycle contract**

```csharp
namespace AncientWarfare3.core.lineage
{
    public enum SyntheticLevyDisposition
    {
        Ignore, RestoreCivilian, RemoveActor, PromotePermanent
    }

    public enum SyntheticLevyTask
    {
        Military, Food, Healing, Transport, Retreat, Formation,
        Social, Sleep, Singing, Laughter, CivilianWork, Marriage,
        Reproduction, Office, School
    }

    public static class SyntheticLevyRules
    {
        public static SyntheticLevyDisposition ResolveDemobilization(
            bool synthetic, bool alive, int militaryMerit)
        {
            if (!alive) return SyntheticLevyDisposition.Ignore;
            if (militaryMerit > 0)
                return SyntheticLevyDisposition.PromotePermanent;
            return synthetic
                ? SyntheticLevyDisposition.RemoveActor
                : SyntheticLevyDisposition.RestoreCivilian;
        }

        public static bool SuppressPersonalHistory(bool synthetic,
            bool promoted)
        {
            return synthetic && !promoted;
        }

        public static bool AllowTask(bool synthetic, SyntheticLevyTask task)
        {
            if (!synthetic) return true;
            return task == SyntheticLevyTask.Military ||
                   task == SyntheticLevyTask.Food ||
                   task == SyntheticLevyTask.Healing ||
                   task == SyntheticLevyTask.Transport ||
                   task == SyntheticLevyTask.Retreat ||
                   task == SyntheticLevyTask.Formation;
        }

        public static bool ShouldClearSyntheticFields(
            SyntheticLevyDisposition disposition)
        {
            return disposition ==
                   SyntheticLevyDisposition.PromotePermanent;
        }

        public static bool ShouldRemoveActor(
            SyntheticLevyDisposition disposition)
        {
            return disposition == SyntheticLevyDisposition.RemoveActor;
        }
    }
}
```

- [ ] **Step 4: Run `--army-rts` and verify GREEN**

- [ ] **Step 5: Commit the lifecycle rules**

```powershell
git add Code/core/lineage/SyntheticLevyRules.cs Tests/AncientWarfare3.Rules.Tests/SyntheticLevyRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: define synthetic levy lifecycle"
```

### Task 3: Replace Actor-ID Pools With Count Ledgers

**Files:**
- Modify: `Code/core/lineage/CityReservePoolRules.cs`
- Modify: `Code/core/lineage/CityReservePoolService.cs`
- Modify: `Code/core/lineage/CityReservePoolPersistenceRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CityReservePoolRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CityReservePoolPersistenceRulesTests.cs.txt`

- [ ] **Step 1: Add failing ledger-state tests**

```csharp
Equal(20, CityReservePoolRules.ResolveAvailableManpower(
    authenticResidents: 30, authenticMobilized: 10,
    activeCitySourcedMilitary: 0));
Equal(5, CityReservePoolRules.ResolveAvailableManpower(
    authenticResidents: 30, authenticMobilized: 10,
    activeCitySourcedMilitary: 15));
False(CityReservePoolRules.CanConfirmManpowerExhausted(
    ledgerReady: false, availableManpower: 0));
True(CityReservePoolRules.CanConfirmManpowerExhausted(
    ledgerReady: true, availableManpower: 0));
Equal(2, CityReservePoolPersistenceRules.CurrentVersion);
```

- [ ] **Step 2: Run `--army-rts` and verify RED**

Expected: missing count-ledger APIs and version 2.

- [ ] **Step 3: Replace runtime pool state**

Add the count-ledger rules used by the tests:

```csharp
public static int ResolveAvailableManpower(int authenticResidents,
    int authenticMobilized, int activeCitySourcedMilitary)
{
    int authentic = CityManpowerRules.AuthenticPopulation(
        authenticResidents, authenticMobilized);
    return CityManpowerRules.NoticeHeadroom(authentic,
        activeCitySourcedMilitary);
}

public static bool CanConfirmManpowerExhausted(bool ledgerReady,
    int availableManpower)
{
    return ledgerReady && availableManpower <= 0;
}
```

Set `CityReservePoolPersistenceRules.CurrentVersion = 2` and reject a version
1 actor-ID snapshot as authoritative; schedule a count-ledger rebuild instead.

Replace per-city selected sets with:

```csharp
private sealed class CityPool
{
    internal int AuthenticResidents;
    internal int AuthenticMobilized;
    internal int SyntheticMobilized;
    internal int ActiveCitySourcedMilitary;
    internal int WarReserveCapacity;
    internal int WarReserveConsumed;
    internal long WarEmergencyId = -1L;
    internal long ReconciledWorldDay = -1L;
    internal bool Ready;
}
```

Implement the O(1) read contract:

```csharp
internal static int CountAvailable(City city)
{
    CityPool pool = ResolveReadyPool(city);
    if (pool == null) return -1;
    int authentic = CityManpowerRules.AuthenticPopulation(
        pool.AuthenticResidents, pool.AuthenticMobilized);
    return CityManpowerRules.NoticeHeadroom(authentic,
        pool.ActiveCitySourcedMilitary);
}
```

`-1` means unknown and forces a city-local rebuild; it is never treated as
confirmed exhaustion. `OpenWarReserve(city, emergencyId)` snapshots
`OpenWarReserve(authentic, active soldiers)` once per formal war, and
`TryReserveWarManpower` increments `WarReserveConsumed` before materialization.
Casualties never decrement it. Remove persistence of `actor_ids`; snapshot
version 2 stores city ID, counts, reserve capacity/consumption/emergency ID,
reconciliation day, and readiness.

Implement reserve mutation under the same main-thread service boundary:

```csharp
internal static int OpenOrReadWarReserve(City city, long emergencyId)
{
    CityPool pool = EnsureReadyPool(city);
    if (pool == null) return -1;
    if (pool.WarEmergencyId != emergencyId)
    {
        int authentic = CityManpowerRules.AuthenticPopulation(
            pool.AuthenticResidents, pool.AuthenticMobilized);
        pool.WarReserveCapacity = CityManpowerRules.OpenWarReserve(
            authentic, pool.ActiveCitySourcedMilitary);
        pool.WarReserveConsumed = 0;
        pool.WarEmergencyId = emergencyId;
    }
    return CityManpowerRules.WarReserveAvailable(
        pool.WarReserveCapacity, pool.WarReserveConsumed);
}

internal static int TryReserveWarManpower(City city, long emergencyId,
    int requested)
{
    int available = OpenOrReadWarReserve(city, emergencyId);
    if (available <= 0) return 0;
    CityPool pool = EnsureReadyPool(city);
    int reserved = Math.Min(Math.Max(0, requested), available);
    pool.WarReserveConsumed += reserved;
    return reserved;
}

internal static void ReleaseUnmaterializedWarReservation(City city,
    long emergencyId, int count)
{
    CityPool pool = ResolvePool(city);
    if (pool == null || pool.WarEmergencyId != emergencyId) return;
    pool.WarReserveConsumed = Math.Max(0,
        pool.WarReserveConsumed - Math.Max(0, count));
}
```

- [ ] **Step 4: Add bounded resident extraction**

```csharp
internal static int TakeAuthenticResidents(Kingdom kingdom, City city,
    int requested, List<Actor> destination)
{
    int available = EnsureAndCountAvailable(city);
    int limit = Math.Min(Math.Max(0, requested), Math.Max(0, available));
    return ScanEligibleResidents(city, kingdom, limit, destination);
}
```

`ScanEligibleResidents` walks `city.units` from the persisted city cursor,
accepts only `TemporaryLevyService.CanRegisterReserve`, and stops when it has
selected `limit` residents or completed one city pass. It does not build or
persist an all-resident ID set.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run `--army-rts`; expected `AW3 army RTS rules passed.`

- [ ] **Step 6: Commit the count ledger**

```powershell
git add Code/core/lineage/CityReservePoolRules.cs Code/core/lineage/CityReservePoolService.cs Code/core/lineage/CityReservePoolPersistenceRules.cs Tests/AncientWarfare3.Rules.Tests/CityReservePoolRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/CityReservePoolPersistenceRulesTests.cs.txt
git commit -m "refactor: use count-based city manpower ledgers"
```

### Task 4: Materialize Exact Wartime Synthetic Shortage

**Files:**
- Create: `Code/core/lineage/SyntheticLevySpawnScope.cs`
- Create: `Code/core/lineage/SyntheticLevyService.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/TemporaryLevyRulesTests.cs.txt`

- [ ] **Step 1: Write failing fallback-count tests**

```csharp
Equal(0, TemporaryLevyRules.SyntheticFallbackRequest(
    ArmyMobilizationPhase.Notice, approvedShortage: 10,
    availableWarReserve: 10));
Equal(7, TemporaryLevyRules.SyntheticFallbackRequest(
    ArmyMobilizationPhase.War, approvedShortage: 10,
    availableWarReserve: 7));
Equal(2, TemporaryLevyRules.SyntheticFallbackRequest(
    ArmyMobilizationPhase.War, approvedShortage: 10,
    availableWarReserve: 2));
Equal(0, TemporaryLevyRules.SyntheticFallbackRequest(
    ArmyMobilizationPhase.War, approvedShortage: 0,
    availableWarReserve: 10));
```

- [ ] **Step 2: Run `--army-rts` and verify RED**

- [ ] **Step 3: Add provenance keys and scoped creation**

Add the phase gate used by the failing tests:

```csharp
public static int SyntheticFallbackRequest(ArmyMobilizationPhase phase,
    int approvedShortage, int availableWarReserve)
{
    return phase == ArmyMobilizationPhase.War
        ? CityManpowerRules.RequiredSynthetic(approvedShortage,
            availableWarReserve)
        : 0;
}
```

```csharp
public const string SYNTHETIC_LEVY = "aw_synthetic_levy";
public const string SYNTHETIC_LEVY_PROMOTED = "aw_synthetic_levy_promoted";
public const string SYNTHETIC_LEVY_SOURCE_CITY_ID =
    "aw_synthetic_levy_source_city_id";
public const string SYNTHETIC_LEVY_SOURCE_KINGDOM_ID =
    "aw_synthetic_levy_source_kingdom_id";
public const string SYNTHETIC_LEVY_EMERGENCY_ID =
    "aw_synthetic_levy_emergency_id";
```

`SyntheticLevySpawnScope` uses a thread-static depth counter and `IDisposable`
token so birth/history patches can reject callbacks during creation.

- [ ] **Step 4: Implement main-thread creation with rollback**

```csharp
internal static Actor TryCreate(City city, Kingdom kingdom, Army army,
    Actor template, long emergencyId)
{
    if (city?.data == null || kingdom?.data == null ||
        army?.data == null || template?.asset == null ||
        World.world?.units == null) return null;
    Actor actor = null;
    try
    {
        using (SyntheticLevySpawnScope.Open())
        {
            actor = World.world.units.createNewUnit(template.asset.id,
                city.getTile(), false, 0f, template.subspecies, null,
                true, true);
            if (actor?.data == null) return null;
            Mark(actor, city, kingdom, emergencyId);
            actor.joinCity(city);
            using (MilitaryRecruitmentScope.Open(
                       MilitaryRecruitmentKind.TemporaryLevy))
                city.makeWarrior(actor);
            if (!actor.isWarrior())
                throw new InvalidOperationException(
                    "synthetic levy did not become warrior");
            AWArmyService.AddToArmy(actor, army);
        }
        if (actor.army != army) throw new InvalidOperationException(
            "synthetic levy army assignment failed");
        return actor;
    }
    catch
    {
        RemoveWithoutPersonalHistory(actor);
        return null;
    }
}

internal static int CreateBatch(City city, Kingdom kingdom, Army army,
    int requested, long emergencyId)
{
    int limit = Math.Min(Math.Max(0, requested),
        TemporaryLevyRules.MaxRecruitsPerWorkItem);
    Actor template = ResolveTemplate(city, army);
    if (template?.asset == null) return 0;
    int created = 0;
    while (created < limit &&
           TryCreate(city, kingdom, army, template, emergencyId) != null)
        created++;
    return created;
}

private static Actor ResolveTemplate(City city, Army army)
{
    Actor captain = null;
    try { captain = army?.getCaptain(); }
    catch { }
    if (captain?.asset != null && captain.kingdom == city?.kingdom)
        return captain;
    if (city?.units == null) return null;
    for (int i = 0; i < city.units.Count; i++)
    {
        Actor actor = city.units[i];
        if (actor?.asset != null && actor.isAlive() && !actor.isRekt() &&
            !IsSynthetic(actor)) return actor;
    }
    return null;
}
```

`RemoveWithoutPersonalHistory` detaches the actor from its army, opens the same
scope, and calls `ActionLibrary.removeUnit(actor)` on the authoritative main
thread.

- [ ] **Step 5: Integrate phase-exclusive recruitment**

The notice work item selects and enlists authentic residents only:

```csharp
int noticeHeadroom = CityReservePoolService.EnsureAndCountAvailable(city);
int requested = Math.Min(approvedShortage, noticeHeadroom);
int selected = CityReservePoolService.TakeAuthenticResidents(
    kingdom, city, requested, CandidateBuffer);
int enlisted = EnlistCandidates(kingdom, city, army, CandidateBuffer);
CityReservePoolService.OnAuthenticMobilized(city, enlisted);
```

The formal-war work item never scans city residents:

```csharp
int available = CityReservePoolService.OpenOrReadWarReserve(
    city, emergencyId);
int syntheticRequest = TemporaryLevyRules.SyntheticFallbackRequest(
    ArmyMobilizationPhase.War, approvedShortage, available);
int reserved = CityReservePoolService.TryReserveWarManpower(
    city, emergencyId, syntheticRequest);
int synthetic = SyntheticLevyService.CreateBatch(
    city, kingdom, army, reserved, emergencyId);
CityReservePoolService.ReleaseUnmaterializedWarReservation(
    city, emergencyId, reserved - synthetic);
```

If `CreateBatch` returns fewer than reserved, release only the unmaterialized
difference. A synthetic death does not release consumed reserve. Do not record
enlistment history for synthetic Actors.

- [ ] **Step 6: Run focused tests and verify GREEN**

- [ ] **Step 7: Commit Actor materialization**

```powershell
git add Code/core/lineage/SyntheticLevySpawnScope.cs Code/core/lineage/SyntheticLevyService.cs Code/core/lineage/LineageKeys.cs Code/core/lineage/TemporaryLevyService.cs Tests/AncientWarfare3.Rules.Tests/TemporaryLevyRulesTests.cs.txt
git commit -m "feat: materialize missing city levies"
```

### Task 5: Demobilization, History, And Task Safety

**Files:**
- Modify: `Code/core/lineage/SyntheticLevyService.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Modify: `Code/core/lineage/TemporaryMilitaryDemobilizationService.cs`
- Modify: `Code/patch/AW_BirthPatch.cs`
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Code/patch/AW_ArmySafetyPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/SyntheticLevyRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ActorDeathInvocationRulesTests.cs.txt`

- [ ] **Step 1: Add failing source guards**

Assert that birth and death patches call
`SyntheticLevyService.SuppressPersonalHistory(actor)` before lineage or person
history writes, and that `TemporaryLevyService.DemobilizeActor` resolves
`SyntheticLevyDisposition` before clearing provenance fields.

```csharp
True(SyntheticLevyRules.ShouldClearSyntheticFields(
    SyntheticLevyDisposition.PromotePermanent));
True(SyntheticLevyRules.ShouldRemoveActor(
    SyntheticLevyDisposition.RemoveActor));
False(SyntheticLevyRules.ShouldRemoveActor(
    SyntheticLevyDisposition.RestoreCivilian));
```

- [ ] **Step 2: Run `--actor-runtime` and `--army-rts`; verify RED**

- [ ] **Step 3: Implement provenance-aware demobilization**

```csharp
SyntheticLevyDisposition disposition =
    SyntheticLevyRules.ResolveDemobilization(
        SyntheticLevyService.IsSynthetic(actor),
        actor.isAlive() && !actor.isRekt(),
        GeneralService.GetMerit(actor));
switch (disposition)
{
    case SyntheticLevyDisposition.RestoreCivilian:
        ClearTemporaryLevyFields(actor);
        TemporaryMilitaryDemobilizationService.RestoreCivilian(actor);
        break;
    case SyntheticLevyDisposition.RemoveActor:
        SyntheticLevyService.RemoveWithoutPersonalHistory(actor);
        break;
    case SyntheticLevyDisposition.PromotePermanent:
        ClearTemporaryLevyFields(actor);
        SyntheticLevyService.Promote(actor);
        break;
}
```

Only authentic restored civilians emit the existing demobilization history.
Synthetic enlistment, removal, and death emit no person-level history.

- [ ] **Step 4: Gate civilian work and personal persistence**

At the beginning of AW3 birth/history handlers, return when
`SyntheticLevySpawnScope.IsActive` or the actor is an unpromoted synthetic
soldier. In the Actor job patch, reject social, sleep, singing, laughter,
civilian work, marriage, reproduction, office, and school jobs while leaving
military, food, healing, transport, retreat, and formation jobs legal.

- [ ] **Step 5: Run both focused suites and verify GREEN**

Expected:

```text
AW3 army RTS rules passed.
AW3 actor runtime rules passed.
```

- [ ] **Step 6: Commit lifecycle safety**

```powershell
git add Code/core/lineage/SyntheticLevyService.cs Code/core/lineage/TemporaryLevyService.cs Code/core/lineage/TemporaryMilitaryDemobilizationService.cs Code/patch/AW_BirthPatch.cs Code/patch/AW_ActorDeathPatch.cs Code/patch/AW_ArmySafetyPatch.cs Tests/AncientWarfare3.Rules.Tests/SyntheticLevyRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/ActorDeathInvocationRulesTests.cs.txt
git commit -m "fix: clean up temporary soldiers after war"
```

### Task 6: One City Army And Phase-Correct Replenishment

**Files:**
- Modify: `Code/core/lineage/ArmyEstablishmentRules.cs`
- Modify: `Code/core/lineage/ArmyFieldIndexService.cs`
- Modify: `Code/core/lineage/StandingArmyService.cs`
- Modify: `Code/core/lineage/ArmyReplenishmentOperationRules.cs`
- Modify: `Code/core/lineage/ArmyReplenishmentOperationService.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/patch/AW_StandingArmyPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CityArmyReinforcementRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyReplenishmentOperationRulesTests.cs.txt`

- [ ] **Step 1: Add failing invariant and completion tests**

```csharp
Equal(ArmyRecruitmentDisposition.Replenish,
    ArmyEstablishmentRules.DecideCityRecruitment(
        existingOrdinaryArmyCount: 1, kingdomFieldArmyCount: 99));
Equal(ArmyRecruitmentDisposition.Create,
    ArmyEstablishmentRules.DecideCityRecruitment(0, 99));
True(ArmyReplenishmentOperationRules.ShouldCompleteAfterFallback(
    living: 10, target: 10, syntheticCreationFailed: false));
False(ArmyReplenishmentOperationRules.ShouldConfirmExhausted(
    ledgerReady: false, availableManpower: 0));
True(ArmyReplenishmentOperationRules.ShouldReleaseDeparture(
    living: 80, target: 100));
```

- [ ] **Step 2: Run `--army-rts` and verify RED**

- [ ] **Step 3: Make the city index authoritative**

All ordinary creation paths first call:

```csharp
if (ArmyFieldIndexService.TryGetCityArmy(city, out Army canonical))
    return canonical;
```

During load or registration, duplicate source-city armies retain the stable
living captain army, then the lowest army ID. Queue member merge and remove the
empty duplicate; never re-anchor it to another city.

Add the terminal rules used by the tests:

```csharp
public static bool ShouldCompleteAfterFallback(int living, int target,
    bool syntheticCreationFailed)
{
    return target > 0 && living >= target && !syntheticCreationFailed;
}

public static bool ShouldConfirmExhausted(bool ledgerReady,
    int availableManpower)
{
    return ledgerReady && availableManpower <= 0;
}

public static bool ShouldReleaseDeparture(int living, int target)
{
    return ArmyMobilizationRules.IsDeploymentReady(living, target);
}
```

- [ ] **Step 4: Use one recruitment operation in Notice and War**

During `Notice`, `TemporaryLevyService.ProcessPreparationMonth` calls only the
authentic resident path. During `War`,
`ArmyReplenishmentOperationService.ProcessOne` calls only the fixed wartime
reserve and synthetic materialization path. On full strength, invalid source,
ended emergency, confirmed phase-specific exhaustion, or deadline it calls
both:

```csharp
Clear(army);
ArmyReplenishmentCompletionService.Complete(army);
```

At 80 percent it releases departure without ending background replenishment.
The RTS controller cannot create a second operation in the same city/month
after terminal completion.

- [ ] **Step 5: Run `--army-rts` and verify GREEN**

- [ ] **Step 6: Commit the army ownership slice**

```powershell
git add Code/core/lineage/ArmyEstablishmentRules.cs Code/core/lineage/ArmyFieldIndexService.cs Code/core/lineage/StandingArmyService.cs Code/core/lineage/ArmyReplenishmentOperationRules.cs Code/core/lineage/ArmyReplenishmentOperationService.cs Code/core/lineage/ArmyRtsControllerService.cs Code/patch/AW_StandingArmyPatch.cs Tests/AncientWarfare3.Rules.Tests/CityArmyReinforcementRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/ArmyReplenishmentOperationRulesTests.cs.txt
git commit -m "fix: enforce one replenishable army per city"
```

### Task 7: Zhulu Gate And Adversarial Verification

**Files:**
- Verify/modify: `Code/core/lineage/MandatePhaseRules.cs`
- Verify/modify: `Code/core/lineage/MandatePhaseService.cs`
- Verify/modify: `Code/core/lineage/ZhuluWarRules.cs`
- Verify/modify: `Code/core/lineage/ZhuluWarService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`
- Modify: `Tests/ArmyRtsAdversarialSimulation/ScenarioFactory.cs`
- Modify: `Tests/ArmyRtsAdversarialSimulation/SimulationAssertions.cs`

- [ ] **Step 1: Add failing declaration-path guards**

```csharp
False(MandatePhaseRules.CanForceChaos(hasMandateHistory: false));
Equal(MandatePhase.Golden,
    MandatePhaseRules.NormalizeLoadedPhase(MandatePhase.Chaos,
        hasMandateHistory: false));
False(ZhuluWarRules.CanStart(new ZhuluEligibilityFacts(
    MandatePhase.Chaos,
    attackerValid: true, defenderValid: true,
    attackerMandateEligible: true,
    defenderMandateEligible: true,
    attackerIsSubject: false, sameSubjectTree: false,
    diplomaticBlocked: false, sameAlliance: false,
    alreadyAtWar: false, ageOverride: false,
    hasMandateHistory: false)));
```

Source guards enumerate every Zhulu AI, diplomacy, and command declaration
call site and assert each reaches `ZhuluWarService.CanDeclare`.

- [ ] **Step 2: Run `--army-rts` and verify any missing gate fails**

If the current code already passes every gate assertion, make no production
change for this task and retain the tests as completion evidence.

- [ ] **Step 3: Add the ten-city/twenty-army scenario**

The scenario begins with empty city armies and dispersed authentic residents,
opens diplomatic notices, proves only existing residents enter during
preparation, starts war, proves every replacement is generated as a Warrior,
applies casualties without refunding reserve, captures and recaptures one
source city, then ends war. Assert:

```csharp
Equal(1, world.OrdinaryArmiesByCity(cityId).Count);
True(army.Living >= army.Target * 80 / 100);
Equal(0, world.CrossCityRecruitCount);
Equal(0, world.UngroupedSyntheticCount);
Equal(0, world.NonWarriorSyntheticSpawnCount);
Equal(0, world.WartimeAuthenticRecruitCount);
Equal(0, world.NoticeSyntheticSpawnCount);
Equal(0, world.StickyReplenishmentCount);
Equal(0, world.SyntheticSurvivorsWithoutMeritAfterPeace);
```

- [ ] **Step 4: Run focused and adversarial verification**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --army-rts
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --actor-runtime
dotnet run --project Tests\ArmyRtsAdversarialSimulation\ArmyRtsAdversarialSimulation.csproj -c Release -- --all --first-seed 0 --seeds 32 --ticks 10000
dotnet run --project Tests\ArmyRtsAdversarialSimulation\ArmyRtsAdversarialSimulation.csproj -c Release -- --scenario battle-10x20 --seed 0
```

Expected: both focused suites pass; all 32 seeds finish; `battle-10x20`
completes all objectives without duplicate attacks or stuck replenishment.

- [ ] **Step 5: Verify performance bounds and production compilation**

Source assertions reject an Actor-ID reserve registry, an all-world actor scan,
synthetic creation outside the main-thread coordinator, and synthetic Actors
entering civilian tasks. Then run:

```powershell
dotnet build AncientWarfare3.csproj -c Release
git diff --check
```

Expected: zero compilation errors and no whitespace errors. If the unrelated
deleted map boundary file still prevents the main project build, compile with
the deployed matching boundary source as a temporary external input and record
that limitation without restoring another session's file.

- [ ] **Step 6: Commit verification changes**

```powershell
git add Code/core/lineage/MandatePhaseRules.cs Code/core/lineage/MandatePhaseService.cs Code/core/lineage/ZhuluWarRules.cs Code/core/lineage/ZhuluWarService.cs Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt Tests/ArmyRtsAdversarialSimulation/ScenarioFactory.cs Tests/ArmyRtsAdversarialSimulation/SimulationAssertions.cs
git commit -m "test: verify city manpower wars end to end"
```

### Task 8: Source-Only Deployment And Runtime Evidence

**Files:**
- Deploy changed production sources to `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`
- Read: `C:/Users/24908/AppData/LocalLow/mkarpenko/WorldBox/Player.log`

- [ ] **Step 1: Copy only changed production source files**

Use `Copy-Item` per changed source path and preserve relative directories. Do
not copy `AncientWarfare3.dll`, `bin`, `obj`, tests, plans, or build output.

- [ ] **Step 2: Verify every deployed source hash**

For every copied file compare:

```powershell
(Get-FileHash $source -Algorithm SHA256).Hash -eq
    (Get-FileHash $destination -Algorithm SHA256).Hash
```

Expected: `True` for every deployed source.

- [ ] **Step 3: Gather a new in-game diagnostic run**

Enable RTS diagnostics, load a world with at least two countries, issue a war
notice, advance one month, start the war, inflict casualties, and end it. The
new log must contain city ID, army ID, phase, authentic population, capacity,
available count, resident enlistments, synthetic creations, roster before and
after, and demobilization disposition.

- [ ] **Step 4: Audit the runtime evidence**

Confirm from the new timestamped log:

- Notice phase increases army roster size using only pre-existing Actors.
- A city with positive authentic population does not report a permanently
  unknown or zero ledger unless half-population capacity is actually consumed.
- Formal war uses no resident scan; synthetic Warriors fill approved shortages
  without casualty refunds.
- No city owns two ordinary armies.
- Replenishment exits or releases departure instead of looping forever.
- Peace removes no-merit synthetic soldiers and restores authentic residents.
- No ordinary pre-Mandate Zhulu declaration appears.

Do not claim runtime completion or mark the goal complete until this evidence
exists.

## Specification Coverage

- Requirements 1-3 (one city-owned ordinary army and flexible missions):
  Task 6.
- Requirements 4-5 (half-population capacity without synthetic feedback):
  Tasks 1 and 3.
- Requirements 6-10 (authentic-only notice recruitment, fixed wartime reserve,
  Warrior-at-creation invariant, no casualty refund, and 80-percent
  deployment): Tasks 3, 4, and 6.
- Requirements 11-13 (restore authentic residents, remove unmerited synthetic
  Actors, preserve merit, and suppress personal history): Tasks 2 and 5.
- Requirement 14 (captured source-city authority): Tasks 3, 6, and 7.
- Requirement 15 (Mandate-history Zhulu gate): Task 7.
- Performance, failure handling, save/load rebuilding, adversarial simulation,
  source deployment, and runtime evidence: Tasks 3 through 8.

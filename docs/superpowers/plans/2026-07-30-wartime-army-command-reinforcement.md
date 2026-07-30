# Persistent City Reserve And Wartime Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Maintain bounded real-actor reserve pools before war, consume those pools for immediate wartime reinforcement, restore attack orders to replacement armies, and add one persistent 20-point exhaustion contribution when an attacking side runs out of reserve manpower.

**Architecture:** `CityReservePoolService` owns civilian reserve membership, freeze state, bounded maintenance, and deterministic actor consumption. `TemporaryLevyService` keeps responsibility for converting a consumed actor into a warrior and attaching it to an army; `KingdomWarDirectorService` keeps strategic assignment; `WarScoreService` persists and composes the reserve-exhaustion contribution. Actor and kingdom data preserve pool membership and freeze generations, while the existing war-score SQLite snapshot preserves the per-side exhaustion latch.

**Tech Stack:** C#, Harmony patches over WorldBox runtime APIs, AW3 authority/deferred work services, SQLite persistence, .NET 9 rules tests, PowerShell source guards.

---

## Baseline Note

The isolated worktree starts at `d373732`. The relevant army information tests pass on `master`, but the complete rules suite currently has one unrelated pre-existing failure:

```text
school portraits retry when the shared live-avatar prefab is not ready:
expected True, got False
```

The implementation must keep all focused reserve, levy, RTS lifecycle, army information, and war-score slices green. The final full-suite comparison may contain only that identical baseline failure unless it is fixed independently before integration.

### Task 1: Define Reserve Capacity, Maintenance, And Exhaustion Rules

**Files:**
- Create: `Code/core/lineage/CityReservePoolRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/CityReservePoolRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add the failing focused rule tests**

Create `CityReservePoolRulesTests.cs.txt` with these cases:

```csharp
using System;

internal static class CityReservePoolRulesTests
{
    internal static void Run()
    {
        Equal(35, CityReservePoolRules.Capacity(100, 80),
            "population caps reserve capacity at 35 percent");
        Equal(20, CityReservePoolRules.Capacity(100, 20),
            "effective warrior slots can be the tighter cap");
        Equal(0, CityReservePoolRules.Capacity(-1, 20),
            "negative population cannot create reserves");

        Equal(true, CityReservePoolRules.CanEnroll(
            alive: true, adult: true, localResident: true,
            baseEligible: true, frozen: false, memberCount: 4,
            capacity: 5), "eligible adult enters a non-full peace pool");
        Equal(false, CityReservePoolRules.CanEnroll(
            alive: true, adult: true, localResident: true,
            baseEligible: true, frozen: false, memberCount: 5,
            capacity: 5), "a full pool rejects without a waiting list");
        Equal(false, CityReservePoolRules.CanEnroll(
            alive: true, adult: true, localResident: true,
            baseEligible: true, frozen: true, memberCount: 0,
            capacity: 5), "an adult event cannot add during war");

        Equal(1, CityReservePoolRules.CityBudget(preparation: false),
            "peace maintenance visits one city per world day");
        Equal(4, CityReservePoolRules.CityBudget(preparation: true),
            "preparation accelerates city maintenance");
        Equal(8, CityReservePoolRules.ActorBudget(preparation: false),
            "peace candidate validation is bounded");
        Equal(32, CityReservePoolRules.ActorBudget(preparation: true),
            "preparation candidate validation remains bounded");

        Equal(true, CityReservePoolRules.ShouldApplyReserveExhaustion(
            attackAssignment: true, reinforcementShortage: 1,
            kingdomFrozen: true, exhaustionConfirmed: true,
            alreadyApplied: false),
            "an attacking army with no reserve receives the penalty");
        Equal(false, CityReservePoolRules.ShouldApplyReserveExhaustion(
            attackAssignment: false, reinforcementShortage: 10,
            kingdomFrozen: true, exhaustionConfirmed: true,
            alreadyApplied: false),
            "defense-only shortage does not receive the penalty");
        Equal(false, CityReservePoolRules.ShouldApplyReserveExhaustion(
            attackAssignment: true, reinforcementShortage: 10,
            kingdomFrozen: true, exhaustionConfirmed: false,
            alreadyApplied: false),
            "an incomplete reserve check defers the penalty");
        Equal(false, CityReservePoolRules.ShouldApplyReserveExhaustion(
            attackAssignment: true, reinforcementShortage: 10,
            kingdomFrozen: true, exhaustionConfirmed: true,
            alreadyApplied: true),
            "the same side and war cannot receive the penalty twice");
        Equal(20, CityReservePoolRules.ReserveExhaustionContribution,
            "reserve exhaustion adds exactly twenty points");
        Equal(100, CityReservePoolRules.ComposeExhaustion(90, 20),
            "total exhaustion remains clamped to one hundred");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(name + ": expected " +
                expected + ", got " + actual);
    }
}
```

Add the production and test files to the rules-test project and add a `--city-reserve-pool-slice` branch in `Program.cs.txt` that calls `CityReservePoolRulesTests.Run()` and prints `AW3 city reserve pool rules passed.`.

- [ ] **Step 2: Run the new slice and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --city-reserve-pool-slice
```

Expected: compilation fails because `CityReservePoolRules` does not exist.

- [ ] **Step 3: Implement the pure rules**

Create `CityReservePoolRules.cs` with this public surface and behavior:

```csharp
using System;

namespace AncientWarfare3.core.lineage
{
    public static class CityReservePoolRules
    {
        public const int PeaceCityBudget = 1;
        public const int PreparationCityBudget = 4;
        public const int PeaceActorBudget = 8;
        public const int PreparationActorBudget = 32;
        public const int ReserveExhaustionContribution = 20;

        public static int Capacity(int population, int effectiveWarriorSlots)
        {
            return CityArmyReinforcementRules.CityCapacity(population,
                effectiveWarriorSlots);
        }

        public static bool CanEnroll(bool alive, bool adult,
            bool localResident, bool baseEligible, bool frozen,
            int memberCount, int capacity)
        {
            return alive && adult && localResident && baseEligible &&
                   !frozen && Math.Max(0, memberCount) <
                   Math.Max(0, capacity);
        }

        public static int CityBudget(bool preparation)
        {
            return preparation ? PreparationCityBudget : PeaceCityBudget;
        }

        public static int ActorBudget(bool preparation)
        {
            return preparation ? PreparationActorBudget : PeaceActorBudget;
        }

        public static bool ShouldApplyReserveExhaustion(
            bool attackAssignment, int reinforcementShortage,
            bool kingdomFrozen, bool exhaustionConfirmed,
            bool alreadyApplied)
        {
            return attackAssignment && reinforcementShortage > 0 &&
                   kingdomFrozen && exhaustionConfirmed && !alreadyApplied;
        }

        public static int ComposeExhaustion(int baseExhaustion,
            int reserveContribution)
        {
            return Math.Max(0, Math.Min(100,
                Math.Max(0, baseExhaustion) +
                Math.Max(0, reserveContribution)));
        }
    }
}
```

- [ ] **Step 4: Run GREEN verification**

Run the `--city-reserve-pool-slice` command again.

Expected: `AW3 city reserve pool rules passed.` and exit code 0.

- [ ] **Step 5: Commit the rule slice**

```powershell
git add Code/core/lineage/CityReservePoolRules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "test: define city reserve pool rules"
```

### Task 2: Persist Event-Driven City Reserve Membership

**Files:**
- Create: `Code/core/lineage/CityReservePoolService.cs`
- Create: `Code/patch/AW_CityReservePoolPatch.cs`
- Create: `Tests/CityReservePoolLifecycleSourceGuardTests.ps1`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/lineage/TemporaryLevyRules.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Code/patch/AW_EnlistPatch.cs`
- Modify: `Code/patch/AW_SlaveryPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add reserve-base-eligibility tests**

Extend the reserve slice with:

```csharp
Equal(true, TemporaryLevyRules.CanRegisterReserve(
    originalEligible: true, protectedIdentity: false, age: 18f),
    "a newly adult eligible civilian can enter the reserve");
Equal(false, TemporaryLevyRules.CanRegisterReserve(
    originalEligible: true, protectedIdentity: false, age: 65f),
    "a person at the enlistment age ceiling cannot enter");
Equal(false, TemporaryLevyRules.CanRegisterReserve(
    originalEligible: true, protectedIdentity: true, age: 20f),
    "protected identities cannot enter the reserve");
```

`AncientWarfare3.Rules.Tests.csproj` already links `Code/core/lineage/TemporaryLevyRules.cs` as `Production/TemporaryLevyRules.cs`; extend the existing linked type and do not add a duplicate project entry.

- [ ] **Step 2: Add a failing lifecycle source guard**

Create a PowerShell guard that reads the exact files above and requires these calls:

```powershell
Require $reservePatch '[HarmonyPatch(typeof(Actor), "eventBecomeAdult")]' `
    'reserve enrollment must use the original adulthood event'
Require $reservePatch 'CityReservePoolService.OnActorBecameAdult(__instance)' `
    'the adulthood event must attempt reserve enrollment'
Require $death 'CityReservePoolService.OnActorInvalidated(__instance)' `
    'death must remove reserve membership immediately'
Require $slavery 'CityReservePoolService.OnActorKingdomChanged(' `
    'kingdom changes must invalidate old reserve ownership'
Require $reservePatch 'CityReservePoolService.OnActorCityChanged(' `
    'city migration must invalidate old city membership'
Require $enlist 'CityReservePoolService.OnActorEnlisted(' `
    'non-reserve enlistment must consume reserve membership'
Require $reserveService 'LineageKeys.CITY_RESERVE_MEMBER' `
    'actor membership must be persisted'
Require $reserveService 'SortedSet<long>' `
    'runtime city membership must be deterministic'
```

Register the guard in `Tests/SourceGuardTests.ps1`.

- [ ] **Step 3: Run RED verification**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --city-reserve-pool-slice
powershell -ExecutionPolicy Bypass -File Tests/CityReservePoolLifecycleSourceGuardTests.ps1
```

Expected: both commands fail because the eligibility method, service, keys, and patches do not exist.

- [ ] **Step 4: Add persistent keys and reserve eligibility**

Add these constants to `LineageKeys.cs`:

```csharp
public const string CITY_RESERVE_MEMBER = "aw_city_reserve_member";
public const string CITY_RESERVE_CITY_ID = "aw_city_reserve_city_id";
public const string CITY_RESERVE_KINGDOM_ID = "aw_city_reserve_kingdom_id";
public const string CITY_RESERVE_GENERATION = "aw_city_reserve_generation";
public const string CITY_RESERVE_KINGDOM_GENERATION =
    "aw_city_reserve_kingdom_generation";
public const string CITY_RESERVE_KINGDOM_FROZEN =
    "aw_city_reserve_kingdom_frozen";
```

Add to `TemporaryLevyRules.cs`:

```csharp
public static bool CanRegisterReserve(bool originalEligible,
    bool protectedIdentity, float age)
{
    return originalEligible && !protectedIdentity && age >= 18f &&
           age < MaximumEnlistmentAge;
}
```

Expose `TemporaryLevyService.CanRegisterReserve(Kingdom, City, Actor)` as an internal method that reuses the current resident, alive/adult, unit profession, slave, protected-identity, original `checkCanMakeWarrior`, and maximum-age checks, but deliberately does not compare current warrior count with warrior slots. Pool capacity is the only reserve-count limit.

- [ ] **Step 5: Implement event-driven membership storage**

Create `CityReservePoolService` with these runtime records and API:

```csharp
private sealed class CityPool
{
    internal readonly SortedSet<long> ActorIds = new SortedSet<long>();
}

private sealed class KingdomPoolState
{
    internal readonly Dictionary<long, CityPool> Cities =
        new Dictionary<long, CityPool>();
    internal long Generation;
    internal bool Frozen;
    internal int CityCursor;
}

internal static void OnActorBecameAdult(Actor actor);
internal static void OnActorInvalidated(Actor actor);
internal static void OnActorCityChanged(Actor actor, City previousCity);
internal static void OnActorKingdomChanged(Actor actor,
    Kingdom previousKingdom);
internal static void OnActorEnlisted(Actor actor);
internal static int CountAvailable(Kingdom kingdom);
internal static int CountAvailable(City city);
internal static void RebuildRuntime();
internal static void ClearRuntime();
```

`OnActorBecameAdult` must resolve city capacity through `CityArmyReinforcementRules.CityCapacity`, call `TemporaryLevyService.CanRegisterReserve`, reject a full or frozen pool immediately, persist all four actor membership fields, and add the actor ID to the city's `SortedSet`. There is no waiting collection.

All invalidation methods must remove from the recorded source city and clear the actor fields to `false/-1`. They must be idempotent because death, enlistment, and migration hooks can overlap.

- [ ] **Step 6: Patch the original lifecycle events**

Create `AW_CityReservePoolPatch.cs` with a postfix on `Actor.eventBecomeAdult`, a prefix/postfix pair on `Actor.joinCity` that captures the old city, and a prefix/postfix pair on `City.setKingdom` that captures the old kingdom and calls `CityReservePoolService.OnCityKingdomChanged` after a real transfer.

Add the direct removal calls to the existing death, enlistment, and actor-kingdom-change patches. Preserve the multiplayer replica gate used by adjacent hooks.

- [ ] **Step 7: Run GREEN lifecycle verification**

Run the reserve slice and `CityReservePoolLifecycleSourceGuardTests.ps1`.

Expected: both pass.

- [ ] **Step 8: Commit event-driven membership**

```powershell
git add Code/core/lineage/CityReservePoolService.cs Code/core/lineage/CityReservePoolRules.cs Code/core/lineage/LineageKeys.cs Code/core/lineage/TemporaryLevyRules.cs Code/core/lineage/TemporaryLevyService.cs Code/patch/AW_CityReservePoolPatch.cs Code/patch/AW_ActorDeathPatch.cs Code/patch/AW_EnlistPatch.cs Code/patch/AW_SlaveryPatch.cs Tests
git commit -m "feat: persist city reserve membership"
```

### Task 3: Add Bounded Peace Maintenance, Preparation, Freeze, And Restore

**Files:**
- Modify: `Code/core/lineage/CityReservePoolService.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Modify: `Tests/CityReservePoolLifecycleSourceGuardTests.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CityReservePoolRulesTests.cs.txt`

- [ ] **Step 1: Add failing freeze and maintenance tests**

Add pure tests for these rules:

```csharp
Equal(true, CityReservePoolRules.CanMaintain(
    frozen: false, worldDayChanged: true),
    "peace maintenance runs once on a new world day");
Equal(false, CityReservePoolRules.CanMaintain(
    frozen: true, worldDayChanged: true),
    "war freeze blocks all additions");
Equal(false, CityReservePoolRules.ShouldUnfreeze(
    activeWarCount: 1),
    "ending one concurrent war does not unfreeze the pool");
Equal(true, CityReservePoolRules.ShouldUnfreeze(
    activeWarCount: 0),
    "leaving the final war reopens peace maintenance");
```

Add `CanMaintain` and `ShouldUnfreeze` only after confirming RED.

- [ ] **Step 2: Extend the source guard for authority and war lifecycle**

Require:

```powershell
Require $authority 'CityReservePoolService.ProcessAuthorityCycle' `
    'reserve repair must run from authority cycles'
Reject $deferred 'CityReservePoolService.ProcessAuthorityCycle' `
    'reserve repair must not run from MapBox.Update presentation work'
Require $warPatch 'CityReservePoolService.OnWarStarted(__result)' `
    'formal war start must freeze both sides before levy conversion'
Require $warPatch 'CityReservePoolService.OnWarEnded(' `
    'war end must reevaluate the final-war freeze'
Require $restore 'new AW3RestoreStage("city_reserve_pools",' `
    'restore must rebuild only persisted reserve membership'
Require $reset 'CityReservePoolService.ClearRuntime' `
    'world reset must clear runtime reserve indexes'
```

Also assert that `OnWarStarted` appears before `TemporaryLevyService.OnWarStarted` in `AW_WarPatch.cs`.

- [ ] **Step 3: Run RED verification**

Run the reserve slice and lifecycle source guard.

Expected: failures for missing freeze rules and lifecycle calls.

- [ ] **Step 4: Implement bounded daily repair**

Add `ProcessAuthorityCycle` to the service. It must use a persisted-independent `lastWorldDay` gate, process at most `CityBudget(preparation)` cities globally, and inspect at most `ActorBudget(preparation)` residents per visited city. `preparation` is true only when a war notice is active and the kingdom is not formally frozen.

For a full city pool, validate/removal work may run, but candidate enrollment must stop without scanning additional residents. A non-full pool advances a per-city actor cursor and attempts event-equivalent enrollment. The method must enqueue no render-frame work and perform no full-world scan.

- [ ] **Step 5: Implement formal freeze transitions**

`OnWarStarted` must freeze every attacker and defender, increment the kingdom generation only when entering the first active war, persist `CITY_RESERVE_KINGDOM_FROZEN=true`, and preserve the partial pool exactly.

`OnWarEnded` and participant leave must call a shared reevaluation. It may clear the persisted freeze only when the kingdom has no active formal wars. A mere active notice enables preparation but does not count as a formal freeze.

- [ ] **Step 6: Restore without refilling**

`RebuildRuntime` must scan persisted actor flags once during world restore, validate IDs/source ownership, and reconstruct the `SortedSet` indexes. If the persisted kingdom freeze is true or formal war restoration is incomplete, it must not enroll any missing actor. Add the restore stage after `military_emergency` and before `temporary_levies`; add `ClearRuntime` beside the same cache reset group.

- [ ] **Step 7: Run GREEN verification and commit**

Run the reserve slice and lifecycle guard, then commit:

```powershell
git add Code/core/lineage/CityReservePoolRules.cs Code/core/lineage/CityReservePoolService.cs Code/core/performance/AWAuthorityCycleService.cs Code/patch/AW_WarPatch.cs Code/core/multiplayer/AW3RuntimeRestorePipeline.cs Tests
git commit -m "feat: maintain and freeze city reserves"
```

### Task 4: Consume Only Frozen Reserve Actors For Army Reinforcement

**Files:**
- Modify: `Code/core/lineage/CityReservePoolService.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Modify: `Code/core/lineage/WartimeMilitaryPotentialService.cs`
- Modify: `Code/core/lineage/ArmyRtsReplenishmentArrivalRules.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsReplenishmentArrivalRulesTests.cs.txt`
- Modify: `Tests/ArmyRtsSchedulingSourceGuardTests.ps1`
- Create: `Tests/CityReserveRecruitmentSourceGuardTests.ps1`

- [ ] **Step 1: Write failing consumption and pre-mission-arrival tests**

Extend the reserve rules tests with deterministic consumption assertions: anchor city before nearby city, ascending actor ID within a city, no duplicate ID across two requests, population 20 blocks consumption, enemy-controlled city blocks consumption, and a fully validated empty set reports `confirmedExhausted=true`.

Extend `ArmyRtsReplenishmentArrivalRulesTests.cs.txt` so a live non-captain member attached to a missionless wartime army returns `Teleport`, while the same record in peacetime returns `Discard`.

- [ ] **Step 2: Add a failing recruitment source guard**

Require these production relationships:

```powershell
Require $levy 'CityReservePoolService.TryConsumeBatch(' `
    'wartime levy recruitment must consume pre-war actor IDs'
Reject $casualtyRegion 'foreach (Actor actor in pCity.units)' `
    'wartime casualty replacement must not scan live residents'
Require $potential 'CityReservePoolService.CountAvailable(' `
    'military potential must reflect remaining reserve membership'
Require $controller 'TryTeleportReinforcementMember' `
    'recruits must teleport before the first mission'
Require $controller 'KingdomWarDirectorService.QueueArmyChanged' `
    'successful arrival must replan the newly operational army'
```

Register the guard in `Tests/SourceGuardTests.ps1`.

- [ ] **Step 3: Run RED verification**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --city-reserve-pool-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-replenishment-arrival-slice
powershell -ExecutionPolicy Bypass -File Tests/CityReserveRecruitmentSourceGuardTests.ps1
```

Expected: failures for missing consumption and missionless teleport behavior.

- [ ] **Step 4: Implement deterministic reserve consumption**

Add:

```csharp
internal static int TryConsumeBatch(Kingdom kingdom, City preferredCity,
    int requested, Army targetArmy, List<Actor> destination,
    out bool confirmedExhausted);
```

The method must require a formally frozen kingdom, order controlled donor cities by preferred-city first, distance, then city ID, and order members by actor ID. Before returning an actor it must validate persisted generation, local residence/kingdom, enlistment eligibility, enemy occupation, and `WartimeRecruitmentPopulationRules.RecruitmentCapacity(population, 1)`. It removes and clears membership atomically before adding the actor to `destination`. Invalid IDs are removed without replacement outside the same bounded batch. `confirmedExhausted` is true only after every indexed city has been checked with no usable ID and no pending validation cursor.

- [ ] **Step 5: Route initial mobilization and casualty recovery through the pool**

Keep `TemporaryLevyService` orchestration, role conversion, biography, army membership, and demand accounting. Replace its wartime resident-selection loops for ordinary field-army recruitment, directed casualty recovery, and captain recovery with `TryConsumeBatch`. Preparation work must only request reserve maintenance; it must not call `makeWarrior` before formal war.

If the pool returns fewer actors than requested, reduce demand only by successful enlistments. If it reports confirmed exhaustion, retain the explicit shortage state and stop scheduling candidate scans. Do not synthesize counts and do not add new reserve members during war.

- [ ] **Step 6: Make military potential use the reserve index**

Change wartime potential to current living ordinary military plus `CityReservePoolService.CountAvailable(kingdom)`. Remove the rotating live-population estimate from the formal-war path so an exhausted side cannot repeatedly believe it has fresh recruits.

- [ ] **Step 7: Teleport real actors before the first mission**

Implement `ShouldTrackArrival` with separate `missionActive` and `wartimeEmergency` inputs. Refactor the existing teleport into `TryTeleportReinforcementMember`; when no mission exists, validate army membership, captain, combat/transport safety, then stop movement and spawn the actor on the captain tile. After successful arrival, queue the kingdom director.

- [ ] **Step 8: Run GREEN verification and commit**

Run the three commands from Step 3 and the existing city-reinforcement source guard:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --city-reserve-pool-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-replenishment-arrival-slice
powershell -ExecutionPolicy Bypass -File Tests/CityReserveRecruitmentSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/CityArmyReinforcementSourceGuard.ps1
```

Expected: all four commands pass. Then commit:

```powershell
git add Code/core/lineage/CityReservePoolService.cs Code/core/lineage/TemporaryLevyService.cs Code/core/lineage/WartimeMilitaryPotentialService.cs Code/core/lineage/ArmyRtsReplenishmentArrivalRules.cs Code/core/lineage/ArmyRtsControllerService.cs Tests
git commit -m "fix: reinforce armies from frozen city reserves"
```

### Task 5: Replan Replacement Armies And Restore Open Attacks

**Files:**
- Modify: `Code/core/lineage/ArmyStrategicIndexService.cs`
- Modify: `Code/core/lineage/ArmyRtsModels.cs`
- Modify: `Code/core/lineage/ArmyStrategicSnapshotService.cs`
- Modify: `Code/core/lineage/KingdomWarDirectorRules.cs`
- Modify: `Code/core/lineage/KingdomWarDirectorService.cs`
- Modify: `Code/core/lineage/CoalitionWarTaskService.cs`
- Modify: `Tests/ArmyRtsSchedulingSourceGuardTests.ps1`
- Modify: `Tests/ArmyRtsSourceGuardTests.ps1`
- Create: `Tests/ReplacementArmyCommandSourceGuardTests.ps1`

- [ ] **Step 1: Add failing replacement-army rule coverage**

Add focused director assertions through the existing RTS command slice:

```csharp
// No homeland threat: every operational replacement is Assault.
var normal = KingdomWarDirectorRules.AllocateWars(
    new[] { new WarAllocationFacts(1, false, true, 0, 2, false) },
    new[] { new ArmyAllocationFacts(10, 20),
            new ArmyAllocationFacts(11, 20) });
Equal(ArmyRtsRole.Assault, normal[0].Role,
    "replacement army does not default to defense");

// Capital threat: only the first required slot is Defense.
var threatened = KingdomWarDirectorRules.AllocateWars(
    new[] { new WarAllocationFacts(2, true, true, 0, 2, true) },
    new[] { new ArmyAllocationFacts(20, 20),
            new ArmyAllocationFacts(21, 20) });
Equal(ArmyRtsRole.Defense, threatened[0].Role,
    "capital threat reserves one defense slot");
Equal(ArmyRtsRole.Assault, threatened[1].Role,
    "additional replacement remains available to attack");

Equal(ArmyRtsRole.Reserve,
    KingdomWarDirectorRules.ResolveMissionRole(
        ArmyRtsRole.Assault, hasStrategicTarget: false,
        forceReady: true),
    "an operational field army without an open target waits in reserve");

Equal(true, KingdomWarDirectorRules.ShouldAllocateFieldArmy(
        unitCount: 20, captainAlive: true, royalGuard: false,
        dedicatedGarrison: false, specialArmy: false),
    "an ordinary former defense army can return to field allocation");
Equal(false, KingdomWarDirectorRules.ShouldAllocateFieldArmy(
        unitCount: 20, captainAlive: true, royalGuard: false,
        dedicatedGarrison: false, specialArmy: true),
    "a special army never enters ordinary defense-to-attack conversion");
```

Use the existing exact constructors: `WarAllocationFacts(long warId, bool capitalThreat, bool warGoalThreat, int signedWarScore, int requiredArmies, bool localTerritoryThreat = false)` and `ArmyAllocationFacts(long armyId, int effectiveForce)`. Do not introduce test-only overloads.

Add `WarParticipantRosterRulesTests.Run()` to `--rts-wartime-lifecycle-slice` so the focused lifecycle command proves that both attacker and defender kingdoms are enumerated. Keep the existing default-suite invocation; the test class is stateless.

- [ ] **Step 2: Add a failing lifecycle source guard**

Require:

```powershell
Require $index 'OnArmyRegistered' 'new armies must enter the strategic index'
Require $index 'KingdomWarDirectorService.QueueArmyChanged' `
    'army lifecycle changes must use the coalesced director queue'
Require $index 'OnArmyRosterChanged' `
    'roster growth must be observed'
Require $index 'CoalitionWarTaskService.OnArmyInvalidated(pArmy.id)' `
    'destroyed assault reservations must be released'
Reject $registeredRegion 'KingdomWarDirectorService.OnArmyChanged' `
    'captain-only creation must not run an immediate stale plan'
Require $snapshot 'AWArmyService.IsSpecialArmy(pArmy)' `
    'special armies must be identified before field allocation'
Require $director 'pArmy.SpecialArmy' `
    'special armies must stay outside ordinary role conversion'
```

The guard must isolate method regions so a call in `OnArmyDisposed` cannot satisfy the roster requirement.

- [ ] **Step 3: Run RED verification**

Run the RTS command slice and the new guard.

Expected: the guard fails because `OnArmyRosterChanged` does not queue the director and disposal does not explicitly release coalition reservations.

- [ ] **Step 4: Queue every relevant lifecycle transition**

Change `OnArmyRegistered`, `OnArmyKingdomChanged`, and `OnArmyRosterChanged` to refresh indexes and call `KingdomWarDirectorService.QueueArmyChanged` for all affected live kingdoms. Queueing, rather than immediate planning, lets the current `newArmy/setArmy` mutation stack finish and coalesces a batch of soldiers into one plan.

On disposal, call `CoalitionWarTaskService.OnArmyInvalidated(pArmy.id)` before removing indexes, then queue the former kingdom. If ownership changes, queue both old and new kingdoms when both IDs can be resolved.

Add a `SpecialArmy` property to `ArmyStrategicFacts`, capture it with `AWArmyService.IsSpecialArmy(pArmy)`, and pass it to the exact expanded rule:

```csharp
public static bool ShouldAllocateFieldArmy(int unitCount,
    bool captainAlive, bool royalGuard, bool dedicatedGarrison,
    bool specialArmy)
{
    return ArmyLogisticsRules.HasMinimumOperationalForce(unitCount) &&
           captainAlive && !royalGuard && !dedicatedGarrison &&
           !specialArmy;
}
```

Update the sole production caller in `IsEligibleFieldArmy`. Dedicated garrisons, royal guards, and every other AW3 special army then remain owned by their dedicated services instead of entering normal defense-to-attack conversion.

- [ ] **Step 5: Preserve open enemy objectives for replacements**

Ensure a stale claim held by a destroyed army is absent before `BuildFrontTargetAssignments`. A replacement crossing `ArmyLogisticsRules.MinimumOperationalForce` must enter `AssignFrontTargets`; with an `OpenAttack` target and no capital threat, `ResolvePlanTarget` must publish `ProposalKind.Attack` and `Role.Assault` or `Role.Reinforcement`, never `Role.Defense`.

Keep the existing rule that a real capital threat consumes only the first defense slot. Allocation remains stateless with respect to an ordinary army's previous `Defense` display role: after defensive demand clears, the next director plan can assign that army to an open attack. When there is no open attack, `ResolveMissionRole` must publish `Reserve` against a valid friendly anchor instead of omitting the army. Do not add a permanent offensive/defensive army class.

- [ ] **Step 6: Run GREEN verification and commit**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-command-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-wartime-lifecycle-slice
powershell -ExecutionPolicy Bypass -File Tests/ArmyRtsSchedulingSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/ReplacementArmyCommandSourceGuardTests.ps1
```

Expected: all pass. Commit:

```powershell
git add Code/core/lineage/ArmyStrategicIndexService.cs Code/core/lineage/ArmyRtsModels.cs Code/core/lineage/ArmyStrategicSnapshotService.cs Code/core/lineage/KingdomWarDirectorRules.cs Code/core/lineage/KingdomWarDirectorService.cs Code/core/lineage/CoalitionWarTaskService.cs Tests
git commit -m "fix: reassign replacement armies to open attacks"
```

### Task 6: Persist The One-Time 20-Point Reserve Exhaustion Contribution

**Files:**
- Modify: `Code/core/db/WarScoreSnapshotTableItem.cs`
- Modify: `Code/core/lineage/WarScoreService.cs`
- Modify: `Code/core/lineage/WarScorePersistence.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Create: `Tests/ReserveExhaustionPersistenceSourceGuardTests.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CityReservePoolRulesTests.cs.txt`

- [ ] **Step 1: Add failing exhaustion composition tests**

Add tests proving base 35 plus reserve 20 produces 55 before relief, a second application remains 20 rather than 40, and defender/attacker fields are independent. Keep the total clamped to 100.

- [ ] **Step 2: Add a failing persistence source guard**

Require:

```powershell
Require $table 'attacker_reserve_exhaustion' `
    'the attacker reserve contribution must be archived'
Require $table 'defender_reserve_exhaustion' `
    'the defender reserve contribution must be archived'
Require $persistence 'ATTACKER_RESERVE_EXHAUSTION' `
    'SQLite persistence must include the attacker column'
Require $persistence 'DEFENDER_RESERVE_EXHAUSTION' `
    'SQLite persistence must include the defender column'
Require $score 'ApplyReserveExhaustion' `
    'war score must expose an idempotent mutation'
Require $controller 'ShouldApplyReserveExhaustion' `
    'only a confirmed attacking shortage may trigger the mutation'
```

- [ ] **Step 3: Run RED verification**

Run the reserve slice and persistence guard.

Expected: failure because no reserve-exhaustion fields or mutation exist.

- [ ] **Step 4: Add schema columns and snapshot fields**

Add integer fields with default zero to `WarScoreSnapshotTableItem` and matching public internal-set properties to `WarScoreSnapshot`:

```csharp
public int AttackerReserveExhaustion { get; internal set; }
public int DefenderReserveExhaustion { get; internal set; }
```

Add both names to `SnapshotColumns`, `CREATE TABLE`, `EnsureColumn`, `WriteSnapshot`, and `ReadSnapshot`. `EnsureColumn` must use `INTEGER NOT NULL DEFAULT 0` so old autosaves migrate without data loss.

- [ ] **Step 5: Add an idempotent war-score mutation**

Implement:

```csharp
internal bool ApplyReserveExhaustion(long warId, WarScoreSide side,
    double worldTime)
```

Under `_gate`, reject inactive wars and non-participant sides. Clone the canonical snapshot, set only the requested side's reserve contribution to `Math.Max(existing, 20)`, return false if unchanged, recalculate exhaustion, touch/save, and replace `_active[warId]`.

In `RecalculateLossesAndExhaustion`, compose casualty/duration exhaustion with the side's reserve contribution before applying existing victory relief, then clamp through the current `0..100` path. Do not alter losses.

- [ ] **Step 6: Trigger only from a confirmed attacking shortage**

When an RTS mission has `ProposalKind.Attack`, `CityArmyReinforcementService.ApprovedTarget` exceeds living members, and `TryConsumeBatch` reports a fully validated empty kingdom reserve, call `CityReservePoolRules.ShouldApplyReserveExhaustion`. Resolve the mission's war and participant side, then call `WarScoreService.ApplyReserveExhaustion`.

Do not trigger for `Defend`, `FrontHold`, `Reserve`, an empty anchor city with other pools available, or a bounded scan that has not confirmed exhaustion. The persisted side field is the once-per-war latch.

- [ ] **Step 7: Run GREEN verification and commit**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --city-reserve-pool-slice
powershell -ExecutionPolicy Bypass -File Tests/ReserveExhaustionPersistenceSourceGuardTests.ps1
powershell -ExecutionPolicy Bypass -File Tests/WarPeaceIntegrationTests.ps1
```

Expected: all pass. Commit:

```powershell
git add Code/core/db/WarScoreSnapshotTableItem.cs Code/core/lineage/WarScoreService.cs Code/core/lineage/WarScorePersistence.cs Code/core/lineage/ArmyRtsControllerService.cs Tests
git commit -m "feat: add reserve exhaustion to war score"
```

### Task 7: Verify Integration, Merge, Deploy, And Test An Autosave

**Files:**
- Verify: `AncientWarfare3.csproj`
- Verify: `docs/superpowers/specs/2026-07-30-wartime-army-command-lifecycle-design.md`
- Deploy changed runtime files to: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run all focused rule slices**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --city-reserve-pool-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-replenishment-arrival-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-wartime-lifecycle-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --rts-command-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --army-map-information-slice
```

Expected: every command exits 0 and prints its slice success message.

- [ ] **Step 2: Run all focused source guards**

Run the new three reserve/replacement guards plus `ArmyRtsSchedulingSourceGuardTests.ps1`, `ArmyRtsSourceGuardTests.ps1`, and `ArmyMapInformationMinimapSourceGuardTests.ps1`.

Expected: all focused guards exit 0.

- [ ] **Step 3: Build the mod**

```powershell
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: 0 compiler errors. Record warnings rather than silently omitting them.

- [ ] **Step 4: Compare the complete rules suite with baseline**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: either full success, or only the identical pre-existing school-portrait assertion recorded in the Baseline Note. Any additional failure blocks integration.

- [ ] **Step 5: Review the implementation diff against every spec test item**

Use `git diff master...HEAD --check`, inspect every production/test file, and confirm: no waiting list, no wartime additions, no live wartime actor scan, save/load does not refill, concurrent wars share one pool, replacement armies replan, defense converts only when threats clear, and reserve exhaustion is idempotent.

Coverage map for the 30 numbered spec tests: Task 5 covers 1-7 and 26-28; Task 2 covers 8-10; Task 3 covers 11-18; Task 4 covers 19-23; Task 6 covers 24-25; the already implemented army-information slice plus Task 7 covers 29; Task 7's complete focused and full-suite verification covers 30. A numbered item without passing evidence blocks integration.

- [ ] **Step 6: Merge the isolated branch into master**

From the main worktree, verify concurrent changes first, merge `fix/wartime-army-command-reinforcement` non-interactively, and rerun Steps 1 through 4 on the merged tree. Do not overwrite unrelated user or concurrent work.

- [ ] **Step 7: Deploy exact runtime files**

Copy only changed production files to matching paths under the installed mod. For every copied file, compare source and deployed SHA-256 hashes and require equality before testing.

- [ ] **Step 8: Test using an autosave, not `save8`**

Load an autosave with an active or preparable war and verify:

1. Newly adult eligible civilians enter non-full peace pools.
2. Full pools do not create waiting candidates.
3. Preparation increases pool completion without making warriors.
4. Formal war freezes the exact current membership.
5. Both attackers and defenders recruit real pooled actors.
6. Reinforcements appear at the army immediately and the flag shows the correct shortage/order state.
7. After an assault army is destroyed, a replacement crossing operational strength receives an open attack objective unless a real capital threat needs the first defense slot.
8. Once all usable reserve actors are consumed, no later birth or migration refills the pool during war.
9. The first confirmed attacking shortage adds 20 reserve exhaustion; repeated checks and save/load do not add another 20.
10. Ending the final war unfreezes maintenance; ending only one concurrent war does not.

- [ ] **Step 9: Commit any acceptance-only corrections separately**

If autosave testing exposes a defect, add a focused failing regression first, make the smallest correction, rerun the affected slice and full focused set, and commit with a message naming the observed defect.

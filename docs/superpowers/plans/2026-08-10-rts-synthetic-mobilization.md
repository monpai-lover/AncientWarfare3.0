# RTS Synthetic Mobilization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace AW3 real-resident wartime levies with synthetic soldiers, guarantee their restricted and temporary lifecycle, and make RTS assignment exact once under native and large-step scheduling.

**Architecture:** A focused `SyntheticMobilizationLedgerService` owns persisted city-war quotas, replacement reserve and bounded spawn/demobilization queues. `SyntheticLevyService` remains the atomic Actor materialization boundary, while shared task and reproduction patches enforce synthetic identity. RTS reconciliation and scheduling consume one shared logical-pass token so new armies receive missions promptly in both scheduler modes.

**Tech Stack:** C# 10, .NET SDK-style projects, Harmony patches, Newtonsoft.Json sidecar persistence, WorldBox/Unity runtime APIs, console-based rules tests and adversarial RTS simulation.

---

## File Map

- Create `Code/core/lineage/SyntheticMobilizationRules.cs`: pure quota, reserve, lifecycle and batch arithmetic.
- Create `Code/core/lineage/SyntheticMobilizationLedgerService.cs`: runtime records, save/load DTOs, city/war queues, army binding and bounded demobilization.
- Create `Tests/AncientWarfare3.Rules.Tests/SyntheticMobilizationRulesTests.cs.txt`: pure rule tests and source guards.
- Modify `Code/core/lineage/SyntheticLevyRules.cs`: permanent synthetic identity, task whitelist and unconditional post-war removal.
- Modify `Code/core/lineage/SyntheticLevyService.cs`: mark city-war identity, remove promotion, expose bounded removal.
- Modify `Code/core/lineage/ArmyReplenishmentOperationService.cs`: consume only the numeric replacement reserve.
- Modify `Code/core/lineage/CityReservePoolService.cs`: perform one-time old snapshot migration, then relinquish all wartime ledger ownership.
- Modify `Code/core/lineage/TemporaryLevyService.cs`: remove synthetic promotion and old real-Actor preparation/recovery ownership.
- Modify `Code/patch/AW_StandingArmyPatch.cs`: restore unscoped vanilla `City.tryToMakeWarrior`.
- Modify `Code/patch/AW_WartimeMilitaryTaskPatch.cs`: enforce synthetic job/task whitelist.
- Modify `Code/patch/AW_DynasticReproductionPatch.cs`: reject `BabyMaker.makeBaby` when either parent is synthetic.
- Modify `Code/patch/AW_WarPatch.cs`: feed war start, join, leave and end events to the new ledger.
- Modify `Code/patch/AW_SavePatch.cs`: persist and restore mobilization records with the existing save sidecar lifecycle.
- Modify `Code/core/lineage/ArmyRtsWarLifecycleService.cs`: bounded discovery of missing war-army lifecycle records.
- Modify `Code/core/lineage/ArmyRtsAssignmentReconciliationService.cs`: wake newly valid armies immediately.
- Modify `Code/core/lineage/KingdomWarDirectorRules.cs`: attacker/defender alternating priority queue rules.
- Modify `Code/core/lineage/KingdomWarDirectorService.cs`: publish a valid first mission within two logical pulses.
- Modify `Code/core/lineage/ArmyRtsControllerService.cs`: persistent cursor coverage for armies above 128 members.
- Modify `Code/core/performance/ArmyRtsSchedulingMode.cs`: frozen owner and shared exact-once gate.
- Modify `Code/core/performance/ArmyRtsSchedulingService.cs`: one bounded RTS pulse per logical token.
- Modify `Code/core/performance/AWCooperativeSimulationRunner.cs`: run RTS pulse on every internal large-step pass.
- Modify `Code/core/performance/AWAuthorityCycleService.cs`: remove duplicate final-pass RTS ownership.
- Modify `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`: include new pure rules and tests.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`: invoke new tests.
- Modify `Tests/ArmyRtsAdversarialSimulation/ScenarioFactory.cs`: add first-order and large-step equivalence scenarios.
- Modify `Tests/ArmyRtsAdversarialSimulation/Program.cs`: expose the new scenarios.

### Task 1: Restore Vanilla Recruitment And Finalize Synthetic Rules

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/SyntheticLevyRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/RecruitmentLifecycleRemovalRulesTests.cs.txt`
- Modify: `Code/core/lineage/SyntheticLevyRules.cs`
- Modify: `Code/patch/AW_StandingArmyPatch.cs`

- [ ] **Step 1: Write failing rules and source guards**

Change the merit expectation to unconditional removal, add sleep/rest to the
whitelist, and assert the vanilla method is no longer gated:

```csharp
Equal(SyntheticLevyDisposition.RemoveActor,
    SyntheticLevyRules.ResolveDemobilization(true, true, 99),
    "military merit never makes a generated levy permanent");
Equal(true, SyntheticLevyRules.AllowTask(true, SyntheticLevyTask.Sleep),
    "generated levies may rest");
Equal(false, SyntheticLevyRules.AllowTask(true, SyntheticLevyTask.Marriage),
    "generated levies cannot marry");

string standingPatch = File.ReadAllText(Path.Combine(root, "Code", "patch",
    "AW_StandingArmyPatch.cs"));
Assert(!standingPatch.Contains("TryToMakeWarrior_Prefix",
    StringComparison.Ordinal),
    "AW3 must not globally block vanilla autonomous recruitment");
```

- [ ] **Step 2: Run rules tests and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: failure reports merit promotion, sleep denial, and the surviving
`TryToMakeWarrior_Prefix`.

- [ ] **Step 3: Implement minimal rule and patch changes**

Make demobilization independent of merit and include rest:

```csharp
public static SyntheticLevyDisposition ResolveDemobilization(
    bool synthetic, bool alive, int militaryMerit)
{
    if (!alive) return SyntheticLevyDisposition.Ignore;
    return synthetic
        ? SyntheticLevyDisposition.RemoveActor
        : SyntheticLevyDisposition.RestoreCivilian;
}

return task == SyntheticLevyTask.Military ||
       task == SyntheticLevyTask.Food ||
       task == SyntheticLevyTask.Healing ||
       task == SyntheticLevyTask.Transport ||
       task == SyntheticLevyTask.Retreat ||
       task == SyntheticLevyTask.Formation ||
       task == SyntheticLevyTask.Sleep;
```

Delete only the Harmony prefix targeting private `City.tryToMakeWarrior`.
Keep `checkCanMakeWarrior` capacity bypass for explicit scoped synthetic and
special-unit creation.

- [ ] **Step 4: Run rules tests and verify GREEN**

Run the command from Step 2. Expected: `Rule tests passed.`

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/SyntheticLevyRules.cs Code/patch/AW_StandingArmyPatch.cs Tests/AncientWarfare3.Rules.Tests/SyntheticLevyRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/RecruitmentLifecycleRemovalRulesTests.cs.txt
git commit -m "fix: restore vanilla recruitment ownership"
```

### Task 2: Add Numeric City-War Mobilization Rules

**Files:**
- Create: `Code/core/lineage/SyntheticMobilizationRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/SyntheticMobilizationRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing pure-rule tests**

```csharp
Equal(50, SyntheticMobilizationRules.Quota(100, 0, 50),
    "50 percent law mobilizes half the real city population");
Equal(40, SyntheticMobilizationRules.Quota(100, 20, 50),
    "existing generated actors do not inflate real population");
Equal(0, SyntheticMobilizationRules.Quota(3, 0, 30),
    "quota uses floor without a small-city minimum");
Equal(8, SyntheticMobilizationRules.Batch(23,
    SyntheticMobilizationRules.SpawnBatchLimit),
    "spawn work is bounded");
Equal(12, SyntheticMobilizationRules.ReplacementDemand(
    target: 40, living: 28, remainingReserve: 15),
    "casualties consume only numeric reserve");
Equal(15, SyntheticMobilizationRules.ReplacementDemand(40, 10, 15),
    "replacement demand cannot exceed reserve");
```

Add both compile includes and invoke `SyntheticMobilizationRulesTests.Run()`.

- [ ] **Step 2: Run tests and verify RED**

Run the rules project. Expected: compile failure because
`SyntheticMobilizationRules` does not exist.

- [ ] **Step 3: Implement the pure rules**

```csharp
public static class SyntheticMobilizationRules
{
    public const int SpawnBatchLimit = 8;
    public const int ReplacementBatchLimit = 8;
    public const int DemobilizationBatchLimit = 16;

    public static int Quota(int cityPopulation, int knownSynthetic,
        int lawPercent)
    {
        long real = Math.Max(0L,
            (long)Math.Max(0, cityPopulation) - Math.Max(0, knownSynthetic));
        long percent = Math.Max(0, Math.Min(100, lawPercent));
        return (int)Math.Min(int.MaxValue, real * percent / 100L);
    }

    public static int Batch(int pending, int limit) =>
        Math.Min(Math.Max(0, pending), Math.Max(0, limit));

    public static int ReplacementDemand(int target, int living,
        int remainingReserve) => Math.Min(Math.Max(0, remainingReserve),
            Math.Max(0, target - Math.Max(0, living)));
}
```

- [ ] **Step 4: Run tests and verify GREEN**

Expected: `Rule tests passed.`

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/SyntheticMobilizationRules.cs Tests/AncientWarfare3.Rules.Tests/SyntheticMobilizationRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: add synthetic mobilization arithmetic"
```

### Task 3: Implement Persisted Mobilization Ledger

**Files:**
- Create: `Code/core/lineage/SyntheticMobilizationLedgerService.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/SyntheticMobilizationRulesTests.cs.txt`

- [ ] **Step 1: Add failing lifecycle and persistence source guards**

Assert the new service exposes these exact boundaries:

```csharp
Assert(source.Contains("OnWarStarted(War pWar)", StringComparison.Ordinal));
Assert(source.Contains("OnKingdomJoinedWar(War pWar, Kingdom pKingdom)",
    StringComparison.Ordinal));
Assert(source.Contains("OnWarEnded(War pWar)", StringComparison.Ordinal));
Assert(source.Contains("ProcessAuthorityCycle()", StringComparison.Ordinal));
Assert(source.Contains("city.getPopulationPeople()", StringComparison.Ordinal));
Assert(!source.Contains("city.units[", StringComparison.Ordinal));
```

- [ ] **Step 2: Run tests and verify RED**

Expected: failure because the ledger service file is absent.

- [ ] **Step 3: Add record, DTO and bounded queue implementation**

Use a `(warId, cityId)` key and these persisted fields. This is the only
runtime ledger for initial deployment and replacements:

```csharp
internal sealed class SyntheticMobilizationRecord
{
    internal long WarId;
    internal long KingdomId;
    internal long CityId;
    internal long ArmyId = -1L;
    internal int PopulationSnapshot;
    internal int LawPercent;
    internal int Quota;
    internal int InitialCreated;
    internal int ReplacementRemaining;
    internal int ReplacementCreated;
    internal int LiveSynthetic;
    internal SyntheticMobilizationPhase Phase;
}
```

`OnWarStarted` and join events enqueue city IDs from both war sides without
processing them immediately. One authority work item dequeues one city,
reads `city.getPopulationPeople()`, subtracts `LiveSynthetic`, resolves the
30/50/70/100 law through `CourtConscriptionLawRules`, and creates the record.
Use Newtonsoft.Json DTOs in the same sidecar directory as
`CityReservePoolService`, with atomic `.tmp` replacement. Restore clamps all
counters to `Quota` and queues active or demobilizing records. If only an old
reserve snapshot exists, import its numeric capacity and consumption once,
write the new record on the next save, and never keep the old service active
as a second wartime ledger.

- [ ] **Step 4: Wire save, war and authority events**

Add host-authoritative calls beside existing reserve-pool calls:

```csharp
SyntheticMobilizationLedgerService.OnWarStarted(__result);
SyntheticMobilizationLedgerService.OnWarEnded(pWar);
SyntheticMobilizationLedgerService.ProcessAuthorityCycle();
SyntheticMobilizationLedgerService.TryWriteSnapshot(pFolder, out _);
SyntheticMobilizationLedgerService.TryRestoreSnapshot(pFolder, out _);
```

Replica callbacks must return before mutating the ledger.

- [ ] **Step 5: Run rules and production compilation**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Release
```

Expected: rules pass; production build has zero errors.

- [ ] **Step 6: Commit**

```powershell
git add Code/core/lineage/SyntheticMobilizationLedgerService.cs Code/core/lineage/LineageKeys.cs Code/patch/AW_SavePatch.cs Code/patch/AW_WarPatch.cs Code/core/performance/AWAuthorityCycleService.cs Tests/AncientWarfare3.Rules.Tests/SyntheticMobilizationRulesTests.cs.txt
git commit -m "feat: persist city war mobilization ledger"
```

### Task 4: Materialize Full Synthetic Levy And Numeric Replacements

**Files:**
- Modify: `Code/core/lineage/SyntheticLevyService.cs`
- Modify: `Code/core/lineage/SyntheticMobilizationLedgerService.cs`
- Modify: `Code/core/lineage/ArmyReplenishmentOperationService.cs`
- Modify: `Code/core/lineage/CityReservePoolService.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/SyntheticLevyRulesTests.cs.txt`

- [ ] **Step 1: Add failing source guards**

Require `CreateBatch` to debit the city-war ledger, prohibit resident actor
indexes from preparation, and prohibit promotion:

```csharp
Assert(replenishment.Contains(
    "SyntheticMobilizationLedgerService.TryReserveReplacement",
    StringComparison.Ordinal));
Assert(!temporaryLevy.Contains("SyntheticLevyService.Promote(",
    StringComparison.Ordinal));
Assert(!reservePool.Contains("TryTakeNextActorId(pool.ActorIds",
    StringComparison.Ordinal));
```

- [ ] **Step 2: Run tests and verify RED**

Expected: all three guards fail against current behavior.

- [ ] **Step 3: Implement deterministic army binding**

Resolve only a live ordinary army with a live non-synthetic captain of the
recorded kingdom. If none exists, use the existing ordinary-army creation
service with a preselected eligible general. Never select a synthetic actor as
captain. Persist `ArmyId`; on captain death or army destruction, rebind before
spawning more actors.

- [ ] **Step 4: Spawn initial quota in bounded batches**

For each `Mobilizing` record request at most
`SyntheticMobilizationRules.SpawnBatchLimit`. Reuse the atomic sequence in
`SyntheticLevyService.TryCreate`, set war/city/kingdom IDs before leaving
`SyntheticLevySpawnScope`, and call
`KingdomWarDirectorService.QueueArmyChanged` after a non-empty batch.

- [ ] **Step 5: Replace replenishment reserve ownership**

```csharp
int reserved = SyntheticMobilizationLedgerService.TryReserveReplacement(
    war.data.id, sourceCity.id, requested);
int created = SyntheticLevyService.CreateBatch(sourceCity, kingdom, army,
    reserved, war.data.id, recruits);
SyntheticMobilizationLedgerService.ReleaseUncreatedReplacement(
    war.data.id, sourceCity.id, reserved - created);
```

Delete the fallback that enlists real reserve Actors. Remove preparation
candidate indexing and all merit-promotion calls. After one-time old-save
migration, `OpenOrReadWarReserve`, `TryReserveWarManpower` and synthetic live
accounting all delegate to the single new record or are removed; no value is
mirrored between two runtime ledgers.

- [ ] **Step 6: Run rules, production build and commit**

Expected: all pass with zero errors.

```powershell
git add Code/core/lineage/SyntheticLevyService.cs Code/core/lineage/SyntheticMobilizationLedgerService.cs Code/core/lineage/ArmyReplenishmentOperationService.cs Code/core/lineage/CityReservePoolService.cs Code/core/lineage/TemporaryLevyService.cs Tests/AncientWarfare3.Rules.Tests/SyntheticLevyRulesTests.cs.txt
git commit -m "feat: mobilize synthetic armies from numeric quotas"
```

### Task 5: Enforce Task Whitelist And Zero Descendants

**Files:**
- Modify: `Code/patch/AW_WartimeMilitaryTaskPatch.cs`
- Modify: `Code/patch/AW_DynasticReproductionPatch.cs`
- Modify: `Code/core/lineage/SyntheticLevyRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/SyntheticLevyRulesTests.cs.txt`

- [ ] **Step 1: Write failing whitelist and reproduction guards**

Test every `SyntheticLevyTask` enum value. Only Military, Food, Healing,
Transport, Retreat, Formation and Sleep may return true. Add source checks for
both parent arguments at `BabyMaker.makeBaby` and for `AiSystemActor.setJob`,
`setTask`, and active-task `update` interception.

- [ ] **Step 2: Run tests and verify RED**

Expected: missing job and direct-parent guards.

- [ ] **Step 3: Add central job and task enforcement**

Patch `AiSystemActor.setJob(string)` using `AccessTools.Method`. Resolve the
requested job/task to a `SyntheticLevyTask`; unknown IDs default to denied.
Denied jobs route to the actor's current RTS military job or a stable military
idle job, with a per-actor latch preventing repeated reassignment in the same
AI update. Apply the same resolver in existing `setTask` and `update` patches.

- [ ] **Step 4: Reject every direct child creation**

Change the existing make-baby prefix to:

```csharp
private static bool MakeBaby_Prefix(Actor pParent1, Actor pParent2,
    ref Actor __result, ref ActorSex pForcedSexType)
{
    if (SyntheticLevyService.IsSynthetic(pParent1) ||
        SyntheticLevyService.IsSynthetic(pParent2))
    {
        __result = null;
        return false;
    }
    // Preserve existing noble-heir sex resolution here.
    return true;
}
```

Also clear stale lover and `pregnant` status during load reconciliation, and
exclude synthetic actors from clan, court, office and personal-history source
boundaries using the existing `SuppressPersonalHistory` predicate.

- [ ] **Step 5: Run rules and production build**

Expected: all whitelist and source guards pass; zero compile errors.

- [ ] **Step 6: Commit**

```powershell
git add Code/patch/AW_WartimeMilitaryTaskPatch.cs Code/patch/AW_DynasticReproductionPatch.cs Code/core/lineage/SyntheticLevyRules.cs Tests/AncientWarfare3.Rules.Tests/SyntheticLevyRulesTests.cs.txt
git commit -m "fix: isolate synthetic levies from civilian life"
```

### Task 6: Guarantee Bounded Post-War Removal

**Files:**
- Modify: `Code/core/lineage/SyntheticMobilizationLedgerService.cs`
- Modify: `Code/core/lineage/SyntheticLevyService.cs`
- Modify: `Code/core/lineage/TemporaryLevyService.cs`
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/SyntheticMobilizationRulesTests.cs.txt`

- [ ] **Step 1: Write failing demobilization tests**

Test a 41-actor queue returns batches `16, 16, 9`, decorated actors resolve to
removal, duplicate death/removal decrements the ledger once, and an ended war
cannot return to `Active` after load.

- [ ] **Step 2: Run tests and verify RED**

Expected: batch helper or lifecycle transition failures.

- [ ] **Step 3: Implement resumable demobilization**

At war end set every matching record to `Demobilizing`, clear unused numeric
reserve, and enqueue stable actor IDs. Each authority work item removes at
most 16 through `SyntheticLevyService.RemoveWithoutPersonalHistory`. Missing
and already-dead actors release accounting exactly once. Complete the record
only when no marked live actor remains.

- [ ] **Step 4: Handle capture, destruction and load**

City capture never changes synthetic provenance. Kingdom destruction and
leaving the war enter demobilization. Load rebuild walks marked synthetic
actors in bounded slices, reattaches valid records, and sends orphan markers
to removal rather than making them civilians.

- [ ] **Step 5: Run tests/build and commit**

```powershell
git add Code/core/lineage/SyntheticMobilizationLedgerService.cs Code/core/lineage/SyntheticLevyService.cs Code/core/lineage/TemporaryLevyService.cs Code/patch/AW_ActorDeathPatch.cs Tests/AncientWarfare3.Rules.Tests/SyntheticMobilizationRulesTests.cs.txt
git commit -m "fix: demobilize every synthetic levy in bounded batches"
```

### Task 7: Repair First-Order RTS Assignment And Full-Roster Cursors

**Files:**
- Modify: `Code/core/lineage/KingdomWarDirectorRules.cs`
- Modify: `Code/core/lineage/KingdomWarDirectorService.cs`
- Modify: `Code/core/lineage/ArmyRtsWarLifecycleService.cs`
- Modify: `Code/core/lineage/ArmyRtsAssignmentReconciliationService.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing priority and cursor tests**

Create pure tests proving attacker/defender alternation, both sides are served
within two pulses, a roster cursor advances `0 -> 128 -> 256 -> complete` for
300 members, and a roster-version change safely restarts the cursor. Add a
handoff regression where a route failure latches target completion and removes
watchdog ownership, the director later retains the same strategic mission,
and online reconciliation must produce the same runnable state as load-time
`RebuildRuntime` without requiring a save reload.

- [ ] **Step 2: Run tests and verify RED**

Expected: missing priority-lane and cursor rules.

- [ ] **Step 3: Add bounded lifecycle discovery**

Before planning, process a persistent war/kingdom/army cursor and call
`ArmyRtsWarLifecycleStateStore.Ensure` for every newly eligible ordinary army.
Roster/captain/join callbacks enqueue that exact army immediately; the periodic
reconciler remains only a repair path.

- [ ] **Step 4: Add alternating first-order queue**

`OnWarStarted` queues attacker then defender lanes. Each logical pulse serves
the opposite side from the previous pulse and publishes the first legal
rally/defend/advance/attack mission using existing target and reachability
rules before full front refinement. A side without a valid led army records a
stable wait reason and sleeps until roster mutation.

- [ ] **Step 5: Make objective handoff transactional**

Add a pure decision rule for the observed stuck sequence:

```csharp
public static bool MustRehydrateRetainedMission(bool handoffLatched,
    bool watchdogRegistered, bool sameStrategicIntent,
    bool replacementPublished)
{
    return handoffLatched && !watchdogRegistered &&
           sameStrategicIntent && !replacementPublished;
}
```

When true, clear `TargetCompletionLatched`, reset route submission/arrival and
job cursors, call `ArmyStallWatchdogService.OnMissionAssigned(army, true)`,
and requeue the controller. If no legal mission is retained, invalidate the
old controller mission before waiting for the director. This matches the
state that currently makes armies move again after a load, but performs it
online at the broken handoff boundary.

- [ ] **Step 6: Replace 128-member truncation with cursors**

Persist member index and roster version in `ArmyRtsJobAssignmentCursor`.
Process at most 128 members per work item, retain the army in the queue until
all members are covered, and restart only when the roster version changes.

- [ ] **Step 7: Run rules and adversarial scenarios**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
dotnet run --project Tests\ArmyRtsAdversarialSimulation\ArmyRtsAdversarialSimulation.csproj -c Release -- rally-recruitment 1337
```

Expected: rules pass; scenario settles without a persistent unassigned army.
The handoff scenario must resume online before its simulated load boundary;
calling rebuild afterward must not change its movement eligibility.

- [ ] **Step 8: Commit**

```powershell
git add Code/core/lineage/KingdomWarDirectorRules.cs Code/core/lineage/KingdomWarDirectorService.cs Code/core/lineage/ArmyRtsWarLifecycleService.cs Code/core/lineage/ArmyRtsAssignmentReconciliationService.cs Code/core/lineage/ArmyRtsControllerService.cs Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "fix: issue first RTS orders to both war sides"
```

### Task 8: Make Large-Step RTS Pulses Exact Once

**Files:**
- Modify: `Code/core/performance/ArmyRtsSchedulingMode.cs`
- Modify: `Code/core/performance/ArmyRtsSchedulingService.cs`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/TestArmyRtsSchedulingSettingsStub.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing exact-once tests**

Test that owner resolution freezes once per session, Native and AW3 entry paths
share one consumed-token set, duplicate token entry is rejected, and tokens
`1..N` are accepted exactly once regardless of entry-path order.

- [ ] **Step 2: Run tests and verify RED**

Expected: current independent `NativeGate` and `Aw3Gate` allow duplicate
logical work or resolve a changed setting dynamically.

- [ ] **Step 3: Implement frozen owner and shared gate**

Replace two gates with one session state:

```csharp
public sealed class ArmyRtsSchedulingGate
{
    private ArmyRtsSchedulerMode _owner;
    private bool _ownerFrozen;
    private long _lastToken = -1L;

    public void StartSession(bool configAw3)
    {
        _owner = ArmyRtsSchedulingRules.ResolveStartupMode(configAw3);
        _ownerFrozen = true;
        _lastToken = -1L;
    }

    public bool TryEnter(ArmyRtsSchedulerOwner caller, long token,
        bool allowed)
    {
        if (!allowed || !_ownerFrozen || token <= _lastToken ||
            !ArmyRtsSchedulingRules.ShouldRunOwner(_owner, caller))
            return false;
        _lastToken = token;
        return true;
    }
}
```

- [ ] **Step 4: Add a large-step RTS stage per internal pass**

Insert `Aw3RtsLogicalPulse` after `Era`. Every pass invokes
`ArmyRtsSchedulingService.ProcessLogicalPass(_logicalTicksAdmitted, paused)`.
Intermediate passes then complete; final pass continues delayed actions and
AW3 authority. Remove RTS execution from the final authority service so that
the shared token is not consumed twice.

- [ ] **Step 5: Run scheduler guards and production build**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Release
```

Expected: scheduler rules pass and production build has zero errors.

- [ ] **Step 6: Commit**

```powershell
git add Code/core/performance/ArmyRtsSchedulingMode.cs Code/core/performance/ArmyRtsSchedulingService.cs Code/core/performance/AWCooperativeSimulationRunner.cs Code/core/performance/AWAuthorityCycleService.cs Tests/AncientWarfare3.Rules.Tests/TestArmyRtsSchedulingSettingsStub.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "fix: advance RTS once per logical simulation pass"
```

### Task 9: Add Adversarial Mobilization And Scheduler Verification

**Files:**
- Modify: `Tests/ArmyRtsAdversarialSimulation/ScenarioFactory.cs`
- Modify: `Tests/ArmyRtsAdversarialSimulation/Program.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/SyntheticMobilizationSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Add failing adversarial scenarios**

Add scenarios for simultaneous wars, 300-member armies, captain death during
mobilization, city capture, save/load during demobilization, and N native vs N
large-step pulses. Assert both sides receive a mission by pulse two, generated
count never exceeds initial quota plus replacement reserve, and final live
synthetic count is zero.

- [ ] **Step 2: Run scenarios and verify RED where integration is incomplete**

```powershell
dotnet run --project Tests\ArmyRtsAdversarialSimulation\ArmyRtsAdversarialSimulation.csproj -c Release -- synthetic-mobilization 1337
dotnet run --project Tests\ArmyRtsAdversarialSimulation\ArmyRtsAdversarialSimulation.csproj -c Release -- large-step-equivalence 1337
```

Expected before final wiring: at least one new assertion fails for missing
integration rather than test setup.

- [ ] **Step 3: Complete only integration exposed by RED tests**

Fix queue wakeups, counter release or session reset at their owning boundary.
Do not add new behavior outside the approved design.

- [ ] **Step 4: Run complete verification**

```powershell
dotnet restore AncientWarfare3.csproj
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
dotnet run --project Tests\ArmyRtsAdversarialSimulation\ArmyRtsAdversarialSimulation.csproj -c Release -- all 1337
dotnet build AncientWarfare3.csproj -c Release
```

Expected: restore succeeds; rules and all adversarial scenarios pass;
production build reports zero errors.

- [ ] **Step 5: Inspect diff and commit**

```powershell
git diff --check
git status --short
git add Tests/ArmyRtsAdversarialSimulation/ScenarioFactory.cs Tests/ArmyRtsAdversarialSimulation/Program.cs Tests/AncientWarfare3.Rules.Tests/SyntheticMobilizationSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "test: cover synthetic mobilization under RTS load"
```

### Task 10: Final Review Without Deployment

**Files:**
- Review only: all files changed by Tasks 1-9

- [ ] **Step 1: Verify requirement guards**

```powershell
rg -n "TryToMakeWarrior_Prefix|SyntheticLevyService\.Promote|TryTakeNextActorId\(pool\.ActorIds" Code
rg -n "SYNTHETIC_LEVY|SyntheticMobilizationLedgerService|Aw3RtsLogicalPulse" Code
```

Expected: the first command has no production matches for removed behavior;
the second shows the intended identity, ledger and scheduler boundaries.

- [ ] **Step 2: Run final clean verification**

Run the complete commands from Task 9 Step 4 and record their output.

- [ ] **Step 3: Review commit and worktree state**

```powershell
git log --oneline --decorate -12
git status --short --branch
```

Expected: implementation commits are present and the isolated worktree is
clean. Do not merge, deploy or push until explicitly requested.

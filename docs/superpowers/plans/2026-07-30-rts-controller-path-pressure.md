# RTS Controller And Path Pressure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Cut RTS controller target-fact work and protect army and transport path capacity without reducing active RTS movement responsiveness.

**Architecture:** A controller-cycle city-threat cache removes duplicate live tile scans and exposes only current-cycle facts to RTS. The global path finder is extended from its existing warrior/non-warrior queues into operational, essential-travel, and ambient classes, retaining current per-actor reuse while recording why a request was reused or replaced. An async RTS planner receives only immutable front snapshots and can pre-rank a future director pass; its output is revision-checked before the main thread consumes it.

**Tech Stack:** C# / .NET 9 rule tests, Unity/WorldBox main-thread APIs, AW3 AWAsyncRuntime, Harmony path interception.

---

## Current Code Boundaries

- Code/core/lineage/CityAttackZoneService.cs owns the live zone/tile warrior scan. It currently creates a capturing callback per tile.
- Code/core/lineage/ArmyRtsObjectiveService.cs and Code/core/lineage/ArmyRtsControllerService.cs consume hostile-military facts.
- Code/core/performance/AWAuthorityCycleService.cs establishes an authority-cycle boundary and resets world-scoped runtime services.
- Code/core/lineage/KingdomWarDirectorService.cs builds incremental front snapshots and owns the current director generation for each kingdom.
- Code/core/pathfinding/AWPathFinder.cs already has one active task and one pending slot per actor, and already reuses same-target requests.
- Code/core/pathfinding/AWPathMovementBridge.cs classifies every intercepted Actor.goTo call only as a Boolean warrior priority today.
- Code/core/pathfinding/ArmyRouteProvider.cs submits RTS captain routes directly to the shared finder.
- Code/core/pathfinding/AWPathDiagnostics.cs and Code/core/policy/RuntimePerformanceDiagnostic.cs emit aggregate path and controller diagnostics only when AW3 performance diagnostics are enabled.
- Code/core/asyncwork/AWAsyncRuntime.cs and Code/core/lineage/AsyncKingdomStrategyService.cs provide the required snapshot -> worker -> main-thread revision-check pattern.

The historical path_reused=0 sample is not proof that reuse code is absent: AWPathFinder.TryReuse and Request already reuse identical actor/target/options. The implementation must therefore record request disposition before adding any destination-tolerance heuristic. No tolerance heuristic is in this plan.

## File Structure

- Create: Code/core/lineage/CityMilitaryThreatFactsRules.cs
  - Pure cache-key, validity, and invalidation rules included in the rules-test project.
- Create: Code/core/lineage/CityMilitaryThreatFacts.cs
  - Main-thread, authority-cycle cache, scan diagnostics, and lifecycle reset.
- Create: Code/core/lineage/ArmyRtsAsyncPlanningRules.cs
  - Pure snapshot/revision acceptance and deterministic front-ranking rules.
- Create: Code/core/lineage/ArmyRtsAsyncPlanningService.cs
  - Captures immutable director snapshots, schedules AW3 async AI work, and stores only validated prefetched ranks.
- Create: Tests/AncientWarfare3.Rules.Tests/CityMilitaryThreatFactsRulesTests.cs.txt
- Create: Tests/AncientWarfare3.Rules.Tests/ArmyRtsAsyncPlanningRulesTests.cs.txt
- Modify: Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
  - Links the new pure production rules and compiles the two new test files.
- Modify: Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
  - Calls both new test suites.
- Modify: Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt
  - Covers three-class priority fairness and request disposition accounting.
- Modify: Code/core/lineage/CityAttackZoneService.cs
  - Routes hostile-military reads through the cache and uses one callback per physical scan.
- Modify: Code/core/lineage/ArmyRtsObjectiveService.cs
  - Continues to call the same service API; no target-state semantics change.
- Modify: Code/core/lineage/ArmyRtsControllerService.cs
  - Uses the cache for completed-target pursuit and diagnostic reads.
- Modify: Code/core/lineage/KingdomWarDirectorService.cs
  - Invalidates relevant facts at war/city state changes and submits/consumes validated async front ranks.
- Modify: Code/core/performance/ArmyRtsSchedulingService.cs
  - Begins and ends the city-fact cache around each admitted native or AW3 RTS scheduling cycle, and resets it on world reset.
- Modify: Code/core/pathfinding/AWPathLifecycleRules.cs
  - Defines work classes, service ordering, fairness limits, and class-specific pending/poll timing.
- Modify: Code/core/pathfinding/AWPathRequest.cs
  - Stores AWPathWorkClass instead of a Boolean priority.
- Modify: Code/core/pathfinding/AWPathFinder.cs
  - Uses three work queues, preserves one pending slot per actor, and reports request disposition.
- Modify: Code/core/pathfinding/AWPathMovementBridge.cs
  - Classifies RTS/transport/school/ambient paths and preserves class through retries.
- Modify: Code/core/pathfinding/ArmyRouteProvider.cs
  - Explicitly submits captain strategic routes as operational work.
- Modify: Code/core/pathfinding/AWPathDiagnostics.cs
  - Tracks path class counts, queue high-water marks, and request disposition.
- Modify: Code/core/policy/RuntimePerformanceDiagnostic.cs
  - Emits aggregate cache, async-prefetch, and path-class counters only under the existing setting.

### Task 1: Lock Down City-Fact Cache Semantics

**Files:**
- Create: Code/core/lineage/CityMilitaryThreatFactsRules.cs
- Create: Tests/AncientWarfare3.Rules.Tests/CityMilitaryThreatFactsRulesTests.cs.txt
- Modify: Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
- Modify: Tests/AncientWarfare3.Rules.Tests/Program.cs.txt

- [ ] **Step 1: Write the failing cache-rule test.**

Add this call to Program.cs.txt:

~~~
CityMilitaryThreatFactsRulesTests.Run();
~~~

Add these compile entries to the test project:

~~~xml
<Compile Include="CityMilitaryThreatFactsRulesTests.cs.txt" />
<Compile Include="..\..\Code\core\lineage\CityMilitaryThreatFactsRules.cs" Link="Production\CityMilitaryThreatFactsRules.cs" />
~~~

Create the test using these assertions:

~~~csharp
var key = new CityMilitaryThreatKey(17L, 9L, 3L);
Assert(CityMilitaryThreatFactsRules.CanCache(
    cycleActive: true, warId: 17L, cityId: 9L, kingdomId: 3L),
    "a valid query inside an authority cycle is cacheable");
Assert(!CityMilitaryThreatFactsRules.CanCache(
    cycleActive: false, warId: 17L, cityId: 9L, kingdomId: 3L),
    "a query outside an authority cycle cannot retain a live fact");
Assert(CityMilitaryThreatFactsRules.KeyMatches(
    key, new CityMilitaryThreatKey(17L, 9L, 3L)),
    "identical war/city/kingdom facts share one entry");
Assert(!CityMilitaryThreatFactsRules.KeyMatches(
    key, new CityMilitaryThreatKey(18L, 9L, 3L)),
    "a changed war never reuses a city fact");
Assert(CityMilitaryThreatFactsRules.ShouldInvalidate(
    cachedCityId: 9L, changedCityId: 9L),
    "city-control changes invalidate that city");
~~~

- [ ] **Step 2: Run the focused suite and verify the expected failure.**

Run:

~~~powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore
~~~

Expected: compilation fails because CityMilitaryThreatKey and CityMilitaryThreatFactsRules do not exist.

- [ ] **Step 3: Write the minimal pure cache rules.**

Create Code/core/lineage/CityMilitaryThreatFactsRules.cs:

~~~csharp
using System;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct CityMilitaryThreatKey :
        IEquatable<CityMilitaryThreatKey>
    {
        internal CityMilitaryThreatKey(long pWarId, long pCityId,
            long pKingdomId)
        {
            WarId = pWarId;
            CityId = pCityId;
            KingdomId = pKingdomId;
        }

        internal long WarId { get; }
        internal long CityId { get; }
        internal long KingdomId { get; }

        internal bool Matches(CityMilitaryThreatKey pOther) =>
            WarId == pOther.WarId && CityId == pOther.CityId &&
            KingdomId == pOther.KingdomId;

        public bool Equals(CityMilitaryThreatKey pOther) =>
            Matches(pOther);

        public override bool Equals(object pObject) =>
            pObject is CityMilitaryThreatKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)WarId;
                hash = hash * 397 ^ (int)CityId;
                return hash * 397 ^ (int)KingdomId;
            }
        }
    }

    internal static class CityMilitaryThreatFactsRules
    {
        internal static bool CanCache(bool pCycleActive, long pWarId,
            long pCityId, long pKingdomId) =>
            pCycleActive && pWarId >= 0L && pCityId >= 0L &&
            pKingdomId >= 0L;

        internal static bool KeyMatches(CityMilitaryThreatKey pLeft,
            CityMilitaryThreatKey pRight) => pLeft.Equals(pRight);

        internal static bool ShouldInvalidate(long pCachedCityId,
            long pChangedCityId) =>
            pCachedCityId >= 0L && pCachedCityId == pChangedCityId;
    }
}
~~~

- [ ] **Step 4: Run the focused suite and verify it passes.**

Run the same command from Step 2.

Expected: Rule tests passed.

- [ ] **Step 5: Commit only the cache-rule test and implementation.**

~~~powershell
git add -- Code/core/lineage/CityMilitaryThreatFactsRules.cs Tests/AncientWarfare3.Rules.Tests/CityMilitaryThreatFactsRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "test: define city military fact cache rules"
~~~

### Task 2: Cache Live City Threat Facts Per Authority Cycle

**Files:**
- Create: Code/core/lineage/CityMilitaryThreatFacts.cs
- Modify: Code/core/lineage/CityAttackZoneService.cs
- Modify: Code/core/lineage/KingdomWarDirectorService.cs
- Modify: Code/core/performance/AWAuthorityCycleService.cs
- Modify: Tests/AncientWarfare3.Rules.Tests/CityMilitaryThreatFactsRulesTests.cs.txt

- [ ] **Step 1: Write the failing cache-entry source test.**

Extend CityMilitaryThreatFactsRulesTests.cs.txt to assert the source contains:

~~~csharp
CityMilitaryThreatFacts.TryGet(
~~~

and does not contain:

~~~csharp
tile.doUnits(actor =>
~~~

Run the rules suite. Expected: the cache-entry assertion fails against the current service.

- [ ] **Step 2: Implement the main-thread cache with no cross-cycle retention.**

Create CityMilitaryThreatFacts.cs with a dictionary keyed by
CityMilitaryThreatKey, a Boolean CycleActive, and counters Requests,
PhysicalScans, Hits, and Invalidations.

Its required methods are:

~~~csharp
internal static void BeginAuthorityCycle()
internal static void EndAuthorityCycle()
internal static bool TryGet(War pWar, City pCity, Kingdom pKingdom,
    out bool pHostile)
internal static void Store(War pWar, City pCity, Kingdom pKingdom,
    bool pHostile)
internal static void RecordPhysicalScan()
internal static void InvalidateCity(City pCity)
internal static void InvalidateWar(War pWar)
internal static long Revision { get; }
internal static CityMilitaryThreatDiagnostics SnapshotDiagnostics()
internal static void Reset()
~~~

BeginAuthorityCycle clears the prior dictionary and activates caching.
EndAuthorityCycle clears the dictionary and deactivates caching. TryGet and
Store use CityMilitaryThreatFactsRules.CanCache; invalid data is never stored.
InvalidateCity removes all keys for that city; InvalidateWar removes all keys
for that war. BeginAuthorityCycle, each invalidation, and Reset increment the
monotonic Revision used to reject an async planner result that observed older
city facts.

- [ ] **Step 3: Replace the tile closure with one scan context per physical scan.**

In CityAttackZoneService.HasHostileMilitaryInside, first call TryGet. On a
miss, instantiate one HostileMilitaryScanContext containing the war,
observing kingdom, Found flag, and a pre-created Func<Actor, bool> visitor.
Reuse that visitor for every tile.doUnits(context.Visit) call. After the scan,
call Store and return context.Found.

The visitor must preserve the current predicate:

~~~csharp
actor?.data != null &&
actor.is_profession_warrior &&
actor.kingdom?.data != null &&
actor.kingdom != observingKingdom &&
!war.onTheSameSide(observingKingdom, actor.kingdom) &&
war.isInWarWith(observingKingdom, actor.kingdom)
~~~

It returns !Found so doUnits exits at the first hostile warrior. If the scan
throws, return false and do not store a fact.

- [ ] **Step 4: Bind invalidation to real state changes and the authority gate.**

Make these exact integrations:

~~~csharp
// ArmyRtsSchedulingService.ProcessCycle, after its gate admits work
CityMilitaryThreatFacts.BeginAuthorityCycle();
try
{
    // existing coalition, director, route, controller, logistics, and watchdog work
}
finally
{
    CityMilitaryThreatFacts.EndAuthorityCycle();
}

// ArmyRtsSchedulingService.Reset
CityMilitaryThreatFacts.Reset();

// KingdomWarDirectorService.OnCityControlChanged
CityMilitaryThreatFacts.InvalidateCity(pCity);

// KingdomWarDirectorService.OnWarStarted / OnWarEnded / OnWarParticipantChanged
CityMilitaryThreatFacts.InvalidateWar(pWar);
~~~

Do not move existing director, controller, or route work outside its current
scheduler. The cache is active only while an admitted authority cycle executes,
so unrelated render/event paths continue to read live facts.

- [ ] **Step 5: Run tests and build.**

~~~powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
~~~

Expected: both complete with zero errors.

- [ ] **Step 6: Commit the live cache integration.**

~~~powershell
git add -- Code/core/lineage/CityMilitaryThreatFacts.cs Code/core/lineage/CityAttackZoneService.cs Code/core/lineage/KingdomWarDirectorService.cs Code/core/performance/ArmyRtsSchedulingService.cs Tests/AncientWarfare3.Rules.Tests/CityMilitaryThreatFactsRulesTests.cs.txt
git commit -m "perf: cache RTS city military facts per authority cycle"
~~~

### Task 3: Add Three-Class Path Scheduling And Root-Cause Telemetry

**Files:**
- Modify: Code/core/pathfinding/AWPathLifecycleRules.cs
- Modify: Code/core/pathfinding/AWPathRequest.cs
- Modify: Code/core/pathfinding/AWPathFinder.cs
- Modify: Code/core/pathfinding/AWPathMovementBridge.cs
- Modify: Code/core/pathfinding/ArmyRouteProvider.cs
- Modify: Code/core/pathfinding/AWPathDiagnostics.cs
- Modify: Code/core/policy/RuntimePerformanceDiagnostic.cs
- Modify: Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt

- [ ] **Step 1: Write failing work-class and scheduling tests.**

Add these assertions to PathfindingPerformanceTests.Run():

~~~csharp
Assert(AWPathWorkClassRules.Classify(
    warrior: true, hasArmy: true, boat: false, transport: false,
    schoolJourney: false) == AWPathWorkClass.Operational,
    "RTS warriors are operational path work");
Assert(AWPathWorkClassRules.Classify(
    warrior: false, hasArmy: false, boat: false, transport: false,
    schoolJourney: true) == AWPathWorkClass.EssentialTravel,
    "school journeys outrank ambient wandering");
Assert(AWPathWorkClassRules.Classify(
    warrior: false, hasArmy: false, boat: false, transport: false,
    schoolJourney: false) == AWPathWorkClass.Ambient,
    "ordinary civilian movement remains ambient");

Assert(AWPathWorkClassRules.Next(
    operationalQueued: 1, essentialQueued: 1, ambientQueued: 1,
    consecutiveOperational: 0, consecutiveNonAmbient: 0) ==
    AWPathWorkClass.Operational,
    "operational work starts first");
Assert(AWPathWorkClassRules.Next(
    operationalQueued: 1, essentialQueued: 1, ambientQueued: 1,
    consecutiveOperational:
        AWPathWorkClassRules.MaximumConsecutiveOperational,
    consecutiveNonAmbient:
        AWPathWorkClassRules.MaximumConsecutiveNonAmbient) ==
    AWPathWorkClass.Ambient,
    "ambient work receives bounded fairness under a permanent war backlog");
~~~

- [ ] **Step 2: Run the focused suite and verify it fails.**

Run the rules suite. Expected: compilation fails because AWPathWorkClass and
AWPathWorkClassRules do not exist.

- [ ] **Step 3: Implement pure work classes and class-preserving requests.**

In AWPathLifecycleRules.cs, add:

~~~csharp
internal enum AWPathWorkClass
{
    Operational = 0,
    EssentialTravel = 1,
    Ambient = 2
}

internal static class AWPathWorkClassRules
{
    internal const int MaximumConsecutiveOperational = 8;
    internal const int MaximumConsecutiveNonAmbient = 16;

    internal static AWPathWorkClass Classify(bool warrior, bool hasArmy,
        bool boat, bool transport, bool schoolJourney)
    {
        if (warrior || hasArmy || boat || transport)
            return AWPathWorkClass.Operational;
        return schoolJourney ? AWPathWorkClass.EssentialTravel :
            AWPathWorkClass.Ambient;
    }

    internal static AWPathWorkClass Next(int operationalQueued,
        int essentialQueued, int ambientQueued,
        int consecutiveOperational, int consecutiveNonAmbient)
    {
        if (ambientQueued > 0 &&
            consecutiveNonAmbient >= MaximumConsecutiveNonAmbient)
            return AWPathWorkClass.Ambient;
        if (essentialQueued > 0 &&
            consecutiveOperational >= MaximumConsecutiveOperational)
            return AWPathWorkClass.EssentialTravel;
        if (operationalQueued > 0) return AWPathWorkClass.Operational;
        if (essentialQueued > 0) return AWPathWorkClass.EssentialTravel;
        return AWPathWorkClass.Ambient;
    }
}
~~~

Replace the request constructor Boolean pHighPriority with
AWPathWorkClass pWorkClass, expose WorkClass, and update retry contexts to
retain that enum. Do not remove the existing exact AWPathRequest.Matches reuse
logic.

- [ ] **Step 4: Replace two finder queues with three queues while preserving latest-wins ownership.**

Keep work slots and their one-pending-task-per-actor behavior. Replace
priorityQueue and queue with operational, essential, and ambient linked lists.
ActorWorkSlot stores WorkClass; EnqueueLocked upgrades a pending slot when its
new class is more urgent and never duplicates its queue node. DequeueLocked
calls AWPathWorkClassRules.Next, updates consecutive service counters, and
returns null only when all three queues are empty.

Expose this read-only diagnostic snapshot:

~~~csharp
internal readonly struct AWPathQueueSnapshot
{
    internal int OperationalQueued { get; }
    internal int EssentialQueued { get; }
    internal int AmbientQueued { get; }
    internal int OperationalActive { get; }
    internal int EssentialActive { get; }
    internal int AmbientActive { get; }
}
~~~

- [ ] **Step 5: Classify submitters explicitly and record disposition.**

In AWPathMovementBridge.SubmitCore, calculate:

~~~csharp
bool schoolJourney = string.Equals(pActor.ai?.task?.id,
    HistoricalSchoolContent.EducationTravelTaskId,
    StringComparison.Ordinal);
AWPathWorkClass workClass = AWPathWorkClassRules.Classify(
    warrior, hasArmy, pActor.asset?.is_boat == true,
    TransportContexts.ContainsKey(actorId), schoolJourney);
~~~

Pass workClass into AWPathRequest. In ArmyRouteProvider.Submit, pass
AWPathWorkClass.Operational directly.

Replace the out bool reused API with AWPathSubmissionDisposition values
Reused, Submitted, ReplacedPending, ReplacedRunning, and Rejected. Record the
old request class before cancellation. This identifies whether the historical
zero reuse came from changing targets, pending replacement, or worker
replacement.

- [ ] **Step 6: Extend aggregate diagnostics and verify the test suite.**

AWPathDiagnostics records generated/reused/replaced/cancelled/completed/failed
totals by work class and queue high-water marks. RuntimePerformanceDiagnostic
adds these fields only to its existing performance-diagnostic output:

~~~text
path_operational_generated
path_essential_generated
path_ambient_generated
path_operational_reused
path_essential_reused
path_ambient_reused
path_replaced_pending
path_replaced_running
path_queue_operational
path_queue_essential
path_queue_ambient
~~~

Run:

~~~powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
~~~

Expected: both complete with zero errors.

- [ ] **Step 7: Commit the path scheduler.**

~~~powershell
git add -- Code/core/pathfinding/AWPathLifecycleRules.cs Code/core/pathfinding/AWPathRequest.cs Code/core/pathfinding/AWPathFinder.cs Code/core/pathfinding/AWPathMovementBridge.cs Code/core/pathfinding/ArmyRouteProvider.cs Code/core/pathfinding/AWPathDiagnostics.cs Code/core/policy/RuntimePerformanceDiagnostic.cs Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt
git commit -m "perf: prioritize operational path requests"
~~~

### Task 4: Precompute RTS Front Rankings Asynchronously

**Files:**
- Create: Code/core/lineage/ArmyRtsAsyncPlanningRules.cs
- Create: Code/core/lineage/ArmyRtsAsyncPlanningService.cs
- Create: Tests/AncientWarfare3.Rules.Tests/ArmyRtsAsyncPlanningRulesTests.cs.txt
- Modify: Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
- Modify: Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
- Modify: Code/core/lineage/KingdomWarDirectorService.cs

- [ ] **Step 1: Write failing stale-result and deterministic-ranking tests.**

Add project links for the new test and pure rules file, call
ArmyRtsAsyncPlanningRulesTests.Run(), and write:

~~~csharp
var stamp = new ArmyRtsAsyncPlanStamp(
    worldGeneration: 4L, kingdomId: 10L, directorGeneration: 7,
    warId: 21L, cityFactsRevision: 3L);
Assert(ArmyRtsAsyncPlanningRules.Accept(
    stamp, currentWorldGeneration: 4L, currentKingdomId: 10L,
    currentDirectorGeneration: 7, currentWarId: 21L,
    currentCityFactsRevision: 3L),
    "a matching async front plan may be consumed");
Assert(!ArmyRtsAsyncPlanningRules.Accept(
    stamp, currentWorldGeneration: 4L, currentKingdomId: 10L,
    currentDirectorGeneration: 8, currentWarId: 21L,
    currentCityFactsRevision: 3L),
    "a changed director generation rejects a stale plan");

var ranked = ArmyRtsAsyncPlanningRules.Rank(new[]
{
    new ArmyRtsAsyncFrontCandidate(3L, score: 20),
    new ArmyRtsAsyncFrontCandidate(2L, score: 20),
    new ArmyRtsAsyncFrontCandidate(1L, score: 25)
});
Assert(ranked[0].CityId == 1L && ranked[1].CityId == 2L,
    "async front ranking is score-descending with city-ID tie breaking");
~~~

- [ ] **Step 2: Run the rules suite and verify it fails.**

Expected: compilation fails because the async plan stamp, candidate, and rules
types are missing.

- [ ] **Step 3: Implement immutable pure snapshot acceptance rules.**

Create ArmyRtsAsyncPlanningRules.cs with immutable value types:

~~~csharp
internal readonly struct ArmyRtsAsyncPlanStamp
{
    internal long WorldGeneration { get; }
    internal long KingdomId { get; }
    internal int DirectorGeneration { get; }
    internal long WarId { get; }
    internal long CityFactsRevision { get; }
}

internal readonly struct ArmyRtsAsyncFrontCandidate
{
    internal long CityId { get; }
    internal int Score { get; }
}
~~~

Accept requires exact equality for every stamp field. Rank copies the input,
sorts score descending then city ID ascending, and reads no Unity or WorldBox
object.

- [ ] **Step 4: Capture and schedule only pure data.**

Create ArmyRtsAsyncPlanningService using the existing
AWAsyncRuntime.TrySchedule pattern. At director publish time, capture for
each selected war only IDs and primitive fields already present in
FrontTargetFacts, ArmyStrategicFacts, and the current city-threat cache: city
ID, enemy force when already known, corridor/reachability flags, distance,
target flags, war ID, and director generation.

The worker calls only ArmyRtsAsyncPlanningRules.Rank. The completion callback
stores ranked city IDs in a prefetched-rank cache keyed by kingdom/war/director
generation. It must not create an ArmyRtsMission, change a target, or read
World, City, War, Actor, or Unity APIs.

- [ ] **Step 5: Validate before use and preserve synchronous fallback.**

In KingdomWarDirectorService.BuildShadowMissions, read a prefetched rank only
when ArmyRtsAsyncPlanningRules.Accept matches live world generation, kingdom
ID, GenerationByKingdom, war ID, and city-facts revision. A mismatch removes
the cached result and executes the current synchronous target-selection path
unchanged.

Call ArmyRtsAsyncPlanningService.InvalidateKingdom from Schedule, InvalidateWar
from all three war lifecycle callbacks, InvalidateCity from
OnCityControlChanged, and ClearRuntime from KingdomWarDirectorService.ClearRuntime.

- [ ] **Step 6: Run tests and build.**

~~~powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
~~~

Expected: both complete with zero errors.

- [ ] **Step 7: Commit the async prefetch path.**

~~~powershell
git add -- Code/core/lineage/ArmyRtsAsyncPlanningRules.cs Code/core/lineage/ArmyRtsAsyncPlanningService.cs Code/core/lineage/KingdomWarDirectorService.cs Tests/AncientWarfare3.Rules.Tests/ArmyRtsAsyncPlanningRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "perf: precompute RTS front rankings asynchronously"
~~~

### Task 5: Publish Comparable Diagnostics And Validate Live Behavior

**Files:**
- Modify: Code/core/policy/RuntimePerformanceDiagnostic.cs
- Modify: Code/core/lineage/CityMilitaryThreatFacts.cs
- Modify: Code/core/lineage/ArmyRtsAsyncPlanningService.cs

- [ ] **Step 1: Add cache and async aggregate snapshots.**

Expose immutable diagnostic snapshots containing:

~~~text
city_threat_requests
city_threat_physical_scans
city_threat_hits
city_threat_invalidations
rts_async_snapshots
rts_async_scheduled
rts_async_completed
rts_async_applied
rts_async_rejected_stale
~~~

All counters are zero-allocation reads and do not log unless
AWPerformanceSettings.EnablePerformanceDiagnostics is enabled.

- [ ] **Step 2: Add fields to the existing one-line sampled diagnostic.**

Append the aggregate keys from Step 1 and the Task 3 path keys to the existing
RuntimePerformanceDiagnostic.EndFrame message. Do not add per-tile, per-actor,
per-request, or per-frame log messages.

- [ ] **Step 3: Re-run automated validation.**

~~~powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
~~~

Expected: Rule tests passed and build output reports zero errors.

- [ ] **Step 4: Commit diagnostics only.**

~~~powershell
git add -- Code/core/policy/RuntimePerformanceDiagnostic.cs Code/core/lineage/CityMilitaryThreatFacts.cs Code/core/lineage/ArmyRtsAsyncPlanningService.cs
git commit -m "perf: report RTS path and threat-cache diagnostics"
~~~

### Task 6: Deploy And Measure The Same War Scenario

**Files:**
- Modify: deployed AW3 DLL only after the build artifact is verified.

- [ ] **Step 1: Verify the final feature changes.**

~~~powershell
git diff --check
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
~~~

Expected: no whitespace errors, Rule tests passed, and zero build errors.

- [ ] **Step 2: Deploy the verified release artifact without starting the game.**

Copy only the successful release DLL and its required mod files to the existing
WorldBox AW3 mod folder. Do not overwrite unrelated mods or start a client.

- [ ] **Step 3: Collect a comparable live diagnostic sample.**

Enable only AW3 Performance Diagnostics. Run the same saved multi-city war at
the same speed and compare:

~~~text
army_rts_controller_stages target_facts
city_threat_physical_scans / city_threat_hits
path class generated/reused/replaced counters
path class queue depths
actor_path_active / actor_path_queue
rts async counters and async_faulted
~~~

Accept the deployment only if armies still rally, march, transport, assault,
retreat, and recover normally; the new cache has hits; no worker/Harmony/stale
world exception appears; and the target-fact stage is lower than the archived
baseline under the comparable workload.

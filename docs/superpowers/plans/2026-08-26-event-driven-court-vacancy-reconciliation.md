# Event-Driven Court Vacancy Reconciliation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace recurring central and local officer vacancy scans with one kingdom-scoped, event-driven vacancy registry and reconciliation cycle.

**Architecture:** A runtime registry stores scoped vacancy keys and missing-seat counts. Vacancy-producing events register exact offices, candidate-pool events wake only the affected kingdom, and one coordinator fills central offices, local chiefs, then lower local offices while reusing one candidate session. Save restoration performs one initialization scan per restore generation; no-candidate outcomes remain dormant.

**Tech Stack:** C#, Harmony, Unity/WorldBox runtime APIs, SQLite-backed court persistence, the repository's `.cs.txt` rules harness, PowerShell source guards, and `dotnet`.

**Execution precondition:** Implement in a dedicated git worktree created from
the current `master`. The primary workspace already contains unrelated user
changes; do not stage, amend, or copy those changes into this feature.

---

## File Map

Create these focused units:

- `Code/core/court/CourtVacancyRules.cs`: pure identity, ordering, seat-count, cascade-bound, and retry rules.
- `Code/core/court/CourtVacancyRegistry.cs`: runtime-only kingdom vacancy index.
- `Code/core/court/CourtCandidateSession.cs`: one candidate snapshot and reserved actor IDs per reconciliation cycle.
- `Code/core/court/CourtVacancyReconciliationService.cs`: event entry points, phased fill, cascading, coalescing, and one technical retry.
- `Code/core/court/CourtVacancyRestoreService.cs`: one-time old-save vacancy discovery.
- `Code/patch/AW_CourtVacancyEventPatch.cs`: native adulthood and ownership event adapters.
- `Tests/AncientWarfare3.Rules.Tests/CourtVacancyRulesTests.cs.txt`: pure rules.
- `Tests/AncientWarfare3.Rules.Tests/CourtVacancySourceGuardTests.cs.txt`: wiring and removed-scan guards.

Modify existing owners rather than duplicating their business rules:

- `CourtService.cs`: narrow central fill/discovery adapters, committed release events, no annual vacancy fill.
- `LocalCourtAppointmentService.cs`: one-seat local fill and vacancy discovery using existing qualification/scoring.
- `CityBureauAnnualWorkService.cs`: annual bureau state and term handling only.
- `OfficerCandidateCatalog.cs`: event-invalidated kingdom snapshot.
- `CivilServiceExamService.cs`: candidate-pool event after committed qualification changes.
- `OfficialCareerStateService.cs`: one coalesced candidate-pool event when a
  committed rank or local-grade batch actually changes eligibility.
- `CustomCourtRuntime.cs`, `ManualLocalChiefAppointmentService.cs`: template/manual appointment events.
- `AW_CityLeaderPatch.cs`, `AW_PromotionPatch.cs`, `AW_HistoricalSchoolPatch.cs`: compatibility and actor events.
- `AW3RuntimeRestorePipeline.cs`, `AWAuthorityCycleService.cs`: restore and reset lifecycle.
- Rules-test project and program files: targeted `--court-vacancy-events` slice.

### Task 1: Add Pure Vacancy Rules

**Files:**
- Create: `Code/core/court/CourtVacancyRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/CourtVacancyRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing tests**

Test central, chief, lower-local, and county identity; repeated-seat counts; cascade bound; and retry behavior:

```csharp
var central = new CourtVacancyKey(7, -1, -1,
    CourtOfficeLayer.Central, "minister");
var chief = new CourtVacancyKey(7, 11, -1,
    CourtOfficeLayer.City, "governor", true);
var local = new CourtVacancyKey(7, 11, -1,
    CourtOfficeLayer.City, "clerk");
var countyA = new CourtVacancyKey(7, 11, 21,
    CourtOfficeLayer.County, CourtOfficeId.CountyMagistrate);
var countyB = new CourtVacancyKey(7, 11, 22,
    CourtOfficeLayer.County, CourtOfficeId.CountyMagistrate);

Equal(CourtVacancyPriority.Central,
    CourtVacancyRules.Priority(central));
Equal(CourtVacancyPriority.LocalChief,
    CourtVacancyRules.Priority(chief));
Equal(CourtVacancyPriority.LocalOffice,
    CourtVacancyRules.Priority(local));
True(!countyA.Equals(countyB));
Equal(2, CourtVacancyRules.MissingSeats(3, 1));
Equal(0, CourtVacancyRules.MissingSeats(1, 3));
Equal(9, CourtVacancyRules.CascadeLimit(9));
True(CourtVacancyRules.ShouldRetry(
    CourtVacancyOutcome.TechnicalFailure, 0));
True(!CourtVacancyRules.ShouldRetry(
    CourtVacancyOutcome.NoCandidate, 0));
True(!CourtVacancyRules.ShouldRetry(
    CourtVacancyOutcome.TechnicalFailure, 1));
```

Add the production file and test to the project, call `Run()` in the normal suite, and add a targeted `--court-vacancy-events` branch.

- [ ] **Step 2: Run the targeted test and require failure**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --court-vacancy-events
```

Expected: compile failure because the vacancy types do not exist.

- [ ] **Step 3: Implement the minimal rules**

```csharp
internal enum CourtVacancyPriority
{
    Central = 0,
    LocalChief = 1,
    LocalOffice = 2
}

internal enum CourtVacancyOutcome
{
    Filled,
    NoCandidate,
    TechnicalFailure,
    Invalid
}

internal static class CourtVacancyRules
{
    internal static CourtVacancyPriority Priority(CourtVacancyKey key) =>
        key.Layer == CourtOfficeLayer.Central ||
        key.Layer == CourtOfficeLayer.Military
            ? CourtVacancyPriority.Central
            : key.IsLocalChief
                ? CourtVacancyPriority.LocalChief
                : CourtVacancyPriority.LocalOffice;

    internal static int MissingSeats(int desired, int occupied) =>
        Math.Max(0, desired - occupied);

    internal static int CascadeLimit(int validOfficeCount) =>
        Math.Max(0, validOfficeCount);

    internal static bool ShouldRetry(CourtVacancyOutcome outcome,
        int attempt) =>
        outcome == CourtVacancyOutcome.TechnicalFailure && attempt == 0;
}
```

`CourtVacancyKey` is immutable. Equality and hashing use kingdom, city, county, layer, and office; `IsLocalChief` is ordering metadata and is not part of durable seat identity.

- [ ] **Step 4: Re-run the targeted test**

Expected: `Court vacancy event tests passed.`

- [ ] **Step 5: Commit**

```powershell
git add Code/core/court/CourtVacancyRules.cs Tests/AncientWarfare3.Rules.Tests/CourtVacancyRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "test: define court vacancy event rules"
```

### Task 2: Add The Runtime Vacancy Registry

**Files:**
- Create: `Code/core/court/CourtVacancyRegistry.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CourtVacancyRulesTests.cs.txt`

- [ ] **Step 1: Add failing registry tests**

```csharp
var state = new CourtVacancyRegistryState();
state.Upsert(local, 2);
state.Upsert(local, 2);
Equal(1, state.ForKingdom(7).Count);
Equal(2, state.ForKingdom(7)[0].MissingSeats);
state.Upsert(local, 0);
Equal(0, state.ForKingdom(7).Count);
state.Upsert(countyA, 1);
state.Upsert(countyB, 1);
Equal(2, state.ForKingdom(7).Count);
state.RemoveCity(7, 11);
Equal(0, state.ForKingdom(7).Count);
```

- [ ] **Step 2: Run the targeted test and require failure**

Expected: missing `CourtVacancyRegistryState`.

- [ ] **Step 3: Implement the registry**

Use `Dictionary<long, Dictionary<CourtVacancyKey, CourtVacancyEntry>>`.
`Upsert(key, missingSeats)` removes zero counts and replaces positive counts.
`ForKingdom` returns a copied array ordered by priority, city, county, layer,
and office. Expose `Register`, `Snapshot`, `Contains`, `Remove`,
`RemoveCity`, `RemoveKingdom`, and `ClearRuntime`.

The registry must never enqueue work; it is an index only.

- [ ] **Step 4: Re-run the targeted test**

Expected: duplicate events coalesce while repeated template seats remain represented by `MissingSeats`.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/court/CourtVacancyRegistry.cs Tests/AncientWarfare3.Rules.Tests/CourtVacancyRulesTests.cs.txt
git commit -m "feat: add runtime court vacancy registry"
```

### Task 3: Build One Candidate Session And Fill Adapters

**Files:**
- Create: `Code/core/court/CourtCandidateSession.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/CourtVacancySourceGuardTests.cs.txt`
- Modify: `Code/core/court/OfficerCandidateCatalog.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/court/LocalCourtAppointmentService.cs`
- Modify: rules-test project and program files

- [ ] **Step 1: Add failing source guards**

Require `CourtCandidateSession` to use `OfficerCandidateCatalog.GetOrBuild`
and `ReservedActorIds`. Require `CourtService.TryFillRegisteredCentralVacancy`,
`LocalCourtAppointmentService.TryFillRegisteredLocalVacancy`, and
`DiscoverVacancies`. Reject `CandidateScanState` from the event fill path.

- [ ] **Step 2: Run the targeted test and require failure**

Expected: session and adapter guards fail.

- [ ] **Step 3: Implement the candidate session**

```csharp
internal sealed class CourtCandidateSession
{
    internal readonly IReadOnlyList<Actor> Actors;
    internal readonly HashSet<long> ReservedActorIds;

    internal CourtCandidateSession(Kingdom kingdom)
    {
        Actors = OfficerCandidateCatalog.GetOrBuild(kingdom,
            Date.getCurrentYear()).ToArray();
        ReservedActorIds =
            CourtService.BuildActiveOfficerActorSetForKingdom(kingdom);
    }

    internal bool IsAvailable(Actor actor, CourtVacancyKey vacancy) =>
        actor?.data != null &&
        (!ReservedActorIds.Contains(actor.data.id) ||
         CourtService.IsExplicitConcurrentOffice(actor, vacancy));

    internal void Reserve(Actor actor, CourtVacancyKey vacancy)
    {
        if (actor?.data != null &&
            !CourtService.IsExplicitConcurrentOffice(actor, vacancy))
            ReservedActorIds.Add(actor.data.id);
    }
}
```

Return a copied snapshot so one cycle cannot be changed by catalog invalidation.

- [ ] **Step 4: Add narrow central and local adapters**

The central adapter calls the existing `FillCentralOffice`, guest-office,
Western-election, qualification, and `SetOfficer` logic. It must not copy
scoring.

The local adapter reuses `DesiredSeats`, `TryLoadActive`,
`TryRepairCityLeader`, `SelectCandidate`, and
`TryAssignLocalOfficer`. `DiscoverVacancies` calculates missing counts for
repeated city office IDs and one county-scoped vacancy for each active county
without a magistrate.

Remove rotating cursor state from event fills. The bounded civil-service
waiting-pool query may remain, but merge it once into the session.

- [ ] **Step 5: Run focused tests and build**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --court-vacancy-events
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: pass, with existing qualification and appointment persistence still owned by existing services.

- [ ] **Step 6: Commit**

```powershell
git add Code/core/court/CourtCandidateSession.cs Code/core/court/OfficerCandidateCatalog.cs Code/core/court/CourtService.cs Code/core/court/LocalCourtAppointmentService.cs Tests/AncientWarfare3.Rules.Tests/CourtVacancySourceGuardTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "refactor: expose court vacancy fill adapters"
```

### Task 4: Implement The Reconciliation Coordinator

**Files:**
- Create: `Code/core/court/CourtVacancyReconciliationService.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: vacancy rule and source-guard tests

- [ ] **Step 1: Add failing priority, cascade, and retry tests**

Add a pure `CourtVacancyCycleRules.Next` test proving central offices are
selected before all chiefs, all chiefs before lower local offices, processed
no-candidate keys are not selected twice without a state change, a key with
`MissingSeats = 2` can consume two successful steps, and the cycle stops at
the valid-office count. Implement `CourtVacancyCycleRules` in
`CourtVacancyRules.cs` rather than creating another file.

- [ ] **Step 2: Run targeted tests and require failure**

Expected: coordinator and cycle rules are absent.

- [ ] **Step 3: Implement event entry points**

```csharp
internal static void RegisterVacancy(CourtVacancyKey key,
    int missingSeats = 1);
internal static void RegisterCityVacancies(Kingdom kingdom, City city);
internal static void RefreshKingdomDefinitions(Kingdom kingdom);
internal static void CandidatePoolChanged(Kingdom kingdom);
internal static void ActorLeftKingdom(Kingdom previous);
internal static void CityChangedKingdom(City city, Kingdom previous,
    Kingdom current);
internal static void KingdomDestroyed(long kingdomId);
internal static void Request(Kingdom kingdom, int attempt = 0);
internal static void ClearRuntime();
```

`Request` uses one coalesced key, `court-vacancy:<kingdomId>`, captures only
the stable kingdom ID, and resolves the live kingdom in the callback.
The callback catches all exceptions and converts them to
`TechnicalFailure`; it must not let `DeferredRuntimeWorkService` apply its own
generic two-attempt exception retry.

- [ ] **Step 4: Implement deterministic phased filling**

Create one `CourtCandidateSession`. Process a snapshot in fixed priority
order. Each successful seat fill consumes one cascade step, then refreshes the
exact missing-seat count; therefore repeated office IDs can fill more than one
seat in the same cycle. A no-candidate key enters a cycle-local blocked set and
is not retried until a later candidate-pool event. Register any prior office
released by a committed promotion and continue until settled or the
valid-office bound is reached.

Route central/military keys to `CourtService`; city/county keys to
`LocalCourtAppointmentService`. Reserve a successful actor unless the
existing explicit concurrency rule permits the combination.

Only `TechnicalFailure` on attempt zero creates one retry ticket with
`NotBeforeFrame = Time.frameCount + 1`. Add an O(1) `DrainDueRetryTickets`
call beside the existing deferred-work drain in `AWAuthorityCycleService`.
This call enqueues due tickets once and prevents backlog catch-up from
consuming the retry in the failure frame. A second technical failure retains
the vacancy and calls `ModClass.LogError`. `NoCandidate` never schedules
delayed or annual work.

- [ ] **Step 5: Run tests and build**

Expected: deterministic ordering, bounded cascade, and one-retry semantics pass.

- [ ] **Step 6: Commit**

```powershell
git add Code/core/court/CourtVacancyReconciliationService.cs Code/core/performance/AWAuthorityCycleService.cs Tests/AncientWarfare3.Rules.Tests/CourtVacancyRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/CourtVacancySourceGuardTests.cs.txt
git commit -m "feat: reconcile court vacancies from events"
```

### Task 5: Publish Exact Vacancy Events After Durable Commits

**Files:**
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/court/LocalCourtAppointmentService.cs`
- Modify: `Code/core/court/ManualLocalChiefAppointmentService.cs`
- Modify: `Code/core/court/CustomCourtRuntime.cs`
- Modify: `Code/patch/AW_PromotionPatch.cs`
- Modify: source guards

- [ ] **Step 1: Add failing commit-order source guards**

Require vacancy publication after committed career close or appointment
projection. Reject publication before `IsCommitted` is known. Require one
`RefreshKingdomDefinitions` call after template migration rather than one
immediate reconcile request per city.

- [ ] **Step 2: Run targeted tests and require failure**

Expected: committed event calls are absent.

- [ ] **Step 3: Publish released office identity centrally**

Capture `OfficialCareerPrior` before clearing. After a committed close or
successful reassignment cleanup, convert the prior into a vacancy key and
register it. Use this path from dismissal, death cleanup, guest closure, term
expiry, and reassignment.

Suppress events for `kingdom_fell`, destroyed cities, removed template
offices, and projection-only concurrent-role changes that did not free a seat.

- [ ] **Step 4: Replace local repair requests**

Replace `CityBureauAnnualWorkService.RequestImmediateReconcile` and
`CityLeaderVacancyRepairService.Request` in dismissal, promotion,
manual-chief, and template flows with exact vacancy refresh calls. Preserve
`GovernorRotationRuntimeScope`; publish the rotation's released/occupied
keys only after its atomic persistence succeeds.

- [ ] **Step 5: Run targeted tests and build**

Expected: all vacancy events occur after durable success, and no retired repair request remains in these files.

- [ ] **Step 6: Commit**

```powershell
git add Code/core/court/CourtService.cs Code/core/court/LocalCourtAppointmentService.cs Code/core/court/ManualLocalChiefAppointmentService.cs Code/core/court/CustomCourtRuntime.cs Code/patch/AW_PromotionPatch.cs Tests/AncientWarfare3.Rules.Tests/CourtVacancySourceGuardTests.cs.txt
git commit -m "refactor: publish committed court vacancies"
```

### Task 6: Publish Candidate-Pool Change Events

**Files:**
- Create: `Code/patch/AW_CourtVacancyEventPatch.cs`
- Modify: `Code/patch/AW_HistoricalSchoolPatch.cs`
- Modify: `Code/core/court/CivilServiceExamService.cs`
- Modify: `Code/core/court/CivilServiceQualificationService.cs`
- Modify: `Code/core/court/OfficialCareerStateService.cs`
- Modify: source guards

- [ ] **Step 1: Add failing native-event source guards**

Require a postfix on `Actor.eventBecomeAdult`, forbid a new age scan, require
old/new kingdom notifications on transfer, and require committed qualification
changes to notify the coordinator.

- [ ] **Step 2: Run targeted tests and require failure**

Expected: adulthood and qualification guards fail.

- [ ] **Step 3: Add the native adulthood hook**

```csharp
[HarmonyPostfix]
[HarmonyPatch(typeof(Actor), "eventBecomeAdult")]
private static void EventBecomeAdult_Postfix(Actor __instance)
{
    Kingdom kingdom = __instance?.kingdom;
    if (kingdom?.data == null || kingdom.isRekt()) return;
    OfficerCandidateCatalog.Invalidate(kingdom);
    CourtVacancyReconciliationService.CandidatePoolChanged(kingdom);
}
```

Do not alter `ActorAgeWorkService` or add all civilians to an age-state map.

- [ ] **Step 4: Extend actor kingdom transfer**

After existing catalog invalidation, notify only the previous and current
kingdoms when identity changed. Skip per-actor reconciliation during loading;
the restore pass owns load initialization.

- [ ] **Step 5: Replace direct post-exam scans**

After a successful persistence commit that grants/upgrades qualification,
invalidate the host catalog and call `CandidatePoolChanged`. Remove
`CourtService.FillVacanciesAfterCivilServiceExam` and
`AW_CityLeaderPatch.FillVacanciesAfterCivilServiceExam` calls. Preserve guest
acting/formal rules inside the registered central fill adapter.

- [ ] **Step 6: Publish committed rank and grade changes once per kingdom**

In `OfficialCareerStateService`, track whether the committed annual mutation
batch changed any `RANK` or `LOCAL_GRADE` value. After the batch commits and
runtime projections are updated, invalidate the kingdom catalog and call
`CandidatePoolChanged` once. Manual rank changes do the same after their
transaction commits. Do not notify for unchanged evaluations or merit-only
updates.

- [ ] **Step 7: Run tests and build**

Expected: event hooks pass and no world/age scan is introduced.

- [ ] **Step 8: Commit**

```powershell
git add Code/patch/AW_CourtVacancyEventPatch.cs Code/patch/AW_HistoricalSchoolPatch.cs Code/core/court/CivilServiceExamService.cs Code/core/court/CivilServiceQualificationService.cs Code/core/court/OfficialCareerStateService.cs Tests/AncientWarfare3.Rules.Tests/CourtVacancySourceGuardTests.cs.txt
git commit -m "feat: wake court vacancies on candidate changes"
```

### Task 7: Remove Periodic Vacancy Search And Retry State

**Files:**
- Delete: `Code/core/court/CityLeaderVacancyRepairService.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/court/CityBureauAnnualWorkService.cs`
- Modify: `Code/patch/AW_CityLeaderPatch.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CourtLocalLayoutAndLeaderRepairSourceGuardTests.cs.txt`
- Modify: vacancy source guards

- [ ] **Step 1: Change source guards to require removed scans**

```csharp
False(courtService.Contains(
    "bool hasCentralVacancy = HasCentralVacancy"));
False(cityBureau.Contains("PendingVacancyRetries"));
False(cityBureau.Contains(
    "LocalCourtAppointmentService.ReconcileCity"));
Contains(cityLeader,
    "CourtVacancyReconciliationService.RegisterCityVacancies");
False(promotion.Contains(
    "CityLeaderVacancyRepairService.Request"));
False(authority.Contains(
    "CityLeaderVacancyRepairService.ClearRuntime"));
```

- [ ] **Step 2: Run targeted tests and require failure**

Expected: current annual/retry paths violate every new guard.

- [ ] **Step 3: Remove annual central filling**

The refresh gate depends only on the configured interval. Keep validation,
career/faction/aristocratic/snapshot work. Remove annual
`HasCentralVacancy`, `EnsureMinimumCourt`, and annual Western vacancy
queueing. Any committed close from annual validation publishes a vacancy event.

- [ ] **Step 4: Split bureau maintenance from filling**

`ProcessCity` validates and closes stale/expired rows, computes filled counts,
updates bureau snapshots, and registers discovered vacancies. It must not call
`ReconcileCity` or `ReconcileCounties` to find candidates.

Keep bounded city slices and snapshot-write retries. Delete
`PendingVacancyRetries`, `VacancyKey`, and `UpdateVacancyRetry`.

- [ ] **Step 5: Make native leader checks O(1)**

After removing an invalid native leader, resolve/register the shared chief
vacancy. If already registered, return without SQLite or candidate selection.
Suppress native selection for official AW3 courts and preserve capture guards.

- [ ] **Step 6: Delete the old repair service and reset**

Delete `CityLeaderVacancyRepairService.cs` and its authority reset call.

- [ ] **Step 7: Run tests and build**

Expected: no annual vacancy search, no self-rescheduling leader repair, successful build.

- [ ] **Step 8: Commit**

```powershell
git add -A Code/core/court/CityLeaderVacancyRepairService.cs Code/core/court/CourtService.cs Code/core/court/CityBureauAnnualWorkService.cs Code/patch/AW_CityLeaderPatch.cs Code/core/performance/AWAuthorityCycleService.cs Tests/AncientWarfare3.Rules.Tests/CourtLocalLayoutAndLeaderRepairSourceGuardTests.cs.txt Tests/AncientWarfare3.Rules.Tests/CourtVacancySourceGuardTests.cs.txt
git commit -m "perf: remove periodic court vacancy scans"
```

### Task 8: Add One-Time Restore Discovery And Ownership Cleanup

**Files:**
- Create: `Code/core/court/CourtVacancyRestoreService.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Code/patch/AW_CourtVacancyEventPatch.cs`
- Modify: `Code/patch/AW_ChroniclePatch.cs`
- Modify: vacancy tests

- [ ] **Step 1: Add failing restore-generation tests**

```csharp
var gate = new CourtVacancyRestoreGenerationGate();
int first = gate.BeginGeneration();
True(gate.TryComplete(first));
True(!gate.TryComplete(first));
int second = gate.BeginGeneration();
True(second != first);
True(gate.TryComplete(second));
```

Source guards require the restore stage after official-career and qualification
projection in loaded and generated-world pipelines.

- [ ] **Step 2: Run targeted tests and require failure**

Expected: generation gate and restore service are absent.

- [ ] **Step 3: Implement one pass per generation**

`BeginGeneration` clears the registry and increments a token.
`RebuildRuntime(token)` returns when that token is already complete.
Otherwise enumerate living kingdoms once, discover central, military, city,
and county vacancies through existing adapters, mark the token complete, and
request one reconciliation for each kingdom that has registered vacancies.

Do not add another `MapBox.on_world_loaded` subscriber.

- [ ] **Step 4: Wire transfer and destruction cleanup**

After a committed non-load city transfer, remove old-owner city keys and
discover new-owner vacancies. Kingdom destruction removes all keys. City
destruction removes its scoped keys. During `pFromLoad`, do nothing because
the restore pass owns initialization.

- [ ] **Step 5: Wire runtime reset and restore stages**

Use `CourtVacancyReconciliationService.ClearRuntime()` in authority reset.
Begin a restore generation in the runtime-cache-reset stage and run vacancy
restore after career and qualification projections in both restore pipelines.

- [ ] **Step 6: Run tests and build**

Expected: exactly-once restore tests pass and project builds.

- [ ] **Step 7: Commit**

```powershell
git add Code/core/court/CourtVacancyRestoreService.cs Code/core/multiplayer/AW3RuntimeRestorePipeline.cs Code/core/performance/AWAuthorityCycleService.cs Code/patch/AW_CourtVacancyEventPatch.cs Code/patch/AW_ChroniclePatch.cs Tests/AncientWarfare3.Rules.Tests/CourtVacancyRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/CourtVacancySourceGuardTests.cs.txt
git commit -m "feat: restore court vacancies once per world load"
```

### Task 9: Verify Compatibility And Complete The Change

**Files:**
- Modify only if a test exposes a scoped regression in files from Tasks 1-8.

- [ ] **Step 1: Run focused court slices**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --court-vacancy-events
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --court-immediate-vacancies
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --court-appointment-failure-backoff
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --city-bureau-retry
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --local-low-office-vacancy
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --custom-local-government
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --regional-government-nine-rank
```

Expected: all pass. Update obsolete guards only when they assert the intentionally retired scan path.

- [ ] **Step 2: Run the full rules suite**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected final line: `Rule tests passed.`

- [ ] **Step 3: Run the full build**

```powershell
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: zero new warnings and zero errors.

- [ ] **Step 4: Verify diff and commit scope**

```powershell
git diff --check
git status --short
git diff --name-only HEAD~8..HEAD
```

Confirm unrelated pre-existing dirty files were not staged or committed.

- [ ] **Step 5: Resolve any failure in its owning task**

If verification fails, return to the task that owns the failing behavior,
apply the minimal correction, rerun that task's focused command, and commit it
with that task's files. Do not create a broad final commit and do not stage
unrelated pre-existing workspace changes.

# AW3 School Runtime Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace synchronous scans, idle frame polling, permanent scholar jobs, unstable school ecology, and small synchronous SQLite transactions with an event-driven, bounded, measurable historical-school runtime.

**Architecture:** A main-thread scheduler coalesces year tokens and drains indexed work under a strict frame budget. Authoritative membership and affiliation events maintain school/city/standing buckets and narrow revisions; scheduled actor tasks provide visible activities without changing ordinary jobs. Durable operations are batched under WAL/NORMAL and project to the world only after commit.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, WorldBox actor AI, System.Data.SQLite, NeoModLoader, .NET 9 pure-rule harness, PowerShell source guards.

**Execution constraint:** Work directly on `master` in the existing workspace. Do not create a branch, worktree, or subagent. Preserve unrelated user changes if they appear.

**Design reference:** `docs/superpowers/specs/2026-07-15-aw3-school-runtime-cultiway-performance-design.md`

---

## File Map

New focused units:

- `Code/core/schools/HistoricalSchoolSchedulerRules.cs`: pure year coalescing, frame-transition, and bounded-year-key rules.
- `Code/core/schools/HistoricalSchoolScheduler.cs`: main-thread work queues, stage cursors, frame budget, and save flush.
- `Code/core/schools/HistoricalSchoolRuntimeIndex.cs`: authoritative ID-only buckets by school, city, standing, travel, and service.
- `Code/core/schools/HistoricalSchoolRevisionRules.cs`: pure change classification and narrow invalidation masks.
- `Code/core/schools/HistoricalSchoolRevisionService.cs`: per-school and per-city revision counters.
- `Code/core/schools/HistoricalSchoolStandingRules.cs`: standing, teacher promotion, leader succession, loyalty, and fair lecture rules.
- `Code/core/schools/HistoricalSchoolTaskLeaseService.cs`: temporary scheduled-task ownership and terminal cleanup.
- `Code/core/schools/HistoricalSchoolVenueProvider.cs`: academy-ready venue boundary plus bounded public-tile fallback.
- `Code/core/schools/HistoricalSchoolWriteBuffer.cs`: ordered durable operations and post-commit projections.
- `Code/core/schools/HistoricalSchoolDiagnostics.cs`: fixed counters and bounded diagnostic samples.
- `Code/core/schools/FormalAffiliationTransferScope.cs`: exact actor/kingdom/city permit for committed appointments.
- `Code/core/db/LineageArchivePragmaService.cs`: connection pragmas and save checkpoint.
- `Tests/AncientWarfare3.Rules.Tests/SchoolRuntimePerformanceTests.cs`: pure scheduler, revision, standing, index, and affiliation rules.

Existing ownership boundaries:

- `HistoricalSchoolRuntime.cs` remains the facade called by Harmony and save hooks.
- `SchoolMembershipService.cs` remains the only membership mutation entry point.
- `HistoricalAffiliationService.cs` remains the only school residence/service mutation entry point.
- `HistoricalSchoolActionService.cs` retains durable lecture/conversion/rediscovery commits, not annual scanning.
- `HistoricalSchoolDebateService.cs` retains debate selection/settlement rules, not frame polling.
- `SchoolGuestOfficeService.cs` retains appointment/dismissal orchestration.
- `CitySchoolSnapshotService.cs` remains the snapshot owner but consumes direct runtime indexes.

---

### Task 1: Establish Failing Runtime, Ecology, and Allocation Tests

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/SchoolRuntimePerformanceTests.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add the pure-rule test entry point**

Create `SchoolRuntimePerformanceTests.cs` with these exact initial assertions. The referenced production types intentionally do not exist yet.

```csharp
using AncientWarfare3.core.schools;

internal static class SchoolRuntimePerformanceTests
{
    public static void Run()
    {
        var years = new HistoricalSchoolSchedulerState();
        True(years.EnqueueYear(73), "first year token is accepted");
        True(years.EnqueueYear(75), "newer year coalesces pending work");
        Equal(75, years.PendingYear, "latest pending year wins");
        Equal(75, years.TakePendingYear(), "pending year is consumed once");
        Equal(-1, years.TakePendingYear(), "empty scheduler stays empty");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) years.HasPendingWork();
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before,
            "idle scheduler query allocates zero bytes");

        Equal(HistoricalSchoolRevisionMask.None,
            HistoricalSchoolRevisionRules.ClassifyAffiliation(4, 4, true, true, -1, -1),
            "identical affiliation does not invalidate");
        Equal(HistoricalSchoolRevisionMask.Residence,
            HistoricalSchoolRevisionRules.ClassifyAffiliation(4, 9, true, true, -1, -1),
            "actual residence move invalidates residence only");
        Equal(HistoricalSchoolRevisionMask.Presence,
            HistoricalSchoolRevisionRules.ClassifyAffiliation(4, 4, true, false, -1, -1),
            "travel departure invalidates presence");
        Equal(HistoricalSchoolRevisionMask.Service,
            HistoricalSchoolRevisionRules.ClassifyAffiliation(4, 4, true, true, -1, 8),
            "appointment invalidates service only");

        Equal(HistoricalSchoolStanding.Teacher,
            HistoricalSchoolStandingRules.ResolvePromotion(
                HistoricalSchoolStanding.Disciple, 3, 10f),
            "three-year reputation-ten disciple becomes teacher");
        Equal(HistoricalSchoolStanding.Disciple,
            HistoricalSchoolStandingRules.ResolvePromotion(
                HistoricalSchoolStanding.Disciple, 2, 30f),
            "membership age cannot be skipped");
        True(HistoricalSchoolStandingRules.CanConvert(30, 18, 5, 0.45f, false),
            "conversion is available after loyalty and teacher absence");
        Equal(false,
            HistoricalSchoolStandingRules.CanConvert(29, 18, 5, 0.45f, false),
            "twelve-year loyalty is strict");
        Equal(false,
            HistoricalSchoolStandingRules.CanConvert(40, 18, 5, 0.45f, true),
            "busy member cannot convert");

        True(FormalAffiliationTransferRules.Allows(42, 7, 11, 42, 7, 11),
            "exact committed transfer is allowed");
        Equal(false,
            FormalAffiliationTransferRules.Allows(42, 7, 11, 43, 7, 11),
            "another actor cannot borrow a permit");
        Equal(false,
            FormalAffiliationTransferRules.Allows(42, 7, 11, 42, 7, 12),
            "another city cannot borrow a permit");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException($"{name}: expected true");
    }
}
```

Append this call immediately before the final success line in `Program.cs`:

```csharp
SchoolRuntimePerformanceTests.Run();
```

- [ ] **Step 2: Link the new production rule files**

Add these entries to the test project `<ItemGroup>`:

```xml
<Compile Include="..\..\Code\core\schools\HistoricalSchoolSchedulerRules.cs" Link="Production\HistoricalSchoolSchedulerRules.cs" />
<Compile Include="..\..\Code\core\schools\HistoricalSchoolRevisionRules.cs" Link="Production\HistoricalSchoolRevisionRules.cs" />
<Compile Include="..\..\Code\core\schools\HistoricalSchoolStandingRules.cs" Link="Production\HistoricalSchoolStandingRules.cs" />
<Compile Include="..\..\Code\core\schools\FormalAffiliationTransferScope.cs" Link="Production\FormalAffiliationTransferScope.cs" />
```

- [ ] **Step 3: Replace guards that currently require bad behavior**

Delete the two source guards that require lecture planning/runtime to exclude serving
scholars. Add guards that reject the old roots:

```powershell
Require-Absent 'school updateAge synchronous runner' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'HistoricalSchoolRuntime.OnWorldYear()'
Require-Absent 'school frame stopwatch allocation' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'Stopwatch.StartNew()'
Require-Absent 'per-frame activity LINQ ordering' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' '.OrderBy('
Require-Absent 'per-frame debate distinct scan' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' '.Distinct()'
Require-Absent 'permanent scholar job restoration' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'RestoreScholarJob('
Require-Absent 'lecture requires vanilla city equality' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'pActor.city?.data?.id == residence.data.id'
Require-Absent 'inactive school map rebuild' 'Code/core/policy/SchoolMapModeService.cs' 'IsActive() ? 4 : 1'
Require-Present 'year token enqueue' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'HistoricalSchoolRuntime.EnqueueWorldYear()'
Require-Present 'temporary school task scheduling' 'Code/core/schools/HistoricalSchoolTaskLeaseService.cs' 'scheduleTask('
Require-Present 'scoped formal affiliation transfer' 'Code/core/schools/FormalAffiliationTransferScope.cs' 'FormalAffiliationTransferRules.Allows'
```

- [ ] **Step 4: Run tests and prove red state**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\SourceGuardTests.ps1
```

Expected: the rule project fails because the four linked production files do not exist;
source guards report the synchronous hook, stopwatch/LINQ, scholar-job restoration, and
missing new scheduler/task/scope files.

- [ ] **Step 5: Commit the failing tests**

```powershell
git add Tests/AncientWarfare3.Rules.Tests Tests/SourceGuardTests.ps1
git commit -m "test: define school runtime performance invariants"
```

---

### Task 2: Implement Pure Scheduler, Revision, Standing, and Transfer Rules

**Files:**
- Create: `Code/core/schools/HistoricalSchoolSchedulerRules.cs`
- Create: `Code/core/schools/HistoricalSchoolRevisionRules.cs`
- Create: `Code/core/schools/HistoricalSchoolStandingRules.cs`
- Create: `Code/core/schools/FormalAffiliationTransferScope.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/SchoolRuntimePerformanceTests.cs`

- [ ] **Step 1: Implement allocation-free year coalescing and bounded year keys**

Create `HistoricalSchoolSchedulerRules.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public sealed class HistoricalSchoolSchedulerState
    {
        private int _pendingYear = -1;
        public int PendingYear => _pendingYear;
        public bool EnqueueYear(int pYear)
        {
            if (pYear < 0 || pYear <= _pendingYear) return false;
            _pendingYear = pYear;
            return true;
        }
        public int TakePendingYear()
        {
            int year = _pendingYear;
            _pendingYear = -1;
            return year;
        }
        public bool HasPendingWork() => _pendingYear >= 0;
        public void Clear() => _pendingYear = -1;
    }

    public sealed class HistoricalSchoolBoundedYearKeys
    {
        private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);
        private int _oldestYear = -1;
        public int Count => _keys.Count;
        public bool Add(int pYear, string pKey)
        {
            if (pYear < 0 || string.IsNullOrEmpty(pKey)) return false;
            Prune(pYear - 1);
            return _keys.Add(pYear + ":" + pKey);
        }
        public void Prune(int pOldestYear)
        {
            if (pOldestYear <= _oldestYear) return;
            _oldestYear = pOldestYear;
            _keys.RemoveWhere(p => ParseYear(p) < pOldestYear);
        }
        public void Clear() { _keys.Clear(); _oldestYear = -1; }
        private static int ParseYear(string pKey)
        {
            int separator = pKey.IndexOf(':');
            return separator > 0 && int.TryParse(pKey.Substring(0, separator), out int year)
                ? year : int.MinValue;
        }
    }
}
```

- [ ] **Step 2: Implement narrow affiliation revision classification**

Create `HistoricalSchoolRevisionRules.cs`:

```csharp
using System;

namespace AncientWarfare3.core.schools
{
    [Flags]
    public enum HistoricalSchoolRevisionMask
    {
        None = 0,
        Residence = 1,
        Presence = 2,
        Service = 4,
        Structure = 8,
        Score = 16,
        Activity = 32
    }

    public static class HistoricalSchoolRevisionRules
    {
        public static HistoricalSchoolRevisionMask ClassifyAffiliation(
            long pOldResidence, long pNewResidence, bool pOldPresent, bool pNewPresent,
            long pOldService, long pNewService)
        {
            HistoricalSchoolRevisionMask result = HistoricalSchoolRevisionMask.None;
            if (pOldResidence != pNewResidence) result |= HistoricalSchoolRevisionMask.Residence;
            if (pOldPresent != pNewPresent) result |= HistoricalSchoolRevisionMask.Presence;
            if (pOldService != pNewService) result |= HistoricalSchoolRevisionMask.Service;
            return result;
        }
    }
}
```

- [ ] **Step 3: Implement standing, promotion, loyalty, and fair rotation rules**

Create `HistoricalSchoolStandingRules.cs` with the persisted enum and deterministic rules:

```csharp
using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public enum HistoricalSchoolStanding
    {
        Member,
        Disciple,
        Teacher,
        Leader,
        CanonicalMaster
    }

    public static class HistoricalSchoolStandingRules
    {
        public const int TeacherMembershipYears = 3;
        public const float TeacherReputation = 10f;
        public const int ConversionLoyaltyYears = 12;
        public const int TeacherAbsenceYears = 5;
        public const float RivalShareMinimum = 0.25f;

        public static HistoricalSchoolStanding ResolvePromotion(
            HistoricalSchoolStanding pCurrent, int pMembershipYears, float pReputation)
        {
            if (pCurrent != HistoricalSchoolStanding.Disciple) return pCurrent;
            return pMembershipYears >= TeacherMembershipYears &&
                   pReputation >= TeacherReputation
                ? HistoricalSchoolStanding.Teacher : pCurrent;
        }

        public static bool CanConvert(int pCurrentYear, int pMembershipStartYear,
            int pYearsWithoutTeacher, float pRivalShare, bool pBusy)
        {
            return !pBusy && pCurrentYear - pMembershipStartYear >= ConversionLoyaltyYears &&
                   pYearsWithoutTeacher >= TeacherAbsenceYears &&
                   !float.IsNaN(pRivalShare) && pRivalShare >= RivalShareMinimum;
        }

        public static int NextFairIndex(int pCurrentIndex, int pCount)
        {
            return pCount <= 0 ? -1 : (Math.Max(-1, pCurrentIndex) + 1) % pCount;
        }
    }
}
```

- [ ] **Step 4: Implement exact transfer rules and the runtime scope**

Create `FormalAffiliationTransferScope.cs`:

```csharp
using System;

namespace AncientWarfare3.core.schools
{
    public static class FormalAffiliationTransferRules
    {
        public static bool Allows(long pPermitActor, long pPermitKingdom, long pPermitCity,
            long pActor, long pKingdom, long pCity)
        {
            return pPermitActor >= 0 && pPermitActor == pActor &&
                   pPermitKingdom >= 0 && pPermitKingdom == pKingdom &&
                   pPermitCity >= 0 && pPermitCity == pCity;
        }
    }

    internal sealed class FormalAffiliationTransferScope : IDisposable
    {
        [ThreadStatic] private static FormalAffiliationTransferScope _current;
        private readonly FormalAffiliationTransferScope _previous;
        private bool _disposed;

        private FormalAffiliationTransferScope(long pActorId, long pKingdomId, long pCityId)
        {
            ActorId = pActorId; KingdomId = pKingdomId; CityId = pCityId;
            _previous = _current; _current = this;
        }

        public long ActorId { get; }
        public long KingdomId { get; }
        public long CityId { get; }
        public static FormalAffiliationTransferScope Open(long pActorId, long pKingdomId,
            long pCityId) => new FormalAffiliationTransferScope(pActorId, pKingdomId, pCityId);
        public static bool Allows(long pActorId, long pKingdomId, long pCityId)
        {
            FormalAffiliationTransferScope permit = _current;
            return permit != null && FormalAffiliationTransferRules.Allows(
                permit.ActorId, permit.KingdomId, permit.CityId,
                pActorId, pKingdomId, pCityId);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(_current, this)) _current = _previous;
        }
    }
}
```

- [ ] **Step 5: Run the pure tests**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: `Rule tests passed.` The source guards remain red because runtime integration is
not implemented yet.

- [ ] **Step 6: Commit the rules**

```powershell
git add Code/core/schools/HistoricalSchoolSchedulerRules.cs Code/core/schools/HistoricalSchoolRevisionRules.cs Code/core/schools/HistoricalSchoolStandingRules.cs Code/core/schools/FormalAffiliationTransferScope.cs
git commit -m "feat: define bounded school runtime rules"
```

---

### Task 3: Configure SQLite WAL/NORMAL and Capture the Pre-Refactor Baseline

**Files:**
- Create: `Code/core/db/LineageArchivePragmaService.cs`
- Modify: `Code/core/db/LineageArchiveManager.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Create: `Code/core/schools/HistoricalSchoolDiagnostics.cs`
- Modify: `Code/core/schools/HistoricalSchoolRuntime.cs`
- Modify: `Code/core/pathfinding/AWPathDiagnostics.cs`
- Create: `docs/performance/2026-07-15-school-path-baseline.md`
- Test: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing pragma and diagnostic guards**

Add:

```powershell
Require-Present 'archive WAL pragma' 'Code/core/db/LineageArchivePragmaService.cs' 'PRAGMA journal_mode=WAL'
Require-Present 'archive NORMAL sync pragma' 'Code/core/db/LineageArchivePragmaService.cs' 'PRAGMA synchronous=NORMAL'
Require-Present 'archive save checkpoint' 'Code/patch/AW_SavePatch.cs' 'LineageArchivePragmaService.CheckpointForSave'
Require-Present 'school performance counters' 'Code/core/schools/HistoricalSchoolDiagnostics.cs' 'IdleAllocatedBytes'
```

Run the source guards and expect these four requirements to fail.

- [ ] **Step 2: Implement connection configuration and checkpoint**

`LineageArchivePragmaService.Configure(SQLiteConnection)` must execute the following as one
command immediately after every `_db.Open()` and every destination backup connection open:

```csharp
command.CommandText =
    "PRAGMA journal_mode=WAL;" +
    "PRAGMA synchronous=NORMAL;" +
    "PRAGMA busy_timeout=2500;" +
    "PRAGMA wal_autocheckpoint=1000;";
command.ExecuteNonQuery();
```

`CheckpointForSave` executes `PRAGMA wal_checkpoint(PASSIVE)` and returns false on a logged
SQLite exception. Call `Configure` in both create and load paths. Call the checkpoint after
pending school commits flush and before `SaveToSaveDirectory` performs `BackupDatabase`.

- [ ] **Step 3: Add fixed, allocation-free diagnostic counters**

`HistoricalSchoolDiagnostics` must use primitive fields and `Interlocked` increments. Its
snapshot method is called only on explicit diagnostics. Record year-enqueue ticks, scheduler
ticks, idle allocated bytes, SQL batches/statements, snapshot rebuild causes, activity
counts, and cache sizes. Extend `AWPathDiagnostics` with generated/reused/fast-step/
vanilla-step counters without changing path behavior yet.

- [ ] **Step 4: Build and deploy the instrumentation-only slice**

Run:

```powershell
dotnet build AncientWarfare3.csproj -c Debug --no-restore -p:AutomaticallyUseReferenceAssemblyPackages=true
dotnet build AncientWarfare3.csproj -c Release --no-restore -p:AutomaticallyUseReferenceAssemblyPackages=true
```

Expected: both builds succeed with zero errors.

- [ ] **Step 5: Record baseline evidence**

Use the existing fixed test world/save and record in the baseline document:

```text
World seed and settings
Actor/city/kingdom counts
Years sampled
Actor update p50/p95/max
Kingdom updateAge p50/p95/max
HistoricalSchoolRuntime frame p50/p95/max and allocated bytes
Path request generated/reused counts
Fast-step/vanilla-step counts (fast remains zero before replacement)
SQLite journal/synchronous values
Every school member/teacher/leader/canonical-master count
Every runtime collection size
```

Do not invent unavailable numbers. Mark a live metric `not captured` until the instrumented
game run supplies it; continue automated work without treating an uncaptured live metric as
passing evidence.

- [ ] **Step 6: Commit instrumentation and baseline format**

```powershell
git add Code/core/db Code/core/schools/HistoricalSchoolDiagnostics.cs Code/core/schools/HistoricalSchoolRuntime.cs Code/core/pathfinding/AWPathDiagnostics.cs Code/patch/AW_SavePatch.cs Tests/SourceGuardTests.ps1 docs/performance/2026-07-15-school-path-baseline.md
git commit -m "perf: establish school and path baselines"
```

---

### Task 4: Add the Authoritative Runtime Index and Narrow Revision Service

**Files:**
- Create: `Code/core/schools/HistoricalSchoolRuntimeIndex.cs`
- Create: `Code/core/schools/HistoricalSchoolRevisionService.cs`
- Modify: `Code/core/schools/SchoolMembershipService.cs`
- Modify: `Code/core/schools/HistoricalAffiliationService.cs`
- Modify: `Code/core/schools/HistoricalSchoolDescentService.cs`
- Modify: `Code/core/lineage/XiaizationService.cs`
- Modify: `Code/patch/AW_HistoricalSchoolPatch.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/SchoolRuntimePerformanceTests.cs`

- [ ] **Step 1: Add failing ID-index tests**

Extend the pure tests with an ID-only index record and assertions:

```csharp
var index = new HistoricalSchoolRuntimeIndex();
index.Upsert(new HistoricalSchoolIndexEntry(42, "ru", 7,
    HistoricalSchoolStanding.Disciple, true, false, -1));
Equal(1, index.MemberCount("ru"), "school member is indexed once");
Equal(1, index.ResidentCount(7, "ru"), "present resident is indexed by city");
index.Upsert(new HistoricalSchoolIndexEntry(42, "ru", 9,
    HistoricalSchoolStanding.Teacher, true, true, -1));
Equal(0, index.ResidentCount(7, "ru"), "old residence bucket is removed");
Equal(1, index.TeacherCount("ru"), "promotion updates teacher bucket");
index.Remove(42);
Equal(0, index.MemberCount("ru"), "death/close removes all buckets");
```

Link `HistoricalSchoolRuntimeIndex.cs` into the rule project. Run and expect missing-type
failure.

- [ ] **Step 2: Implement ID-only index entries and buckets**

The index must own one entry per actor and update these sets atomically on `Upsert`:

```csharp
Dictionary<long, HistoricalSchoolIndexEntry> ByActor;
Dictionary<string, HashSet<long>> MembersBySchool;
Dictionary<string, HashSet<long>> TeachersBySchool;
Dictionary<string, HashSet<long>> LeadersBySchool;
Dictionary<long, Dictionary<string, HashSet<long>>> PresentByCitySchool;
Dictionary<int, HashSet<long>> TravelByBucket;
Dictionary<long, HashSet<long>> ServingByKingdom;
```

Remove an actor from every old bucket before adding its new immutable entry. Empty nested
sets and dictionaries must be removed immediately. Expose count and stable-ID enumeration,
not mutable collection instances.

- [ ] **Step 3: Implement per-city/per-school revision counters**

`HistoricalSchoolRevisionService` stores structure/score/activity revisions by school and
residence/presence/service revisions by city. `ApplyAffiliationChange(old, next)` calls
`HistoricalSchoolRevisionRules.ClassifyAffiliation` and increments only changed dimensions.
It marks only the old and new residence cities dirty.

- [ ] **Step 4: Wire all authoritative mutations**

After a proven membership or affiliation commit:

```csharp
HistoricalSchoolRuntimeIndex.Instance.Upsert(
    HistoricalSchoolIndexEntry.From(actorId, membership, affiliation));
HistoricalSchoolRevisionService.ApplyMembershipChange(oldMembership, membership);
HistoricalSchoolRevisionService.ApplyAffiliationChange(oldAffiliation, affiliation);
```

On committed death/close, remove the actor. On master descent, add canonical standing.
On `City.setKingdom`, `City.destroyCity`, and full Xiaization transition, update the living
Xia-city index. A one-time `LoadState` reconstruction from active DB rows is allowed; no
annual world or DB reconstruction is allowed.

- [ ] **Step 5: Remove unconditional global revision advancement**

Delete `AdvanceResidenceRevision()` calls that execute for identical affiliation state.
Keep compatibility properties only as adapters over the new revision service until UI
consumers move in Task 9.

- [ ] **Step 6: Run tests and commit**

Run pure rules, source guards, and Debug build. Expected: pure index tests pass; existing
runtime behavior remains functional.

```powershell
git add Code/core/schools/HistoricalSchoolRuntimeIndex.cs Code/core/schools/HistoricalSchoolRevisionService.cs Code/core/schools/SchoolMembershipService.cs Code/core/schools/HistoricalAffiliationService.cs Code/core/schools/HistoricalSchoolDescentService.cs Code/core/lineage/XiaizationService.cs Code/patch/AW_HistoricalSchoolPatch.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "perf: index school runtime mutations"
```

---

### Task 5: Move the Year Boundary to a Bounded Frame Scheduler

**Files:**
- Create: `Code/core/schools/HistoricalSchoolScheduler.cs`
- Modify: `Code/core/schools/HistoricalSchoolRuntime.cs`
- Modify: `Code/patch/AW_HistoricalSchoolPatch.cs`
- Modify: `Code/patch/AW_DeferredRuntimeWorkPatch.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Test: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Make the year-hook guard fail on current code**

Run source guards and preserve the failure showing `KingdomUpdateAge_Postfix` still calls
`OnWorldYear` synchronously.

- [ ] **Step 2: Replace the synchronous entry point**

Change the Harmony postfix body to exactly:

```csharp
private static void KingdomUpdateAge_Postfix()
{
    HistoricalSchoolRuntime.EnqueueWorldYear();
}
```

`EnqueueWorldYear` reads `Date.getCurrentYear()`, coalesces it in
`HistoricalSchoolSchedulerState`, records diagnostics, and returns. It must not call
`LoadState`, `LivingXiaCities`, any service `ProcessYear`, or any store method.

- [ ] **Step 3: Implement deterministic stage work**

`HistoricalSchoolScheduler` owns a queue of these stage IDs:

```csharp
Bootstrap, Descent, ServiceClose, ServiceAppointment, Promotion, LecturePlan,
DebatePlan, Conversion, Rediscovery, RuntimeCommit
```

Each stage stores a stable cursor into `HistoricalSchoolRuntimeIndex` or the 14-entry school
registry. `ProcessFrame` uses `Stopwatch.GetTimestamp()` and stops after 0.75 ms, one durable
batch, or one visible actor transition. Empty state returns before reading the timestamp.

- [ ] **Step 4: Keep persistence and saves coherent**

The final `RuntimeCommit` freezes the newest eligible/world year. Save flush drains only
durable-ready scheduler work and pending DB operations; it does not force unfinished actor
movement. Fresh-world clear empties the scheduler and coalesced year.

- [ ] **Step 5: Verify the updateAge boundary**

Run source guards and inspect `AW_HistoricalSchoolPatch.cs`. Expected: no scan or SQL is
reachable from the postfix; the guard requiring `EnqueueWorldYear` passes.

- [ ] **Step 6: Commit**

```powershell
git add Code/core/schools/HistoricalSchoolScheduler.cs Code/core/schools/HistoricalSchoolRuntime.cs Code/patch/AW_HistoricalSchoolPatch.cs Code/patch/AW_DeferredRuntimeWorkPatch.cs Code/patch/AW_SavePatch.cs
git commit -m "perf: defer school years to bounded scheduler"
```

---

### Task 6: Persist Standing, Promote Teachers, and Elect Senior Leaders

**Files:**
- Modify: `Code/core/db/SchoolMembershipTableItem.cs`
- Modify: `Code/core/schools/HistoricalSchoolState.cs`
- Modify: `Code/core/schools/HistoricalSchoolStore.cs`
- Modify: `Code/core/schools/SchoolMembershipService.cs`
- Modify: `Code/core/schools/SchoolLineageService.cs`
- Modify: `Code/core/schools/HistoricalSchoolLectureRules.cs`
- Modify: `Code/core/schools/HistoricalSchoolScheduler.cs`
- Modify: `Code/core/schools/SchoolRosterReadModelService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/SchoolRuntimePerformanceTests.cs`

- [ ] **Step 1: Add failing leader and lecture-fairness tests**

Add tests that select the lowest `StartYear`, then lowest actor ID, from qualified teachers;
test that a fair cursor visits 14 schools exactly once before repeating and that all 14 are
served within two eight-slot years.

- [ ] **Step 2: Extend the fresh-world membership schema**

Add:

```csharp
public string standing;
public int loyalty_until_year = -1;
```

Add `Standing` and `LoyaltyUntilYear` to `SchoolMembershipRecord` as optional trailing
constructor parameters. All SELECT, INSERT, exact-readback, conversion, death, and load
paths must read/write them. No migration branch is added.

- [ ] **Step 3: Assign initial standings**

Use this exact mapping:

```text
HistoricalDescent -> CanonicalMaster
DirectDiscipleship -> Disciple
LaterDiscipleship -> Disciple
ExplicitConversion -> Member
PreservedWork -> Member
```

Join/conversion sets `LoyaltyUntilYear = currentYear + 12`.

- [ ] **Step 4: Promote and elect from indexed candidates**

The promotion stage processes only due disciple IDs. At three membership years, reputation
10 promotes to `Teacher` in one durable operation. Leader election chooses the qualified
teacher with earliest start year and then lowest actor ID. Canonical master is the live head
while present; its death schedules leader election without reserving a second canonical
master.

- [ ] **Step 5: Remove the contradictory lecture threshold**

Set later-teacher lecture eligibility to the persisted `Teacher` or `Leader` standing.
Delete `LaterTeacherMinimumReputation = 25f`; the single promotion rule remains three years
and reputation 10.

- [ ] **Step 6: Render persisted standing**

`SchoolRosterReadModelService` must use standing for founder/leader/teacher/disciple/member
rows. It must not infer every unclassified member as Confucian or teacher.

- [ ] **Step 7: Run rules, build, and commit**

```powershell
git add Code/core/db/SchoolMembershipTableItem.cs Code/core/schools/HistoricalSchoolState.cs Code/core/schools/HistoricalSchoolStore.cs Code/core/schools/SchoolMembershipService.cs Code/core/schools/SchoolLineageService.cs Code/core/schools/HistoricalSchoolLectureRules.cs Code/core/schools/HistoricalSchoolScheduler.cs Code/core/schools/SchoolRosterReadModelService.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: sustain school teacher succession"
```

---

### Task 7: Replace Activity Polling with Direct Queues and Temporary Task Leases

**Files:**
- Create: `Code/core/schools/HistoricalSchoolTaskLeaseService.cs`
- Modify: `Code/core/schools/HistoricalSchoolActivityQueue.cs`
- Modify: `Code/core/schools/HistoricalSchoolDebateActivityService.cs`
- Modify: `Code/core/schools/HistoricalSchoolActivityQueueRules.cs`
- Modify: `Code/ai/behaviours/actor/BehHistoricalSchoolLecture.cs`
- Modify: `Code/ai/behaviours/actor/BehHistoricalSchoolDebate.cs`
- Modify: `Code/core/schools/SchoolMembershipService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/SchoolRuntimePerformanceTests.cs`

- [ ] **Step 1: Add failing lease terminal-state tests**

Test acquire, duplicate rejection, exact completion, stale completion rejection, expiry,
death release, and `Clear`. Test that an empty activity scheduler executes 10,000 calls with
zero allocated bytes after warm-up.

- [ ] **Step 2: Implement task leases**

Each lease stores:

```csharp
ActorId, ActivityId, TaskId, SchoolId, CityId, VenueKey, StartFrame, ExpiryFrame
```

`TrySchedule` inserts the lease first and then calls:

```csharp
pActor.scheduleTask(pTaskId, pTarget);
```

If scheduling throws, remove the exact lease and release the venue. Completion and cancel
compare the activity ID so a stale callback cannot release a newer task.

- [ ] **Step 3: Replace LINQ scans with ready/expiry queues**

Maintain direct queues for pending, ready-to-commit, and expiry IDs plus one active record
per actor. State transitions update queues once. `ProcessFrame` dequeues at most one valid
transition; stale queue IDs are skipped without enumerating or sorting active dictionaries.
Use `HistoricalSchoolBoundedYearKeys` for operation and actor-year deduplication.

- [ ] **Step 4: Remove permanent job restoration**

Every lecture/debate terminal path releases its task lease and venue. Delete all
`setCitizenJob(aw_historical_school_scholar)` cleanup. Canonical masters retain their job
because it was never replaced; every other actor resumes its existing job automatically.

- [ ] **Step 5: Correct foreign-resident validation**

Validate `ResidenceCityId`, membership, presence, lease, claimed venue, and current-tile
distance. Delete every lecture/debate condition that requires `Actor.city` to equal school
residence. Serving scholars are rejected only while a court/combat/critical task lease is
active, not solely because `ServiceKingdomId >= 0`.

- [ ] **Step 6: Run allocation tests, guards, build, and commit**

Expected: stopwatch/LINQ/job-restoration/city-equality guards pass.

```powershell
git add Code/core/schools/HistoricalSchoolTaskLeaseService.cs Code/core/schools/HistoricalSchoolActivityQueue.cs Code/core/schools/HistoricalSchoolDebateActivityService.cs Code/core/schools/HistoricalSchoolActivityQueueRules.cs Code/ai/behaviours/actor/BehHistoricalSchoolLecture.cs Code/ai/behaviours/actor/BehHistoricalSchoolDebate.cs Code/core/schools/SchoolMembershipService.cs Tests
git commit -m "perf: schedule bounded school activities"
```

---

### Task 8: Make Travel and Venues Local, Bounded, and Academy-Ready

**Files:**
- Create: `Code/core/schools/HistoricalSchoolVenueProvider.cs`
- Modify: `Code/core/schools/HistoricalSchoolVenueService.cs`
- Modify: `Code/core/schools/HistoricalSchoolRecruitCandidateCache.cs`
- Modify: `Code/core/schools/HistoricalSchoolTravelService.cs`
- Modify: `Code/content/schools/HistoricalSchoolContent.cs`
- Create: `Code/ai/behaviours/actor/BehHistoricalSchoolIdleRoam.cs`
- Modify: `Code/ai/behaviours/actor/BehHistoricalSchoolTravel.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/SchoolRuntimePerformanceTests.cs`

- [ ] **Step 1: Add failing cache-bound and venue-priority tests**

Test a fixed-capacity LRU with 128 cities, city-destruction removal, exact venue release,
academy provider priority, public-tile fallback, and rejection of the city center as a
universal fallback.

- [ ] **Step 2: Implement the provider boundary**

Define:

```csharp
internal interface IHistoricalSchoolVenueSource
{
    bool TryFind(City pCity, Actor pActor, string pSchoolId,
        HistoricalSchoolVenueKind pKind, out WorldTile pPrimary, out WorldTile pSecondary);
}
```

Register an empty academy source first and a public-city source second. Public candidates
are stable, inside `tile.zone.city == pCity`, walkable, not the center, and separated by
occupancy keys.

- [ ] **Step 3: Bound caches**

Both candidate and venue-tile caches use a 128-city LRU. Entries store city identity plus a
zone/version stamp. City death, kingdom transfer, or invalid tile removes the entry.
Operation claims remain only while queued/active/persistence-pending.

- [ ] **Step 4: Schedule travel without replacing jobs**

Delete `RestoreScholarJob`. `EnsureTravelTask` acquires a task lease and uses
`scheduleTask(TravelTaskId, destinationTile)`. Quarterly processing iterates only the
runtime index's eligible travel bucket and performs cooldown checks before touching actor
status or task state.

- [ ] **Step 5: Add canonical-master idle roaming**

The permanent canonical job contains idle roam, wait, and stuck recovery. Idle roam selects
a valid tile 6-18 tiles from the actor inside the residence city, schedules movement once,
waits, and returns. It never chooses a border tile or city center by default. Non-canonical
members keep their vanilla jobs.

- [ ] **Step 6: Verify and commit**

Run pure tests, guards, and both builds.

```powershell
git add Code/core/schools/HistoricalSchoolVenueProvider.cs Code/core/schools/HistoricalSchoolVenueService.cs Code/core/schools/HistoricalSchoolRecruitCandidateCache.cs Code/core/schools/HistoricalSchoolTravelService.cs Code/content/schools/HistoricalSchoolContent.cs Code/ai/behaviours/actor/BehHistoricalSchoolIdleRoam.cs Code/ai/behaviours/actor/BehHistoricalSchoolTravel.cs Tests
git commit -m "perf: bound scholar travel and venues"
```

---

### Task 9: Replace Annual Snapshots with Indexed Fair Planners

**Files:**
- Modify: `Code/core/schools/HistoricalSchoolScheduler.cs`
- Modify: `Code/core/schools/HistoricalSchoolActionService.cs`
- Modify: `Code/core/schools/HistoricalSchoolDebateService.cs`
- Modify: `Code/core/schools/SchoolGuestOfficeService.cs`
- Modify: `Code/core/schools/HistoricalSchoolTravelService.cs`
- Delete: `Code/core/schools/HistoricalSchoolAnnualMemberSnapshot.cs`
- Delete: `Code/core/schools/HistoricalSchoolAnnualMemberSnapshotBuilder.cs`
- Test: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add guards rejecting annual snapshot construction**

```powershell
Require-Absent 'annual member snapshot runtime' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolAnnualMemberSnapshotBuilder.Build'
Require-Absent 'annual active affiliation array' 'Code/core/schools/SchoolGuestOfficeService.cs' 'ActiveSnapshots()'
Require-Absent 'quarter active affiliation array' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'ActiveSnapshots('
```

Run and expect all three to fail.

- [ ] **Step 2: Plan lectures fairly from teacher buckets**

The lecture stage walks school IDs from the persisted fair cursor, then teacher IDs in
seniority order. It attempts one teacher per school before a second pass and freezes at
most eight plans. Candidate sampling occurs only after a venue activates.

- [ ] **Step 3: Plan debates, conversions, and rediscovery incrementally**

Debates consume residence-city school buckets. Conversion consumes at most eight member IDs
from stable city cursors and applies loyalty/teacher-absence/busy rules. Rediscovery checks
the 14 school IDs and preserved-work city index, never all members or cities.

- [ ] **Step 4: Process guest offices from service and vacancy indexes**

Service close walks only `ServingByKingdom`; appointments walk only indexed court vacancies
and resident eligible scholars. Preserve the existing maximum 16 appointments and atomic
start/end persistence.

- [ ] **Step 5: Delete obsolete annual snapshot types**

Remove both files only after `rg -n "HistoricalSchoolAnnualMemberSnapshot" Code` returns no
production references.

- [ ] **Step 6: Run guards/build and commit**

```powershell
git add -A Code/core/schools Tests/SourceGuardTests.ps1
git commit -m "perf: plan school years from live indexes"
```

---

### Task 10: Make Snapshot/UI Work Demand-Driven

**Files:**
- Modify: `Code/core/court/CitySchoolDirtyQueue.cs`
- Modify: `Code/core/court/CitySchoolRetryScheduler.cs`
- Modify: `Code/core/court/CitySchoolResidentIndexRules.cs`
- Modify: `Code/core/court/CitySchoolSnapshotService.cs`
- Modify: `Code/core/policy/SchoolMapModeService.cs`
- Modify: `Code/core/policy/SchoolMapBottomBarController.cs`
- Modify: `Code/core/schools/SchoolRosterReadModelService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/SchoolRuntimePerformanceTests.cs`

- [ ] **Step 1: Add failing dirty-queue allocation tests**

Link `CitySchoolDirtyQueue.cs` into the pure test project. Warm it up, then call empty
`TryDequeue` 10,000 times and assert zero allocated bytes. Test FIFO, duplicate mark,
requeue-front, and removal.

- [ ] **Step 2: Replace batch arrays with `TryDequeue`**

Add:

```csharp
public bool TryDequeue(out long pCityId)
{
    if (_queue.First == null) { pCityId = -1; return false; }
    pCityId = _queue.First.Value;
    _queue.RemoveFirst();
    _nodes.Remove(pCityId);
    return true;
}
```

`ProcessDirty` returns before retry/batch context work when dirty and due-retry counts are
both zero. A non-empty fixed local list is created only after the first ID is dequeued.

- [ ] **Step 3: Consume runtime residence buckets directly**

Delete global resident-index rebuilding keyed by membership/residence global versions.
For each requested city, obtain only that city's present actor IDs and school buckets from
`HistoricalSchoolRuntimeIndex`.

- [ ] **Step 4: Gate map and bottom-bar processing**

When map mode is inactive, no dirty snapshot is rebuilt. The bottom bar returns without
calling `Hide` repeatedly unless its own `_visibleOrPending` flag is true. Explicit school
window/court/AI requests set a snapshot-demand flag for affected cities.

- [ ] **Step 5: Key roster UI by narrow revisions**

Use selected-school structural/score/activity revisions plus residence revisions only for
cities represented in that roster. A debate reputation change must not invalidate every
school roster and city snapshot.

- [ ] **Step 6: Verify and commit**

Expected: empty dirty queue allocates zero; inactive-map guard passes; UI rules/build pass.

```powershell
git add Code/core/court Code/core/policy/SchoolMapModeService.cs Code/core/policy/SchoolMapBottomBarController.cs Code/core/schools/SchoolRosterReadModelService.cs Tests
git commit -m "perf: rebuild school UI only on demand"
```

---

### Task 11: Batch School Writes and Make Ledger Decay Lazy

**Files:**
- Create: `Code/core/schools/HistoricalSchoolWriteBuffer.cs`
- Modify: `Code/core/schools/HistoricalSchoolTeachingPersistenceDb.cs`
- Modify: `Code/core/schools/HistoricalSchoolStore.cs`
- Modify: `Code/core/schools/GuestOfficePersistenceDb.cs`
- Modify: `Code/core/schools/GuestOfficeEndPersistence.cs`
- Modify: `Code/core/schools/SchoolMembershipService.cs`
- Modify: `Code/core/schools/HistoricalSchoolScheduler.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/SchoolRuntimePerformanceTests.cs`

- [ ] **Step 1: Add failing write-buffer ordering tests**

Use fake operations to test FIFO order, duplicate operation-key rejection, one batch per
frame, committed/replayed projection, clean-failure release, unknown retry with exponential
backoff, bounded capacity 512, and save flush that ignores unfinished movement.

- [ ] **Step 2: Implement the operation contract**

```csharp
internal interface IHistoricalSchoolWriteOperation
{
    string OperationKey { get; }
    HistoricalSchoolTeachingPersistenceOutcome Execute(
        SQLiteConnection pDb, SQLiteTransaction pTransaction);
    void AfterCommit(HistoricalSchoolTeachingPersistenceOutcome pOutcome);
    void OnCleanFailure();
}
```

The buffer opens one transaction, executes a bounded FIFO batch, commits, then applies
post-commit projections in the same order. Unknown outcomes retain exact operations for
readback; callbacks never execute before commit.

- [ ] **Step 3: Add transaction-aware persistence overloads**

Teaching, debate/ledger, membership join/convert/close, appointment, and dismissal methods
accept an existing `SQLiteTransaction`. Public compatibility wrappers may open their own
transaction only outside scheduler use. Nested transactions are forbidden.

- [ ] **Step 4: Replace annual ledger updates with arithmetic decay**

`EffectiveLedger(row, currentYear)` applies the existing decay rate for
`currentYear - LastDecayYear` without writing. A ledger write stores the effective value and
sets `LastDecayYear=currentYear`. Delete `ApplyLedgerDecay` from the year scheduler.

- [ ] **Step 5: Flush safely on save**

Flush durable-ready operations, retain unresolved unknowns, checkpoint WAL, then backup.
Do not complete movement, teleport actors, or force a lecture/debate commit that is not
ready.

- [ ] **Step 6: Run SQLite microbench and commit**

Run at least five 100-operation samples. Expected median is no greater than 2.08 ms on the
same machine (`1.66 ms * 1.25`). Record results in the baseline document.

```powershell
git add Code/core/schools/HistoricalSchoolWriteBuffer.cs Code/core/schools/HistoricalSchoolTeachingPersistenceDb.cs Code/core/schools/HistoricalSchoolStore.cs Code/core/schools/GuestOfficePersistenceDb.cs Code/core/schools/GuestOfficeEndPersistence.cs Code/core/schools/SchoolMembershipService.cs Code/core/schools/HistoricalSchoolScheduler.cs Code/patch/AW_SavePatch.cs Tests docs/performance/2026-07-15-school-path-baseline.md
git commit -m "perf: batch durable school operations"
```

---

### Task 12: Apply Formal Transfer Only After Appointment Commit

**Files:**
- Modify: `Code/core/schools/HistoricalAffiliationService.cs`
- Modify: `Code/patch/AW_HistoricalSchoolPatch.cs`
- Modify: `Code/core/schools/SchoolGuestOfficeService.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/schools/GuestOfficeEndPersistence.cs`
- Test: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add guards for permit use and dismissal retention**

Require the appointment projection to open `FormalAffiliationTransferScope` and call
`joinCity`. Reject any travel-service call to `joinCity`. Require guest end projection not
to restore home city/kingdom.

- [ ] **Step 2: Permit only the exact nested vanilla transfer**

`CanJoinCity` and `CanJoinKingdom` first check the exact active permit. The city check uses
actor ID, target kingdom ID, and target city ID. The kingdom check uses the same permit's
kingdom while the permitted `joinCity` call is active. Outside the scope, retain the rule
that a travel-eligible scholar cannot casually join a foreign formal affiliation.

- [ ] **Step 3: Project committed appointments**

After affiliation and career are committed/adopted:

```csharp
using (FormalAffiliationTransferScope.Open(actor.data.id, host.id, residence.data.id))
{
    actor.joinCity(residence);
}
```

Verify `actor.city == residence && actor.kingdom == host` before marking the projection
complete. Failure remains in the bounded pending projection queue.

- [ ] **Step 4: Retain host affiliation on dismissal**

The atomic end clears office, status, service start/end active fields, and
`ServiceKingdomId`; it sets school lifecycle to `Resident` at the host residence. It does
not call `joinCity(home)`, `joinKingdom(home)`, or rewrite `HomeKingdomId`/
`HometownCityId`.

- [ ] **Step 5: Verify and commit**

Run pure transfer tests, source guards, guest persistence tests, and Debug/Release builds.

```powershell
git add Code/core/schools/HistoricalAffiliationService.cs Code/patch/AW_HistoricalSchoolPatch.cs Code/core/schools/SchoolGuestOfficeService.cs Code/core/court/CourtService.cs Code/core/schools/GuestOfficeEndPersistence.cs Tests
git commit -m "fix: naturalize scholars only on appointment"
```

---

### Task 13: Bound Every School Collection and Complete Automated Verification

**Files:**
- Modify: `Code/core/schools/HistoricalSchoolActivityQueue.cs`
- Modify: `Code/core/schools/HistoricalSchoolDebateActivityService.cs`
- Modify: `Code/core/schools/HistoricalSchoolVenueService.cs`
- Modify: `Code/core/schools/HistoricalSchoolRecruitCandidateCache.cs`
- Modify: `Code/core/schools/SchoolLineageService.cs`
- Modify: `Code/core/schools/SchoolMembershipService.cs`
- Modify: `Code/core/schools/SchoolGuestOfficeService.cs`
- Modify: `Code/core/schools/HistoricalSchoolRuntime.cs`
- Modify: `Tests/SourceGuardTests.ps1`
- Test: `Tests/AncientWarfare3.Rules.Tests/SchoolRuntimePerformanceTests.cs`

- [ ] **Step 1: Add bound/clear tests for every collection**

Cover current/previous-year operation keys, 512 pending durable operations, 128 cached
cities, active-only venues, committed-only death handling, active-only travel reservations,
and full fresh-world clear. Each test must assert an exact maximum count after inserting
more than the limit.

- [ ] **Step 2: Implement pruning at ownership boundaries**

Prune year keys when enqueuing a new year, city caches on city death/transfer and LRU insert,
death IDs after durable successor handling, travel reservations on every terminal state,
and stale pending queue nodes while dequeuing. `ClearRuntime` calls every owner exactly once.

- [ ] **Step 3: Remove obsolete bad source guards**

Delete guards that require deferred scans, permanent job restoration, or global version
polling. Add guards for no `Stopwatch.StartNew`, no annual snapshots, no inactive map build,
no `RestoreScholarJob`, no lecture city equality, WAL/NORMAL, scoped transfer, and bounded
key/cache classes.

- [ ] **Step 4: Run the full automated suite**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\SourceGuardTests.ps1
dotnet build AncientWarfare3.csproj -c Debug --no-restore -p:AutomaticallyUseReferenceAssemblyPackages=true
dotnet build AncientWarfare3.csproj -c Release --no-restore -p:AutomaticallyUseReferenceAssemblyPackages=true
git diff --check
```

Expected: rule tests and source guards pass; both builds have zero errors; diff check has no
output.

- [ ] **Step 5: Commit the completed school slice**

```powershell
git add Code Tests
git commit -m "perf: finish bounded historical school runtime"
```

---

### Task 14: Run Fresh-World School Ecology and Performance Acceptance

**Files:**
- Create: `docs/performance/2026-07-15-school-runtime-results.md`
- Modify only code required to correct an observed acceptance failure.

- [ ] **Step 1: Deploy the Debug build and create a fresh fixed-seed world**

Record seed, map size, speed, camera state, initial actor/city/kingdom counts, build commit,
and database path. Do not load the old development database.

- [ ] **Step 2: Capture years 50, 100, and 200**

At each checkpoint record every school's living members, disciples, teachers, leader,
canonical master, lectures in the prior two years, conversions, rediscovery, and cache
sizes. Query the archive and runtime diagnostics rather than estimating from UI.

- [ ] **Step 3: Verify exact ecology invariants**

Require one or zero live canonical masters per school, a leader whenever one eligible
teacher exists, a lecture within two years for continuously eligible schools, no conversion
inside twelve years, rediscovery within five years when preconditions exist, and no
canonical-master-only school after a disciple crosses the teacher threshold.

- [ ] **Step 4: Verify performance invariants**

Require year-hook p95 <= 0.10 ms, idle frame allocation 0 B, active scheduler <= 0.75 ms,
normal annual backlog <= 120 frames, zero inactive map rebuilds, bounded caches, and lower
Actor/updateAge p95 than the baseline. Record exact p50/p95/max and allocation values.

- [ ] **Step 5: Inspect movement state**

Sample active scholars after lecture, debate, travel success, path failure, appointment,
and dismissal. No actor may remain indefinitely at city center/border; foreign residents
must preserve formal affiliation until appointment; dismissed scholars must retain host
formal affiliation.

- [ ] **Step 6: Correct failures with a new failing regression test**

For each failure, add the smallest pure/source/integration test that reproduces it before
editing production code. Repeat the relevant checkpoint; do not weaken an acceptance limit.

- [ ] **Step 7: Commit verified results**

```powershell
git add docs/performance/2026-07-15-school-runtime-results.md Code Tests
git commit -m "test: verify long-run school performance"
```

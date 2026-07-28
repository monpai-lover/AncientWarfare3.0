# Circulating Officials and Civil Service Examination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make governor rotation atomic after Nine-Rank unlock and add persistent imperial examinations or lesser tribute examinations that let educated nobles, declined nobles, and commoners enter and advance through the court.

**Architecture:** Keep all policy decisions in pure rule classes, persist each examination session and candidate exactly once in SQLite, and let annual kingdom work enqueue due sessions while the authority-cycle scheduler advances one session and at most eight candidates at a time. Court appointment reads a cached qualification projection backed by the exam tables, while UI reads detached persisted rows and never scans live actors.

**Tech Stack:** C# 11 / .NET Framework 4.8 mod code, net9 isolated rule tests, System.Data.SQLite, NeoModLoader window APIs, Unity UI, PowerShell source guards.

---

### Task 1: Lock Down Pure Examination, Career, and Rotation Rules

**Files:**
- Create: `Code/core/court/CivilServiceExamRules.cs`
- Modify: `Code/core/court/OfficialCareerRankRules.cs`
- Modify: `Code/core/court/OfficialCirculationRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/CivilServiceExamRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add a failing civil-service slice to the existing rules runner**

Add `--civil-service-exam-slice` to `Program.cs.txt`, call `CivilServiceExamRulesTests.Run()`, and add this test source plus the production rule source to the test project.

```csharp
if (args.Length == 1 && args[0] == "--civil-service-exam-slice")
{
    CivilServiceExamRulesTests.Run();
    Console.WriteLine("AW3 civil-service examination rules passed.");
    return;
}
```

- [ ] **Step 2: Write failing rule tests for all fixed design values**

Tests must assert: three-year cadence; first full-year opening; imperial versus tribute mode; 96 candidates; 8 candidates per cycle; 60 pass mark; four quota formulas; stable tie-breaking; deterministic score jitter; qualification progression; tribute graduates entering imperial metropolitan examination; no class score bonus; middle/high office service gates; and complete deranged governor rotation or no plan.

```csharp
Equal(CivilServiceExamMode.Imperial,
    CivilServiceExamRules.ResolveMode(true, false), "mandate uses imperial exam");
Equal(64, CivilServiceExamRules.LocalQuota(30), "local quota is capped");
Equal(8, CivilServiceExamRules.AuthorityCandidateBudget,
    "authority work remains bounded");
True(OfficialCareerRankRules.CanEnterOffice(14, 10,
        hasLowerService: true, hasMiddleService: true,
        hasPassingEvaluation: true),
    "qualified middle-service official may enter high office");
Equal(false, OfficialCareerRankRules.CanEnterOffice(14, 10,
        hasLowerService: true, hasMiddleService: false,
        hasPassingEvaluation: true),
    "rank alone cannot bypass service history");
```

- [ ] **Step 3: Run the focused test and confirm RED**

Run:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --civil-service-exam-slice
```

Expected: compile failure because `CivilServiceExamRules` and new career/rotation methods do not exist.

- [ ] **Step 4: Implement the minimal pure rules**

Create enums and methods without WorldBox, SQLite, or Unity dependencies:

```csharp
public enum CivilServiceExamMode { Tribute, Imperial }
public enum CivilServiceExamStage { Scheduled, Local, Metropolitan, Palace, National, Ranking, Completed, Cancelled }

public static class CivilServiceExamRules
{
    public const int CandidateLimit = 96;
    public const int AuthorityCandidateBudget = 8;
    public const int PassMark = 60;
    public static CivilServiceExamMode ResolveMode(bool mandate, bool empire) =>
        mandate || empire ? CivilServiceExamMode.Imperial : CivilServiceExamMode.Tribute;
    public static int FirstOpeningYear(int completionYear) => completionYear + 1;
    public static bool IsCycleYear(int year, int anchor) =>
        year >= anchor && (year - anchor) % 3 == 0;
    public static int LocalQuota(int cityCount) => Math.Min(64, Math.Max(12, cityCount * 4));
    public static int MetropolitanQuota(int cityCount) => Math.Min(32, Math.Max(6, cityCount * 2));
    public static int PrefecturalQuota(int cityCount) => Math.Min(48, Math.Max(8, cityCount * 3));
    public static int NationalQuota(int cityCount) => Math.Min(20, Math.Max(4, cityCount));
}
```

Add `CanEnterOffice`, `ResolveInitialAppointmentRank`, and service-history inputs to `OfficialCareerRankRules`; add immutable rotation facts and a deterministic `TryBuildRotationPlan` to `OfficialCirculationRules`. Rotation returns false unless every due governor has a distinct valid destination and nobody remains in the same city.

- [ ] **Step 5: Run focused rules tests and commit**

Expected: `AW3 civil-service examination rules passed.`

```powershell
git add Code/core/court/CivilServiceExamRules.cs Code/core/court/OfficialCareerRankRules.cs Code/core/court/OfficialCirculationRules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "test: define civil service and rotation rules"
```

### Task 2: Add Durable Examination Tables and Indexes

**Files:**
- Create: `Code/core/db/CivilServiceExamSessionTableItem.cs`
- Create: `Code/core/db/CivilServiceExamCandidateTableItem.cs`
- Modify: `Code/core/db/LineageArchiveIndexRules.cs`
- Create: `Code/core/court/CivilServiceExamPersistence.cs`
- Create: `Tests/CivilServiceExamPersistenceSourceGuard.ps1`

- [ ] **Step 1: Write a failing source guard for schema and transaction invariants**

The guard must require both `[TableDef]` declarations, the design fields, a unique `(KINGDOM_ID,CYCLE_YEAR)` index, session due/status and candidate Actor/session indexes, parameterized SQL, and a single transaction that writes stage scores and advances the candidate cursor together.

```powershell
Require-Text $indexes 'uq_CivilServiceExamSession_kingdom_cycle' 'unique cycle index'
Require-Text $persistence 'transaction = pDb.BeginTransaction();' 'atomic stage write'
Reject-Text $persistence 'SELECT *' 'bounded explicit projection'
```

- [ ] **Step 2: Run the guard and confirm RED**

Run `powershell -ExecutionPolicy Bypass -File Tests/CivilServiceExamPersistenceSourceGuard.ps1`.

Expected: missing table and persistence files.

- [ ] **Step 3: Create both table models and required indexes**

Use the exact fields from the design spec. Make session IDs and candidate IDs primary keys. Add a unique index on `SESSION_ID,ACTOR_ID` so retries cannot duplicate candidates.

```csharp
[TableDef("CivilServiceExamSession")]
public sealed class CivilServiceExamSessionTableItem :
    AbstractTableItem<CivilServiceExamSessionTableItem>
{
    [TableItemDef(pIsPrimary: true)] public long id;
    public long kingdom_id = -1L;
    public int cycle_year = -1;
    public string mode = "";
    public string stage = "scheduled";
    public string status = "scheduled";
    public long next_due_world_day = -1L;
    public int candidate_cursor;
}
```

- [ ] **Step 4: Implement idempotent persistence operations**

`CivilServiceExamPersistence` must expose `TryCreateSession`, `InsertCandidates`, `LoadDueSession`, `LoadCandidatesPage`, `CommitCandidateBatch`, `CompleteStage`, `FinalizeRanking`, `CancelActiveSession`, and `LoadLatestQualification`. Use explicit columns and parameters. The stage batch transaction updates candidate results and session cursor atomically; retrying a committed batch must be a no-op.

- [ ] **Step 5: Run the guard and commit**

```powershell
git add Code/core/db/CivilServiceExamSessionTableItem.cs Code/core/db/CivilServiceExamCandidateTableItem.cs Code/core/db/LineageArchiveIndexRules.cs Code/core/court/CivilServiceExamPersistence.cs Tests/CivilServiceExamPersistenceSourceGuard.ps1
git commit -m "feat: persist civil service examinations"
```

### Task 3: Make Governor Rotation Atomic and Preserve Vacancy Fallback

**Files:**
- Modify: `Code/core/court/OfficialCareerStateService.cs`
- Modify: `Code/patch/AW_CityLeaderPatch.cs`
- Create: `Tests/OfficialCirculationAtomicSourceGuard.ps1`

- [ ] **Step 1: Write failing guards for the two current root causes**

Require `CourtService.HasNineRankSystem` in both files, reject the old `HasOfficialCourt` rotation gate, reject `removeLeader()` before the complete plan is validated, and require a local acting fallback when no foreign qualified candidate exists.

- [ ] **Step 2: Run the guard and confirm RED**

Expected: current code still enables circulation at official-court unlock and removes leaders before finding destinations.

- [ ] **Step 3: Replace release-then-search with plan-validate-commit**

Build `GovernorRotationFacts` for due governors and cities, call `TryBuildRotationPlan`, resolve every live Actor and City again, then commit all leader changes. If preflight or any appointment fails, restore the prior leader projections and leave term rows unchanged; on no plan, renew the existing assignment for one year.

- [ ] **Step 4: Restore safe city vacancy behavior**

When Nine-Rank circulation is enabled but there is no qualified foreign candidate, choose a local educated candidate and project a one-year acting governorship instead of returning with an empty city. Acting officials receive no automatic rank floor and remain replaceable on the next annual pass.

- [ ] **Step 5: Run focused rules, guard, and commit**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --civil-service-exam-slice
powershell -ExecutionPolicy Bypass -File Tests/OfficialCirculationAtomicSourceGuard.ps1
git add Code/core/court/OfficialCareerStateService.cs Code/patch/AW_CityLeaderPatch.cs Tests/OfficialCirculationAtomicSourceGuard.ps1
git commit -m "fix: rotate governors atomically after nine rank"
```

### Task 4: Enforce Career Ladders Without Emptying Existing Courts

**Files:**
- Create: `Code/core/court/CivilServiceQualificationService.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/court/OfficialCareerService.cs`
- Modify: `Code/core/court/OfficialCareerStateService.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Create: `Tests/CivilServiceCareerGateSourceGuard.ps1`

- [ ] **Step 1: Write failing appointment-gate tests and guards**

Cover: pre-tech behavior unchanged; post-tech new civil appointments require education and a state-valid qualification; incumbents are grandfathered; rulers, heirs, feudatory princes, canonical masters, and military offices keep their explicit exemptions; unqualified candidates can only be acting officials; no call site uses `ApplyOfficeRankFloor` to promote into an office.

- [ ] **Step 2: Run and confirm RED**

Run the civil-service rules slice plus `CivilServiceCareerGateSourceGuard.ps1`.

- [ ] **Step 3: Implement qualification projection and repair**

Add actor keys for qualification, issuing kingdom, session, result year, and entry bonus. `CivilServiceQualificationService` reads the projection first, validates it against the kingdom, and repairs it from `CivilServiceExamPersistence.LoadLatestQualification` when absent or stale.

- [ ] **Step 4: Apply gates at candidate discovery and appointment commit**

Both automatic and manual paths must call the same `CanReceiveFormalCivilAppointment`. Revalidate at commit time. Replace office-rank flooring with `ResolveInitialAppointmentRank` for first appointments and reject middle/high appointments without required service history.

- [ ] **Step 5: Run tests and commit**

```powershell
git add Code/core/court Code/core/lineage/LineageKeys.cs Tests/CivilServiceCareerGateSourceGuard.ps1
git commit -m "feat: enforce examination career ladder"
```

### Task 5: Schedule and Advance Persistent Exam Sessions

**Files:**
- Create: `Code/core/court/CivilServiceExamCandidateQuery.cs`
- Create: `Code/core/court/CivilServiceExamService.cs`
- Modify: `Code/core/policy/KingdomAnnualWorkService.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Modify: `Code/core/policy/RecentFeatureBenchmarkRules.cs`
- Modify: `Code/content/RecentFeatureBenchmarkContent.cs`
- Modify: `Code/core/policy/UpdateAgeBenchmarkRules.cs`
- Create: `Tests/CivilServiceExamRuntimeSourceGuard.ps1`

- [ ] **Step 1: Write failing runtime guards**

Require annual work to call only `OnKingdomYear`, authority work to call `ProcessAuthorityCycle`, reset/load hooks to rebuild the due queue and qualification projections, an eight-candidate page limit, a 96-candidate source limit, and named annual/runtime benchmark categories. Reject exam calls from actor update, UI `Update`, and `MapBox.Update` postfixes.

- [ ] **Step 2: Run guard and confirm RED**

- [ ] **Step 3: Implement bounded candidate discovery**

Query existing officer, actor archive, school membership, and academy indexes. Merge and deduplicate IDs, resolve at most 96 live actors, classify social origin from current traits plus archive lineage, require `HistoricalSchoolEducationService.IsEducated`, and persist immutable snapshots. Never iterate `World.world.units`.

- [ ] **Step 4: Implement session lifecycle and deterministic scoring**

`OnKingdomYear` creates only due sessions after tech completion. `ProcessAuthorityCycle` takes one due session, scores at most eight candidates, commits the batch, and schedules the next stage. Imperial sessions run local, metropolitan, palace, ranking; tribute sessions run prefectural, national. Stage mode is frozen at creation.

```csharp
public static void ProcessAuthorityCycle()
{
    long day = CurrentWorldDay();
    if (!TryTakeDueSession(day, out long sessionId)) return;
    ProcessCandidateBatch(sessionId,
        CivilServiceExamRules.AuthorityCandidateBudget, day);
}
```

- [ ] **Step 5: Implement recovery and cancellation**

On load, rebuild only active due sessions and qualification projections. On ruler death continue under the successor; on kingdom destruction cancel the active session; on candidate death or emigration mark absence; on title changes preserve current mode and recalculate only the next session.

- [ ] **Step 6: Run runtime guard, rules slice, build, and commit**

```powershell
powershell -ExecutionPolicy Bypass -File Tests/CivilServiceExamRuntimeSourceGuard.ps1
& "C:\Program Files\dotnet\dotnet.exe" build AncientWarfare3.csproj -c Debug
git add Code/core/court/CivilServiceExamCandidateQuery.cs Code/core/court/CivilServiceExamService.cs Code/core/policy/KingdomAnnualWorkService.cs Code/core/performance/AWAuthorityCycleService.cs Code/core/multiplayer/AW3RuntimeRestorePipeline.cs Code/core/policy/RecentFeatureBenchmarkRules.cs Code/content/RecentFeatureBenchmarkContent.cs Code/core/policy/UpdateAgeBenchmarkRules.cs Tests/CivilServiceExamRuntimeSourceGuard.ps1
git commit -m "feat: run persistent examination sessions"
```

### Task 6: Add Examination Technology and AI Research

**Files:**
- Modify: `Code/content/policies/KingdomPolicyDefs.cs`
- Modify: `Code/core/policy/KingdomPolicyTechOrderRules.cs`
- Modify: `Code/core/policy/KingdomPolicyAI.cs`
- Modify: `Locales/aw3_court.csv`
- Create: `Tests/CivilServiceExamPolicySourceGuard.ps1`

- [ ] **Step 1: Add failing policy assertions**

Assert that the new ID is ordered after Nine-Rank, requires Nine-Rank, is considered only after Nine-Rank, and does not become a prerequisite of Three Departments. Require Chinese, English, and traditional Chinese text.

- [ ] **Step 2: Run and confirm RED**

- [ ] **Step 3: Add the policy node and AI score**

```csharp
new KingdomPolicyDef
{
    Id = "aw_tech_civil_service_examination",
    Kind = PolicyNodeKind.Tech,
    NameKey = "aw_tech_civil_service_examination",
    DescKey = "aw_tech_civil_service_examination_desc",
    FallbackName = "贡举制度",
    RequiredTechs = new[] { "aw_tech_nine_rank_system" },
    Cost = 90f,
    Column = 3,
    Row = 3
}
```

AI score increases for large courts, vacancies, educated unqualified candidates, and imperial/mandate status, but the tech remains one candidate among available research.

- [ ] **Step 4: Run policy guard and commit**

```powershell
git add Code/content/policies/KingdomPolicyDefs.cs Code/core/policy/KingdomPolicyTechOrderRules.cs Code/core/policy/KingdomPolicyAI.cs Locales/aw3_court.csv Tests/CivilServiceExamPolicySourceGuard.ps1
git commit -m "feat: add examination policy research"
```

### Task 7: Record Examination History Without Flooding the Database

**Files:**
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Code/core/lineage/ChronicleKeys.cs`
- Modify: `Code/core/court/OfficialCareerBiographyRules.cs`
- Modify: `Locales/aw3_court.csv`
- Create: `Tests/CivilServiceExamHistorySourceGuard.ps1`

- [ ] **Step 1: Write failing history guards**

Require country events for opening/final results and person events for qualifications, top three, and first formal appointment. Reject per-person failure history writes. Require all locale keys in three languages.

- [ ] **Step 2: Run and confirm RED**

- [ ] **Step 3: Add idempotent history calls at committed boundaries**

History methods receive snapshot names and target IDs so dead or migrated candidates retain accurate records. Call them only after persistence reports a newly committed state transition.

- [ ] **Step 4: Run guard and commit**

```powershell
git add Code/core/lineage/ChronicleEvents.cs Code/core/lineage/ChronicleKeys.cs Code/core/court/OfficialCareerBiographyRules.cs Locales/aw3_court.csv Tests/CivilServiceExamHistorySourceGuard.ps1
git commit -m "feat: record examination history"
```

### Task 8: Build the Examination Read Model and Window

**Files:**
- Create: `Code/core/court/CivilServiceExamReadModel.cs`
- Create: `Code/ui/items/CivilServiceExamCandidateRow.cs`
- Create: `Code/ui/windows/CivilServiceExamWindow.cs`
- Modify: `Code/ui/windows/CourtWindow.cs`
- Modify: `Code/ui/AW_LineageWindowIds.cs`
- Modify: `Locales/aw3_court.csv`
- Modify: `Tests/WindowUiRegressionTests.ps1`
- Create: `Tests/CivilServiceExamUiSourceGuard.ps1`

- [ ] **Step 1: Write failing UI guards**

Require the court entry button, dynamic “科举/贡举” label, disabled prerequisite tooltip, same default and minimum size as CourtWindow, permanent scrollbar, stage tabs, detached candidate rows, palace top-three controls, history tab, and a return-to-court button. Reject `World.world.units`, candidate discovery, and database writes from UI files.

- [ ] **Step 2: Run both UI guards and confirm RED**

- [ ] **Step 3: Implement detached read models and pooled candidate rows**

Read sessions and candidate snapshots with explicit limits. Resolve a live portrait when available; otherwise use archived species/skin/head data. Reuse row objects and render at most eight portraits per frame.

- [ ] **Step 4: Implement the resizable examination window**

Use `WideWindowChrome`, court dimensions, high-contrast bands, stable row heights, and a visible scrollbar. The ranking action accepts only current palace finalists and calls an idempotent service command; AI and read-only realms do not expose editable controls.

- [ ] **Step 5: Add the court entry and localization**

Court summary gets a compact button beside “返回国家”. It remains visible when locked; its tooltip states the exact missing technology. `CivilServiceExamWindow.BackToCourt` calls `CourtWindow.Open(_kingdomId)`.

- [ ] **Step 6: Run UI guards and commit**

```powershell
powershell -ExecutionPolicy Bypass -File Tests/CivilServiceExamUiSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests/WindowUiRegressionTests.ps1
git add Code/core/court/CivilServiceExamReadModel.cs Code/ui/items/CivilServiceExamCandidateRow.cs Code/ui/windows/CivilServiceExamWindow.cs Code/ui/windows/CourtWindow.cs Code/ui/AW_LineageWindowIds.cs Locales/aw3_court.csv Tests/WindowUiRegressionTests.ps1 Tests/CivilServiceExamUiSourceGuard.ps1
git commit -m "feat: add examination window"
```

### Task 9: Full Regression, Build, Deployment, and Runtime Acceptance

**Files:**
- Modify only if verification exposes defects in files from Tasks 1-8.

- [ ] **Step 1: Run all focused verification**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --civil-service-exam-slice
powershell -ExecutionPolicy Bypass -File Tests/CivilServiceExamPersistenceSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests/OfficialCirculationAtomicSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests/CivilServiceCareerGateSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests/CivilServiceExamRuntimeSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests/CivilServiceExamPolicySourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests/CivilServiceExamHistorySourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests/CivilServiceExamUiSourceGuard.ps1
```

Expected: every command exits 0.

- [ ] **Step 2: Run broad source guards and both builds**

```powershell
powershell -ExecutionPolicy Bypass -File Tests/SourceGuardTests.ps1
& "C:\Program Files\dotnet\dotnet.exe" build AncientWarfare3.csproj -c Debug
& "C:\Program Files\dotnet\dotnet.exe" build AncientWarfare3.csproj -c Release
```

Expected: source guards pass; both builds report 0 warnings and 0 errors.

- [ ] **Step 3: Deploy only after all verification is green**

Use the repository’s existing F-drive deployment script or documented copy procedure. Do not write to the D-drive installation until this step and do not delete unrelated game files.

- [ ] **Step 4: Run real-game acceptance for at least six world years**

Verify two exam cycles, all three social origins, imperial and non-imperial labels, qualification persistence across save/load, player palace ranking, AI ranking, first appointments, at least one successful multi-city governor rotation, and an unplannable rotation that preserves all incumbents. Inspect `Player.log` for compile, SQLite, async-write, duplicate-session, and UI exceptions, and compare annual/actor/recent-runtime benchmarks for new spikes.

- [ ] **Step 5: Commit any verification fixes and record evidence**

```powershell
git status --short
git diff --check
git log -10 --oneline
```

Do not mark the goal complete until the focused tests, broad guards, Debug/Release builds, deployment, save/load, two-cycle runtime test, and log inspection all have current evidence.

### Task 10: Drive Admission by Court Vacancies and Render Real Stage Rosters

**Files:**
- Modify: `Code/core/court/CivilServiceExamRules.cs`
- Modify: `Code/core/court/CivilServiceExamService.cs`
- Modify: `Code/core/db/CivilServiceExamSessionTableItem.cs`
- Modify: `Code/core/court/CivilServiceExamPersistence.cs`
- Modify: `Code/core/court/CivilServiceExamReadModel.cs`
- Modify: `Code/ui/windows/CivilServiceExamWindow.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CivilServiceExamRulesTests.cs.txt`
- Modify: `Tests/CivilServiceExamRuntimeSourceGuard.ps1`
- Modify: `Tests/CivilServiceExamUiSourceGuard.ps1`

- [ ] **Step 1: Write failing vacancy-quota and stage-roster tests**

Cover zero vacancies retaining one reserve graduate, increasing vacancies increasing final seats, preliminary stages using four-times and two-times funnels, hard city-scale caps, and score failures never passing merely to fill seats. Add UI rule assertions that later-stage rosters require evidence of prior-stage passage or legal advancement.

- [ ] **Step 2: Run the focused tests and confirm RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --civil-service-exam-slice
```

Expected: compile failure because vacancy-quota and stage-roster rule methods do not exist.

- [ ] **Step 3: Persist a frozen demand snapshot per sitting**

Add central vacancy count, city vacancy count, and final admission quota to the session schema. Populate them when the session is created from bounded court and city indexes. Existing rows receive safe defaults and retain their historical results.

- [ ] **Step 4: Apply vacancy-aware stage quotas**

Resolve final seats as `min(finalStageCap, vacancies + max(1, ceil(vacancies / 4)))`. Resolve local or prefectural seats as `min(existingStageCap, finalSeats * 4)` and metropolitan seats as `min(existingStageCap, finalSeats * 2)`. Continue requiring the 60-point pass mark.

- [ ] **Step 5: Filter every stage tab by actual participation**

Move stage-roster predicates into `CivilServiceExamRules` so they are unit tested. Local and prefectural tabs require that stage's score; metropolitan and national tabs require prior-stage passage or persisted advancement; palace requires metropolitan passage and `gongshi` status. The all/history tabs remain unchanged.

- [ ] **Step 6: Verify, build, deploy, and run two sittings**

Run the focused Rules slice and all civil-service source guards, build Debug and Release, deploy while preserving `.runtime`, load the newest autosave, and advance through at least one imperial and one tributary sitting. Verify candidate counts, different stage rosters, vacancy-linked final graduates, office filling, localized history, and absence of SQLite/runtime errors.

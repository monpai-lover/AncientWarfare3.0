# Relaxed Historical School Admission Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Increase bounded annual historical-school admission throughput for nobles and officials while preserving strict commoner admission and one-candidate-per-frame scheduling.

**Architecture:** Keep the existing `HistoricalSchoolEliteEnrollmentService` pipeline unchanged and alter only the capacities owned by `HistoricalSchoolEliteEnrollmentRules`. Extend the isolated rule executable and source guard first so the new values, priority behavior, and frame budgets are executable requirements before production constants change.

**Tech Stack:** C# 10/.NET 9 isolated rule executable, PowerShell source guards, Unity/WorldBox mod project, Harmony-integrated AW3 runtime.

---

### Task 1: Lock the relaxed capacities in failing tests

**Files:**
- Modify: `Tests/HistoricalSchoolEliteEnrollmentSlice/Program.cs`
- Modify: `Tests/HistoricalSchoolEliteEnrollmentSourceGuard.ps1`
- Test: `Tests/HistoricalSchoolEliteEnrollmentSlice/HistoricalSchoolEliteEnrollmentSlice.csproj`

- [ ] **Step 1: Update the isolated rule expectations**

Replace the old capacity assertions in `Program.cs` with exact new limits:

```csharp
Equal(6, HistoricalSchoolEliteEnrollmentRules
        .MaxSuccessfulJoinsPerRealmPerYear,
    "each realm has a six-student continuity floor");
Equal(16, HistoricalSchoolEliteEnrollmentRules
        .MaxSuccessfulJoinsPerRealmHardCap,
    "dynamic realm admissions retain a sixteen-student hard cap");
Equal(24, HistoricalSchoolEliteEnrollmentRules
        .MaxCandidateAttemptsPerRealmPerYear,
    "each realm may attempt twenty-four bounded candidates");
Equal(24, HistoricalSchoolEliteEnrollmentRules
        .MaxNobleArchiveRowsPerRealmYear,
    "noble archive recovery inspects twenty-four rows");
Equal(24, HistoricalSchoolEliteEnrollmentRules
        .MaxAcademyResidentsPerYear,
    "each academy inspects twenty-four residents");
Equal(2, HistoricalSchoolEliteEnrollmentRules
        .MaxCommonerAdmissionsPerAcademyYear,
    "each academy still admits at most two commoners per year");
Equal(6, HistoricalSchoolEliteEnrollmentRules
        .RealmSuccessfulJoinLimit(0, 0),
    "a realm retains the six-student continuity floor");
Equal(14, HistoricalSchoolEliteEnrollmentRules
        .RealmSuccessfulJoinLimit(8, 2),
    "unchanged teacher and academy bonuses raise a realm to fourteen");
Equal(14, HistoricalSchoolEliteEnrollmentRules
        .RealmSuccessfulJoinLimit(100, 100),
    "bounded bonuses cannot exceed fourteen with the current formula");
```

Change the initial independent-budget assertion from six selected candidates to seven because realm 1 may now take all five unique elites and realm 2 may take both elites:

```csharp
Equal(7, selected.Count,
    "each realm has an independent relaxed enrollment budget");
```

Expand the crowded-realm fixture to 22 nobles plus two academy commoners, request 24 slots, and assert all 24 are selected while exactly two are commoners:

```csharp
var crowdedRealm = new List<HistoricalSchoolEliteCandidate>();
for (int actor = 0; actor < 22; actor++)
    crowdedRealm.Add(Candidate(8, 800 + actor,
        HistoricalSchoolElitePriority.UntitledNoble));
crowdedRealm.Add(Candidate(8, 900,
    HistoricalSchoolElitePriority.AcademyCommoner));
crowdedRealm.Add(Candidate(8, 901,
    HistoricalSchoolElitePriority.AcademyCommoner));
IReadOnlyList<HistoricalSchoolEliteCandidate> crowdedSelected =
    HistoricalSchoolEliteEnrollmentRules.SelectCandidates(
        crowdedRealm, pYear: 8, pPerRealmLimit: 24);
Equal(24, crowdedSelected.Count,
    "the relaxed candidate budget can select twenty-four actors");
```

Keep the existing `FrameAttemptBudget` and `RealmPreparationBudget` assertions at one.

- [ ] **Step 2: Add exact numeric and scheduling checks to the source guard**

Read the rules source next to the service source:

```powershell
$rulesPath = Join-Path $repo `
    'Code\core\schools\HistoricalSchoolEliteEnrollmentRules.cs'
$rules = Get-Content -Raw $rulesPath
```

Add exact checks for the approved constants and frame budgets:

```powershell
foreach ($required in @(
    'MaxSuccessfulJoinsPerRealmPerYear = 6;',
    'MaxSuccessfulJoinsPerRealmHardCap = 16;',
    'MaxCandidateAttemptsPerRealmPerYear = 24;',
    'MaxNobleArchiveRowsPerRealmYear = 24;',
    'MaxAcademyResidentsPerYear = 24;',
    'MaxCommonerAdmissionsPerAcademyYear = 2;',
    'return pRemainingCandidates > 0 ? 1 : 0;',
    'return pRemainingRealms > 0 ? 1 : 0;')) {
    if ($rules -notmatch [regex]::Escape($required)) {
        throw "Relaxed school admission rule is missing: $required"
    }
}
```

- [ ] **Step 3: Run the isolated test and prove it is red**

Run:

```powershell
dotnet run --project Tests/HistoricalSchoolEliteEnrollmentSlice/HistoricalSchoolEliteEnrollmentSlice.csproj -c Debug --no-restore
```

Expected: FAIL because the production base join limit is still `4` rather than `6`.

- [ ] **Step 4: Run the source guard and prove it is red**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/HistoricalSchoolEliteEnrollmentSourceGuard.ps1
```

Expected: FAIL with `Relaxed school admission rule is missing` for one of the old constants.

- [ ] **Step 5: Commit the red tests**

```powershell
git add -- Tests/HistoricalSchoolEliteEnrollmentSlice/Program.cs Tests/HistoricalSchoolEliteEnrollmentSourceGuard.ps1
git commit -m "test: specify relaxed school admission limits"
```

### Task 2: Raise bounded admission throughput

**Files:**
- Modify: `Code/core/schools/HistoricalSchoolEliteEnrollmentRules.cs:45-51`
- Test: `Tests/HistoricalSchoolEliteEnrollmentSlice/Program.cs`
- Test: `Tests/HistoricalSchoolEliteEnrollmentSourceGuard.ps1`

- [ ] **Step 1: Change only the approved capacity constants**

Replace the constants with:

```csharp
public const int MaxSuccessfulJoinsPerRealmPerYear = 6;
public const int MaxSuccessfulJoinsPerRealmHardCap = 16;
public const int MaxCandidateAttemptsPerRealmPerYear = 24;
public const int MaxTeacherIdsPerSchool = 8;
public const int MaxNobleArchiveRowsPerRealmYear = 24;
public const int MaxAcademyResidentsPerYear = 24;
public const int MaxCommonerAdmissionsPerAcademyYear = 2;
```

Do not modify eligibility methods, teacher bonuses, commoner quota, frame attempt budget, realm preparation budget, or the service's persistence path.

- [ ] **Step 2: Run the isolated rules test**

Run:

```powershell
dotnet run --project Tests/HistoricalSchoolEliteEnrollmentSlice/HistoricalSchoolEliteEnrollmentSlice.csproj -c Debug --no-restore
```

Expected: PASS with `Historical school elite enrollment rules passed.`

- [ ] **Step 3: Run the source guard**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/HistoricalSchoolEliteEnrollmentSourceGuard.ps1
```

Expected: PASS with `Historical school elite enrollment source guard passed.`

- [ ] **Step 4: Commit the production change**

```powershell
git add -- Code/core/schools/HistoricalSchoolEliteEnrollmentRules.cs
git commit -m "feat: relax historical school admission throughput"
```

### Task 3: Verify education integration and production builds

**Files:**
- Verify: `Code/core/schools/HistoricalSchoolEliteEnrollmentRules.cs`
- Verify: `Code/core/schools/HistoricalSchoolEliteEnrollmentService.cs`
- Verify: `AncientWarfare3.csproj`

- [ ] **Step 1: Run focused education regression guards**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/HistoricalSchoolEducationDiscoveryTests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/HistoricalSchoolEducationJourneyTests.ps1
```

Expected: both scripts print their `passed` message and exit zero.

- [ ] **Step 2: Build the mod in Debug**

Run:

```powershell
dotnet build AncientWarfare3.csproj -c Debug --no-restore
```

Expected: exit zero with no compile errors.

- [ ] **Step 3: Build the mod in Release**

Run:

```powershell
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: exit zero with no compile errors.

- [ ] **Step 4: Check the scoped diff**

Run:

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; unrelated shared-worktree changes may remain, but the school-admission files are clean after their scoped commits.

### Task 4: Deploy for runtime acceptance

**Files:**
- Deploy source: `Code/core/schools/HistoricalSchoolEliteEnrollmentRules.cs`
- Deploy target: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0/Code/core/schools/HistoricalSchoolEliteEnrollmentRules.cs`

- [ ] **Step 1: Confirm WorldBox is not running**

Run:

```powershell
Get-Process -Name worldbox -ErrorAction SilentlyContinue
```

Expected: no process output. If WorldBox is running, do not hot-deploy and report the blocker.

- [ ] **Step 2: Copy the verified production rule file**

Run:

```powershell
Copy-Item -LiteralPath `
  'F:\WorldBox New Mod\AncientWarfare3.0\Code\core\schools\HistoricalSchoolEliteEnrollmentRules.cs' `
  -Destination `
  'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0\Code\core\schools\HistoricalSchoolEliteEnrollmentRules.cs' `
  -Force
```

Expected: command exits zero.

- [ ] **Step 3: Verify the deployed file is byte-identical**

Run:

```powershell
$source = Get-FileHash -Algorithm SHA256 `
  'F:\WorldBox New Mod\AncientWarfare3.0\Code\core\schools\HistoricalSchoolEliteEnrollmentRules.cs'
$target = Get-FileHash -Algorithm SHA256 `
  'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0\Code\core\schools\HistoricalSchoolEliteEnrollmentRules.cs'
if ($source.Hash -ne $target.Hash) { throw 'Deployed school rules differ from source.' }
```

Expected: exit zero with equal SHA-256 hashes.

- [ ] **Step 4: Record runtime acceptance criteria**

In a multi-year save, verify that rulers, heirs, feudatory princes, titled nobles, and officials acquire school identities more consistently; academy commoners remain rare; and the annual benchmark has no new single-frame spike. Runtime behavior cannot be proven by the isolated executable alone.

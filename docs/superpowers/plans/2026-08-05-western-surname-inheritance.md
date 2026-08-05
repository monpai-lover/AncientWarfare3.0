# Western Surname Inheritance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Western surnames survive loss of political status, inherit through the valid paternal or maternal branch, and repair affected living actors without changing dead archives.

**Architecture:** Put selection, founder-source, persistence, birth publication, and old-save repair in focused new files. Keep `LineageService.cs` untouched; use the existing `AW_BirthPatch.ApplyParentsMeta_Postfix` as the single ordered integration point. Persist before publishing live actor state and use stable actor/branch IDs for every choice.

**Tech Stack:** C# 9/net48 production code, .NET 9 rule executable, System.Data.SQLite, JSON name generators, PowerShell source guards.

---

## File Map

- `WesternSurnameInheritanceRules.cs`: pure parent/profile/existing-identity decision.
- `WesternSurnameSourceRules.cs`: deterministic 50/50 city versus word-library selection.
- `WesternSurnameBranchResolver.cs`: convert live/archive parent state into validated candidates.
- `WesternSurnameInheritancePersistence.cs`: transactional archive inheritance.
- `WesternSurnameBirthService.cs`: persistence-first live publication.
- `WesternSurnameRepairRules.cs` and `WesternSurnameRepairService.cs`: bounded legacy repair.
- `AWWesternFamilyNameRules.cs`: surname-visible commoner projection and token idempotence.
- `WesternFamilyIdentityRules.cs`: persisted `DISPLAY_STEM` authority.
- `WesternLineageAdmissionService.cs`: founder creation through the shared source rules.
- `WesternLineageMigrationService.cs`: delegate its existing lifecycle to the repair service.
- `AW_BirthPatch.cs`: the only birth integration edit.

### Task 1: Register A Fixed Test Slice

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/WesternSurnameInheritanceRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/WesternSurnameInheritancePersistenceSqlTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/WesternSurnameRepairRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Create runnable test shells**

```csharp
public static class WesternSurnameInheritanceRulesTests
{
    public static void Run() { }
}
public static class WesternSurnameInheritancePersistenceSqlTests
{
    public static void Run() { }
}
public static class WesternSurnameRepairRulesTests
{
    public static void Run() { }
}
```

- [ ] **Step 2: Register all three files and a `--western-surname-inheritance-slice` branch** that calls each `Run()` and prints `Western surname inheritance rules passed.`
- [ ] **Step 3: Run the empty slice**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --western-surname-inheritance-slice
```

Expected: exit `0` and the exact success line.

- [ ] **Step 4: Commit**

```powershell
git add Tests/AncientWarfare3.Rules.Tests
git commit -m "test: register western surname inheritance slice"
```

### Task 2: Make Persisted Surnames Visible To Commoners

**Files:**
- Modify: `Code/core/naming/AWWesternFamilyNameRules.cs`
- Modify: `Code/core/lineage/WesternFamilyIdentityRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternLineageNamingRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternFamilyIdentityRulesTests.cs.txt`

- [ ] **Step 1: Flip the old commoner assertions and add token cases**

```csharp
Equal("Alden de Rive", AWWesternFamilyNameRules.BuildActor(
    "Alden", "de Rive", noble: false), "commoners retain surnames");
Equal("Alden de Rive", AWWesternFamilyNameRules.BuildActor(
    "Alden de Rive", "de Rive", noble: false), "full token is idempotent");
Equal("婷婷 de Rive", AWWesternFamilyNameRules.BuildActor(
    "婷婷", "de Rive", noble: false), "given-name characters are preserved");
```

Add a branch test where persisted `rawDisplayStem="de Saved"` and city
`"Changed"`; expect `de Saved`. With an empty raw stem, expect city recovery.

- [ ] **Step 2: Run the Western naming slice and verify RED**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --western-lineage-naming-rules-slice
```

Expected: FAIL on commoner visibility or persisted-stem priority.

- [ ] **Step 3: Implement the minimal projection rules**

```csharp
public static string BuildActor(string given, string familyStem, bool noble)
{
    string family = NormalizeWhitespace(familyStem);
    string normalizedGiven = RemoveOneTrailingToken(
        NormalizeWhitespace(given), family);
    if (family.Length == 0) return normalizedGiven;
    return normalizedGiven.Length == 0
        ? family
        : normalizedGiven + " " + family;
}
```

In `ProjectBranch`, select a non-empty normalized `rawDisplayStem` before
rebuilding from tradition and city. Retain the existing fallback for legacy
empty values.

- [ ] **Step 4: Re-run the slice and verify PASS**
- [ ] **Step 5: Commit**

```powershell
git add Code/core/naming/AWWesternFamilyNameRules.cs Code/core/lineage/WesternFamilyIdentityRules.cs Tests/AncientWarfare3.Rules.Tests/WesternLineageNamingRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/WesternFamilyIdentityRulesTests.cs.txt
git commit -m "fix: display persisted western surnames for commoners"
```

### Task 3: Define Parent And Founder Selection

**Files:**
- Create: `Code/core/lineage/WesternSurnameInheritanceRules.cs`
- Create: `Code/core/lineage/WesternSurnameSourceRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternSurnameInheritanceRulesTests.cs.txt`
- Modify: test `.csproj`

- [ ] **Step 1: Add failing table-driven tests** for paternal preference,
  maternal fallback, profile mismatch, existing different branch preservation,
  deterministic city/word coverage, and empty-source fallback.

```csharp
var decision = WesternSurnameInheritanceRules.Resolve(
    childProfile: NamingProfileId.Western,
    existing: WesternSurnameBranchCandidate.None,
    paternal: Candidate(1, NamingProfileId.OrcNomadic),
    maternal: Candidate(2, NamingProfileId.Western));
Equal(2L, decision.Source.ShiId, "invalid paternal profile falls back");
Equal(WesternSurnameInheritanceAction.PreserveExisting,
    WesternSurnameInheritanceRules.Resolve(NamingProfileId.Western,
        Candidate(9, NamingProfileId.Western), Candidate(1), Candidate(2)).Action,
    "different stable child identity is never overwritten");
```

- [ ] **Step 2: Run the surname slice and verify compile failure** because the new types are absent.
- [ ] **Step 3: Implement the exact pure API**

```csharp
public enum WesternSurnameInheritanceAction { Reject, Inherit, PreserveExisting }
public readonly struct WesternSurnameBranchCandidate
{
    public long LineageId { get; }
    public long ShiId { get; }
    public NamingProfileId Profile { get; }
    public bool Complete { get; }
}
public static WesternSurnameInheritanceDecision Resolve(
    NamingProfileId childProfile, WesternSurnameBranchCandidate existing,
    WesternSurnameBranchCandidate paternal,
    WesternSurnameBranchCandidate maternal);
public static WesternSurnameSource ResolveFounderSource(
    long actorId, long cultureId, string cityStem, string wordStem);
```

Use `AWNamingSeedRules.Combine(actorId, cultureId)` and a parity bit for the
50/50 choice. Never use `Randy`, `UnityEngine.Random`, or `System.Random`.

- [ ] **Step 4: Run the surname slice and verify PASS**
- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/WesternSurname*Rules.cs Tests/AncientWarfare3.Rules.Tests/WesternSurnameInheritanceRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "feat: define western surname inheritance rules"
```

### Task 4: Persist Inheritance Atomically

**Files:**
- Create: `Code/core/lineage/WesternSurnameInheritancePersistence.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternSurnameInheritancePersistenceSqlTests.cs.txt`
- Modify: test `.csproj`

- [ ] **Step 1: Add in-memory SQLite tests** proving legal inheritance,
  profile rejection, existing-branch preservation, replay, rollback injection,
  unavailable DB failure, and common status preservation.

```csharp
var result = WesternSurnameInheritancePersistence.TryCommit(db,
    new WesternSurnameInheritanceRequest(childId: 12, sourceShiId: 4,
        expectedProfile: "western"));
Equal(WesternSurnameCommitStatus.Applied, result.Status, "first commit");
Equal(WesternSurnameCommitStatus.AlreadyApplied,
    WesternSurnameInheritancePersistence.TryCommit(db, request).Status,
    "replay is idempotent");
```

- [ ] **Step 2: Run the surname slice and verify RED**
- [ ] **Step 3: Implement `TryCommit` as one transaction** that rereads the
  source `ShiBranch`, verifies its profile, rereads the child archive, protects
  an existing complete different branch, and writes lineage/shi/family/clan,
  distance, status, naming profile, tradition, and display stem. Expose an
  internal `AfterActorWriteForTests` callback and roll back on its exception.
- [ ] **Step 4: Re-run and verify PASS**
- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/WesternSurnameInheritancePersistence.cs Tests/AncientWarfare3.Rules.Tests/WesternSurnameInheritancePersistenceSqlTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "feat: persist western surname inheritance atomically"
```

### Task 5: Resolve Parents And Publish Birth Identity

**Files:**
- Create: `Code/core/lineage/WesternSurnameBranchResolver.cs`
- Create: `Code/core/lineage/WesternSurnameBirthService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternSurnameInheritanceRulesTests.cs.txt`

- [ ] **Step 1: Add failing resolver/service tests** proving live and archive
  candidates require lineage, shi, branch profile, and display stem; persistence
  failure leaves all live keys unchanged.
- [ ] **Step 2: Run the surname slice and verify RED**
- [ ] **Step 3: Implement persistence-first orchestration**

```csharp
internal static WesternSurnameBirthResult TryInherit(
    Actor child, Actor parent1, Actor parent2)
{
    WesternSurnameInheritanceDecision decision =
        WesternSurnameBranchResolver.ResolvePreferred(child, parent1, parent2);
    if (decision.Action != WesternSurnameInheritanceAction.Inherit)
        return WesternSurnameBirthResult.FromDecision(decision);
    WesternSurnameCommitResult committed =
        WesternSurnameInheritancePersistence.TryCommit(
            LineageArchiveManager.Instance?.OperatingDB,
            WesternSurnameInheritanceRequest.From(child, decision.Source));
    if (!committed.AppliedOrExisting) return WesternSurnameBirthResult.Failed;
    Publish(child, committed.Identity);
    return WesternSurnameBirthResult.Committed;
}
```

`Publish` writes the structured lineage keys, then calls the existing display,
localized-name, archive-projection, and graphics-dirty boundaries.

- [ ] **Step 4: Re-run and verify PASS**
- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/WesternSurnameBranchResolver.cs Code/core/lineage/WesternSurnameBirthService.cs Tests/AncientWarfare3.Rules.Tests/WesternSurnameInheritanceRulesTests.cs.txt
git commit -m "feat: inherit western surnames at birth"
```

### Task 6: Integrate At The Single Birth Boundary

**Files:**
- Modify: `Code/patch/AW_BirthPatch.cs`
- Create: `Tests/WesternSurnameInheritanceSourceGuard.ps1`

- [ ] **Step 1: Write a source guard** requiring this textual order inside
  `ApplyParentsMeta_Postfix`:

```text
WesternSurnameBirthService.TryInherit
LineageService.ResolveBirthAdmissionDecision
WesternLineageParentEdgeService.RecordBirth
LineageService.OnActorBornWithParents
```

It must also reject a second `BabyHelper.applyParentsMeta` Harmony patch.

- [ ] **Step 2: Run the guard and verify RED**
- [ ] **Step 3: Integrate the committed result**

```csharp
WesternSurnameBirthResult surname =
    WesternSurnameBirthService.TryInherit(pBaby, pParent1, pParent2);
WesternLineageBirthAdmissionDecision decision =
    LineageService.ResolveBirthAdmissionDecision(pBaby, pParent1, pParent2);
bool effectiveFullPath = decision.UseFullPath || surname.Committed;
if (decision.UseLightweightEdges && !surname.Committed)
    WesternLineageParentEdgeService.RecordBirth(pBaby, pParent1, pParent2,
        pUseLightweightEdges: true);
LineageService.OnActorBornWithParents(pBaby, pParent1, pParent2,
    effectiveFullPath);
```

- [ ] **Step 4: Run the guard and surname slice; verify PASS**
- [ ] **Step 5: Commit**

```powershell
git add Code/patch/AW_BirthPatch.cs Tests/WesternSurnameInheritanceSourceGuard.ps1
git commit -m "fix: route ordinary western births through surname inheritance"
```

### Task 7: Add Stable Founder Surnames

**Files:**
- Modify: `name_generators/default/clans.json`
- Modify: `Code/core/lineage/WesternLineageAdmissionService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternLineageAdmissionRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternLineageAdmissionPersistenceSqlTests.cs.txt`
- Modify: `Tests/WesternLineageNamingSourceGuard.ps1`

- [ ] **Step 1: Add failing tests** proving deterministic city/word selection,
  persisted source stem on reload, real city origin persistence, and paternal
  relative selection.
- [ ] **Step 2: Run the admission slice and verify RED**
- [ ] **Step 3: Add the suffix-free generator object**

```json
{
  "id": "western_family_stem",
  "templates": [
    { "format": "{奇幻人类姓氏:family_name}" }
  ]
}
```

Generate both candidates, call `WesternSurnameSourceRules.ResolveFounderSource`,
persist the chosen value in `DISPLAY_STEM`, and keep the actual city in
`ORIGIN_CITY_CHINESE_NAME`. Make `FindRelative` use the shared branch resolver.

- [ ] **Step 4: Run admission and naming slices; verify PASS**
- [ ] **Step 5: Commit**

```powershell
git add name_generators/default/clans.json Code/core/lineage/WesternLineageAdmissionService.cs Tests/AncientWarfare3.Rules.Tests/WesternLineageAdmissionRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/WesternLineageAdmissionPersistenceSqlTests.cs.txt Tests/WesternLineageNamingSourceGuard.ps1
git commit -m "feat: create stable western founder surnames"
```

### Task 8: Repair Existing Living Descendants

**Files:**
- Create: `Code/core/lineage/WesternSurnameRepairRules.cs`
- Create: `Code/core/lineage/WesternSurnameRepairService.cs`
- Modify: `Code/core/lineage/WesternLineageMigrationService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternSurnameRepairRulesTests.cs.txt`
- Modify: persistence SQL tests and test `.csproj`

- [ ] **Step 1: Add failing tests** for strict actor-ID cursors, budget limits,
  resume, dead/vanished/profile-changed skips, different-branch preservation,
  rollback, second-run zero changes, and version publication only at completion.
- [ ] **Step 2: Run the surname slice and verify RED**
- [ ] **Step 3: Implement the bounded job**

```csharp
internal const string MigrationKey = "western_surname_inheritance";
internal const int MigrationVersion = 1;
internal static void Request();
internal static void Reset();
internal static void ProcessAuthorityCycle(int budget = 8);
```

Page `ActorArchive` joined to parent edges by child ID ascending, re-resolve the
live child and parents before every commit, reuse Task 4 persistence, and publish
only after commit. Let `WesternLineageMigrationService` delegate its existing
request/reset/cycle calls to this service; do not add a second global scheduler.

- [ ] **Step 4: Run the surname and Western admission slices; verify PASS**
- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/WesternSurnameRepairRules.cs Code/core/lineage/WesternSurnameRepairService.cs Code/core/lineage/WesternLineageMigrationService.cs Tests/AncientWarfare3.Rules.Tests/WesternSurnameRepairRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/WesternSurnameInheritancePersistenceSqlTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "fix: repair missing western surnames in old saves"
```

### Task 9: Verify The Western Surname Feature

**Files:**
- Modify: `Tests/WesternSurnameInheritanceSourceGuard.ps1`
- Test only otherwise

- [ ] **Step 1: Extend the guard** to require persistence-before-live-write,
  stable cursor and budget, persisted-stem priority, surname-visible commoners,
  and a suffix-free generator; reject `Randy`, `System.Random`, global Actor
  scans, and `LineageService.cs` changes in this feature commit range.
- [ ] **Step 2: Run all focused guards and suites**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --western-surname-inheritance-slice
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\WesternSurnameInheritanceSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\WesternLineageNamingSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\WesternLineageAdmissionSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\WesternLineageMigrationSourceGuard.ps1
```

Expected: all commands exit `0`.

- [ ] **Step 3: Run full verification**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Release --no-restore
git diff --check
```

Expected: rules pass, net48 build exits `0`, and no whitespace errors.

- [ ] **Step 4: Commit final guard changes**

```powershell
git add Tests/WesternSurnameInheritanceSourceGuard.ps1
git commit -m "test: guard western surname inheritance boundaries"
```

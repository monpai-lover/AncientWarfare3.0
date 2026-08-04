# Xiaization Naming And Name Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Separate culture-level Xia personal naming from kingdom-level institutional Xiaization and make surname/clan-name integration complete, branch-consistent, resumable, and idempotent.

**Architecture:** Introduce three independent authorities and three bounded migration services: level-5 kingdom institutions, integrated-culture actor naming, and policy-owned name-integration materialization. Convert Western stems once per source branch in a database transaction, then publish living actors. Keep dead archives and protected authored names unchanged.

**Tech Stack:** C# 11/net48, WorldBox culture/policy/court APIs, System.Data.SQLite, .NET 9 rules/SQL harness, AW authority-cycle and restore pipelines.

---

## Shared-File Order

Complete the Western surname plan before Tasks 12-13 here. Until then, do not
edit `AWWesternFamilyNameRules.cs`, `WesternFamilyIdentityRules.cs`,
`WesternLineageAdmissionService.cs`, `WesternLineageMigrationService.cs`, or
their tests. Only the final integration owner edits `AW_BirthPatch.cs`,
`LineageService.cs`, test `.csproj`, `Program.cs.txt`, `AWAuthorityCycleService`,
and `AW3RuntimeRestorePipeline`.

### Task 1: Separate The Three Authorities

**Files:**
- Create: `Code/core/policy/KingdomInstitutionalXiaizationRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/XiaizationAuthoritySeparationRulesTests.cs.txt`
- Modify: test `.csproj` and `Program.cs.txt`

- [ ] **Step 1: Add failing authority tests**

```csharp
Equal(true, KingdomInstitutionalXiaizationRules.ShouldUseXiaPersonalNaming(
    integrated: true, fullyIntegrated: false));
Equal(false, KingdomInstitutionalXiaizationRules.ShouldUseXiaInstitutions(4));
Equal(true, KingdomInstitutionalXiaizationRules.ShouldUseXiaInstitutions(5));
Equal(false, KingdomInstitutionalXiaizationRules.ShouldUseIntegratedSurname(
    kingdomIntegrated: false));
```

- [ ] **Step 2: Register `--xiaization-naming-transition`; run and verify compile failure**
- [ ] **Step 3: Implement the three pure predicates** exactly as shown, with
  institution threshold fixed at level 5.
- [ ] **Step 4: Re-run; expect `Xiaization naming transition rules passed.`**
- [ ] **Step 5: Commit `feat: separate Xiaization authorities`**

### Task 2: Select Institutional Profiles By Kingdom Level

**Files:**
- Modify: `Code/core/policy/KingdomPolicyProfileRules.cs`
- Modify: `Code/core/policy/KingdomPolicyProfileService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternPolicyProfileRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/KingdomInstitutionalXiaizationRulesTests.cs.txt`

- [ ] **Step 1: Add tests** where two kingdoms share an integrated culture but
  only the level-5 kingdom resolves Xia; stored Xia remains sticky only while
  level-5 authority remains true; common nodes remain available.
- [ ] **Step 2: Run `--western-policy-profile-slice`; verify RED**
- [ ] **Step 3: Change rule inputs from `fullyIntegratedCulture` to
  `institutionallyXiaized` and resolve it from
  `XiaizationService.GetLevel(kingdom) == 5` in `Resolve/EnsureAssigned`.
- [ ] **Step 4: Run policy-profile and Xiaization slices; verify PASS**
- [ ] **Step 5: Commit `fix: select Xia institutions by kingdom level`**

### Task 3: Persist The Level-5 Transition

**Files:**
- Create: `Code/core/db/KingdomInstitutionalXiaizationStateTableItem.cs`
- Create: `Code/core/policy/KingdomInstitutionalXiaizationService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/KingdomInstitutionalXiaizationPersistenceSqlTests.cs.txt`
- Modify: `Code/core/policy/KingdomPolicyService.cs`

- [ ] **Step 1: Add SQLite tests** for version/phase/cursor/failure state,
  current/completed/locked/decision-queue filtering, common-node retention,
  replay, and rollback-before-publish.
- [ ] **Step 2: Run Xiaization slice and verify RED**
- [ ] **Step 3: Implement phases**

```csharp
internal enum KingdomInstitutionalXiaizationPhase
{
    Prepared, PolicyMigrated, CourtRefreshing, Complete
}
internal static void Request(Kingdom kingdom);
internal static void ProcessAuthorityCycle(int budget = 8);
internal static void Reset();
```

`MigrateHotPolicyState` removes or archives Western-only current, completed,
locked, and queued decisions while retaining common IDs. Persist phase before
publishing each runtime step.

- [ ] **Step 4: Re-run SQL and policy tests; verify PASS**
- [ ] **Step 5: Commit `feat: persist institutional Xiaization transition`**

### Task 4: Refresh The Court During Transition

**Files:**
- Modify: `Code/core/court/CourtInstitutionService.cs`
- Modify: `Code/core/court/CourtService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/KingdomInstitutionalCourtTransitionRulesTests.cs.txt`

- [ ] **Step 1: Add failing tests** for bounded Western-office cleanup,
  immediate Xia office availability, faction-cache and dominant-school refresh,
  and unchanged diplomacy/war facts.
- [ ] **Step 2: Run Xiaization slice and verify RED**
- [ ] **Step 3: Let `CourtInstitutionService.GetInstitution/Refresh` consume
  the level-5 authority. During `CourtRefreshing`, call bounded officer
  validation, `RecalculateFactionCache`, and dominant-school refresh, then
  persist `Complete`.
- [ ] **Step 4: Re-run and verify PASS**
- [ ] **Step 5: Commit `fix: refresh Xia court after transition`**

### Task 5: Route Integrated Cultures To Xia Personal Naming

**Files:**
- Modify: `Code/core/naming/AWCultureNamingTraditionRules.cs`
- Modify: `Code/core/naming/AWCultureNamingTraditionService.cs`
- Create: `Code/core/lineage/IntegratedCultureNamingMigrationRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/IntegratedCultureNamingMigrationRulesTests.cs.txt`
- Modify: `Tests/WesternLineageNamingSourceGuard.ps1`

- [ ] **Step 1: Replace the old guard assertion and add tests** proving
  `Integrated=true,Fully=false` resolves Xia for actors/inheritance while the
  kingdom policy profile remains Western.
- [ ] **Step 2: Run integrated-naming and Xiaization slices; verify RED**
- [ ] **Step 3: Pass both culture traits into effective, inherited, and actor
  profile resolution and use Task 1's personal-naming predicate. Keep policy and
  court callers on the level-5 predicate.
- [ ] **Step 4: Re-run focused slices and source guard; verify PASS**
- [ ] **Step 5: Commit `fix: use Xia names for integrated cultures`**

### Task 6: Protect Authored Names During Migration

**Files:**
- Modify: `Code/core/naming/AWLocalizedNameService.cs`
- Modify: `Code/content/XiaNamingRepair.cs`
- Modify: `Code/core/lineage/IntegratedCultureNamingMigrationRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/IntegratedCultureNamingMigrationRulesTests.cs.txt`

- [ ] **Step 1: Add failing decision tests**

```csharp
Equal(RecordProfileOnly, Decide(alive: true, sameCulture: true,
    xiaProfile: true, alreadyXia: false, customName: true,
    authoredHistorical: false));
Equal(Skip, Decide(alive: false, sameCulture: true, xiaProfile: true,
    alreadyXia: false, customName: false, authoredHistorical: false));
```

Also test `figure`, `first`, and historical-master protection and no display
overwrite.

- [ ] **Step 2: Run Xiaization slice and verify RED**
- [ ] **Step 3: Centralize a protected-name predicate. `RecordProfileOnly`
  may persist profile/readiness metadata but must not call `setName` or replace
  `aw_chinese_name`. Apply the same protection to kingdom-name repair.
- [ ] **Step 4: Re-run and verify PASS**
- [ ] **Step 5: Commit `feat: protect names during Xia naming migration`**

### Task 7: Convert Western Branch Stems Deterministically

**Files:**
- Create: `Code/core/lineage/WesternStemConversionRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/WesternStemConversionRulesTests.cs.txt`

- [ ] **Step 1: Add failing tests** for `德/冯/范/迪`, spaces, `·`, `・`,
  first CJK extraction, no-CJK fallback, `shi != surname`, replay, and two
  source branches with the same visible surname remaining distinct.

```csharp
Equal("贾", WesternStemConversionRules.ResolveSurname(
    sourceShiId: 7, rawStem: "德·贾阿拉"));
Equal("明", WesternStemConversionRules.ResolveSurname(
    sourceShiId: 8, rawStem: "范 明洛斯"));
```

- [ ] **Step 2: Run Xiaization slice and verify RED**
- [ ] **Step 3: Implement `NormalizeCore`, `TryFirstCjk`,
  `ResolveSurname(long,string)`, and `ResolveShi(long,string)`. Strip only a
  recognized leading particle plus optional separator and use
  `AWNamingSeedRules.Combine` for fallback.
- [ ] **Step 4: Re-run and verify PASS**
- [ ] **Step 5: Commit `feat: convert Western branch stems deterministically`**

### Task 8: Create Or Reuse Xia Child Branches Transactionally

**Files:**
- Modify: `Code/core/lineage/XiaizedFamilyBranchTransitionPersistence.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/XiaizedBranchMigrationPersistenceSqlTests.cs.txt`

- [ ] **Step 1: Add SQLite tests** for child reuse, same-visible-surname branch
  separation, source/parent linkage, rollback, living-only rebind, and unchanged
  dead archives.
- [ ] **Step 2: Run Xiaization slice and verify RED**
- [ ] **Step 3: Add this transaction boundary**

```csharp
internal static XiaizedBranchCommitResult GetOrCreateXiaChildBranch(
    SQLiteConnection db, long sourceShiId, long parentShiId,
    long sourceLineageId, string surname, string shi);
```

Lookup by persisted source branch identity, not visible surname. Commit the new
branch and living archive rebind together; publish live actors only after
success.

- [ ] **Step 4: Re-run and verify PASS**
- [ ] **Step 5: Commit `feat: migrate living branches transactionally`**

### Task 9: Resume Culture-Scoped Actor Migration

**Files:**
- Create: `Code/core/db/CultureNamingMigrationStateTableItem.cs`
- Create: `Code/core/lineage/IntegratedCultureNamingMigrationService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/IntegratedCultureNamingMigrationPersistenceSqlTests.cs.txt`

- [ ] **Step 1: Add tests** for stable actor-ID cursor, budget 24, resume,
  failure retry, late arrival, live/culture revalidation, no-lineage naming-only
  identity, replica no-write, and repeat-run zero change.
- [ ] **Step 2: Run Xiaization slice and verify RED**
- [ ] **Step 3: Implement the bounded API**

```csharp
internal static void Request(Culture culture);
internal static void Reset();
internal static void ProcessAuthorityCycle(int budget = 24);
```

For Western/Orc branches call Task 8; for unlineaged actors persist only a Xia
localized naming identity. Recheck actor life/culture/profile immediately before
commit and publish.

- [ ] **Step 4: Re-run and verify PASS**
- [ ] **Step 5: Commit `feat: resume integrated culture naming migration`**

### Task 10: Define Branch-Aware Name Integration Materialization

**Files:**
- Create: `Code/core/lineage/NameIntegrationMaterializationRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/NameIntegrationMaterializationRulesTests.cs.txt`

- [ ] **Step 1: Add failing tests** for branch-clan, stable personal-clan,
  record-only protected actors, skip decisions, one clan per branch, no invented
  genealogy, and culture integration not satisfying marriage/history gates.
- [ ] **Step 2: Run Xiaization slice and verify RED**
- [ ] **Step 3: Implement a decision API**

```csharp
internal enum NameIntegrationAction
{
    Skip, MaterializeBranchClan, MaterializePersonalClan, RecordProfileOnly
}
internal static NameIntegrationDecision Decide(
    bool kingdomIntegrated, NamingProfileId profile, long shiId,
    bool protectedName, bool actorIntegrated);
```

Derive branch and personal clan seeds from stable IDs; do not use per-call
random clan generation.

- [ ] **Step 4: Re-run and verify PASS**
- [ ] **Step 5: Commit `feat: define name integration materialization rules`**

### Task 11: Replace The One-Shot Integration Effect

**Files:**
- Create: `Code/core/db/KingdomNameIntegrationMigrationStateTableItem.cs`
- Create: `Code/core/lineage/NameIntegrationMaterializationService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/NameIntegrationMaterializationPersistenceSqlTests.cs.txt`
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Modify: `Code/core/policy/KingdomPolicyInheritanceService.cs`
- Modify: `Code/core/lineage/KingdomIdentityContinuityService.cs`

- [ ] **Step 1: Add SQLite tests** for completed-policy/load repair, missing
  kingdom/actor flags, stable branch clan, replayed policy effects, rollback,
  cursor resume, and archive/live consistency.
- [ ] **Step 2: Run Xiaization slice and verify RED**
- [ ] **Step 3: Implement versioned request/reset/budget processing. Policy
  completion and inherited continuity set kingdom authority and request the job;
  the service writes branch/personal materialization in transactions and
  publishes actor flags only after commit.
- [ ] **Step 4: Re-run and verify PASS**
- [ ] **Step 5: Commit `fix: materialize branch-aware integrated names`**

### Task 12: Integrate Birth, Admission, And Existing Entry Points

**Files:**
- Modify: `Code/core/lineage/LineageService.cs`
- Modify: `Code/patch/AW_BirthPatch.cs`
- Modify: `Code/core/lineage/WesternLineageAdmissionService.cs`
- Modify: `Code/content/figures/HistoricalFigureService.cs`
- Create: `Tests/XiaizationNamingSourceGuard.ps1`

- [ ] **Step 1: Write a failing guard/tests** requiring integrated-culture
  ordinary births to take the full Xia naming path, name-integration
  materialization after birth/immigration/admission, and marriage/historical
  gates to read only kingdom policy state.
- [ ] **Step 2: Run guard and Xiaization slice; verify RED**
- [ ] **Step 3: Compose with the already-merged Western surname birth call**.
  `OnActorBorn` and admission decisions treat integrated actor culture as Xia
  naming authority. After identity publication, call
  `NameIntegrationMaterializationService.MaterializeActorIfRequired`.
  Replace `LineageService.ApplyNameIntegration`'s unbounded actor loop with
  kingdom-authority persistence plus `NameIntegrationMaterializationService.Request`.
  Do not replace or reorder the Western persistence-first inheritance boundary.
- [ ] **Step 4: Run Xiaization, Western surname, integrated naming, and
  Western admission slices; verify PASS**
- [ ] **Step 5: Commit `fix: repair Xia names at admission boundaries`**

### Task 13: Wire Scheduling, Restore, And Full Verification

**Files:**
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Modify: `Code/core/multiplayer/AW3WorldLoadCoordinator.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/XiaizationNamingSourceGuard.ps1`

- [ ] **Step 1: Add ordering/source tests** requiring archive/family readiness
  before migration request, migration completion before dependent official
  projection, host authority only, and reset of all three services.
- [ ] **Step 2: Run guard and verify RED**
- [ ] **Step 3: Wire bounded work**: institutional transition, integrated
  culture migration, and name-integration materialization all run through the
  existing authority-cycle gate. Restore incomplete rows after DB/family
  readiness in both load and generated-world pipelines. Do not mark persisted
  culture restoration complete when trait publication fails.
- [ ] **Step 4: Run focused verification**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --xiaization-naming-transition
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --integrated-naming-rules-slice
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --western-surname-inheritance-slice
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --western-lineage-admission-rules-slice
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --western-policy-profile-slice
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\XiaizationNamingSourceGuard.ps1
```

- [ ] **Step 5: Run full verification and commit**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Release --no-restore
git diff --check
git add Code Tests
git commit -m "chore: wire and verify Xiaization migrations"
```

Expected: all commands exit `0`; no war-type, participant, dead-archive,
protected-name, or unrelated kingdom-identity changes appear in the diff.

# Dynastic Male-Line Continuity And Guard Accession Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve every active male title line through a bounded, title-specific offspring-cap exception and ensure a registered heir immediately leaves the royal guard before eventual accession.

**Architecture:** Add one pure policy class and one rebuildable runtime eligibility index. The hot reproduction gates read the index and bypass only `Actor.hasReachedOffspringLimit`; existing world-law, meta-population, partner, fertility, nutrition, pregnancy-duration, safety, and 70/30 sex rules remain authoritative. Heir registration and accession use one idempotent royal-guard release operation, while ordinary guards remain excluded from succession.

**Tech Stack:** C# 10, Harmony patches, WorldBox `Actor`/`BabyHelper` APIs, SQLite-backed `Enfeoffment` authority with in-memory projections, PowerShell source guards, .NET rule-test projects.

---

### Task 1: Define The Male-Line Continuation Policy With Failing Tests

**Files:**
- Create: `Code/core/lineage/DynasticMaleLineContinuityRules.cs`
- Create: `Tests/DynasticMaleLineContinuityRulesTests.cs`
- Create: `Tests/DynasticMaleLineContinuityRulesTests.csproj`

- [ ] **Step 1: Create the rule-test project and write failing role/bypass tests**

Create a console test project that links the not-yet-created production rule file and asserts these exact cases:

```csharp
Equal(true, DynasticMaleLineContinuityRules.IsEligibleRole(
    isKing: false, isRegisteredHeir: false,
    isFeudatoryPrince: false, isFeudatorySuccessor: false,
    holdsActiveMaleTitle: true, isExpectedMaleTitleSuccessor: false));
Equal(true, DynasticMaleLineContinuityRules.IsEligibleRole(
    false, true, false, false, false, false));
Equal(true, DynasticMaleLineContinuityRules.IsEligibleRole(
    false, false, false, false, false, true));
Equal(false, DynasticMaleLineContinuityRules.IsEligibleRole(
    false, false, false, false, false, false));
Equal(true, DynasticMaleLineContinuityRules.ShouldBypassPersonalOffspringLimit(
    eligibleRole: true, alive: true, adult: true, breedingAge: true,
    canProduceBabies: true, hasLivingSon: false));
Equal(false, DynasticMaleLineContinuityRules.ShouldBypassPersonalOffspringLimit(
    true, true, true, true, true, hasLivingSon: true));
Equal(false, DynasticMaleLineContinuityRules.ShouldBypassPersonalOffspringLimit(
    eligibleRole: false, true, true, true, true, false));
```

The project must target `net9.0`, compile `DynasticMaleLineContinuityRulesTests.cs`, and link `../Code/core/lineage/DynasticMaleLineContinuityRules.cs`.

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet run --project Tests/DynasticMaleLineContinuityRulesTests.csproj -c Debug
```

Expected: build failure because `DynasticMaleLineContinuityRules.cs` and its API do not yet exist.

- [ ] **Step 3: Implement the minimal pure policy**

Create the class with these APIs and behavior:

```csharp
public static bool IsEligibleRole(bool isKing, bool isRegisteredHeir,
    bool isFeudatoryPrince, bool isFeudatorySuccessor,
    bool holdsActiveMaleTitle, bool isExpectedMaleTitleSuccessor)
{
    return isKing || isRegisteredHeir || isFeudatoryPrince ||
           isFeudatorySuccessor || holdsActiveMaleTitle ||
           isExpectedMaleTitleSuccessor;
}

public static bool ShouldBypassPersonalOffspringLimit(bool eligibleRole,
    bool alive, bool adult, bool breedingAge, bool canProduceBabies,
    bool hasLivingSon)
{
    return eligibleRole && alive && adult && breedingAge &&
           canProduceBabies && !hasLivingSon;
}

public static bool HasPersonalOffspringRoom(bool vanillaRoom,
    bool continuationBypass)
{
    return vanillaRoom || continuationBypass;
}
```

- [ ] **Step 4: Run the test and verify GREEN**

Run the command from Step 2. Expected: exit code 0 and `Dynastic male-line continuity rule tests passed.`

- [ ] **Step 5: Commit only the rule and its tests**

```powershell
git add Code/core/lineage/DynasticMaleLineContinuityRules.cs Tests/DynasticMaleLineContinuityRulesTests.cs Tests/DynasticMaleLineContinuityRulesTests.csproj
git commit -m "feat: define dynastic male-line continuity rules"
```

### Task 2: Build A Rebuildable Title-Holder And Successor Index

**Files:**
- Create: `Code/core/lineage/DynasticMaleLineContinuityService.cs`
- Modify: `Code/core/lineage/NobleRankService.cs`
- Modify: `Code/core/lineage/DynasticTitleService.cs`
- Modify: `Code/core/lineage/DynasticLivingSonIndexService.cs`
- Modify: `Code/patch/AW_NobleHeirPregnancyPatch.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Create: `Tests/DynasticMaleLineContinuitySourceGuard.ps1`

- [ ] **Step 1: Write a failing source guard for bounded runtime indexing**

Require all of the following exact integration points:

```powershell
Require-Text 'Code/core/lineage/NobleRankService.cs' `
  'DynasticMaleLineContinuityService.OnTitleProjectionChanged'
Require-Text 'Code/core/lineage/DynasticTitleService.cs' `
  'DynasticMaleLineContinuityService.OnChildBorn'
Require-Text 'Code/core/lineage/DynasticLivingSonIndexService.cs' `
  'DynasticMaleLineContinuityService.OnActorDying'
Require-Text 'Code/patch/AW_NobleHeirPregnancyPatch.cs' `
  'DynasticMaleLineContinuityService.OnActorLoaded'
Require-Text 'Code/core/performance/AWAuthorityCycleService.cs' `
  'DynasticMaleLineContinuityService.ProcessAuthorityCycle'
Require-Absent 'Code/core/lineage/DynasticMaleLineContinuityService.cs' `
  'OperatingDB'
Require-Absent 'Code/core/lineage/DynasticMaleLineContinuityService.cs' `
  'World.world.units.ToList'
```

- [ ] **Step 2: Run the guard and verify RED**

Run:

```powershell
pwsh -NoProfile -File Tests/DynasticMaleLineContinuitySourceGuard.ps1
```

Expected: failure on the first missing service integration.

- [ ] **Step 3: Implement the runtime index and event hooks**

The service owns only rebuildable state:

```csharp
private static readonly HashSet<long> ActiveMaleTitleHolders = new();
private static readonly HashSet<long> ExpectedMaleTitleSuccessors = new();
private static readonly Dictionary<long, long> SuccessorByHolder = new();
private static readonly Queue<long> DirtyHolders = new();
private static readonly HashSet<long> EnqueuedHolders = new();
public const int MaxHolderRefreshesPerCycle = 8;
```

Implement `HasEligibleRole(Actor)`, `NeedsContinuation(Actor)`, `OnTitleProjectionChanged(Actor)`, `OnChildBorn(Actor, Actor, Actor)`, `OnActorDying(Actor)`, `OnActorLoaded(Actor)`, `ProcessAuthorityCycle()`, and `Reset()`. `HasEligibleRole` combines current king, registered kingdom heir, active feudatory prince, registered feudatory successor, active male personal-title holder, and indexed expected successor. Refresh a dirty male title holder by enumerating only that holder's children and selecting the eldest eligible living male through `NobleRankRules.SelectEldestEligibleId`; remove the previous successor mapping before adding the new one.

Call `OnTitleProjectionChanged` after `NobleRankService.Project`, `ClearProjection`, committed inheritance, and revocation. Call `OnChildBorn` from `DynasticTitleService.OnChildBorn`, `OnActorDying` from the existing death path, and `OnActorLoaded` beside the existing pregnancy load hook in `AW_NobleHeirPregnancyPatch.ActorLoad_Postfix`. Process at most eight dirty holders in each authority cycle and reset all collections during world cleanup. Do not query SQLite from this service.

- [ ] **Step 4: Run the focused rules and source guard**

```powershell
dotnet run --project Tests/DynasticMaleLineContinuityRulesTests.csproj -c Debug
pwsh -NoProfile -File Tests/DynasticMaleLineContinuitySourceGuard.ps1
```

Expected: both commands exit 0.

- [ ] **Step 5: Commit the bounded index**

```powershell
git add Code/core/lineage/DynasticMaleLineContinuityService.cs Code/core/lineage/NobleRankService.cs Code/core/lineage/DynasticTitleService.cs Code/core/lineage/DynasticLivingSonIndexService.cs Code/patch/AW_NobleHeirPregnancyPatch.cs Code/core/performance/AWAuthorityCycleService.cs Tests/DynasticMaleLineContinuitySourceGuard.ps1
git commit -m "feat: index dynastic title successors"
```

### Task 3: Bypass Only The Personal Offspring Cap

**Files:**
- Modify: `Code/patch/AW_DynasticReproductionPatch.cs`
- Modify: `Code/core/lineage/NobleHeirPregnancyRules.cs`
- Modify: `Code/core/lineage/NobleHeirPregnancyService.cs`
- Modify: `Code/core/lineage/DynasticReproductionService.cs`
- Modify: `Tests/NobleHeirPregnancyRulesTests.cs`
- Modify: `Tests/DynasticReproductionCompatibilitySourceGuard.ps1`

- [ ] **Step 1: Add failing tests for personal-cap-only bypass**

Change `NobleHeirPregnancyRules.EvaluateRetry` to accept separate `pPersonalOffspringRoom`, `pPersonalOffspringLimitBypass`, and `pMetaLimitRoom` inputs. Add assertions proving: a qualified no-son line starts when vanilla personal room is false and bypass is true; an ordinary noble waits; a meta-population limit still waits; an existing son clears the request. Extend the source guard to require a Harmony transpiler on `BabyHelper.canMakeBabies` and to reject any write to `stats["offspring"]`.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet run --project Tests/NobleHeirPregnancyRulesTests.csproj -c Debug
pwsh -NoProfile -File Tests/DynasticReproductionCompatibilitySourceGuard.ps1
```

Expected: compile failure for the changed retry signature and source-guard failure for the missing `BabyHelper.canMakeBabies` hook.

- [ ] **Step 3: Wire the vanilla and AW3 gates to the same eligibility service**

Add a Harmony transpiler for `BabyHelper.canMakeBabies` that replaces only the call to `Actor.hasReachedOffspringLimit()` with:

```csharp
private static bool ReachedPersonalOffspringLimit(Actor actor)
{
    if (actor == null) return true;
    bool reached = actor.hasReachedOffspringLimit();
    if (!reached) return false;
    Actor partner = actor.lover;
    return !DynasticMaleLineContinuityService.NeedsContinuation(actor) &&
           !DynasticMaleLineContinuityService.NeedsContinuation(partner);
}
```

The transpiler must verify exactly one replacement and throw during patch installation if the vanilla method shape changed. Do not prefix-return from `canMakeBabies`, because adult, fertility, and nutrition checks must still execute.

In `NobleHeirPregnancyService.ProcessMother`, compute the values separately:

```csharp
bool personalRoom = !pMother.hasReachedOffspringLimit();
bool continuationBypass =
    DynasticMaleLineContinuityService.NeedsContinuation(pMother) ||
    DynasticMaleLineContinuityService.NeedsContinuation(father);
bool metaRoom = !BabyHelper.isMetaLimitsReached(pMother);
```

Pass all three to `EvaluateRetry`. Replace broad `LINEAGE_STATUS == NOBLE` retry eligibility with the unified continuation service for cap bypass; ordinary nobles may retain existing pregnancy timing only while they remain below the vanilla cap. Use the same service for reproduction decision weight and the existing 70/30 sex preference so the expected personal-title successor receives protection before inheritance.

- [ ] **Step 4: Verify focused GREEN and broad compatibility**

```powershell
dotnet run --project Tests/NobleHeirPregnancyRulesTests.csproj -c Debug
dotnet run --project Tests/NobleRemarriageRulesTests.csproj -c Debug
dotnet run --project Tests/DynasticMaleLineContinuityRulesTests.csproj -c Debug
pwsh -NoProfile -File Tests/DynasticReproductionCompatibilitySourceGuard.ps1
```

Expected: all four commands exit 0; ordinary-noble cap assertions remain limited while titled no-son assertions pass.

- [ ] **Step 5: Commit the fertility gate**

```powershell
git add Code/patch/AW_DynasticReproductionPatch.cs Code/core/lineage/NobleHeirPregnancyRules.cs Code/core/lineage/NobleHeirPregnancyService.cs Code/core/lineage/DynasticReproductionService.cs Tests/NobleHeirPregnancyRulesTests.cs Tests/DynasticReproductionCompatibilitySourceGuard.ps1
git commit -m "fix: preserve titled male succession lines"
```

### Task 4: Release A Registered Heir From Royal-Guard Service

**Files:**
- Modify: `Code/core/lineage/RoyalGuardOfficeRules.cs`
- Modify: `Code/core/lineage/RoyalGuardService.cs`
- Modify: `Code/core/lineage/HeirService.cs`
- Modify: `Code/core/lineage/AccessionIdentityService.cs`
- Modify: `Code/patch/AW_RoyalGuardPatch.cs`
- Modify: `Tests/RoyalGuardOfficeExclusivitySourceGuard.ps1`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Rewrite the old exclusivity assertions as failing heir-priority assertions**

Keep `CanBecomeSuccessionCandidate(true) == false` for ordinary guards. Add assertions that `CanEndLifetimeGuardService("became_heir")` and `CanEndLifetimeGuardService("became_king")` are true. Require `HeirService.StoreHeirSelection` to call `RoyalGuardService.ReleaseForRegisteredHeir`, require `AccessionIdentityService.Prepare` to call the same idempotent compatibility operation, and require royal-guard candidate selection to continue passing `isCurrentHeir` into `RoyalGuardSelectionRules.IsEligibleCore`.

- [ ] **Step 2: Run the guards and verify RED**

```powershell
pwsh -NoProfile -File Tests/RoyalGuardOfficeExclusivitySourceGuard.ps1
pwsh -NoProfile -File Tests/SourceGuardTests.ps1
```

Expected: failure because heir/accession reasons are not legal and the unified release operation is absent.

- [ ] **Step 3: Implement idempotent heir-priority cleanup**

Allow only `became_heir` and `became_king` in addition to the existing death/extinction reasons. Add:

```csharp
public static bool ReleaseForRegisteredHeir(Kingdom kingdom, Actor actor,
    string reason)
{
    if (kingdom?.data == null || actor?.data == null) return false;
    if (!IsRoyalGuard(actor)) return true;
    if (reason != "became_heir" && reason != "became_king") return false;
    DismissGuard(actor, reason);
    return !IsRoyalGuard(actor) && !actor.hasTrait(LineageKeys.TRAIT_GUARD) &&
           !HasGuardCitizenJob(actor) && !IsInGuardRoster(kingdom, actor) &&
           !IsAssignedToGuardArmy(actor);
}
```

Use existing citizen-job, roster, captain, army removal, and empty-army cleanup helpers rather than duplicating field writes. At the beginning of `RefreshHeirAndReturn`, release a currently stored heir carrying stale guard state before candidate evaluation. In `StoreHeirSelection`, call `ReleaseForRegisteredHeir(..., "became_heir")` before writing `KINGDOM_HEIR_ID`; if it fails, do not commit that candidate. In `AccessionIdentityService.Prepare`, if the actor is the kingdom's registered heir, call the same method with `became_king` before `CanReplaceLifetimeGuardIdentity`; non-registered guards still fail. Make the `AW_RoyalGuardPatch.SetKing_Prefix` compatibility gate accept only a guard that was successfully released as that kingdom's registered heir. Keep current-heir exclusion in `IsGuardCandidate`.

- [ ] **Step 4: Verify cleanup and succession guards GREEN**

```powershell
pwsh -NoProfile -File Tests/RoyalGuardOfficeExclusivitySourceGuard.ps1
pwsh -NoProfile -File Tests/SourceGuardTests.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Debug
```

Expected: both guards and the complete pure-rules suite exit 0.

- [ ] **Step 5: Commit the guard/heir transition**

```powershell
git add Code/core/lineage/RoyalGuardOfficeRules.cs Code/core/lineage/RoyalGuardService.cs Code/core/lineage/HeirService.cs Code/core/lineage/AccessionIdentityService.cs Code/patch/AW_RoyalGuardPatch.cs Tests/RoyalGuardOfficeExclusivitySourceGuard.ps1 Tests/SourceGuardTests.ps1
git commit -m "fix: release registered heirs from royal guard"
```

### Task 5: Regression Verification, Build, Deployment, And Three-Generation Test

**Files:**
- Verify only: all files from Tasks 1-4
- Deploy target: the installed WorldBox `Mods/AncientWarfare3.0` directory resolved by the repository's existing deployment command

- [ ] **Step 1: Run every focused test and guard**

```powershell
dotnet run --project Tests/DynasticMaleLineContinuityRulesTests.csproj -c Release
dotnet run --project Tests/NobleHeirPregnancyRulesTests.csproj -c Release
dotnet run --project Tests/NobleRemarriageRulesTests.csproj -c Release
pwsh -NoProfile -File Tests/DynasticMaleLineContinuitySourceGuard.ps1
pwsh -NoProfile -File Tests/DynasticReproductionCompatibilitySourceGuard.ps1
pwsh -NoProfile -File Tests/RoyalGuardOfficeExclusivitySourceGuard.ps1
pwsh -NoProfile -File Tests/SourceGuardTests.ps1
```

Expected: every command exits 0 without warning output.

- [ ] **Step 2: Run the full rule suite and build both configurations**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Debug --nologo
dotnet build AncientWarfare3.csproj -c Release --nologo
```

Expected: tests pass; Debug and Release builds finish with 0 errors. Record pre-existing warnings separately and do not claim they were introduced by this change without a baseline comparison.

- [ ] **Step 3: Inspect the scoped diff and whitespace**

```powershell
git diff --check -- Code/core/lineage/DynasticMaleLineContinuityRules.cs Code/core/lineage/DynasticMaleLineContinuityService.cs Code/core/lineage/NobleRankService.cs Code/core/lineage/DynasticTitleService.cs Code/core/lineage/DynasticLivingSonIndexService.cs Code/core/lineage/NobleHeirPregnancyRules.cs Code/core/lineage/NobleHeirPregnancyService.cs Code/core/lineage/DynasticReproductionService.cs Code/core/lineage/RoyalGuardOfficeRules.cs Code/core/lineage/RoyalGuardService.cs Code/core/lineage/HeirService.cs Code/core/lineage/AccessionIdentityService.cs Code/patch/AW_DynasticReproductionPatch.cs Code/patch/AW_NobleHeirPregnancyPatch.cs Code/patch/AW_RoyalGuardPatch.cs Tests/DynasticMaleLineContinuityRulesTests.cs Tests/DynasticMaleLineContinuityRulesTests.csproj Tests/DynasticMaleLineContinuitySourceGuard.ps1 Tests/NobleHeirPregnancyRulesTests.cs Tests/DynasticReproductionCompatibilitySourceGuard.ps1 Tests/RoyalGuardOfficeExclusivitySourceGuard.ps1 Tests/SourceGuardTests.ps1
```

Expected: no whitespace errors and no unrelated files in the scoped diff.

- [ ] **Step 4: Deploy only after WorldBox is closed and run an in-game acceptance pass**

Use the repository's established deployment script or command, then create a new world and verify: an ordinary five-child actor remains capped; a male title holder with only daughters continues ten-month pregnancies; the exception stops after a living son; that eldest living son seeks his own son before inheritance; a newly registered heir immediately loses guard trait/job/captain/army/roster; an ordinary guard cannot become heir; and a legacy-save registered heir with stale guard state can accede after cleanup.

- [ ] **Step 5: Commit any test-only corrections, then report evidence**

Stage only files listed in this plan. Report test commands, build results, deployed path, and the observed three-generation/guard state transitions. Do not stage or revert unrelated working-tree changes.

---

## Self-Review

- Spec coverage: Tasks 1-3 cover title-specific cap bypass, existing ten-month pregnancy, 70/30 preference, son-death recovery, expected personal-title successor, bounded indexing, and no SQLite hot-path reads. Task 4 covers immediate heir release, accession fallback, full guard cleanup, idempotence, and continued exclusion of ordinary guards. Task 5 covers focused, broad, build, deployment, and in-game verification.
- Scope exclusions: no `stats["offspring"]` mutation, no general noble/ordinary-person fertility increase, no world-law/meta-limit bypass, no new authoritative save columns, and no change to female title inheritance.
- Type consistency: `DynasticMaleLineContinuityRules`, `DynasticMaleLineContinuityService`, `ReleaseForRegisteredHeir`, `OnTitleProjectionChanged`, `ProcessAuthorityCycle`, and `Reset` use the same names in every task.
- Placeholder scan: the plan contains no TBD/TODO/later steps; every edit has an exact file, behavior, test command, and expected result.

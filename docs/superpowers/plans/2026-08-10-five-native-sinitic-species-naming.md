# Five Native Sinitic Species Naming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make dog, fox, lemon-person, rabbit, and turtle civilizations use their current word-library names with the monkey surname-first inheritance and genealogy lifecycle, without acquiring monkey content or Xia institutions.

**Architecture:** Add a distinct `NativeSinitic` naming profile and one exact species catalog. Route personal, city, and kingdom generation to each species' current generators, extract complete structured surname/given components from current templates, and share the monkey lineage lifecycle through semantic predicates. Existing saves repair identities lazily at existing actor/branch boundaries.

**Tech Stack:** C# 10, Harmony patches, WorldBox/NeoModLoader runtime APIs, AW3 integrated naming engine, SQLite lineage archive, .NET rule-test executable, PowerShell source guards.

---

### Task 1: Exact Species And Structured Name Rules

**Files:**
- Create: `Code/core/naming/AWNativeSiniticSpeciesRules.cs`
- Create: `Code/core/naming/AWNativeSiniticNamePartsRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/NativeSiniticNamingRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing exact-species tests**

Test `IsNativeSiniticSpecies` against all five exact ids and reject `dog`, `fox`, `rabbit`, `turtle`, `lemon_snail`, `miniciv_lemon_snail`, `civ_bear`, and `civ_monkey`.

- [ ] **Step 2: Write failing complete-name extraction tests**

Specify this API and cases:

```csharp
NativeSiniticNameParts parts = AWNativeSiniticNamePartsRules.Resolve(
    "山田太郎", "山田", "太");
Equal(true, parts.Valid);
Equal("山田", parts.FamilyName);
Equal("太郎", parts.GivenName);

parts = AWNativeSiniticNamePartsRules.Resolve("狐甲子", "狐", "");
Equal("甲子", parts.GivenName);
```

Reject empty family, family not at the visible prefix, and empty remaining given name.

- [ ] **Step 3: Run the focused test harness and verify RED**

Run the rule executable with a temporary early invocation of the new test group. Expected: compile failure because both production rule types are absent.

- [ ] **Step 4: Implement the minimal pure rules**

Use an ordinal exact-id set. `Resolve` trims the generated full name and family, verifies `StartsWith(family, Ordinal)`, removes exactly one prefix, trims separator whitespace, and returns an immutable valid/invalid result.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the new test group before the unrelated succession group. Expected: all new assertions pass.

- [ ] **Step 6: Commit**

```powershell
git add Code/core/naming/AWNativeSiniticSpeciesRules.cs Code/core/naming/AWNativeSiniticNamePartsRules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "test: define native Sinitic species name rules"
```

### Task 2: Persisted Naming Profile And Generator Routing

**Files:**
- Modify: `Code/core/naming/AWNamingProfileRules.cs`
- Modify: `Code/core/naming/AWCultureNamingTraditionRules.cs`
- Modify: `Code/core/naming/AWCultureNamingTraditionService.cs`
- Modify: `Code/core/naming/AWLocalizedNameProfileReadinessRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/NativeSiniticNamingRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternLineageNamingRulesTests.cs.txt`

- [ ] **Step 1: Add failing profile tests**

Require `NamingProfileId.NativeSinitic`, serialized as `native_sinitic`, to outrank orc/Western after Xia and monkey. Verify it never persists a Western tradition and survives effective, inherited, and actor-profile resolution.

- [ ] **Step 2: Add failing generator tests for all five species**

For each id require `civ_*_name`, `civ_*_city`, and `civ_*_kingdom`. Verify actor fallback remains the same species generator and never returns `western_*`, `human_name`, `Xia_name`, or `civ_monkey_name`.

- [ ] **Step 3: Verify RED**

Expected: missing enum/profile overload and wrong Western natural profile.

- [ ] **Step 4: Implement profile selection and persistence**

Add a `nativeSinitic` argument to `AWNamingProfileRules.Resolve`, update all callers, and add serialize/parse/readiness handling. `AWCultureNamingTraditionService` derives the flag only from `AWNativeSiniticSpeciesRules`.

- [ ] **Step 5: Implement species generator routing**

Add a native-Sinitic resolver that maps actor/city/kingdom to `pSpeciesId + suffix`; other object kinds retain their explicit generator. Fallback uses the same mapping and cannot cross profile.

- [ ] **Step 6: Run naming rule groups and verify GREEN**

Expected: native-Sinitic and existing Western/monkey/orc routing tests pass.

- [ ] **Step 7: Commit**

```powershell
git add Code/core/naming Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: add native Sinitic naming profile"
```

### Task 3: Runtime Structured Identity From Current Libraries

**Files:**
- Create: `Code/core/naming/AWNativeSiniticIdentityService.cs`
- Modify: `Code/core/naming/AWLocalizedNameService.cs`
- Modify: `Code/core/naming/AWActorInitialNameRules.cs`
- Modify: `Code/core/naming/ActorManualRenameService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/NativeSiniticNamingRulesTests.cs.txt`
- Create: `Tests/NativeSiniticNamingSourceGuard.ps1`

- [ ] **Step 1: Write failing source and identity tests**

Require all five current generator ids, forbid code-owned five-species surname/given arrays, and verify generated `family_name` plus complete generated name produces complete structured fields rather than the partial `given_name` tag.

- [ ] **Step 2: Verify RED**

Expected: runtime identity service and source contracts are missing.

- [ ] **Step 3: Implement identity capture**

At actor projection, native-Sinitic actors preserve family identity like monkey actors. Capture `family_name` from generated components, derive the complete given name through `AWNativeSiniticNamePartsRules`, and atomically write both localized and lineage structured fields.

- [ ] **Step 4: Implement inherited-family override**

When a valid parent family exists, keep the current generator for all words but replace only the generated family prefix in the final structured identity. Never alter the selected given-name words.

- [ ] **Step 5: Extend manual rename ordering**

Treat `NativeSinitic` like monkey/Xia for surname-first split editing while preserving the existing custom-name guard.

- [ ] **Step 6: Verify focused rules and source guard GREEN**

Expected: current word-library routes are present, no hard-coded pool exists, and complete Japanese/Shanhai template output is retained.

- [ ] **Step 7: Commit**

```powershell
git add Code/core/naming Tests
git commit -m "feat: persist native Sinitic structured names"
```

### Task 4: Monkey-Equivalent Birth And Genealogy Lifecycle

**Files:**
- Modify: `Code/core/lineage/WesternLineageEligibilityRules.cs`
- Modify: `Code/core/lineage/LineageService.cs`
- Modify: `Code/core/lineage/LineageArchiveWriter.cs`
- Modify: `Code/patch/AW_BirthPatch.cs`
- Modify: `Code/patch/AW_ClanEventPatch.cs`
- Modify: `Code/patch/AW_PromotionPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternLineageEligibilityRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternLineageAdmissionRulesTests.cs.txt`
- Modify: `Tests/NativeSiniticNamingSourceGuard.ps1`

- [ ] **Step 1: Write failing lifecycle rules**

Require `NativeSinitic` to use the full birth/archive path, never lightweight Western edges, and use the AW lineage system when a stable lineage id exists.

- [ ] **Step 2: Write failing source contracts**

Require birth, clan event, archive, promotion, and display paths to call a semantic native-Sinitic genealogy predicate. Forbid adding the five ids to monkey policy or biological-Xia sprite gates.

- [ ] **Step 3: Verify RED**

Expected: profile falls through Western admission or is absent from full birth path.

- [ ] **Step 4: Generalize the monkey lifecycle boundary**

Add `UsesNativeSiniticGenealogy(Actor)` for monkey plus the five-species profile. Use it only in naming, birth, lineage, archive, and family-tree admission. Keep `IsNativeXiaCultureActor` and institutional predicates unchanged.

- [ ] **Step 5: Generalize family identity initialization**

For the five species, resolve existing Shi/family first, otherwise obtain the current generator surname. Synchronize `family_name`, `chinese_family_name`, and initial `clan_name`; apply surname-first projection; archive before UI exposure.

- [ ] **Step 6: Verify lifecycle rules and source guards GREEN**

Expected: all five use full genealogy, monkey remains unchanged, Western actors retain their existing path, and policy/source exclusion guards pass.

- [ ] **Step 7: Commit**

```powershell
git add Code/core/lineage Code/patch Tests
git commit -m "feat: extend monkey genealogy lifecycle to native Sinitic species"
```

### Task 5: Lazy Existing-Save Repair

**Files:**
- Create: `Code/core/lineage/NativeSiniticIdentityMigrationRules.cs`
- Create: `Code/core/lineage/NativeSiniticIdentityMigrationService.cs`
- Modify: `Code/core/lineage/LineageService.cs`
- Modify: `Code/core/naming/AWLocalizedNameService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/NativeSiniticIdentityMigrationRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/NativeSiniticNamingSourceGuard.ps1`

- [ ] **Step 1: Write failing migration decision tests**

Cover complete identity reuse, protected custom/historical skip, legacy Western identity conversion, one branch surname reused by descendants, idempotent repeat, and failed-write retry.

- [ ] **Step 2: Verify RED**

Expected: migration rule/service types are absent.

- [ ] **Step 3: Implement pure migration decisions**

Return explicit `Skip`, `Reuse`, or `Repair` actions. A repair requires profile match, no protected name, and an incomplete or Western-shaped structured identity.

- [ ] **Step 4: Implement bounded event-driven migration**

Invoke repair only at actor naming, promotion/succession, and family-tree/branch admission boundaries. Resolve one surname per existing branch from founder/root stable seed, use the actor's current generator for missing given names, and commit through existing bounded/transactional writers.

- [ ] **Step 5: Add no-scan source guards**

Forbid update-loop registration, world actor enumeration, synchronous load-wide migration, and direct unbounded archive writes in the migration service.

- [ ] **Step 6: Verify migration tests and source guard GREEN**

Expected: repeat repair is a no-op, custom names survive, and no periodic scan exists.

- [ ] **Step 7: Commit**

```powershell
git add Code/core/lineage Code/core/naming Tests
git commit -m "feat: lazily repair native Sinitic saved identities"
```

### Task 6: Integrated Verification

**Files:**
- Modify only if a test reveals an in-scope defect.

- [ ] **Step 1: Run naming source guards**

```powershell
pwsh -File Tests/NativeSiniticNamingSourceGuard.ps1
pwsh -File Tests/XiaExpansionAndCivMonkeyNamingTests.ps1
pwsh -File Tests/CivMonkeyClanSurnameTests.ps1
```

Expected: all pass.

- [ ] **Step 2: Run the rule test executable**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Debug
```

Expected: all naming-related groups pass. Report separately if the pre-existing `ArchiveCandidateLossRescanIsBounded` failure at baseline commit `5fd1ff57` remains.

- [ ] **Step 3: Build the mod**

```powershell
dotnet build AncientWarfare3.csproj -c Debug
```

Expected: zero compile errors.

- [ ] **Step 4: Review diff for scope and resource authority**

Confirm no current five-species word-library content was replaced, no monkey policy/content leaked, no deployment files changed, and no unrelated succession fix was bundled.

- [ ] **Step 5: Commit any verification-only correction**

```powershell
git add <in-scope-files>
git commit -m "test: verify native Sinitic naming lifecycle"
```

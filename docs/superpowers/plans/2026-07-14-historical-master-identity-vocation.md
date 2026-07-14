# Historical Master Identity And Vocation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give all 84 historical school masters conservative surname/clan identities, stable founder-city clan names, visible identity rows, and a complete data-driven military-service gate with exactly 11 historically eligible masters.

**Architecture:** Keep the approved static roster in `HistoricalMasterIdentityRules`, split evidence and pure vocation types into focused files, and project the resolved definition through the existing atomic descent pipeline. Add one runtime adapter for Actor-to-definition resolution, then use it at vanilla Harmony boundaries and every AW3 military candidate source; no recruitment hot path reads SQLite or scans the world.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, Unity/WorldBox publicized APIs, System.Data.SQLite, NeoModLoader locale CSVs, existing focused harnesses under `F:/tmp`.

**Design reference:** `docs/superpowers/specs/2026-07-14-historical-master-identity-vocation-design.md`

**Execution constraint:** Work inline on `master`; do not create a branch or worktree. Do not add old-save migration. Do not push unless the user explicitly requests it.

---

## File Map

- Create `Code/content/schools/HistoricalMasterIdentity.cs`: evidence enum and immutable canonical identity value.
- Modify `Code/content/schools/HistoricalMasterIdentityRules.cs`: the approved 84-person identity/vocation data and pure clan-name formatter.
- Create `Code/content/schools/HistoricalMasterVocationRules.cs`: pure eight-context military matrix.
- Modify `Code/content/schools/HistoricalSchoolMasterDefinition.cs`: expose evidence and military eligibility.
- Modify `Code/content/schools/HistoricalSchoolMasterRegistry.cs`: pass identity metadata into every runtime definition and validate the 84/11/73 invariants.
- Modify `Code/core/schools/HistoricalMasterLineagePersistence.cs`: carry evidence and permit an empty surname only for a valid unknown identity.
- Modify `Code/core/schools/HistoricalSchoolDescentService.cs`: construct committed identities with evidence.
- Modify `Code/core/schools/HistoricalSchoolStore.cs`: compare evidence as part of the frozen descent request.
- Modify `Code/core/schools/HistoricalMasterIdentityProjection.cs`: project empty unknown surnames and enforce the founder-city clan title.
- Modify `Code/core/lineage/LineageService.cs`: use the shared founder-city formatter for canonical master clans.
- Modify `Code/patch/AW_UnitWindowPatch.cs`: always show canonical-master surname and branch rows.
- Modify `Locales/others.csv`: add the unknown-surname presentation key.
- Create `Code/core/schools/HistoricalMasterVocationService.cs`: O(1) runtime Actor/Army adapter over the pure matrix.
- Create `Code/patch/AW_HistoricalMasterVocationPatch.cs`: guard vanilla profession, warrior, army, captain, and army-creation boundaries.
- Modify `Code/core/lineage/AWArmyService.cs`: prevent rejected assignments from entering special-army unit lists.
- Modify `Code/core/lineage/RoyalGuardService.cs`: reject every canonical master before guard scoring and appointment.
- Modify `Code/core/lineage/SlaveService.cs`: reject every canonical master from slave-army combat roles.
- Modify `Code/core/lineage/MandateRebelService.cs`: reject every canonical master from levy mobilization.
- Modify `Code/core/lineage/MandateBorderDefenseService.cs`: admit only whitelisted canonical masters.
- Modify `Code/core/lineage/GeneralService.cs`: admit and retain only whitelisted canonical masters.
- Modify `Code/core/schools/HistoricalSchoolTravelService.cs`: pause travel for active military service and restore the scholar job afterward.
- Modify `F:/tmp/AW3HistoricalSchoolRuleTests/AW3HistoricalSchoolRuleTests.csproj`: link the new pure files.
- Modify `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`: identity, formatter, matrix, source-contract, UI, and travel assertions.
- Modify `F:/tmp/AW3HistoricalMasterLineageSQLiteTests/AW3HistoricalMasterLineageSQLiteTests.csproj`: link the evidence type.
- Modify `F:/tmp/AW3HistoricalMasterLineageSQLiteTests/Program.cs`: distinct and unknown surname persistence cases.

## Task 1: Canonical Identity Evidence And Pure Vocation Matrix

**Files:**
- Create: `Code/content/schools/HistoricalMasterIdentity.cs`
- Create: `Code/content/schools/HistoricalMasterVocationRules.cs`
- Modify: `Code/content/schools/HistoricalMasterIdentityRules.cs`
- Modify: `Code/content/schools/HistoricalSchoolMasterDefinition.cs`
- Modify: `Code/content/schools/HistoricalSchoolMasterRegistry.cs`
- Modify: `F:/tmp/AW3HistoricalSchoolRuleTests/AW3HistoricalSchoolRuleTests.csproj`
- Modify: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Link the future pure files in the historical-school harness**

Add these entries beside the existing identity-rule link:

```xml
<Compile Include="F:/WorldBox New Mod/AncientWarfare3.0/Code/content/schools/HistoricalMasterIdentity.cs" Link="HistoricalMasterIdentity.cs"
         Condition="Exists('F:/WorldBox New Mod/AncientWarfare3.0/Code/content/schools/HistoricalMasterIdentity.cs')" />
<Compile Include="F:/WorldBox New Mod/AncientWarfare3.0/Code/content/schools/HistoricalMasterVocationRules.cs" Link="HistoricalMasterVocationRules.cs"
         Condition="Exists('F:/WorldBox New Mod/AncientWarfare3.0/Code/content/schools/HistoricalMasterVocationRules.cs')" />
```

- [ ] **Step 2: Add failing identity and vocation assertions**

Add one harness section that checks all approved invariants:

```csharp
HistoricalMasterCanonicalIdentity kong = HistoricalMasterIdentityRules.Resolve("孔丘");
Check(kong.IsValid && kong.FamilyEvidence == HistoricalMasterFamilyEvidence.KnownDistinct &&
      kong.FamilyName == "子" && kong.ShiName == "孔" && kong.GivenName == "丘",
    "Kong Qiu is not projected as Zi surname / Kong shi / Qiu given name");

HistoricalMasterCanonicalIdentity mo = HistoricalMasterIdentityRules.Resolve("墨翟");
Check(mo.IsValid && mo.FamilyEvidence == HistoricalMasterFamilyEvidence.Unknown &&
      mo.FamilyName == "" && mo.ShiName == "墨",
    "unknown pre-Qin surname was fabricated");

Check(HistoricalSchoolMasterRegistry.All.Count == 84 &&
      HistoricalSchoolMasterRegistry.All.Count(p => p.FamilyEvidence ==
          HistoricalMasterFamilyEvidence.KnownDistinct) == 12 &&
      HistoricalSchoolMasterRegistry.All.Count(p => p.FamilyEvidence ==
          HistoricalMasterFamilyEvidence.KnownSame) == 22 &&
      HistoricalSchoolMasterRegistry.All.Count(p => p.FamilyEvidence ==
          HistoricalMasterFamilyEvidence.Unknown) == 50,
    "84-person surname evidence partition is wrong");

string[] militaryNames =
{
    "禽滑釐", "孟胜", "公孙鞅", "孙武", "田穰苴", "吴起",
    "孙膑", "尉缭", "白起", "公孙衍", "范蠡"
};
string[] actualMilitary = HistoricalSchoolMasterRegistry.All
    .Where(p => p.MilitaryEligible).Select(p => p.CanonicalName)
    .OrderBy(p => p, StringComparer.Ordinal).ToArray();
Check(actualMilitary.SequenceEqual(militaryNames.OrderBy(p => p,
          StringComparer.Ordinal)),
    "historical military whitelist is wrong");

foreach (HistoricalSchoolMasterDefinition master in HistoricalSchoolMasterRegistry.All)
foreach (HistoricalMasterMilitaryContext context in
         Enum.GetValues<HistoricalMasterMilitaryContext>())
{
    bool expected = master.MilitaryEligible &&
        context != HistoricalMasterMilitaryContext.RoyalGuard &&
        context != HistoricalMasterMilitaryContext.SlaveArmyCadre &&
        context != HistoricalMasterMilitaryContext.RebelLevy;
    Check(HistoricalMasterVocationRules.CanEnter(
              pCanonicalMaster: true, pDefinitionResolved: true,
              master.MilitaryEligible, context) == expected,
        master.CanonicalName + " vocation mismatch for " + context);
}
Check(!HistoricalMasterVocationRules.CanEnter(true, false, true,
          HistoricalMasterMilitaryContext.General) &&
      HistoricalMasterVocationRules.CanEnter(false, false, false,
          HistoricalMasterMilitaryContext.General),
    "unresolved canonical fail-closed or non-canonical pass-through is wrong");
```

- [ ] **Step 3: Run the historical-school harness and confirm RED**

Run:

```powershell
dotnet run --project F:/tmp/AW3HistoricalSchoolRuleTests/AW3HistoricalSchoolRuleTests.csproj --no-restore -c Release
```

Expected: compilation fails because `HistoricalMasterFamilyEvidence`, `FamilyEvidence`, `MilitaryEligible`, `HistoricalMasterMilitaryContext`, and `HistoricalMasterVocationRules` do not exist.

- [ ] **Step 4: Create the immutable evidence type**

Create `HistoricalMasterIdentity.cs` with the exact invariants:

```csharp
namespace AncientWarfare3.content.schools
{
    public enum HistoricalMasterFamilyEvidence
    {
        Unknown = 0,
        KnownDistinct = 1,
        KnownSame = 2
    }

    public readonly struct HistoricalMasterCanonicalIdentity
    {
        public HistoricalMasterCanonicalIdentity(string pCanonicalName, string pShiName,
            string pGivenName, string pFamilyName,
            HistoricalMasterFamilyEvidence pFamilyEvidence, bool pMilitaryEligible)
        {
            CanonicalName = pCanonicalName ?? "";
            ShiName = pShiName ?? "";
            GivenName = pGivenName ?? "";
            FamilyName = pFamilyName ?? "";
            FamilyEvidence = pFamilyEvidence;
            MilitaryEligible = pMilitaryEligible;
        }

        public string CanonicalName { get; }
        public string ShiName { get; }
        public string GivenName { get; }
        public string FamilyName { get; }
        public HistoricalMasterFamilyEvidence FamilyEvidence { get; }
        public bool MilitaryEligible { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(CanonicalName) &&
            !string.IsNullOrWhiteSpace(ShiName) &&
            !string.IsNullOrWhiteSpace(GivenName) &&
            CanonicalName == ShiName + GivenName &&
            (FamilyEvidence == HistoricalMasterFamilyEvidence.Unknown
                ? string.IsNullOrEmpty(FamilyName)
                : !string.IsNullOrWhiteSpace(FamilyName) &&
                  (FamilyEvidence == HistoricalMasterFamilyEvidence.KnownSame
                      ? FamilyName == ShiName
                      : FamilyEvidence == HistoricalMasterFamilyEvidence.KnownDistinct &&
                        FamilyName != ShiName));
    }
}
```

- [ ] **Step 5: Create the pure vocation matrix**

Create `HistoricalMasterVocationRules.cs`:

```csharp
namespace AncientWarfare3.content.schools
{
    public enum HistoricalMasterMilitaryContext
    {
        OrdinaryWarrior,
        NormalArmy,
        ArmyCaptain,
        BorderArmy,
        General,
        RoyalGuard,
        SlaveArmyCadre,
        RebelLevy
    }

    public static class HistoricalMasterVocationRules
    {
        public static bool CanEnter(bool pCanonicalMaster, bool pDefinitionResolved,
            bool pMilitaryEligible, HistoricalMasterMilitaryContext pContext)
        {
            if (!pCanonicalMaster) return true;
            if (!pDefinitionResolved || !pMilitaryEligible) return false;
            return pContext != HistoricalMasterMilitaryContext.RoyalGuard &&
                   pContext != HistoricalMasterMilitaryContext.SlaveArmyCadre &&
                   pContext != HistoricalMasterMilitaryContext.RebelLevy;
        }
    }
}
```

- [ ] **Step 6: Replace the registry identity builder with the approved data**

Move the old identity struct out of `HistoricalMasterIdentityRules.cs`. Give its `Add` helper explicit surname, evidence, and military parameters. Use these complete sets:

```text
KnownDistinct:
孔丘=子/孔, 曾参=姒/曾, 孔伋=子/孔, 孟轲=姬/孟,
公孙鞅=姬/公孙, 韩非=姬/韩,
孙武=妫/孙, 田穰苴=妫/田, 孙膑=妫/孙,
秦越人=姬/秦, 吕不韦=姜/吕, 公输班=姬/公输

KnownSame:
李耳, 董仲舒, 氾胜之, 贾思勰, 王祯, 落下闳,
淳于意, 张机, 华佗, 葛洪, 刘安, 伍被, 苏飞, 东方朔,
卓王孙, 桑弘羊, 丁缓, 司马谈, 司马迁, 刘向, 班固, 荀悦

MilitaryEligible:
禽滑釐, 孟胜, 公孙鞅, 孙武, 田穰苴, 吴起,
孙膑, 尉缭, 白起, 公孙衍, 范蠡
```

Every existing canonical name not present in the first two sets uses an empty surname and `Unknown`. Preserve the current canonical Shi and given-name split exactly; do not normalize `乌氏` in actor identity data.

- [ ] **Step 7: Project the metadata through master definitions**

Add constructor parameters and properties to `HistoricalSchoolMasterDefinition`:

```csharp
HistoricalMasterFamilyEvidence pFamilyEvidence = HistoricalMasterFamilyEvidence.Unknown,
bool pMilitaryEligible = false

public HistoricalMasterFamilyEvidence FamilyEvidence { get; }
public bool MilitaryEligible { get; }
```

In `HistoricalSchoolMasterRegistry.AddSchool`, pass `identity.FamilyEvidence` and `identity.MilitaryEligible`. Replace the old unconditional non-empty-family validation with `identity.IsValid`, definition/identity equality, and exact counts `12`, `22`, `50`, `11`, and `73`.

- [ ] **Step 8: Run the harness and confirm GREEN**

Run the Task 1 command again.

Expected: `AW3 historical school rules passed`.

- [ ] **Step 9: Rebuild Debug before committing**

Run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
dotnet build AncientWarfare3.csproj -c Debug -t:Rebuild --no-incremental --no-restore -p:TargetFrameworkRootPath='C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build' -p:OutputPath='F:\tmp\AW3Build\current\debug\bin\Debug\net48\'
```

Expected: `0 warnings`, `0 errors`.

- [ ] **Step 10: Commit the static data slice**

```powershell
git add -- Code/content/schools/HistoricalMasterIdentity.cs Code/content/schools/HistoricalMasterIdentityRules.cs Code/content/schools/HistoricalMasterVocationRules.cs Code/content/schools/HistoricalSchoolMasterDefinition.cs Code/content/schools/HistoricalSchoolMasterRegistry.cs
git commit -m "feat: define historical master identities and vocations"
```

## Task 2: Unknown Surname Persistence And Actor Projection

**Files:**
- Modify: `Code/core/schools/HistoricalMasterLineagePersistence.cs`
- Modify: `Code/core/schools/HistoricalSchoolDescentService.cs`
- Modify: `Code/core/schools/HistoricalSchoolStore.cs`
- Modify: `Code/core/schools/HistoricalMasterIdentityProjection.cs`
- Modify: `F:/tmp/AW3HistoricalMasterLineageSQLiteTests/AW3HistoricalMasterLineageSQLiteTests.csproj`
- Modify: `F:/tmp/AW3HistoricalMasterLineageSQLiteTests/Program.cs`

- [ ] **Step 1: Link the evidence type into the SQLite harness**

Add:

```xml
<Compile Include="F:/WorldBox New Mod/AncientWarfare3.0/Code/content/schools/HistoricalMasterIdentity.cs" Link="Production/HistoricalMasterIdentity.cs" />
```

- [ ] **Step 2: Add failing distinct and unknown persistence cases**

Change the Kong helper call to persist surname `子`, then add an unknown case:

```csharp
HistoricalMasterLineageCommitIdentity kong = new HistoricalMasterLineageCommitIdentity(
    42, "孔丘", "孔", "丘", "子",
    HistoricalMasterFamilyEvidence.KnownDistinct, 7, 70, 123.5d);

HistoricalMasterLineageCommitIdentity mo = new HistoricalMasterLineageCommitIdentity(
    43, "墨翟", "墨", "翟", "",
    HistoricalMasterFamilyEvidence.Unknown, 7, 70, 124.5d);
```

Stage each tuple and assert:

```csharp
Check(Text(db, "SELECT FAMILY_NAME FROM LineageGroup WHERE FOUNDER_ACTOR_ID=42") == "子",
    "documented distinct surname was not persisted");
Check(Text(db, "SELECT FAMILY_NAME FROM LineageGroup WHERE FOUNDER_ACTOR_ID=43") == "",
    "unknown surname was fabricated");
Check(Text(db, "SELECT CLAN_NAME FROM ShiBranch WHERE FOUNDER_ACTOR_ID=43") == "墨",
    "unknown surname lost its valid shi branch");
```

Also assert that `Unknown` plus non-empty family and `KnownDistinct` plus empty/equal family are invalid.

- [ ] **Step 3: Run the SQLite harness and confirm RED**

```powershell
dotnet run --project F:/tmp/AW3HistoricalMasterLineageSQLiteTests/AW3HistoricalMasterLineageSQLiteTests.csproj --no-restore -c Release
```

Expected: compilation fails because the commit identity does not accept evidence, or runtime validation rejects the empty surname.

- [ ] **Step 4: Carry evidence in the frozen commit identity**

Add the evidence constructor parameter/property and use a shared validation predicate:

```csharp
public HistoricalMasterFamilyEvidence FamilyEvidence { get; }

private bool FamilyValid => FamilyEvidence == HistoricalMasterFamilyEvidence.Unknown
    ? string.IsNullOrEmpty(FamilyName)
    : !string.IsNullOrWhiteSpace(FamilyName) &&
      (FamilyEvidence == HistoricalMasterFamilyEvidence.KnownSame
          ? FamilyName == ShiName
          : FamilyEvidence == HistoricalMasterFamilyEvidence.KnownDistinct &&
            FamilyName != ShiName);
```

`IsValid` must require `FamilyValid` instead of unconditional non-empty `FamilyName`. SQLite still stores the exact string and strict readback still compares it byte-for-byte.

- [ ] **Step 5: Update both descent reconstruction sites and frozen request checks**

At both `new HistoricalMasterLineageCommitIdentity` calls in `HistoricalSchoolDescentService`, pass `master.FamilyEvidence`. In `HistoricalSchoolStore.CommitHistoricalDescent`, require:

```csharp
pIdentity.FamilyName == pMaster.CanonicalFamilyName &&
pIdentity.FamilyEvidence == pMaster.FamilyEvidence
```

Do not add migration or infer evidence from a stored string.

- [ ] **Step 6: Project exact known or empty unknown fields**

Keep `HistoricalMasterIdentityProjection.ApplyCanonicalActorFields` assignments exact:

```csharp
pActor.data.set(LineageKeys.FAMILY_NAME, pMaster.CanonicalFamilyName);
pActor.data.set(LineageKeys.CHINESE_FAMILY_NAME, pMaster.CanonicalFamilyName);
```

Extend `MatchesRequest` and `MatchesProjectedIdentity` to compare evidence through the definition/commit identity and to accept empty family fields for `Unknown`. Keep actor `name` and `display_name` equal to `CanonicalName`.

- [ ] **Step 7: Run persistence and historical rules GREEN**

```powershell
dotnet run --project F:/tmp/AW3HistoricalMasterLineageSQLiteTests/AW3HistoricalMasterLineageSQLiteTests.csproj --no-restore -c Release
dotnet run --project F:/tmp/AW3HistoricalSchoolRuleTests/AW3HistoricalSchoolRuleTests.csproj --no-restore -c Release
```

Expected: both pass messages, including the empty-family/valid-branch case.

- [ ] **Step 8: Rebuild Debug and commit**

Run the Task 1 Debug command, then:

```powershell
git add -- Code/core/schools/HistoricalMasterLineagePersistence.cs Code/core/schools/HistoricalSchoolDescentService.cs Code/core/schools/HistoricalSchoolStore.cs Code/core/schools/HistoricalMasterIdentityProjection.cs
git commit -m "fix: persist conservative master surnames"
```

## Task 3: Founder-City Clan Titles And Identity Rows

**Files:**
- Modify: `Code/content/schools/HistoricalMasterIdentityRules.cs`
- Modify: `Code/core/schools/HistoricalMasterIdentityProjection.cs`
- Modify: `Code/core/lineage/LineageService.cs`
- Modify: `Code/patch/AW_UnitWindowPatch.cs`
- Modify: `Locales/others.csv`
- Modify: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Add failing formatter and source-contract tests**

Add pure assertions:

```csharp
Check(HistoricalMasterIdentityRules.BuildClanDisplayName("曲阜", "孔") == "曲阜孔氏",
    "simple founder-city clan title is wrong");
Check(HistoricalMasterIdentityRules.BuildClanDisplayName("城", "公孙") == "城公孙氏",
    "compound shi clan title is wrong");
Check(HistoricalMasterIdentityRules.BuildClanDisplayName("城", "乌氏") == "城乌氏",
    "existing shi suffix was duplicated");
Check(HistoricalMasterIdentityRules.BuildClanDisplayName("", "孔") == "" &&
      HistoricalMasterIdentityRules.BuildClanDisplayName("曲阜", "") == "",
    "partial clan title was emitted");
```

Add source assertions requiring:

```text
HistoricalMasterIdentityProjection uses BuildClanDisplayName and founder_city_name.
LineageService canonical branch uses BuildClanDisplayName and does not call EnsureSingleShiSuffix alone.
AW_UnitWindowPatch detects IsCanonicalMaster, uses aw_family_name_unknown, and shows the clan row without noble/integration gates.
Locales/others.csv contains aw_family_name_unknown with four columns.
```

- [ ] **Step 2: Run the historical harness and confirm RED**

Run the historical-school command.

Expected: formatter and UI source-contract assertions fail.

- [ ] **Step 3: Add the pure founder-city formatter**

Add:

```csharp
public static string BuildClanDisplayName(string pFounderCityName, string pShiName)
{
    string place = (pFounderCityName ?? "").Trim();
    string shi = NormalizeShiName(pShiName);
    return string.IsNullOrEmpty(place) || string.IsNullOrEmpty(shi)
        ? ""
        : place + shi + "氏";
}
```

Do not change canonical `ShiName`; normalization applies only to the WorldBox clan title.

- [ ] **Step 4: Replace both short-name overrides**

In `LineageService.RenameClanByLeader`, canonical masters use:

```csharp
string expected = HistoricalMasterIdentityRules.BuildClanDisplayName(
    pClan.data.founder_city_name, definition?.CanonicalShiName);
if (!string.IsNullOrEmpty(expected) && pClan.data.name != expected)
    try { pClan.setName(expected); } catch { }
return;
```

In `HistoricalMasterIdentityProjection`, resolve place from `clan.data.founder_city_name`; if empty, look up `pIdentity.HometownCityId` and use that city's stored name. Build the same expected value. If it remains empty, return false so the existing pending projection retry retains the actor instead of accepting a partial title. Compare this exact title in `MatchesProjectedIdentity`.

- [ ] **Step 5: Add a canonical-master identity branch to the actor window**

Before ordinary noble/integration visibility rules, render:

```csharp
HistoricalSchoolMasterDefinition master =
    HistoricalSchoolDescentService.DefinitionFor(actor);
if (master != null)
{
    string familyValue = master.FamilyEvidence == HistoricalMasterFamilyEvidence.Unknown
        ? AW_L10n.Text("aw_family_name_unknown", "Unknown")
        : master.CanonicalFamilyName;
    KeyValueField familyRow = ShowRawRow(__instance, "aw_family_name", familyValue);
    if (familyRow != null && master.FamilyEvidence != HistoricalMasterFamilyEvidence.Unknown)
    {
        string knownFamily = master.CanonicalFamilyName;
        familyRow.on_click_value = () => ShiBranchListWindow.OpenFor(knownFamily);
    }
    if (!string.IsNullOrEmpty(master.CanonicalShiName) && shiId >= 0)
    {
        KeyValueField shiRow = ShowRawRow(__instance, "aw_clan_name",
            master.CanonicalShiName);
        if (shiRow != null)
        {
            long branchId = shiId;
            shiRow.on_click_value = () => FamilyTreeWindow.OpenBigTree(branchId);
        }
    }
    return;
}
```

Keep the existing identity row before this branch. Add the necessary `content.schools`, `core.schools`, and `ui` namespace imports.

- [ ] **Step 6: Add the presentation-only locale value**

Append to `Locales/others.csv`:

```csv
aw_family_name_unknown,未详,Unknown,未詳
```

- [ ] **Step 7: Run tests, Debug build, and diff validation**

```powershell
dotnet run --project F:/tmp/AW3HistoricalSchoolRuleTests/AW3HistoricalSchoolRuleTests.csproj --no-restore -c Release
dotnet run --project F:/tmp/AW3HistoricalMasterLineageSQLiteTests/AW3HistoricalMasterLineageSQLiteTests.csproj --no-restore -c Release
git diff --check
```

Then run the Task 1 Debug build.

Expected: both harnesses pass; build has zero warnings/errors; no whitespace errors.

- [ ] **Step 8: Commit the naming and UI slice**

```powershell
git add -- Code/content/schools/HistoricalMasterIdentityRules.cs Code/core/schools/HistoricalMasterIdentityProjection.cs Code/core/lineage/LineageService.cs Code/patch/AW_UnitWindowPatch.cs Locales/others.csv
git commit -m "fix: show historical master surname and clan"
```

## Task 4: Runtime Vocation Adapter And Vanilla Boundary Guards

**Files:**
- Create: `Code/core/schools/HistoricalMasterVocationService.cs`
- Create: `Code/patch/AW_HistoricalMasterVocationPatch.cs`
- Modify: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Add failing source-contract assertions for every vanilla boundary**

Require the new patch source to contain Harmony targets for:

```text
City.checkCanMakeWarrior
City.makeWarrior
Actor.setProfession
Actor.setArmy
ArmyManager.newArmy
Army.setCaptain
```

Require `City.makeWarrior` to be a prefix, `Actor.setArmy` and `Army.setCaptain` to allow null removal, and every non-null admission to call `HistoricalMasterVocationService` with the appropriate context.

- [ ] **Step 2: Run the historical harness and confirm RED**

Expected: missing runtime adapter and patch source-contract failures.

- [ ] **Step 3: Create the O(1) runtime adapter**

Implement:

```csharp
internal static class HistoricalMasterVocationService
{
    public static bool CanEnter(Actor pActor, HistoricalMasterMilitaryContext pContext)
    {
        if (pActor?.data == null) return false;
        pActor.data.get(LineageKeys.SCHOOL_MASTER_ID, out string masterId, "");
        HistoricalSchoolMasterDefinition definition =
            HistoricalSchoolDescentService.DefinitionFor(pActor);
        bool canonical = !string.IsNullOrEmpty(masterId) || definition != null;
        return HistoricalMasterVocationRules.CanEnter(canonical,
            !canonical || definition != null, definition?.MilitaryEligible == true,
            pContext);
    }

    public static bool CanEnterArmyRole(Actor pActor, string pRole)
    {
        HistoricalMasterMilitaryContext context = pRole == AWArmyRole.RoyalGuard
            ? HistoricalMasterMilitaryContext.RoyalGuard
            : pRole == AWArmyRole.SlaveArmy
                ? HistoricalMasterMilitaryContext.SlaveArmyCadre
                : pRole == AWArmyRole.BorderArmy
                    ? HistoricalMasterMilitaryContext.BorderArmy
                    : HistoricalMasterMilitaryContext.NormalArmy;
        return CanEnter(pActor, context);
    }

    public static bool CanJoinArmy(Actor pActor, Army pArmy)
    {
        if (pArmy == null) return true;
        string role = AWArmyService.IsRoleArmy(pArmy, AWArmyRole.RoyalGuard)
            ? AWArmyRole.RoyalGuard
            : SlaveService.IsSlaveArmy(pArmy)
                ? AWArmyRole.SlaveArmy
                : AWArmyService.IsRoleArmy(pArmy, AWArmyRole.BorderArmy)
                    ? AWArmyRole.BorderArmy
                    : "";
        return CanEnterArmyRole(pActor, role);
    }
}
```

Do not cache Actor references or query SQLite.

- [ ] **Step 4: Create the defensive Harmony patch**

Implement the six boundaries with `HarmonyPriority(Priority.Last)` where another AW3 prefix may replace an Actor:

```csharp
[HarmonyPostfix]
[HarmonyPatch(typeof(City), nameof(City.checkCanMakeWarrior))]
private static void CheckCanMakeWarrior_Postfix(Actor pActor, ref bool __result)
{
    if (__result && !HistoricalMasterVocationService.CanEnter(pActor,
            HistoricalMasterMilitaryContext.OrdinaryWarrior)) __result = false;
}

[HarmonyPrefix]
[HarmonyPatch(typeof(City), nameof(City.makeWarrior))]
private static bool MakeWarrior_Prefix(Actor pActor) =>
    HistoricalMasterVocationService.CanEnter(pActor,
        HistoricalMasterMilitaryContext.OrdinaryWarrior);

[HarmonyPrefix]
[HarmonyPatch(typeof(Actor), "setProfession")]
private static bool SetProfession_Prefix(Actor __instance, UnitProfession pType) =>
    pType != UnitProfession.Warrior || HistoricalMasterVocationService.CanEnter(
        __instance, HistoricalMasterMilitaryContext.OrdinaryWarrior);

[HarmonyPrefix]
[HarmonyPatch(typeof(Actor), nameof(Actor.setArmy))]
private static bool SetArmy_Prefix(Actor __instance, Army pObject) =>
    pObject == null || HistoricalMasterVocationService.CanJoinArmy(__instance, pObject);
```

For `ArmyManager.newArmy`, return `false` with `__result = null` unless the final `pActor` passes both `NormalArmy` and `ArmyCaptain`. For `Army.setCaptain`, always allow `pActor == null`; otherwise require both `CanJoinArmy(pActor, __instance)` and `CanEnter(pActor, ArmyCaptain)`:

```csharp
private static bool SetCaptain_Prefix(Army __instance, Actor pActor)
{
    return pActor == null ||
           HistoricalMasterVocationService.CanJoinArmy(pActor, __instance) &&
           HistoricalMasterVocationService.CanEnter(pActor,
               HistoricalMasterMilitaryContext.ArmyCaptain);
}
```

A royal-guard or slave-army role therefore remains denied even to whitelisted masters, while a whitelisted border captain passes both checks.

- [ ] **Step 5: Run source contracts and full Debug build GREEN**

Run the historical harness and Task 1 Debug build.

Expected: pass message and zero warnings/errors. Inspect Harmony patch discovery in the compile output; no ambiguous overload errors are allowed.

- [ ] **Step 6: Commit the vanilla-boundary slice**

```powershell
git add -- Code/core/schools/HistoricalMasterVocationService.cs Code/patch/AW_HistoricalMasterVocationPatch.cs
git commit -m "fix: guard historical masters from conscription"
```

## Task 5: AW3 Candidate Filters, Special Armies, And Travel Binding

**Files:**
- Modify: `Code/core/lineage/AWArmyService.cs`
- Modify: `Code/core/lineage/RoyalGuardService.cs`
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/core/lineage/MandateRebelService.cs`
- Modify: `Code/core/lineage/MandateBorderDefenseService.cs`
- Modify: `Code/core/lineage/GeneralService.cs`
- Modify: `Code/core/schools/HistoricalSchoolTravelService.cs`
- Modify: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Add failing source contracts for all AW3 paths**

Require these exact private/runtime seams to call `HistoricalMasterVocationService` before scoring, flag mutation, profession change, or army list mutation:

```text
AWArmyService.EnsureArmy / AddToArmy / SetCaptainIfChanged
RoyalGuardService.IsGuardCandidate / AppointGuard
SlaveService.CanBeSlaveArmyCaptainCandidate / slave candidate collection
MandateRebelService.CanMobilize
MandateBorderDefenseService.CanBeBorderGuard
GeneralService.CanRemainGeneral
HistoricalSchoolTravelService.IsServingOrBound
```

Also require `AWArmyService.AddToArmy` to verify `pActor.army == pArmy` after `setArmy` before calling `pArmy.listUnit`.

- [ ] **Step 2: Run the historical harness and confirm RED**

Expected: each unmodified service reports a missing vocation contract.

- [ ] **Step 3: Guard shared special-army mutation**

At `AWArmyService.EnsureArmy`, reject a captain when `CanEnterArmyRole(pCaptain, pRole)` is false. At `AddToArmy`, return before mutation when `CanJoinArmy` is false and add this postcondition:

```csharp
pActor.setArmy(pArmy);
if (pActor.army != pArmy) return;
if (!pArmy.units.Contains(pActor)) pArmy.listUnit(pActor);
```

At `SetCaptainIfChanged`, reject before writing `data.id_captain` or calling `setCaptain`.

- [ ] **Step 4: Guard royal and slave armies for every canonical master**

Add `RoyalGuard` checks to both guard candidate and appointment entry. Add `SlaveArmyCadre` checks to non-slave cadre, captain, promotion, and enslaved combat-candidate collection. The rule intentionally rejects a canonical master even if that actor has become a slave.

- [ ] **Step 5: Guard rebel, border, and general roles**

Add:

```csharp
if (!HistoricalMasterVocationService.CanEnter(pActor,
        HistoricalMasterMilitaryContext.RebelLevy)) return false;
```

to `CanMobilize`; use `BorderArmy` in `CanBeBorderGuard`; use `General` in `CanRemainGeneral`. Non-canonical behavior remains unchanged. Because fief command requires an active general and still calls the guarded warrior boundary, it needs no separate whitelist.

- [ ] **Step 6: Pause and resume school travel around military service**

In `IsServingOrBound`, treat these as bound before heir resolution:

```csharp
if (pActor.isWarrior() || pActor.hasArmy() || GeneralService.IsGeneral(pActor))
    return true;
```

When `PrepareDestination` reaches an `AtHome` or `Resident` master who is no longer bound, call the existing `RestoreScholarJob(actor)` before selecting a new destination. Do not override kings, heirs, city leaders, court officials, guests, or active generals because they return earlier as bound.

- [ ] **Step 7: Run focused suites and Debug build GREEN**

```powershell
dotnet run --project F:/tmp/AW3HistoricalSchoolRuleTests/AW3HistoricalSchoolRuleTests.csproj --no-restore -c Release
dotnet run --project F:/tmp/AW3CourtExpansionRuleTests/AW3CourtExpansionRuleTests.csproj --no-restore -c Release
dotnet run --project F:/tmp/AW3CorrectnessRuleTests/AW3CorrectnessRuleTests.csproj --no-restore -c Release
```

Then run Task 1 Debug build.

Expected: all pass messages and zero warnings/errors.

- [ ] **Step 8: Commit the AW3 integration slice**

```powershell
git add -- Code/core/lineage/AWArmyService.cs Code/core/lineage/RoyalGuardService.cs Code/core/lineage/SlaveService.cs Code/core/lineage/MandateRebelService.cs Code/core/lineage/MandateBorderDefenseService.cs Code/core/lineage/GeneralService.cs Code/core/schools/HistoricalSchoolTravelService.cs
git commit -m "fix: enforce historical master military roles"
```

## Task 6: Full Regression, Review, And Deployment

**Files:**
- Verify: entire tracked repository
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`
- Preserve: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0/.runtime/`

- [ ] **Step 1: Run every affected focused harness**

```powershell
dotnet run --project F:/tmp/AW3HistoricalSchoolRuleTests/AW3HistoricalSchoolRuleTests.csproj --no-restore -c Release
dotnet run --project F:/tmp/AW3HistoricalSchoolRuleTests/SpawnHarmonyIntegration/AW3HistoricalSchoolSpawnHarmonyTests.csproj --no-restore -c Release
dotnet run --project F:/tmp/AW3HistoricalMasterLineageSQLiteTests/AW3HistoricalMasterLineageSQLiteTests.csproj --no-restore -c Release
dotnet run --project F:/tmp/AW3GuestOfficeAtomicIntegrationTests/AW3GuestOfficeAtomicIntegrationTests.csproj --no-restore -c Release
dotnet run --project F:/tmp/AW3SchoolTeachingAtomicIntegrationTests/AW3SchoolTeachingAtomicIntegrationTests.csproj --no-restore -c Release
dotnet run --project F:/tmp/AW3CourtExpansionRuleTests/AW3CourtExpansionRuleTests.csproj --no-restore -c Release
dotnet run --project F:/tmp/AW3CourtLayoutRuleTests/AW3CourtLayoutRuleTests.csproj --no-restore -c Release
dotnet run --project F:/tmp/AW3CorrectnessRuleTests/AW3CorrectnessRuleTests.csproj --no-restore -c Release
dotnet run --project F:/tmp/AW3PathfindingRuleTests/AW3PathfindingRuleTests.csproj --no-restore -c Release
```

Expected: every process exits zero and prints its pass message.

- [ ] **Step 2: Run clean Debug and Release rebuilds**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
dotnet build AncientWarfare3.csproj -c Debug -t:Rebuild --no-incremental --no-restore -p:TargetFrameworkRootPath='C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build' -p:OutputPath='F:\tmp\AW3Build\current\debug\bin\Debug\net48\'
dotnet build AncientWarfare3.csproj -c Release -t:Rebuild --no-incremental --no-restore -p:TargetFrameworkRootPath='C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build' -p:OutputPath='F:\tmp\AW3Build\current\release\bin\Release\net48\'
```

Expected: both builds report zero warnings and zero errors.

- [ ] **Step 3: Validate source, locale, and repository state**

```powershell
git diff --check
git status --short
rg -n "EnsureSingleShiSuffix\(pMaster\.CanonicalShiName\)|pActor\.hasTrait\(\"figure\"\).*first" Code/core/schools Code/core/lineage Code/patch
rg -n "aw_family_name_unknown|trait_aw_historical_school_master" Locales
```

Inspect every remaining search result. The old short canonical clan override must be absent; vanilla historical-figure checks may remain only when they are unrelated to canonical school-master vocation.

- [ ] **Step 4: Perform focused code review**

Review the complete implementation against the approved design. Reject any Critical or Important issue involving:

```text
fabricated surname persistence
literal 未详 in actor or SQLite data
canonical actor-name changes
current-residence clan naming
warrior count increments after a blocked promotion
army list membership after a blocked setArmy
guard/slave/rebel admission for any canonical master
database access or world scans in recruitment gates
blocked army removal or blocked stopBeingWarrior
```

Fix findings and rerun Steps 1-3 before continuing.

- [ ] **Step 5: Commit any final review correction**

If review changes code, stage only those files and commit:

```powershell
git commit -m "fix: harden historical master vocation gates"
```

Skip this commit when review produces no code change; do not create an empty commit.

- [ ] **Step 6: Deploy tracked files while preserving runtime data**

Record the runtime database hash before deployment:

```powershell
Get-FileHash 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0\.runtime\aw3_lineage_archive.db' -Algorithm SHA256
```

Copy only paths returned by `git ls-files`, creating their parent directories under the loaded mod directory. Do not delete destination `.runtime`, `.git`, or untracked user files:

```powershell
$source = (Resolve-Path '.').Path
$target = 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
$runtime = Join-Path $target '.runtime\aw3_lineage_archive.db'
$before = (Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash
foreach ($tracked in (git ls-files))
{
    $relative = $tracked.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $from = Join-Path $source $relative
    $to = Join-Path $target $relative
    $parent = Split-Path -Parent $to
    if (-not (Test-Path -LiteralPath $parent))
    {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Copy-Item -LiteralPath $from -Destination $to -Force
}
$after = (Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash
if ($before -ne $after) { throw '.runtime database changed during deployment' }
```

Compare SHA256 hashes for these representative deployed files:

```text
Code/content/schools/HistoricalMasterIdentityRules.cs
Code/content/schools/HistoricalMasterVocationRules.cs
Code/core/schools/HistoricalMasterVocationService.cs
Code/patch/AW_HistoricalMasterVocationPatch.cs
Code/patch/AW_UnitWindowPatch.cs
Locales/others.csv
```

```powershell
$representative = @(
    'Code/content/schools/HistoricalMasterIdentityRules.cs',
    'Code/content/schools/HistoricalMasterVocationRules.cs',
    'Code/core/schools/HistoricalMasterVocationService.cs',
    'Code/patch/AW_HistoricalMasterVocationPatch.cs',
    'Code/patch/AW_UnitWindowPatch.cs',
    'Locales/others.csv'
)
foreach ($relative in $representative)
{
    $workHash = (Get-FileHash -LiteralPath (Join-Path $source $relative) -Algorithm SHA256).Hash
    $liveHash = (Get-FileHash -LiteralPath (Join-Path $target $relative) -Algorithm SHA256).Hash
    if ($workHash -ne $liveHash) { throw "deployment hash mismatch: $relative" }
}
```

- [ ] **Step 7: Record the manual new-world acceptance checklist**

Do not claim live behavior without launching a new world. Functional acceptance must observe:

```text
孔丘: 姓=子, 氏=孔, canonical actor name=孔丘
孔丘 clan title: founder-city full name + 孔氏
墨翟: 姓=未详 in UI but empty surname in durable data
乌氏倮 clan title: one trailing 氏
protected master rejected by ordinary recruitment, border, guard, slave army, rebel levy, captain, and general paths
one whitelisted master successfully enters normal military/general service
military service pauses travel and discharge restores scholar behavior
```

No push is part of this plan.

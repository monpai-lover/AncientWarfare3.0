# AW3 Naming, Occupation, And Court AI Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Xia alliance naming cooperate with the optional Chinese Name mod, prevent native non-Xia cities from being logged as Xia foreign occupation, and make AI research the complete court-technology chain in the intended order.

**Architecture:** Keep optional-mod ownership at compile time with the existing `一米_中文名` symbol. Keep occupation classification and AI priority order in pure rules so they can be tested without loading WorldBox; runtime services only gather game state and delegate to those rules.

**Tech Stack:** C#/.NET, Harmony, NeoModLoader compile-time symbols, temporary console regression harness under `F:\tmp`, Git.

---

## File Map

- Modify `Code/patch/AW_XiaNamingPatch.cs`: omit only the alliance Postfix when Chinese Name is compiled in.
- Modify `Code/content/XiaFallbackNameRules.cs`: supply deterministic ASCII Xia alliance fallback names.
- Modify `Code/content/XiaNameSets.cs`: make the vanilla generator fallback English as well.
- Modify `Code/core/lineage/ForeignOccupationDetectionRules.cs`: require Xia city identity before Xia occupation types are possible.
- Modify `Code/core/lineage/ForeignOccupationService.cs`: stop treating a single Xia resident as Xia city identity.
- Create `Code/core/policy/KingdomPolicyTechOrderRules.cs`: own the complete ordered list of 12 technologies.
- Modify `Code/core/policy/KingdomPolicyAI.cs`: delegate technology priority lookup to the pure order rule.
- Create `F:\tmp\AW3RuleRegression\AW3RuleRegression.csproj` and `Program.cs`: temporary focused test harness; never add it to the repository.

### Task 1: Xia Alliance Optional-Mod Ownership And English Fallback

**Files:**
- Create: `F:\tmp\AW3RuleRegression\AW3RuleRegression.csproj`
- Create: `F:\tmp\AW3RuleRegression\Program.cs`
- Modify: `Code/patch/AW_XiaNamingPatch.cs`
- Modify: `Code/content/XiaFallbackNameRules.cs`
- Modify: `Code/content/XiaNameSets.cs`

- [ ] **Step 1: Create the temporary test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="F:\WorldBox New Mod\AncientWarfare3.0\Code\content\XiaNameRepairRules.cs" Link="XiaNameRepairRules.cs" />
    <Compile Include="F:\WorldBox New Mod\AncientWarfare3.0\Code\content\XiaFallbackNameRules.cs" Link="XiaFallbackNameRules.cs" />
    <Compile Include="F:\WorldBox New Mod\AncientWarfare3.0\Code\core\lineage\ForeignOccupationDetectionRules.cs" Link="ForeignOccupationDetectionRules.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing alliance fallback test in `Program.cs`**

```csharp
using AncientWarfare3.content;

for (long seed = -64; seed <= 64; seed++)
{
    string first = XiaFallbackNameRules.LocalAllianceName(seed);
    string second = XiaFallbackNameRules.LocalAllianceName(seed);
    if (first != second || string.IsNullOrWhiteSpace(first))
        throw new Exception($"Alliance fallback must be deterministic for seed {seed}.");
    if (first.Any(ch => ch > 0x7f))
        throw new Exception($"Alliance fallback must be ASCII without Chinese Name: {first}");
}

Console.WriteLine("AW3 focused rule regressions passed.");
```

- [ ] **Step 3: Run RED and confirm the current Chinese fallback fails**

Run: `dotnet run --project F:\tmp\AW3RuleRegression\AW3RuleRegression.csproj`

Expected: FAIL with `Alliance fallback must be ASCII without Chinese Name`.

- [ ] **Step 4: Implement the deterministic English fallback**

Replace the alliance arrays and method in `XiaFallbackNameRules.cs` with:

```csharp
private static readonly string[] AllianceNames =
{
    "Nine Provinces League",
    "Four Seas Pact",
    "Jade Concord",
    "Xia Covenant"
};

public static string LocalAllianceName(long pSeed)
{
    return Pick(AllianceNames, pSeed);
}
```

Replace the `AllianceGenerator` dictionary in `XiaNameSets.cs` with:

```csharp
RegisterDictionaryGenerator(
    AllianceGenerator,
    new[]
    {
        "fixed_alliance", "Nine Provinces League,Four Seas Pact,Jade Concord,Xia Covenant"
    },
    "fixed_alliance");
```

Wrap only `Alliance_AddFounders_Postfix` in `AW_XiaNamingPatch.cs`:

```csharp
#if !一米_中文名
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Alliance), nameof(Alliance.addFounders))]
        private static void Alliance_AddFounders_Postfix(Alliance __instance,
            Kingdom pKingdom1, Kingdom pKingdom2)
        {
            bool usesXiaName = XiaAllianceNamingRules.ShouldUseXiaName(
                LineageService.IsXiaKingdom(pKingdom1),
                LineageService.IsXiaKingdom(pKingdom2));
            if (!usesXiaName || __instance?.data == null) return;

            string name = XiaNamingRepair.GenerateAllianceName(__instance);
            bool valid = !XiaNameRepairRules.IsInvalidGeneratedMetaName(name);
            if (!XiaAllianceNamingRules.ShouldRenameAfterCreation(usesXiaName, valid)) return;
            __instance.setName(name, pTrack: false);
        }
#endif
```

- [ ] **Step 5: Run GREEN**

Run: `dotnet run --project F:\tmp\AW3RuleRegression\AW3RuleRegression.csproj`

Expected: PASS and print `AW3 focused rule regressions passed.`

- [ ] **Step 6: Commit only the production files**

```powershell
git add -- Code/patch/AW_XiaNamingPatch.cs Code/content/XiaFallbackNameRules.cs Code/content/XiaNameSets.cs
git commit -m "fix: 协调夏联盟命名链路"
```

### Task 2: Strict Xia Identity For Foreign Occupation

**Files:**
- Modify: `F:\tmp\AW3RuleRegression\Program.cs`
- Modify: `Code/core/lineage/ForeignOccupationDetectionRules.cs`
- Modify: `Code/core/lineage/ForeignOccupationService.cs`

- [ ] **Step 1: Add failing occupation cases before the final success print**

```csharp
using AncientWarfare3.core.lineage;

string type;
if (ForeignOccupationDetectionRules.TryDetectOccupation(
        ownerIsXia: false,
        legalCore: true,
        mandateCoreControlRatio: 0.8f,
        cityHasXiaIdentity: false,
        differentCultureOrLanguage: false,
        sameOwnerOriginCity: false,
        out type))
    throw new Exception("A non-Xia legal-core city must not enter Xia foreign occupation.");

if (!ForeignOccupationDetectionRules.TryDetectOccupation(
        ownerIsXia: false,
        legalCore: true,
        mandateCoreControlRatio: 0.8f,
        cityHasXiaIdentity: true,
        differentCultureOrLanguage: true,
        sameOwnerOriginCity: false,
        out type) || type != ForeignOccupationDetectionRules.TypePseudoDynasty)
    throw new Exception("A true Xia legal-core city at 65% control must remain pseudo-dynasty occupation.");

if (!ForeignOccupationDetectionRules.TryDetectOccupation(
        ownerIsXia: false,
        legalCore: true,
        mandateCoreControlRatio: 0.2f,
        cityHasXiaIdentity: true,
        differentCultureOrLanguage: false,
        sameOwnerOriginCity: false,
        out type) || type != ForeignOccupationDetectionRules.TypeForeignEntry)
    throw new Exception("A true Xia city below pseudo-dynasty control must remain foreign entry.");

if (!ForeignOccupationDetectionRules.TryDetectOccupation(
        ownerIsXia: false,
        legalCore: true,
        mandateCoreControlRatio: 0.8f,
        cityHasXiaIdentity: false,
        differentCultureOrLanguage: true,
        sameOwnerOriginCity: false,
        out type) || type != ForeignOccupationDetectionRules.TypeNormalConquest)
    throw new Exception("A non-Xia culture mismatch must remain normal conquest even when it is legal core.");
```

- [ ] **Step 2: Run RED and confirm legal core alone produces `pseudo_dynasty`**

Run: `dotnet run --project F:\tmp\AW3RuleRegression\AW3RuleRegression.csproj`

Expected: FAIL with `A non-Xia legal-core city must not enter Xia foreign occupation.`

- [ ] **Step 3: Restrict Xia occupation branches to Xia-identity cities**

Replace the body after the owner check in `ForeignOccupationDetectionRules.TryDetectOccupation` with:

```csharp
if (ownerIsXia) return false;

if (cityHasXiaIdentity)
{
    if (legalCore && mandateCoreControlRatio >= 0.65f)
    {
        type = TypePseudoDynasty;
        return true;
    }

    type = TypeForeignEntry;
    return true;
}

if (differentCultureOrLanguage)
{
    type = TypeNormalConquest;
    return true;
}

return false;
```

Keep the existing public signature for compatibility, but explicitly discard the now-unneeded origin hint at the top of the method:

```csharp
_ = sameOwnerOriginCity;
```

In `ForeignOccupationService.TryDetectOccupation`, change Xia identity gathering to:

```csharp
bool cityXia = IsXiaOriginCity(pCity) || HasXiaCultureOrLanguage(pCity);
```

Delete the now-unused `HasXiaResidents` method so occupation detection no longer scans every city unit.

- [ ] **Step 4: Run GREEN**

Run: `dotnet run --project F:\tmp\AW3RuleRegression\AW3RuleRegression.csproj`

Expected: PASS and print `AW3 focused rule regressions passed.`

- [ ] **Step 5: Commit the occupation fix**

```powershell
git add -- Code/core/lineage/ForeignOccupationDetectionRules.cs Code/core/lineage/ForeignOccupationService.cs
git commit -m "fix: 严格判定夏地外族入据"
```

### Task 3: Complete AI Court Technology Order

**Files:**
- Modify: `F:\tmp\AW3RuleRegression\Program.cs`
- Create: `Code/core/policy/KingdomPolicyTechOrderRules.cs`
- Modify: `Code/core/policy/KingdomPolicyAI.cs`

- [ ] **Step 1: Add the failing technology-order test**

```csharp
using AncientWarfare3.core.policy;

string[] expectedTechs =
{
    "aw_tech_writing",
    "aw_tech_pottery_casting",
    "aw_tech_bronze_casting",
    "aw_tech_well_field_survey",
    "aw_tech_iron_plow",
    "aw_tech_chariot_training",
    "aw_tech_enfeoffment_study",
    "aw_tech_granary_accounting",
    "aw_tech_city_defense",
    "aw_tech_official_court",
    "aw_tech_rites_music",
    "aw_tech_three_departments"
};

if (KingdomPolicyTechOrderRules.Count != expectedTechs.Length)
    throw new Exception("AI technology order must contain all 12 technologies.");
if (expectedTechs.Distinct(StringComparer.Ordinal).Count() != expectedTechs.Length)
    throw new Exception("The expected technology set contains duplicates.");
for (int index = 0; index < expectedTechs.Length; index++)
{
    string id = expectedTechs[index];
    if (!KingdomPolicyTechOrderRules.Contains(id))
        throw new Exception($"AI technology order is missing {id}.");
    if (KingdomPolicyTechOrderRules.PreferredIndex(id, 99) != index)
        throw new Exception($"Unexpected AI priority for {id}.");
}
if (KingdomPolicyTechOrderRules.PreferredIndex("aw_unknown", 7) != expectedTechs.Length + 7)
    throw new Exception("Unknown technologies must retain their layout fallback priority.");
if (KingdomPolicyTechOrderRules.CanConsider(
        "aw_tech_rites_music", pOfficialCourtCompleted: false, pRitesMusicCompleted: false))
    throw new Exception("AI must not skip official court for rites and music.");
if (KingdomPolicyTechOrderRules.CanConsider(
        "aw_tech_three_departments", pOfficialCourtCompleted: true, pRitesMusicCompleted: false))
    throw new Exception("AI must not skip rites and music for three departments.");
if (!KingdomPolicyTechOrderRules.CanConsider(
        "aw_tech_rites_music", pOfficialCourtCompleted: true, pRitesMusicCompleted: false))
    throw new Exception("AI must allow rites and music after official court.");
if (!KingdomPolicyTechOrderRules.CanConsider(
        "aw_tech_three_departments", pOfficialCourtCompleted: true, pRitesMusicCompleted: true))
    throw new Exception("AI must allow three departments after the court chain is complete.");
```

- [ ] **Step 2: Run RED and confirm the pure order rule is missing**

Run: `dotnet run --project F:\tmp\AW3RuleRegression\AW3RuleRegression.csproj`

Expected: compile FAIL because `KingdomPolicyTechOrderRules` does not exist.

- [ ] **Step 3: Create the complete pure rule**

Create `KingdomPolicyTechOrderRules.cs`:

```csharp
using System;

namespace AncientWarfare3.core.policy
{
    public static class KingdomPolicyTechOrderRules
    {
        private static readonly string[] Order =
        {
            "aw_tech_writing",
            "aw_tech_pottery_casting",
            "aw_tech_bronze_casting",
            "aw_tech_well_field_survey",
            "aw_tech_iron_plow",
            "aw_tech_chariot_training",
            "aw_tech_enfeoffment_study",
            "aw_tech_granary_accounting",
            "aw_tech_city_defense",
            "aw_tech_official_court",
            "aw_tech_rites_music",
            "aw_tech_three_departments"
        };

        public static int Count => Order.Length;

        public static bool Contains(string pId)
        {
            return Array.IndexOf(Order, pId) >= 0;
        }

        public static bool CanConsider(string pId, bool pOfficialCourtCompleted,
            bool pRitesMusicCompleted)
        {
            if (pId == "aw_tech_rites_music") return pOfficialCourtCompleted;
            if (pId == "aw_tech_three_departments")
                return pOfficialCourtCompleted && pRitesMusicCompleted;
            return true;
        }

        public static int PreferredIndex(string pId, int pLayoutFallback)
        {
            int index = Array.IndexOf(Order, pId);
            return index >= 0 ? index : Order.Length + Math.Max(0, pLayoutFallback);
        }
    }
}
```

Add it to the temporary project:

```xml
<Compile Include="F:\WorldBox New Mod\AncientWarfare3.0\Code\core\policy\KingdomPolicyTechOrderRules.cs" Link="KingdomPolicyTechOrderRules.cs" />
```

Delete the private `TechOrder` array from `KingdomPolicyAI.cs`, then change `PreferredIndex` to:

```csharp
private static int PreferredIndex(KingdomPolicyDef pDef)
{
    int layoutFallback = Math.Max(0, pDef.Column * 3 + pDef.Row);
    if (pDef.Kind == PolicyNodeKind.Tech)
        return KingdomPolicyTechOrderRules.PreferredIndex(pDef.Id, layoutFallback);

    int index = Array.IndexOf(SocialOrder, pDef.Id);
    return index >= 0 ? index : SocialOrder.Length + layoutFallback;
}
```

In `PickResearch`, read both completed states once and insert the AI-only gate
before availability filtering:

```csharp
bool officialCourtCompleted = pKind != PolicyNodeKind.Tech ||
    KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Tech, "aw_tech_official_court");
bool ritesMusicCompleted = pKind != PolicyNodeKind.Tech ||
    KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Tech, "aw_tech_rites_music");

return defs
    .Where(def => pKind != PolicyNodeKind.Tech || KingdomPolicyTechOrderRules.CanConsider(
        def.Id, officialCourtCompleted, ritesMusicCompleted))
    .Where(def => !KingdomPolicyService.IsNodeLocked(pKingdom, def.Id))
    .Where(def => IsAvailable(pKingdom, def))
    .OrderByDescending(def => ScoreResearch(pKingdom, def))
    .FirstOrDefault();
```

This filter is necessary because the Confucian court bonus gives `rites_music`
90 points while adjacent order positions differ by only 20 points. It affects AI
choice only and deliberately does not cancel an in-progress project.

- [ ] **Step 4: Run GREEN**

Run: `dotnet run --project F:\tmp\AW3RuleRegression\AW3RuleRegression.csproj`

Expected: PASS and print `AW3 focused rule regressions passed.`

- [ ] **Step 5: Commit the AI order fix**

```powershell
git add -- Code/core/policy/KingdomPolicyTechOrderRules.cs Code/core/policy/KingdomPolicyAI.cs
git commit -m "fix: 补全官场科技研发顺序"
```

### Task 4: Dual-Configuration Verification

**Files:**
- Verify: `AncientWarfare3.csproj`
- Verify: all production files changed in Tasks 1-3

- [ ] **Step 1: Run all focused rule regressions**

Run: `dotnet run --project F:\tmp\AW3RuleRegression\AW3RuleRegression.csproj`

Expected: exit 0 and `AW3 focused rule regressions passed.`

- [ ] **Step 2: Build the normal configuration with Chinese Name integration**

The machine has the net48 reference assemblies in the NuGet cache rather than a
machine-wide targeting pack. Restore and build with that root explicitly:

```powershell
dotnet restore AncientWarfare3.csproj --ignore-failed-sources `
  '-p:TargetFrameworkRootPath=C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\'
dotnet build AncientWarfare3.csproj --no-restore `
  '-p:TargetFrameworkRootPath=C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\' `
  '-p:BaseOutputPath=F:\tmp\AW3Build\with-cn\'
```

Expected: build succeeds with 0 errors; the `一米_中文名` symbol excludes AW3's alliance Postfix.

- [ ] **Step 3: Build the fallback configuration without the optional symbol**

Use a separate intermediate directory so this build cannot reuse the binary from
the previous symbol configuration:

```powershell
dotnet restore AncientWarfare3.csproj --ignore-failed-sources `
  '-p:TargetFrameworkRootPath=C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\' `
  '-p:BaseIntermediateOutputPath=F:\tmp\AW3Build\without-cn-obj\'
dotnet build AncientWarfare3.csproj --no-restore `
  '-p:TargetFrameworkRootPath=C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\' `
  '-p:DefineConstants=DEBUG%3BTRACE' `
  '-p:BaseIntermediateOutputPath=F:\tmp\AW3Build\without-cn-obj\' `
  '-p:BaseOutputPath=F:\tmp\AW3Build\without-cn\'
```

Expected: build succeeds with 0 errors; AW3's alliance Postfix compiles and uses English fallback names.

- [ ] **Step 4: Check formatting, commits, and preserved deletions**

Run: `git diff --check`

Expected: no whitespace errors.

Run: `git status -sb`

Expected: only the user's intentional `Tests/` and `Verification/` deletions remain unstaged; no `F:\tmp` files appear in Git.

Run: `git log --oneline -n 5`

Expected: separate plan, alliance, occupation, and AI-order commits are visible on `master`; no push has occurred.

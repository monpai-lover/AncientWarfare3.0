# AW3 Primitive Court Roster Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the valid registered heir as a standalone primitive-court rank between the king and generals while preserving actor-ID role deduplication.

**Architecture:** Add one pure tier/validity rule and one heir rank constant to `CourtPyramidRules`. `CourtReadModelService` resolves the cached heir once, adds an heir seed only for the primitive tier, and lets the existing layout merge combine concurrent heir, general, and governor roles.

**Tech Stack:** C# 11, .NET Framework 4.8, Unity UI read models, WorldBox actor/kingdom APIs, temporary .NET 9 console rule tests.

**Execution constraints:** Work directly on `master`, execute inline without subagents, and never stage the user's intentional `Tests/` or `Verification/` deletions.

---

## File Map

- Modify `Code/core/court/CourtPyramidRules.cs`: define heir rank and the pure primitive-tier inclusion rule.
- Modify `Code/core/court/CourtReadModelService.cs`: add the valid cached heir seed before layout.
- Modify only temporarily `F:\tmp\AW3CourtExpansionRuleTests\Program.cs`: cover tier gating, rank, runtime wiring, and actor-ID deduplication.

### Task 1: Define And Verify Primitive Heir Placement

**Files:**
- Modify: `F:\tmp\AW3CourtExpansionRuleTests\Program.cs`
- Modify: `Code/core/court/CourtPyramidRules.cs`

- [ ] **Step 1: Add failing pure-rule and layout tests**

Add these assertions before the final success print:

```csharp
Check(CourtPyramidRules.ShouldAddStandaloneHeir(CourtTier.Primitive, hasValidHeir: true),
    "primitive court must force a valid standalone heir seed");
Check(!CourtPyramidRules.ShouldAddStandaloneHeir(CourtTier.SanGongJiuQing, hasValidHeir: true) &&
      !CourtPyramidRules.ShouldAddStandaloneHeir(CourtTier.SanShengLiuBu, hasValidHeir: true),
    "official courts must not force a standalone heir seed");
Check(!CourtPyramidRules.ShouldAddStandaloneHeir(CourtTier.Primitive, hasValidHeir: false),
    "primitive court must omit an invalid or missing heir");
Check(CourtPyramidRules.HeirRank > CourtPyramidRules.KingRank &&
      CourtPyramidRules.HeirRank < CourtPyramidRules.GeneralRank,
    "heir rank must sit below the king and above generals");

List<CourtPyramidNodeModel> mergedHeir = CourtPyramidRules.BuildLayout(
    new[]
    {
        new CourtPyramidNodeModel(2, CourtPyramidRoleId.Heir,
            CourtPyramidRoleId.Heir, CourtPyramidRules.HeirRank, 0, false),
        new CourtPyramidNodeModel(2, CourtPyramidRoleId.General,
            CourtPyramidRoleId.General, CourtPyramidRules.GeneralRank, 0, false),
        new CourtPyramidNodeModel(2, CourtOfficeId.Governor,
            CourtPyramidRoleId.Governor, CourtPyramidRules.GovernorRank, 0, false)
    }, 100f, 80f);
Check(mergedHeir.Count == 1 && mergedHeir[0].Rank == CourtPyramidRules.HeirRank &&
      mergedHeir[0].Roles.SequenceEqual(new[]
      {
          CourtPyramidRoleId.Heir,
          CourtPyramidRoleId.General,
          CourtPyramidRoleId.Governor
      }),
    "heir concurrent roles must merge into one heir-rank node");
```

- [ ] **Step 2: Run RED**

Run:

```powershell
dotnet run --project F:\tmp\AW3CourtExpansionRuleTests\AW3CourtExpansionRuleTests.csproj
```

Expected: compilation fails because `HeirRank` and `ShouldAddStandaloneHeir` do not exist.

- [ ] **Step 3: Add the minimal pure rule**

In `CourtPyramidRules`, add:

```csharp
public const int KingRank = 0;
public const int HeirRank = 10;
public const int HighOfficeRank = 10;

public static bool ShouldAddStandaloneHeir(string pTier, bool hasValidHeir)
{
    return hasValidHeir && pTier == CourtTier.Primitive;
}
```

Keep the existing official-office rank values unchanged.

- [ ] **Step 4: Run GREEN for the pure behavior**

Run the court harness again. Expected: the new tier/rank/dedup assertions pass; any runtime-wiring assertion added in Task 2 still fails until Task 2 is implemented.

### Task 2: Wire The Cached Heir Into The Primitive Read Model

**Files:**
- Modify: `F:\tmp\AW3CourtExpansionRuleTests\Program.cs`
- Modify: `Code/core/court/CourtReadModelService.cs`

- [ ] **Step 1: Add failing runtime-wiring assertions**

Extend the existing `courtReadModelSource` checks:

```csharp
Check(courtReadModelSource.Contains("AddPrimitiveHeir(seeds, pKingdom, tier)",
        StringComparison.Ordinal),
    "court read model must add the primitive heir seed before layout");
Check(courtReadModelSource.Contains("HeirService.PeekRegisteredHeir(pKingdom)",
        StringComparison.Ordinal),
    "primitive heir must come from the cached O(1) heir lookup");
Check(!courtReadModelSource.Contains("pKingdom.getUnits()", StringComparison.Ordinal),
    "primitive heir display must not add a kingdom population scan");
```

- [ ] **Step 2: Run RED for runtime wiring**

Run the court harness. Expected: fail with `court read model must add the primitive heir seed before layout`.

- [ ] **Step 3: Add the primitive heir seed**

Resolve the tier once in `Build`, add the heir before the other dynamic roles, and pass the tier into the office/vacancy method:

```csharp
string tier = CourtService.ResolveTier(pKingdom);
AddKing(seeds, pKingdom);
AddPrimitiveHeir(seeds, pKingdom, tier);
List<CourtOfficerView> officers = CourtService.GetActiveOfficers(pKingdom, 96);
AddOfficersAndVacancies(seeds, pKingdom, officers, tier);
```

Implement the helper without any unit enumeration:

```csharp
private static void AddPrimitiveHeir(List<CourtPyramidNodeModel> pSeeds,
    Kingdom pKingdom, string pTier)
{
    Actor heir = HeirService.PeekRegisteredHeir(pKingdom);
    if (!CourtPyramidRules.ShouldAddStandaloneHeir(pTier, heir?.data != null)) return;
    string school = ActorSchool(heir, CourtSchoolId.Ru);
    pSeeds.Add(new CourtPyramidNodeModel(heir.data.id, CourtPyramidRoleId.Heir,
        CourtPyramidRoleId.Heir, CourtPyramidRules.HeirRank, 0, false)
    {
        ActorName = SafeActorName(heir),
        SchoolId = school,
        SchoolIconPath = RegisteredSchoolIconPath(school),
        Influence = SafeStat(heir, "stewardship")
    });
}
```

Change `AddOfficersAndVacancies` to accept `string pTier` and use
`CourtTierRules.CentralOfficesForTier(pTier)`. Keep `AddCachedHeirRole` after
layout so official tiers retain their existing merge-only behavior.

- [ ] **Step 4: Run GREEN and focused regressions**

Run:

```powershell
dotnet run --project F:\tmp\AW3CourtExpansionRuleTests\AW3CourtExpansionRuleTests.csproj
dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj
```

Expected: both exit 0 with `court school rules passed` and `direct-son rules passed`.

- [ ] **Step 5: Build both configurations**

Run:

```powershell
dotnet restore AncientWarfare3.csproj --ignore-failed-sources '-p:RestorePackagesPath=C:\Users\24908\.nuget\packages' '-p:TargetFrameworkRootPath=C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\'
dotnet build AncientWarfare3.csproj --no-restore '-p:RestorePackagesPath=C:\Users\24908\.nuget\packages' '-p:TargetFrameworkRootPath=C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\'
dotnet build AncientWarfare3.csproj --no-restore '-p:RestorePackagesPath=C:\Users\24908\.nuget\packages' '-p:TargetFrameworkRootPath=C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\' -p:DefineConstants=DEBUG%3BTRACE
```

Expected: restore succeeds from the existing cache; both builds report 0 warnings and 0 errors.

- [ ] **Step 6: Audit and commit only production files**

Run `git diff --check` and `git status --short`. Confirm the only unrelated
changes are the user's unstaged test-directory deletions, then commit:

```powershell
git add -- Code/core/court/CourtPyramidRules.cs Code/core/court/CourtReadModelService.cs
git commit -m "feat: show heir in primitive court"
```

Do not stage the temporary harness or any deleted repository tests.

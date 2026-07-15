# AW3 Kingdom Extinction School Affiliation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow school members to complete WorldBox's cityless-kingdom survivor conversion without weakening normal travel nationality rules.

**Architecture:** Put the exceptional transfer decision in a pure rule and call it from the existing affiliation guard. Keep extinction detection and actor mutation in their existing owners; the new rule only identifies the exact source/target lifecycle transition.

**Tech Stack:** C# 11, .NET Framework 4.8, WorldBox publicized API, Harmony, PowerShell source guards, .NET 9 pure rule harness.

---

### Task 1: Extinction Release Rule

**Files:**
- Create: `Code/core/schools/SchoolAffiliationTransferRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] Add assertions showing that a live cityless civilization with a stable
  city index may transfer a school actor only to the actor's own wild kingdom.
- [ ] Run
  `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release`
  and verify compilation fails because `SchoolAffiliationTransferRules` does
  not exist.
- [ ] Add this minimal rule:

```csharp
public static bool AllowsExtinctionRelease(
    bool sourceIsLiveCivilization,
    bool cityIndexStable,
    bool sourceHasCities,
    bool targetMatchesActorWildKingdom)
{
    return sourceIsLiveCivilization && cityIndexStable && !sourceHasCities &&
           targetMatchesActorWildKingdom;
}
```

- [ ] Link the production rule into the rules project and rerun the harness.

### Task 2: Affiliation Guard Integration

**Files:**
- Modify: `Code/core/schools/HistoricalAffiliationService.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Add source guards requiring `CanJoinKingdom` to call
  `SchoolAffiliationTransferRules.AllowsExtinctionRelease`, read
  `World.world.kingdoms.hasDirtyCities()`, and compare the target asset ID with
  `pActor.asset.kingdom_id_wild`.
- [ ] Run `powershell -ExecutionPolicy Bypass -File Tests/SourceGuardTests.ps1`
  and verify the new guards fail.
- [ ] In `CanJoinKingdom`, derive the source kingdom, stable-index state,
  source city state, and exact wild target match. Return `true` when the pure
  rule allows the extinction release, then preserve the existing normal travel
  checks unchanged.
- [ ] Rerun the rules harness and source guards.

### Task 3: Academy Scale

**Files:**
- Modify: `Tests/SourceGuardTests.ps1`
- Modify: `Code/content/schools/SchoolAcademyBuildingContent.cs`

- [ ] Change the academy scale source guard to require
  `new Vector3(0.07975f, 0.07975f, 0.25f)` and run it to observe failure against
  the current `0.055f` implementation.
- [ ] Change only `academy.scale_base` to the required vector; retain
  `BuildingFundament(3, 3, 2, 0)`.
- [ ] Rerun the source guards and confirm the scale and footprint checks pass.

### Task 4: Full Verification and Delivery

**Files:**
- No additional source files.

- [ ] Run the Release rules harness and all PowerShell source guards.
- [ ] Run `dotnet build AncientWarfare3.csproj -c Debug --no-restore` and the
  matching Release build.
- [ ] Run `git diff --check` and inspect the staged diff by commit group.
- [ ] Stop WorldBox, deploy the repository payload to the installed AW3 mod,
  start a fresh run, and inspect the latest log for the original three null
  reference signatures.
- [ ] Commit the extinction fix and academy scale as separate logical commits,
  then push `master` to `origin` as previously requested.

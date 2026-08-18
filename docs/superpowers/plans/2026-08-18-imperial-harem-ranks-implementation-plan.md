# Imperial Harem Ranks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add fixed imperial harem ranks, commoner consort eligibility, and deterministic succession-safe persistence while preserving all lower-tier household behavior.

**Architecture:** Keep `RulerHousehold` as the only relationship table. Add a pure rank resolver and a bounded normalization service; persist stable rank codes on insert and normalize legacy imperial rows once before read-model construction. Keep female-ruler and non-imperial behavior unchanged.

**Tech Stack:** C# 11/net48 production DLL, SQLite persistence, Unity UI, PowerShell source guards, net9 isolated rules tests.

---

## File Map

- Create: `Code/core/lineage/RulerHouseholdRankRules.cs` for fixed seats, rank resolution, and pure scoring.
- Create: `Code/core/lineage/RulerHouseholdRankMigrationService.cs` for idempotent legacy normalization.
- Modify: `Code/core/lineage/RulerHouseholdRules.cs` for imperial capacity and candidate-class gates.
- Modify: `Code/core/lineage/RulerHouseholdModels.cs` for rank-slot metadata in read rows.
- Modify: `Code/core/lineage/RulerHouseholdQuery.cs` for commoner candidate SQL and rank-aware reads.
- Modify: `Code/core/lineage/RulerHouseholdService.cs` for rank assignment and migration calls.
- Modify: `Code/core/lineage/RulerHouseholdReadModelService.cs` for fixed title and historical prefix data.
- Modify: `Code/core/db/DiplomacyActionIndexRules.cs` for active-rank uniqueness/index support.
- Modify: `Locales/aw3_ruler_household.csv` for ten titles and historical labels.
- Modify: `Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt` for rule coverage.
- Create: `Tests/ImperialHaremRules.Isolated.Tests/ImperialHaremRules.Isolated.Tests.csproj` and `Program.cs` for pure rank tests.
- Create: `Tests/ImperialHaremRules.Isolated.Tests/ImperialHaremRankRulesTests.cs` for migration and scoring tests.
- Create: `Tests/RulerHouseholdImperialSourceGuard.ps1` for production wiring and bounded-query checks.

### Task 1: Add Pure Fixed-Rank Rules

**Files:**
- Create: `Code/core/lineage/RulerHouseholdRankRules.cs`
- Modify: `Code/core/lineage/RulerHouseholdRules.cs`
- Test: `Tests/ImperialHaremRules.Isolated.Tests/ImperialHaremRankRulesTests.cs`

- [ ] **Step 1: Write failing rule tests.** Add tests for the exact order, empire capacity 9 consorts, non-empire fallback, commoner consort eligibility, and age non-revocation:

```csharp
Equal("empress", RulerHouseholdRankRules.SeatCode(0), "first imperial seat");
Equal("consort_kang", RulerHouseholdRankRules.SeatCode(9), "last imperial seat");
Equal(9, RulerHouseholdRules.ConsortCapacity(RulerHouseholdRealmTier.Empire),
    "imperial active consort capacity");
True(RulerHouseholdRules.IsCandidateClassEligible(
    RulerHouseholdCandidateClass.Commoner,
    RulerHouseholdKind.Consort), "qualified commoner consort");
False(RulerHouseholdRules.IsCandidateClassEligible(
    RulerHouseholdCandidateClass.Commoner,
    RulerHouseholdKind.PrincipalWife), "commoner is not principal wife by default");
True(RulerHouseholdRankRules.KeepsSeatAfterAge(36), "age does not revoke rank");
```

Add the isolated project links so the test compiles the production rules
directly:

```xml
<Compile Include="Program.cs" />
<Compile Include="ImperialHaremRankRulesTests.cs" />
<Compile Include="..\..\Code\core\lineage\RulerHouseholdRules.cs"
         Link="Production\RulerHouseholdRules.cs" />
<Compile Include="..\..\Code\core\lineage\RulerHouseholdRankRules.cs"
         Link="Production\RulerHouseholdRankRules.cs" />
```

- [ ] **Step 2: Run the isolated rules project and verify it fails.**

Run: `dotnet run --project Tests/ImperialHaremRules.Isolated.Tests/ImperialHaremRules.Isolated.Tests.csproj`

Expected: compile failure because `RulerHouseholdRankRules` and the new capacity behavior do not exist.

- [ ] **Step 3: Implement the minimal pure rules.** Use a stable array and no locale text in the domain layer:

```csharp
public static readonly string[] ImperialSeatCodes =
{
    "empress", "consort_de", "consort_li", "consort_zhuang",
    "consort_xian", "consort_hui", "consort_an", "consort_he",
    "consort_xi", "consort_kang"
};

public static string SeatCode(int pSlot) =>
    pSlot >= 0 && pSlot < ImperialSeatCodes.Length
        ? ImperialSeatCodes[pSlot] : "";

public static bool IsFixedImperialRank(string pRankCode) =>
    Array.IndexOf(ImperialSeatCodes, pRankCode ?? "") >= 0;

public static bool KeepsSeatAfterAge(int pAge) => true;

public static string NextEmptySeat(ISet<string> pUsed, bool pPrincipal)
{
    if (pUsed == null) return "";
    int first = pPrincipal ? 0 : 1;
    for (int i = first; i < ImperialSeatCodes.Length; i++)
        if (!pUsed.Contains(ImperialSeatCodes[i])) return ImperialSeatCodes[i];
    return "";
}
```

Update `ConsortCapacity` to return `ImperialSeatCodes.Length - 1` for
`Empire`, and update the class gate so commoners are eligible only for
`Consort`; noble and existing domestic-slave behavior remain unchanged.

- [ ] **Step 4: Run the isolated project and the existing household slice.**

Run: `dotnet run --project Tests/ImperialHaremRules.Isolated.Tests/ImperialHaremRules.Isolated.Tests.csproj`

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --ruler-household`

Expected: both commands pass; existing tests that assert eight imperial consorts must be updated to assert nine.

- [ ] **Step 5: Commit the pure-rule slice.**

```powershell
git add -- Code/core/lineage/RulerHouseholdRankRules.cs Code/core/lineage/RulerHouseholdRules.cs Tests/ImperialHaremRules.Isolated.Tests Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt
git commit -m "feat: define fixed imperial harem ranks"
```

### Task 2: Admit Qualified Commoner Consorts Without World Scans

**Files:**
- Modify: `Code/core/lineage/RulerHouseholdQuery.cs`
- Modify: `Code/core/lineage/RulerHouseholdService.cs`
- Modify: `Code/core/lineage/RulerHouseholdModels.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt`
- Create: `Tests/RulerHouseholdImperialSourceGuard.ps1`

- [ ] **Step 1: Add ranking facts and a pure score test.** Extend the offer candidate model with `CandidateClass`, `AttributeScore`, and `LineagePriority`; assert a stronger commoner outranks a weaker non-lineage noble for consort selection while a commoner never passes the principal-wife gate.

```csharp
int commoner = RulerHouseholdRankRules.ConsortScore(
    attributeScore: 92, lineagePriority: 0, noble: false);
int noble = RulerHouseholdRankRules.ConsortScore(
    attributeScore: 61, lineagePriority: 0, noble: true);
True(commoner > noble, "attributes outrank noble status for consorts");
```

- [ ] **Step 2: Run the household slice and verify the new score test fails.**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --ruler-household`

Expected: failure for the missing score or commoner selection behavior.

- [ ] **Step 3: Replace the archive candidate class predicate.** In `ReadOfferCandidateIds`, keep the existing noble-only predicate for principal wives, but use a parameterized non-slave predicate for consorts:

```csharp
string candidateClassWhere = pKind == RulerHouseholdKind.PrincipalWife
    ? "STATUS='noble' AND LINEAGE_ID>=0 AND SHI_ID>=0"
    : pIncludeSlaves
        ? "IFNULL(STATUS,'') NOT IN ('slave','slave_lineage') OR STATUS='slave_lineage'"
        : "IFNULL(STATUS,'') NOT IN ('slave','slave_lineage')";
```

The production query must still use `IS_ALIVE`, sex, age bounds, excluded-parent, kingdom, and `LIMIT @limit`; do not enumerate `World.world.units`.

- [ ] **Step 4: Score live candidates after the bounded archive query.** `PrepareCandidate` remains the authority for safety gates. For eligible consorts, calculate the existing attribute-derived score and apply noble/lineage values only as tie-breakers. Keep source/diplomatic offers with their existing `pIncludeSlaves` value and preserve the foreign-offer slave rejection.

- [ ] **Step 5: Add source guards.** `Tests/RulerHouseholdImperialSourceGuard.ps1` must require the commoner predicate, `LIMIT @limit`, `RulerHouseholdRankRules.ConsortScore(`, and reject `World.world.units_only_alive`, `getSimpleList()`, and unbounded `foreach (Actor` in household candidate code.

- [ ] **Step 6: Run source guards and commit.**

Run: `powershell -ExecutionPolicy Bypass -File Tests/RulerHouseholdImperialSourceGuard.ps1`

```powershell
git add -- Code/core/lineage/RulerHouseholdQuery.cs Code/core/lineage/RulerHouseholdService.cs Code/core/lineage/RulerHouseholdModels.cs Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt Tests/RulerHouseholdImperialSourceGuard.ps1
git commit -m "feat: allow qualified commoners into imperial consorts"
```

### Task 3: Persist Fixed Seats and Migrate Existing Saves

**Files:**
- Create: `Code/core/lineage/RulerHouseholdRankMigrationService.cs`
- Modify: `Code/core/lineage/RulerHouseholdService.cs`
- Modify: `Code/core/lineage/RulerHouseholdQuery.cs`
- Modify: `Code/core/db/DiplomacyActionIndexRules.cs`
- Test: `Tests/ImperialHaremRules.Isolated.Tests/ImperialHaremRankRulesTests.cs`

- [ ] **Step 1: Write migration tests.** Cover deterministic assignment, idempotent second execution, and the over-capacity close rule:

```csharp
var rows = new[]
{
    Legacy(30, RulerHouseholdKind.PrincipalWife),
    Legacy(10, RulerHouseholdKind.Consort),
    Legacy(20, RulerHouseholdKind.Consort)
};
var first = RulerHouseholdRankMigrationService.AssignLegacy(rows);
var second = RulerHouseholdRankMigrationService.AssignLegacy(first);
Equal("empress", first[0].RankCode, "principal wife gets empress");
Equal("consort_de", first[1].RankCode, "oldest consort gets first seat");
Equal(first[1].RankCode, second[1].RankCode, "migration is idempotent");
```

- [ ] **Step 2: Run the isolated project and verify the migration tests fail.**

Run: `dotnet run --project Tests/ImperialHaremRules.Isolated.Tests/ImperialHaremRules.Isolated.Tests.csproj`

Expected: compile failure for the migration service.

- [ ] **Step 3: Implement bounded transactional normalization.** Read only active records for one ruler, order by principal-kind, `START_YEAR`, `START_TIME`, and `RELATIONSHIP_ID`, assign missing/legacy codes, and close active records after slot 10 with `legacy_harem_over_capacity`. Re-running must detect valid fixed codes and perform zero writes.

- [ ] **Step 4: Wire normalization before reads and rank assignment before inserts.** Call normalization from the existing authority-year household maintenance and from imperial read-model construction as a repair fallback. In `InsertRelationship`, resolve the next unused fixed seat inside the same transaction and persist the stable rank code; lower-tier realms continue to store the existing generic title key.

- [ ] **Step 5: Add active-rank index protection.** Add a partial index for active fixed rank codes scoped by ruler. The migration must clear conflicting legacy rows before the index is created so old saves cannot fail schema initialization.

- [ ] **Step 6: Add localization and historical title composition.** Add the ten title keys plus `aw_household_historical_prefix` to `Locales/aw3_ruler_household.csv`. `RulerHouseholdReadModelService` uses the fixed rank code for the current row and prefixes a closed former-ruler row only when it is rendering historical context; it never rewrites `RankCode` to localized text.

- [ ] **Step 7: Run full verification and commit.**

Run: `dotnet run --project Tests/ImperialHaremRules.Isolated.Tests/ImperialHaremRules.Isolated.Tests.csproj`

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --ruler-household`

Run: `powershell -ExecutionPolicy Bypass -File Tests/RulerHouseholdImperialSourceGuard.ps1`

```powershell
git add -- Code/core/lineage/RulerHouseholdRankMigrationService.cs Code/core/lineage/RulerHouseholdService.cs Code/core/lineage/RulerHouseholdQuery.cs Code/core/db/DiplomacyActionIndexRules.cs Code/core/lineage/RulerHouseholdReadModelService.cs Locales/aw3_ruler_household.csv Tests/ImperialHaremRules.Isolated.Tests Tests/RulerHouseholdImperialSourceGuard.ps1
git commit -m "feat: persist imperial harem seats across succession"
```

## Plan Self-Check

- Fixed ten-seat order, nine-consort capacity, succession history, commoner
  consorts, and age behavior are covered by Tasks 1-3.
- Lower-tier fallback and female-ruler behavior remain explicit in Task 1 and
  the insert path in Task 3.
- All candidate queries stay bounded and indexed; the source guard enforces it.

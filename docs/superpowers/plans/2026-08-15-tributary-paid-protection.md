# Tributary Paid Protection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn loose tributary relations into annually renewed paid protection, prevent every direct suzerain/tributary war path while the relation is active, and optionally place one eligible tributary noblewoman in a bounded royal household after successful payment.

**Architecture:** Keep payment math, protection decisions, and recipient ordering in pure rule classes. Persist each relation's due/attempt/paid state independently, run settlement from the existing annual suzerain callback, and put the same authoritative protection predicate in UI/AI queueing, declaration submission, service start, and the final Harmony engine gate. Generalize `RulerHousehold` from current-ruler ownership to explicit owner ownership while preserving old diplomacy behavior and bounded monthly pregnancy work.

**Tech Stack:** C# 9, .NET Framework 4.8 production mod, SQLite, Harmony/WorldBox APIs, .NET 9 rules test executable, PowerShell source guards and source-only deployment.

---

## File Map

- Create `Code/core/lineage/TributaryPaymentRules.cs`: pure tier, scaling, and payment-outcome rules.
- Create `Code/core/lineage/TributarySettlementPersistence.cs`: relation-scoped due/attempt/paid SQLite writes.
- Create `Code/core/lineage/TributarySettlementService.cs`: annual transfer, renewal, ending, offering, history, and diagnostics.
- Create `Code/core/lineage/TributaryProtectionRules.cs` and `TributaryProtectionService.cs`: pure and authoritative symmetric relation predicates.
- Create `Code/core/lineage/RoyalHouseholdRecipientRules.cs` and `TributaryHouseholdOfferingService.cs`: recipient/capacity policy and one-woman offering orchestration.
- Modify `Code/core/db/VassalRelationTableItem.cs` and `RulerHouseholdTableItem.cs`: backward-compatible reflected schema fields.
- Modify `Code/core/lineage/VassalService.cs`, `VassalAIService.cs`, `WarDecisionService.cs`, `WarTerritoryService.cs`, `DiplomaticWarDeclarationService.cs`, and `Code/patch/AW_WarPatch.cs`: annual integration and all war gates.
- Modify `Code/core/lineage/RulerHouseholdModels.cs`, `RulerHouseholdQuery.cs`, `RulerHouseholdRules.cs`, `RulerHouseholdService.cs`, and `RulerHouseholdPregnancyService.cs`: explicit owner, source-aware lifecycle, and bounded pregnancy.
- Modify `Code/core/lineage/LineageDTO.cs`, `Code/ui/items/VassalRelationListItem.cs`, `HistoryLocalizationRules.cs`, and localization CSVs: display and history.
- Create focused tests and add `--tributary-protection-only` to the existing rules executable.

### Task 1: Lock Payment Tiers and Scaling with Pure Tests

**Files:**
- Create: `Code/core/lineage/TributaryPaymentRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/TributaryPaymentRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add the focused test entry before unconditional runs**

Add at the top of `Program.cs.txt`:

```csharp
if (args.Length == 1 && args[0] == "--tributary-protection-only")
{
    TributaryPaymentRulesTests.Run();
    TributaryRelationPersistenceTests.Run();
    TributaryProtectionRulesTests.Run();
    RoyalHouseholdRecipientRulesTests.Run();
    TributaryProtectionSourceGuardTests.Run();
    Console.WriteLine("Tributary paid protection tests passed.");
    return;
}
```

Add the Task 1 compile items:

```xml
<Compile Include="TributaryPaymentRulesTests.cs.txt" />
<Compile Include="..\..\Code\core\lineage\TributaryPaymentRules.cs" Link="Production\TributaryPaymentRules.cs" />
```

- [ ] **Step 2: Write the failing boundary and fulfillment tests**

Create `TributaryPaymentRulesTests.cs.txt`:

```csharp
using AncientWarfare3.core.lineage;

internal static class TributaryPaymentRulesTests
{
    public static void Run()
    {
        Equal(100, TributaryPaymentRules.FactorPercent(49.99f, 100f), "49.99 percent");
        Equal(75, TributaryPaymentRules.FactorPercent(50f, 100f), "50 boundary");
        Equal(50, TributaryPaymentRules.FactorPercent(75f, 100f), "75 boundary");
        Equal(25, TributaryPaymentRules.FactorPercent(100f, 100f), "100 boundary");
        Equal(25, TributaryPaymentRules.FactorPercent(124.99f, 100f), "124.99 percent");
        Equal(0, TributaryPaymentRules.FactorPercent(125f, 100f), "125 refusal");
        Equal(0, TributaryPaymentRules.FactorPercent(1f, 0f), "power against zero refuses");
        Equal(100, TributaryPaymentRules.FactorPercent(0f, 0f), "two powerless realms");
        Equal(1, TributaryPaymentRules.ScaleGold(1, 25, 1), "minimum positive gold");
        Equal(2, TributaryPaymentRules.ScaleGold(10, 25, 99), "gold floor");
        Near(3f, TributaryPaymentRules.ScalePolitical(12f, 25, 99f), "political scaling");
        Equal(true, TributaryPaymentRules.IsPaid(.01f, 0), "political renews");
        Equal(true, TributaryPaymentRules.IsPaid(0f, 1), "gold renews");
        Equal(false, TributaryPaymentRules.IsPaid(0f, 0), "zero transfer fails");
        Equal("tribute_refused_power", TributaryPaymentRules.EndReason(0, 0f, 0), "refusal reason");
        Equal("tribute_unpaid", TributaryPaymentRules.EndReason(25, 0f, 0), "insolvency reason");
    }

    private static void Near(float expected, float actual, string name)
    {
        if (Math.Abs(expected - actual) > .0001f)
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }
}
```

- [ ] **Step 3: Run the slice and verify the new rule is missing**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --tributary-protection-only
```

Expected: compilation fails on `TributaryPaymentRules`. Keep only the Task 1 test call enabled until later test files exist.

- [ ] **Step 4: Implement the minimal pure rules**

Create `TributaryPaymentRules.cs`:

```csharp
using System;

namespace AncientWarfare3.core.lineage
{
    public static class TributaryPaymentRules
    {
        public static float PowerRatio(float tributaryPower, float suzerainPower)
        {
            float tributary = Normalize(tributaryPower);
            float suzerain = Normalize(suzerainPower);
            if (suzerain <= 0f) return tributary <= 0f ? 0f : float.PositiveInfinity;
            return tributary / suzerain;
        }

        public static int FactorPercent(float tributaryPower, float suzerainPower)
        {
            float ratio = PowerRatio(tributaryPower, suzerainPower);
            if (ratio >= 1.25f) return 0;
            if (ratio >= 1f) return 25;
            if (ratio >= .75f) return 50;
            if (ratio >= .5f) return 75;
            return 100;
        }

        public static int ScaleGold(int baseRequest, int factorPercent, int available)
        {
            int request = Math.Max(0, baseRequest);
            int factor = Math.Max(0, Math.Min(100, factorPercent));
            int stock = Math.Max(0, available);
            if (request == 0 || factor == 0 || stock == 0) return 0;
            int scaled = (int)Math.Floor(request * factor / 100d);
            return Math.Min(stock, Math.Max(1, scaled));
        }

        public static float ScalePolitical(float baseRequest,
            int factorPercent, float available)
        {
            float factor = Math.Max(0, Math.Min(100, factorPercent)) / 100f;
            return Math.Min(Normalize(available), Normalize(baseRequest) * factor);
        }

        public static bool IsPaid(float political, int gold) =>
            Normalize(political) > 0f || gold > 0;

        public static string EndReason(int factor, float political, int gold)
        {
            if (factor <= 0) return "tribute_refused_power";
            return IsPaid(political, gold) ? "" : "tribute_unpaid";
        }

        private static float Normalize(float value) =>
            float.IsNaN(value) || value < 0f ? 0f : value;
    }
}
```

- [ ] **Step 5: Run the Task 1 slice**

Expected: `Tributary paid protection tests passed.`

- [ ] **Step 6: Commit Task 1 only**

```powershell
git add -- Code/core/lineage/TributaryPaymentRules.cs Tests/AncientWarfare3.Rules.Tests/TributaryPaymentRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: define tributary payment tiers"
```

### Task 2: Persist Relation-Scoped Annual Settlement State

**Files:**
- Modify: `Code/core/db/VassalRelationTableItem.cs`
- Create: `Code/core/lineage/TributarySettlementPersistence.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/TributaryRelationPersistenceTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Add reflected old-save fields**

Append to `VassalRelationTableItem`:

```csharp
[TableItemDef(pDefaultValue: "-1")] public int last_tribute_attempt_year = -1;
[TableItemDef(pDefaultValue: "-1")] public int last_tribute_paid_year = -1;
[TableItemDef(pDefaultValue: "-1")] public int next_tribute_due_year = -1;
[TableItemDef(pDefaultValue: "-1")] public int last_tribute_factor_percent = -1;
```

- [ ] **Step 2: Write failing in-memory SQLite tests**

Create two active loose rows and one formal row. Assert:

```csharp
TributarySettlementPersistence.InitializeNewRelation(db, 10L, 140);
Equal(141, ScalarInt(db, "SELECT NEXT_TRIBUTE_DUE_YEAR FROM VassalRelation WHERE RELATION_ID=10"), "new due year");
Equal(true, TributarySettlementPersistence.TryBeginAttempt(db, 10L, 141), "first attempt");
Equal(false, TributarySettlementPersistence.TryBeginAttempt(db, 10L, 141), "same-year duplicate");
TributarySettlementPersistence.MarkPaid(db, 10L, 141, 75);
Equal(141, ScalarInt(db, "SELECT LAST_TRIBUTE_PAID_YEAR FROM VassalRelation WHERE RELATION_ID=10"), "paid year");
Equal(142, ScalarInt(db, "SELECT NEXT_TRIBUTE_DUE_YEAR FROM VassalRelation WHERE RELATION_ID=10"), "one-year renewal");
Equal(75, ScalarInt(db, "SELECT LAST_TRIBUTE_FACTOR_PERCENT FROM VassalRelation WHERE RELATION_ID=10"), "factor");
Equal(true, TributarySettlementPersistence.TryBeginAttempt(db, 11L, 141), "legacy minus-one due now");
Equal(false, TributarySettlementPersistence.TryBeginAttempt(db, 12L, 141), "formal row excluded");
```

Also prove relations 10 and 11 do not mutate each other's paid/due values.

- [ ] **Step 3: Run and verify the persistence type is missing**

Expected: compilation fails on `TributarySettlementPersistence`.

- [ ] **Step 4: Implement atomic persistence**

Create `TributarySettlementPersistence.cs` with these operations:

```csharp
internal static void InitializeNewRelation(SQLiteConnection db,
    long relationId, int currentYear)
```

Update the exact active loose row, set attempts/paid/factor to `-1`, and `NEXT_TRIBUTE_DUE_YEAR=currentYear+1`; require one changed row.

```csharp
internal static bool TryBeginAttempt(SQLiteConnection db,
    long relationId, int currentYear)
```

Use one conditional `UPDATE`: active, `END_TIME<0`, loose tier, `LAST_TRIBUTE_ATTEMPT_YEAR<>currentYear`, and `NEXT_TRIBUTE_DUE_YEAR<0 OR <=currentYear`. Set the attempt year before any transfer and migrate `-1` due to current year.

```csharp
internal static void MarkPaid(SQLiteConnection db,
    long relationId, int currentYear, int factorPercent)
```

Require the current attempt year, then set paid year, factor, and next due to `currentYear+1`; require one changed row. Use parameters for every value.

- [ ] **Step 5: Run Tasks 1-2 tests**

Expected: new, legacy, duplicate-tick, formal-row, inactive-row, and independent-relation tests pass.

- [ ] **Step 6: Commit schema and persistence**

```powershell
git add -- Code/core/db/VassalRelationTableItem.cs Code/core/lineage/TributarySettlementPersistence.cs Tests/AncientWarfare3.Rules.Tests/TributaryRelationPersistenceTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: persist annual tributary settlement"
```

### Task 3: Split Formal Tribute from Paid Tributary Settlement

**Files:**
- Create: `Code/core/lineage/TributarySettlementService.cs`
- Modify: `Code/core/lineage/VassalService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/TributarySettlementSourceGuardTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write source-contract tests for the annual split**

Extract the `SetVassalInternal` and `SettleAnnualTribute` source sections and assert:

```csharp
True(create.Contains("TributarySettlementPersistence.InitializeNewRelation("), "new tributary receives due year");
True(annual.Contains("TributarySettlementService.SettleDueRelations(pSuzerain)"), "annual entry delegates loose settlement");
True(annual.Contains("CountsAsVassal(relation.contract_tier)"), "legacy loop is formal-only");
True(annual.IndexOf("SettleDueRelations", StringComparison.Ordinal) <
     annual.IndexOf("VASSAL_TRIBUTE_LAST_YEAR", StringComparison.Ordinal),
    "aggregate formal marker cannot suppress loose relation settlement");
```

Require the new service to call `GetWarPowerScore(tributary, pIncludeVassals: true)` and `GetWarPowerScore(suzerain, pIncludeVassals: true)`. This existing method counts active/reserve warriors, cities, territory, and weighted formal vassals while `GetVassals` excludes loose tributaries.

- [ ] **Step 2: Run and verify the source guard fails**

Run the focused command. Expected: missing initialization, delegation, and formal-only filter assertions fail.

- [ ] **Step 3: Initialize only newly created loose tributaries**

Immediately after the relation insert in `SetVassalInternal`:

```csharp
if (VassalContractTierRules.IsLooseTributary(contractTier))
    TributarySettlementPersistence.InitializeNewRelation(DB, relationId,
        Date.getCurrentYear());
```

If initialization throws, close the inserted row as `creation_rollback`, log its relation ID, and return false before publishing runtime projection fields.

- [ ] **Step 4: Implement relation-by-relation annual orchestration**

Create `TributarySettlementService.SettleDueRelations(Kingdom pSuzerain)`. It must read active direct loose rows ordered by `RELATION_ID`, resolve the live tributary, then for each row:

```csharp
if (!TributarySettlementPersistence.TryBeginAttempt(DB, relationId, year))
    continue;
float tributaryPower = VassalService.GetWarPowerScore(tributary,
    pIncludeVassals: true);
float suzerainPower = VassalService.GetWarPowerScore(pSuzerain,
    pIncludeVassals: true);
int factor = TributaryPaymentRules.FactorPercent(tributaryPower,
    suzerainPower);
if (factor == 0)
{
    VassalService.EndVassal(tributary, "tribute_refused_power");
    continue;
}
```

Calculate current base requests through `VassalFiscalRules.PoliticalTribute` and `GoldTribute`, scale through `TributaryPaymentRules`, transfer through `KingdomPolicyService.TransferPoliticalPoints` and an internal `VassalService.TransferTributaryCapitalGold` wrapper over the existing private gold transfer. If both actual transfers are zero, end with `tribute_unpaid`. Otherwise call `MarkPaid`, then `TributaryHouseholdOfferingService.TryOffer`, then record both kingdom histories.

Use a result value rather than parallel diagnostics variables:

```csharp
internal readonly struct TributarySettlementResult
{
    internal TributarySettlementResult(long relationId, int year,
        float powerRatio, int factorPercent, float politicalTransferred,
        int goldTransferred, string outcome, string offeringOutcome)
    {
        RelationId = relationId; Year = year; PowerRatio = powerRatio;
        FactorPercent = factorPercent;
        PoliticalTransferred = politicalTransferred;
        GoldTransferred = goldTransferred;
        Outcome = outcome ?? ""; OfferingOutcome = offeringOutcome ?? "";
    }
    internal long RelationId { get; }
    internal int Year { get; }
    internal float PowerRatio { get; }
    internal int FactorPercent { get; }
    internal float PoliticalTransferred { get; }
    internal int GoldTransferred { get; }
    internal string Outcome { get; }
    internal string OfferingOutcome { get; }
}
```

Catch exceptions per relation. Keep its due year unchanged but its attempt year set, so the same annual tick cannot double-charge and another relation still settles.

- [ ] **Step 5: Preserve formal-vassal behavior**

Call loose settlement at the beginning of `VassalService.SettleAnnualTribute`, before `VASSAL_TRIBUTE_LAST_YEAR`. Filter the existing aggregate transfer loop:

```csharp
List<ActiveRelationDetails> relations = ReadDirectRelations(pSuzerain)
    .Where(relation => VassalContractTierRules.CountsAsVassal(
        relation.contract_tier)).ToList();
```

Do not change formal rates, caps, history, or aggregate-year semantics.

- [ ] **Step 6: Run focused tests and the Release build**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --tributary-protection-only
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: source guards pass and the build has zero errors.

- [ ] **Step 7: Commit the annual split**

```powershell
git add -- Code/core/lineage/TributarySettlementService.cs Code/core/lineage/VassalService.cs Tests/AncientWarfare3.Rules.Tests/TributarySettlementSourceGuardTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: settle loose tributaries annually"
```

### Task 4: Make Tributary Protection an Unbypassable War Gate

**Files:**
- Create: `Code/core/lineage/TributaryProtectionRules.cs`
- Create: `Code/core/lineage/TributaryProtectionService.cs`
- Modify: `Code/core/lineage/WarDecisionService.cs`
- Modify: `Code/core/lineage/WarTerritoryService.cs`
- Modify: `Code/core/lineage/DiplomaticWarDeclarationService.cs`
- Modify: `Code/core/lineage/VassalAIService.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/TributaryProtectionRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/TributaryProtectionSourceGuardTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write symmetric pure predicate tests**

Test this signature in both directions:

```csharp
bool forward = TributaryProtectionRules.IsDirectActivePair(
    10L, 20L, relationVassalId: 10L, relationSuzerainId: 20L,
    relationActive: true, relationEndTime: -1d,
    relationContractTier: VassalContractTierRules.Tributary);
bool reverse = TributaryProtectionRules.IsDirectActivePair(
    20L, 10L, 10L, 20L, true, -1d,
    VassalContractTierRules.Tributary);
```

Assert both true; inactive, ended, formal-vassal, same-kingdom, and unrelated-third-party inputs false.

- [ ] **Step 2: Write source guards for every war layer**

Require:

```csharp
True(canQueue.Contains("TributaryProtectionService.IsProtectedPair("), "queue gate");
True(startWar.Contains("TributaryProtectionService.IsProtectedPair("), "service start gate");
True(targetGate.Contains("TributaryProtectionService.IsProtectedPair("), "UI/AI target gate");
True(submission.Contains("CanQueueWarPair("), "submission uses pair gate");
True(finalGate.IndexOf("TributaryProtectionService.IsProtectedPair(", StringComparison.Ordinal) <
     finalGate.IndexOf("IsAw3AllowedWarStart", StringComparison.Ordinal),
    "final protection precedes AW3 depth bypass");
False(subjectBranch.Contains("GetTributarySuzerainId"), "tributary skips independence AI");
```

Enumerate direct `World.world.diplomacy.startWar` calls: only guarded `WarDecisionService.StartWar` may call the engine.

- [ ] **Step 3: Implement pure and authoritative predicates**

`TributaryProtectionRules.IsDirectActivePair` is direction-independent and requires active, `end_time < 0`, loose tier, distinct participants, and exact vassal/suzerain IDs.

`TributaryProtectionService.IsProtectedPair(Kingdom left, Kingdom right)` first locates a directional runtime projection via `GetTributarySuzerainId`, then queries that exact projected relation ID and validates IDs/tier/active/end time through the pure rule. A stale projection or unavailable DB returns false with a sampled warning; it must not block unrelated wars.

- [ ] **Step 4: Gate queueing and service start with one reason**

After participant validation in both `CanQueueWarPair` and `StartWar`:

```csharp
if (TributaryProtectionService.IsProtectedPair(pAttacker, pDefender))
{
    pReason = "active_tributary_protection";
    return false;
}
```

Use `pFailureReason` in `StartWar`. This check applies regardless of system-war, declaration-lock, no-CB, or independence flags.

- [ ] **Step 5: Fix the final engine-gate ordering**

At the top of `ShouldBlockWarStart`:

```csharp
if (TributaryProtectionService.IsProtectedPair(pAttacker, pDefender))
{
    LogBlockedWar(pAttacker, pDefender,
        pType?.id ?? "tributary_protection");
    return true;
}
if (IsAw3AllowedWarStart) return false;
```

This must precede the current AW3 depth early return so system and independence paths cannot bypass it. `AW_WarPatch.DiplomacyStartWar_Prefix` remains the final caller.

- [ ] **Step 6: Reuse the predicate in target filtering and split subject AI**

At the top of `WarTerritoryService.IsVassalDecisionOnlyTarget`, return true for a protected pair; existing `WarDecisionAI` and UI report construction already use this method. Keep `DiplomaticWarDeclarationService.CanQueueCurrentGoal` on `CanQueueWarPair` rather than duplicating policy.

Replace `VassalAIService.OnKingdomYear` subject routing with:

```csharp
bool formalVassal = VassalService.GetSuzerainId(pKingdom) >= 0;
bool tributary = VassalService.GetTributarySuzerainId(pKingdom) >= 0;
bool acted = formalVassal
    ? TryIndependenceWar(pKingdom)
    : tributary
        ? false
        : TryAbsorbVassal(pKingdom, court) ||
          TryVassalWar(pKingdom, court) ||
          TryActiveVassal(pKingdom, court);
```

Inside `TryIndependenceWar`, resolve only `VassalService.GetSuzerain`. Loose tributaries exit only through annual refusal/non-payment.

- [ ] **Step 7: Run focused and treaty-gate regressions**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --tributary-protection-only
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --diplomatic-war-treaty-gate
```

Expected: both exit 0; formal-vassal independence remains unchanged.

- [ ] **Step 8: Commit the war gate**

```powershell
git add -- Code/core/lineage/TributaryProtectionRules.cs Code/core/lineage/TributaryProtectionService.cs Code/core/lineage/WarDecisionService.cs Code/core/lineage/WarTerritoryService.cs Code/core/lineage/DiplomaticWarDeclarationService.cs Code/core/lineage/VassalAIService.cs Code/patch/AW_WarPatch.cs Tests/AncientWarfare3.Rules.Tests/TributaryProtectionRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/TributaryProtectionSourceGuardTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "fix: block wars across active tributary protection"
```

### Task 5: Define Royal Household Recipient Ordering and Capacity

**Files:**
- Create: `Code/core/lineage/RoyalHouseholdRecipientRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/RoyalHouseholdRecipientRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write failing eligibility, capacity, and order tests**

Use value-only candidates:

```csharp
var king = new RoyalHouseholdRecipientCandidate(1L,
    RoyalHouseholdOwnerRole.King, true, 1, 4, true, 1d);
var heir = new RoyalHouseholdRecipientCandidate(2L,
    RoyalHouseholdOwnerRole.Heir, true, 0, 2, true, 2d);
var prince = new RoyalHouseholdRecipientCandidate(3L,
    RoyalHouseholdOwnerRole.Prince, true, 0, 1, true, 3d);
```

Assert king before heir before prince; legitimate prince before illegitimate prince, then earlier birth, then lower actor ID. Assert full/ineligible recipients have no vacancy. Assert ruler capacity `2/4/8`, heir `2`, prince `1`, former role `0`; over-capacity blocks new entries but produces no deletion decision.

- [ ] **Step 2: Run and verify recipient types are missing**

Expected: compilation fails on `RoyalHouseholdRecipientCandidate`.

- [ ] **Step 3: Implement the pure recipient types**

Create `RoyalHouseholdRecipientRules.cs`:

```csharp
namespace AncientWarfare3.core.lineage
{
    internal enum RoyalHouseholdOwnerRole
    {
        None = 0, King = 1, Heir = 2, Prince = 3
    }

    internal readonly struct RoyalHouseholdRecipientCandidate
    {
        internal RoyalHouseholdRecipientCandidate(long actorId,
            RoyalHouseholdOwnerRole role, bool eligible,
            int activeConsorts, int capacity, bool legitimateBirth,
            double birthTime)
        {
            ActorId = actorId; Role = role; Eligible = eligible;
            ActiveConsorts = activeConsorts; Capacity = capacity;
            LegitimateBirth = legitimateBirth; BirthTime = birthTime;
        }
        internal long ActorId { get; }
        internal RoyalHouseholdOwnerRole Role { get; }
        internal bool Eligible { get; }
        internal int ActiveConsorts { get; }
        internal int Capacity { get; }
        internal bool LegitimateBirth { get; }
        internal double BirthTime { get; }
        internal bool HasVacancy => Eligible && Capacity > 0 &&
                                    ActiveConsorts < Capacity;
    }

    internal static class RoyalHouseholdRecipientRules
    {
        internal static int Capacity(RoyalHouseholdOwnerRole role,
            RulerHouseholdRealmTier tier) => role switch
        {
            RoyalHouseholdOwnerRole.King =>
                RulerHouseholdRules.ConsortCapacity(tier),
            RoyalHouseholdOwnerRole.Heir => 2,
            RoyalHouseholdOwnerRole.Prince => 1,
            _ => 0
        };

        internal static int Compare(RoyalHouseholdRecipientCandidate left,
            RoyalHouseholdRecipientCandidate right)
        {
            int role = left.Role.CompareTo(right.Role);
            if (role != 0) return role;
            if (left.Role == RoyalHouseholdOwnerRole.Prince &&
                left.LegitimateBirth != right.LegitimateBirth)
                return left.LegitimateBirth ? -1 : 1;
            int birth = left.BirthTime.CompareTo(right.BirthTime);
            return birth != 0 ? birth : left.ActorId.CompareTo(right.ActorId);
        }
    }
}
```

- [ ] **Step 4: Run the focused recipient tests**

Expected: all Task 1-5 focused tests pass.

- [ ] **Step 5: Commit recipient policy**

```powershell
git add -- Code/core/lineage/RoyalHouseholdRecipientRules.cs Tests/AncientWarfare3.Rules.Tests/RoyalHouseholdRecipientRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "feat: define royal tribute recipient order"
```

### Task 6: Generalize Household Persistence and Lifecycle to Explicit Owners

**Files:**
- Modify: `Code/core/db/RulerHouseholdTableItem.cs`
- Modify: `Code/core/lineage/RulerHouseholdModels.cs`
- Modify: `Code/core/lineage/RulerHouseholdQuery.cs`
- Modify: `Code/core/lineage/RulerHouseholdRules.cs`
- Modify: `Code/core/lineage/RulerHouseholdService.cs`
- Modify: `Code/core/lineage/RulerHouseholdPregnancyService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/RulerHouseholdOwnerSourceGuardTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Add owner/source metadata with old-save defaults**

Append to `RulerHouseholdTableItem`:

```csharp
[TableItemDef(pDefaultValue: "")] public string owner_role_at_entry = "";
[TableItemDef(pDefaultValue: "")] public string source_kind = "";
[TableItemDef(pDefaultValue: "-1")] public long source_relation_id = -1;
[TableItemDef(pDefaultValue: "-1")] public int source_tribute_year = -1;
```

Keep database column `RULER_ACTOR_ID` for save compatibility. Interpret it as household owner; blank `SOURCE_KIND` remains legacy diplomacy.

- [ ] **Step 2: Extend records and bounded queries**

Add to `RulerHouseholdRecord`:

```csharp
public string OwnerRoleAtEntry = "";
public string SourceKind = "";
public long SourceRelationId = -1L;
public int SourceTributeYear = -1;
public bool IsTributaryOffering => SourceKind == "tributary_offering";
```

Extend `RulerHouseholdQuery.Projection` and `ReadRecord` in the same order. Add exact APIs:

```csharp
public bool HasTributaryOffering(long relationId, int tributeYear)
public IReadOnlyList<long> ReadActiveOwnerIdsByRecipient(long kingdomId,
    int limit)
public IReadOnlyList<RulerHouseholdRecord> ReadActiveByOwner(long ownerActorId,
    int limit)
```

Dedup filters source kind/relation/year regardless of later status. Owner enumeration uses `SELECT DISTINCT RULER_ACTOR_ID ... ORDER BY RULER_ACTOR_ID LIMIT @limit`; no world-actor scan.

- [ ] **Step 3: Write lifecycle tests before production changes**

Extend `RulerHouseholdRulesTests`:

```csharp
Equal(false, RulerHouseholdRules.ShouldCloseRelationship(
    true, ownerAlive: true, partnerAlive: true,
    ownerStillReigning: false, sameRecipientRealm: true,
    tributaryOffering: true), "tribute consort survives role change");
Equal(true, RulerHouseholdRules.ShouldCloseRelationship(
    true, true, true, false, true, tributaryOffering: false),
    "ordinary household keeps legacy reign closure");
Equal(true, RulerHouseholdRules.ShouldCloseRelationship(
    true, false, true, false, true, tributaryOffering: true),
    "owner death closes");
Equal(true, RulerHouseholdRules.ShouldCloseRelationship(
    true, true, true, false, false, tributaryOffering: true),
    "leaving recipient realm closes");
```

The source guard requires `ResolveManagedFather` to accept an active non-ruler owner in the same recipient kingdom and requires monthly work to use bounded owner IDs while `MaximumPregnancyStartsPerKingdomMonth` remains one.

- [ ] **Step 4: Make closure source-aware**

Replace the rule with:

```csharp
public static bool ShouldCloseRelationship(bool active,
    bool ownerAlive, bool partnerAlive, bool ownerStillReigning,
    bool sameRecipientRealm, bool tributaryOffering)
{
    if (!active) return false;
    if (!ownerAlive || !partnerAlive || !sameRecipientRealm) return true;
    return !tributaryOffering && !ownerStillReigning;
}
```

Pass `row.IsTributaryOffering` from `OnKingdomYear`. Keep principal-wife mutual-lover validation unchanged.

- [ ] **Step 5: Add explicit-owner commit without weakening ordinary offers**

Keep `TryCommit` as the ordinary diplomacy API and route it through a private shared commit with current ruler, role `king`, source `diplomatic_offer`, proposal ID, relation/year `-1`.

Add:

```csharp
internal static bool TryCommitTributaryConsort(Kingdom source,
    Kingdom recipient, Actor owner, Actor candidate,
    string ownerRoleAtEntry, long relationId, int tributeYear,
    int capacity, out string reason)
```

Require a live adult male owner in recipient realm, `activeConsorts < capacity`, an eligible unrelated candidate, no existing household, and no relation/year duplicate. Write all source fields, migrate to recipient capital, and set `RULER_HOUSEHOLD_RULER_ID` to owner ID. Do not call `becomeLoversWith` for tribute consorts. Migration failure closes only the new relationship and never touches tribute payment.

- [ ] **Step 6: Preserve consorts on accession and role loss**

Always count active rows by owner. Never delete excess rows when accession changes capacity; `active >= capacity` only blocks additions. A former king/heir/prince has capacity zero for new offerings but retains existing tribute consorts.

- [ ] **Step 7: Generalize father resolution and monthly pregnancy**

Replace the current-ruler condition in `ResolveManagedFather`:

```csharp
Actor owner = FindActor(row.RulerActorId);
if (!row.Active || !IsLiveActor(owner) || owner.kingdom?.data == null ||
    pMother.kingdom != owner.kingdom ||
    owner.kingdom.id != row.RecipientKingdomId)
    return null;
```

In `ProcessKingdomMonth`, query bounded active owner IDs for that kingdom, rotate by month key, read bounded consort rows, and stop after `PregnancyStartsForMonth(totalEligible)`, still one. Do not scan `World.world.units`.

- [ ] **Step 8: Run household and full regressions**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --tributary-protection-only
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected: legacy household tests and new source-aware tests pass.

- [ ] **Step 9: Commit explicit-owner household support**

```powershell
git add -- Code/core/db/RulerHouseholdTableItem.cs Code/core/lineage/RulerHouseholdModels.cs Code/core/lineage/RulerHouseholdQuery.cs Code/core/lineage/RulerHouseholdRules.cs Code/core/lineage/RulerHouseholdService.cs Code/core/lineage/RulerHouseholdPregnancyService.cs Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/RulerHouseholdOwnerSourceGuardTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "feat: support persistent royal household owners"
```

### Task 7: Offer One Noblewoman after Successful Financial Tribute

**Files:**
- Create: `Code/core/lineage/TributaryHouseholdOfferingService.cs`
- Modify: `Code/core/lineage/TributarySettlementService.cs`
- Modify: `Code/core/lineage/RulerHouseholdService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/TributaryHouseholdOfferingSourceGuardTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write bounded-selection source guards**

Require the offering service to use the registered read-only heir, direct sons from `SuccessionRelationshipIndex.GetChildIds`, indexed archive candidate queries, and `HouseholdCandidatePriority`. Forbid `World.world.units` iteration. Require at most one `TryCommitTributaryConsort` call and require settlement to invoke offering only after `IsPaid` and `MarkPaid`.

- [ ] **Step 2: Run and verify the offering service is missing**

Expected: focused source guard fails on the missing service and call order.

- [ ] **Step 3: Build recipient candidates in approved order**

Add current male ruler, registered male heir, then adult male direct sons. De-duplicate heir from princes. Require live, adult, breeding-capable, recipient-domestic actors and a non-republic realm. Use existing birth legitimacy and `data.created_time` for prince ordering. Resolve capacity with `RoyalHouseholdRecipientRules.Capacity`, sort with `Compare`, and select the first `HasVacancy` candidate.

- [ ] **Step 4: Reuse noblewoman eligibility and stable priority**

Extract the indexed candidate-query/sort portion of `BuildOfferCandidatePool` into `BuildEligibleConsortCandidates(Kingdom source)` so it does not assume current ruler. Keep age 18-33, living/domestic, noble lineage and shi, unmarried/no household/non-slave checks. For the selected owner, recheck close kin.

Priority remains direct daughter of tributary ruler, ruling lineage, other eligible noblewomen, then current age and actor ID. Do not scan all actors.

- [ ] **Step 5: Commit at most one and never roll back payment**

Expose:

```csharp
internal static string TryOffer(Kingdom tributary, Kingdom suzerain,
    long relationId, int tributeYear)
```

Return stable outcomes `offered`, `duplicate`, `no_recipient`, `no_candidate`, `migration_failed`, or `error`. Catch errors locally. Every result leaves successful financial renewal intact; only `offered` additionally writes person and bilateral kingdom history.

- [ ] **Step 6: Run focused and full tests**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --tributary-protection-only
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected: no unbounded-scan guard failure; no offering outcome invalidates renewal.

- [ ] **Step 7: Commit offering integration**

```powershell
git add -- Code/core/lineage/TributaryHouseholdOfferingService.cs Code/core/lineage/TributarySettlementService.cs Code/core/lineage/RulerHouseholdService.cs Tests/AncientWarfare3.Rules.Tests/TributaryHouseholdOfferingSourceGuardTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "feat: offer noblewomen with annual tribute"
```

### Task 8: Add Relation Display, History, Localization, and Diagnostics

**Files:**
- Modify: `Code/core/lineage/LineageDTO.cs`
- Modify: `Code/core/lineage/VassalService.cs`
- Modify: `Code/ui/items/VassalRelationListItem.cs`
- Modify: `Code/core/lineage/HistoryLocalizationRules.cs`
- Modify: `Code/core/lineage/TributarySettlementService.cs`
- Modify: `Locales/aw3_centralization.csv`
- Modify: `Locales/aw3_diplomacy.csv`
- Modify: `Locales/war.csv`
- Modify: `Locales/others.csv`
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoryLocalizationRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/TributaryPresentationSourceGuardTests.cs.txt`

- [ ] **Step 1: Extend relation read models and SQL projections**

Add to `ActiveRelationDetails` and `VassalRelationInfo`:

```csharp
public int last_tribute_paid_year = -1;
public int next_tribute_due_year = -1;
public int last_tribute_factor_percent = -1;
```

Extend `ReadDirectRelations` and `ReadActiveRelationDetails` projections in the same ordinal order. Copy values in `BuildRelationRow` only when `row.is_tributary`.

- [ ] **Step 2: Display annual state only for tributaries**

In `VassalRelationListItem.BuildTip`, add localized lines for last paid year (`never` if negative), last actual factor (`unknown` if negative, otherwise percent), and next due year. Formal-vassal rows remain unchanged.

- [ ] **Step 3: Add distinct history and failure keys**

Add complete Simplified Chinese, English, and Traditional Chinese entries for:

```text
aw_hist_tributary_paid
aw_hist_tributary_offering
aw_hist_tributary_refused_power
aw_hist_tributary_unpaid
aw_vassal_last_tribute_paid_year
aw_vassal_last_tribute_factor
aw_vassal_next_tribute_due_year
aw_diplomacy_failure_active_tributary_protection
```

Register history keys in `HistoryLocalizationRules`. Add `active_tributary_protection` to the diplomacy UI reason switch. Extend localization tests to require all three non-empty language columns.

- [ ] **Step 4: Emit one diagnostic only for attempted due relations**

After each attempted relation, log:

```csharp
ModClass.LogInfo("[TributarySettlement] relation=" + relationId +
    " tributary=" + tributary.id + " suzerain=" + suzerain.id +
    " year=" + year + " due=" + dueYear +
    " ratio=" + ratio.ToString("0.000") + " factor=" + factor +
    " political=" + politicalTransferred.ToString("0.0") +
    " gold=" + goldTransferred + " outcome=" + outcome +
    " offering=" + offeringOutcome);
```

Do not log non-due relations. Exception logs include relation ID, both kingdom IDs, year, and one stage: `read`, `attempt`, `power`, `transfer`, `persist`, `offering`, or `history`.

- [ ] **Step 5: Write presentation source guards and run localization tests**

Require all three DTO fields to flow from SQL to tooltip, require the failure key in the UI switch/CSV, and require the due-only logging position after successful `TryBeginAttempt`.

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --tributary-protection-only
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --war-return-display-slice
```

Expected: both pass with no localization completeness error.

- [ ] **Step 6: Commit presentation and diagnostics**

```powershell
git add -- Code/core/lineage/LineageDTO.cs Code/core/lineage/VassalService.cs Code/ui/items/VassalRelationListItem.cs Code/core/lineage/HistoryLocalizationRules.cs Code/core/lineage/TributarySettlementService.cs Locales/aw3_centralization.csv Locales/aw3_diplomacy.csv Locales/war.csv Locales/others.csv Tests/AncientWarfare3.Rules.Tests/HistoryLocalizationRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/TributaryPresentationSourceGuardTests.cs.txt
git commit -m "feat: present annual tributary protection"
```

### Task 9: Full Regression, Review, Source-Only Deployment, and Runtime Acceptance

**Files:**
- Modify only Task 1-8 files when a verification failure is caused by this feature.
- Preserve unrelated dirty RTS return, Xia minimap, supporter leaderboard, and user changes; do not stage, revert, or claim them as this feature.

- [ ] **Step 1: Run focused, full, build, and whitespace verification**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --tributary-protection-only
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
git diff --check
```

Expected: all exit 0; Release has zero errors; `git diff --check` prints nothing.

- [ ] **Step 2: Audit protection and bounded-work call sites**

```powershell
rg -n "startWar\(|CanQueueWarPair\(|ShouldBlockWarStart\(|IsProtectedPair\(" Code -g "*.cs"
rg -n "World\.world\.units|foreach \(Actor" Code/core/lineage/TributaryHouseholdOfferingService.cs Code/core/lineage/RulerHouseholdPregnancyService.cs
```

Expected: only guarded `WarDecisionService` starts the engine; queue/start/final paths share the protection service; offering has no world-unit scan; pregnancy uses bounded owner/relationship queries.

- [ ] **Step 3: Request code review and resolve correctness findings**

Use `superpowers:requesting-code-review` for the feature commit range. Review exact boundaries, formal-vassal regression, same-year double charging, stale projection false positives, AW3-depth/system/independence bypass, offering rollback coupling, over-capacity deletion, and unbounded monthly work. Fix each correctness finding with a failing test first, then repeat Step 1.

- [ ] **Step 4: Inspect the scoped feature diff**

```powershell
git status --short
git log --oneline --decorate -10
git diff --name-only 055c7f9e..HEAD
```

Expected: feature commits include only plan-listed files. Separate pre-existing dirty files from feature staging and review evidence.

- [ ] **Step 5: Deploy source only**

Confirm WorldBox is closed, then:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy-local.ps1
```

Expected: a backup path under `.aw3-deploy-backups` followed by `DEPLOY-DONE`. Do not copy or deploy `AncientWarfare3.dll`; NML compiles source at runtime.

- [ ] **Step 6: Verify deployed hashes for the feature manifest**

Resolve the destination printed by `deploy-local.ps1`. Compare SHA-256 for every production/localization file changed in Tasks 1-8. Expected: every source/deployed file exists and hashes match.

- [ ] **Step 7: Perform two-year in-game acceptance**

Verify all of these in a new relation and a legacy-save relation:

1. New tributary protection is immediate; first payment is next year.
2. Legacy `NEXT_TRIBUTE_DUE_YEAR=-1` attempts once in the current year.
3. Both war directions are disabled/rejected through UI, AI, queue, submission, system, independence, and final engine gate.
4. `<50`, exact `50`, exact `75`, exact `100`, and `>=125` ratios yield `100/75/50/25/0`.
5. Any positive resource transfer renews one year; both zero ends `tribute_unpaid`; factor zero ends `tribute_refused_power`; neither ending auto-starts war.
6. At most one noblewoman is offered per relation/year; no candidate/full capacity/migration failure does not undo payment.
7. Heir/prince consorts survive accession and role loss, count against new capacity, and retain bounded pregnancy behavior.
8. Formal-vassal tribute and independence remain unchanged.
9. `Player.log` has one diagnostic per attempted due relation, no non-due annual spam, and no AW3 exception.

- [ ] **Step 8: Commit any runtime hardening separately**

Add the smallest failing rule/source test, fix only feature files, repeat Steps 1 and 5-7, then:

```powershell
git add -- <exact feature files from the failing test and fix>
git commit -m "fix: harden tributary paid protection"
```

Report focused/full/build results, deployed destination, hash equality, two-year outcomes, refusal/unpaid outcomes, offering recipient role, and relevant log lines.

## Self-Review Record

- Spec coverage: Tasks 1-3 cover exact tiers, complete war-power inputs, relation-level annual state, next-year first payment, legacy migration, at-most-once attempts, renewal, refusal, insolvency, and unchanged formal tribute. Task 4 covers both directions, every war type and every gate, the AW3-depth bypass, and formal-vassal/tributary AI split. Tasks 5-7 cover recipient priority/capacity, candidate priority, one offering, explicit ownership, accession/role-loss retention, over-capacity behavior, and bounded pregnancy. Task 8 covers display, history, localization, and diagnostics. Task 9 covers regression, source-only deployment, and runtime acceptance.
- Placeholder scan: no forbidden placeholder marker or unspecified error-handling step remains. Every code task names exact files, signatures, required branches, commands, expected failures/passes, and commit boundaries.
- Type consistency: `TributaryPaymentRules.FactorPercent/ScaleGold/ScalePolitical/IsPaid/EndReason`, `TributarySettlementPersistence.InitializeNewRelation/TryBeginAttempt/MarkPaid`, `TributaryProtectionService.IsProtectedPair`, `RoyalHouseholdOwnerRole`, `RulerHouseholdRecord.IsTributaryOffering`, `RulerHouseholdService.TryCommitTributaryConsort`, and `TributaryHouseholdOfferingService.TryOffer` use consistent names and value types throughout.
- Non-goals preserved: no configurable multi-year cadence, manual woman/recipient selection, automatic third-party war participation, loose-tributary military-network support, automatic punishment war, or formal-vassal rule change is introduced.

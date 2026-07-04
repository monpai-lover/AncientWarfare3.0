# AW3 Economy War Mandate Finish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the remaining city economy, war target/peace, restoration, advanced general rebellion, and mandate-only ruler title loops without replacing the existing AW3 systems.

**Architecture:** Keep the current AW3 pattern: durable `[TableDef]` SQLite state, small rule classes for testable decisions, services under `Code/core`, existing `HistoryWriter` calls for records, and Harmony patches only at existing yearly or war event boundaries. Do not build a dedicated general/fief UI in this pass.

**Tech Stack:** C# net48, NeoModLoader, Harmony, WorldBox API, AW3 SQLite archive layer, existing AW3 policy/history/window services, small console rule tests under `Tests`.

---

## Scope And File Structure

This plan is split into six independently testable waves.

Create:

- `Code/core/db/CityEconomyStateTableItem.cs`: durable city economy role and contribution snapshot.
- `Code/core/db/PeaceSettlementTableItem.cs`: durable peace result summary linked to a war goal.
- `Code/core/db/MandateRulerTitleTableItem.cs`: mandate-only temple name and double posthumous title storage.
- `Code/core/policy/CityEconomyRules.cs`: pure city role and contribution math.
- `Code/core/policy/CityEconomyService.cs`: yearly city economy updater and history writer.
- `Code/core/lineage/WarTargetSelectionRules.cs`: pure war target option scoring.
- `Code/core/lineage/PeaceSettlementRules.cs`: pure automatic peace result rules.
- `Code/core/lineage/GeneralRebellionRules.cs`: pure kingdom-crisis and rebellion branch rules.
- `Code/core/lineage/MandateRulerTitleDefs.cs`: mandate temple name and title pair definitions.
- `Code/core/lineage/MandateRulerTitleRules.cs`: pure mandate title scoring.
- `Code/core/lineage/MandateRulerTitleService.cs`: title persistence and mandate history records.
- `Tests/CityEconomyRuleTests/CityEconomyRuleTests.csproj`
- `Tests/CityEconomyRuleTests/Program.cs`
- `Tests/PeaceSettlementRuleTests/PeaceSettlementRuleTests.csproj`
- `Tests/PeaceSettlementRuleTests/Program.cs`
- `Tests/GeneralRebellionRuleTests/GeneralRebellionRuleTests.csproj`
- `Tests/GeneralRebellionRuleTests/Program.cs`
- `Tests/MandateRulerTitleRuleTests/MandateRulerTitleRuleTests.csproj`
- `Tests/MandateRulerTitleRuleTests/Program.cs`

Modify:

- `Code/patch/AW_KingdomPolicyPatch.cs`: call `CityEconomyService.OnKingdomYear`.
- `Code/core/policy/KingdomPolicyService.cs`: include city economy contributions in point gain.
- `Code/core/policy/CityTechService.cs`: notify city economy map/tooltips only if city economy needs city tech report.
- `Code/core/lineage/WarTerritoryService.cs`: expose detailed target options and use peace settlement rules.
- `Code/ui/windows/WarDecisionTargetWindow.cs`: add target detail view for cities and claimants.
- `Code/core/lineage/RoyalClaimService.cs`: expose specific hosted restoration claims by target kingdom/city.
- `Code/core/lineage/GeneralRebellionService.cs`: use `GeneralRebellionRules`.
- `Code/core/lineage/PosthumousTitleService.cs` or `Code/core/lineage/ChronicleEvents.cs`: invoke mandate title service after a mandate reign ends.
- `Code/ui/windows/MandateDynastyWindow.cs`: show mandate ruler titles in dynasty/reign rows.
- `Locales/aw3_policy_ui.csv`: policy/war/economy UI labels.
- `Locales/others.csv` or a new `Locales/aw3_history_events.csv`: history event text keys.
- `README.md`
- `docs/AW3_Roadmap.md`

Do not create a general/fief management window or fief map mode in this plan.

---

## Task 1: City Economy Rule Tests

**Files:**
- Create: `Tests/CityEconomyRuleTests/CityEconomyRuleTests.csproj`
- Create: `Tests/CityEconomyRuleTests/Program.cs`
- Create: `Code/core/policy/CityEconomyRules.cs`

- [ ] **Step 1: Add the test project file**

Create `Tests/CityEconomyRuleTests/CityEconomyRuleTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>11</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\AncientWarfare3.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add failing tests for role selection and contributions**

Create `Tests/CityEconomyRuleTests/Program.cs`:

```csharp
using System;
using AncientWarfare3.core.policy;

namespace CityEconomyRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            ExpectRole("capital", CityEconomyRole.CapitalAdmin,
                isCapital: true, population: 80, marketBuildings: 0, farmBuildings: 0,
                militaryBuildings: 0, workshopBuildings: 0, adoptedTechCount: 2,
                totalTechCount: 8, isBorder: false, occupiedUnrest: false);

            ExpectRole("market", CityEconomyRole.MarketTrade,
                isCapital: false, population: 90, marketBuildings: 3, farmBuildings: 0,
                militaryBuildings: 0, workshopBuildings: 0, adoptedTechCount: 3,
                totalTechCount: 8, isBorder: false, occupiedUnrest: false);

            ExpectRole("frontier", CityEconomyRole.FrontierMilitary,
                isCapital: false, population: 55, marketBuildings: 0, farmBuildings: 0,
                militaryBuildings: 2, workshopBuildings: 0, adoptedTechCount: 1,
                totalTechCount: 8, isBorder: true, occupiedUnrest: false);

            ExpectRole("occupied", CityEconomyRole.OccupiedUnrest,
                isCapital: false, population: 60, marketBuildings: 3, farmBuildings: 3,
                militaryBuildings: 3, workshopBuildings: 3, adoptedTechCount: 5,
                totalTechCount: 8, isBorder: false, occupiedUnrest: true);

            CityEconomyContribution contribution = CityEconomyRules.CalculateContribution(
                CityEconomyRole.MarketTrade, population: 100, adoptedTechCount: 4, totalTechCount: 8,
                distanceFromCapital: 30, slavePopulation: 10, nonCore: false);
            if (contribution.PolicyPoints <= 0.4f || contribution.TechPoints <= 0.2f || contribution.TaxValue <= 8f)
                throw new Exception("Expected market city to contribute policy, tech, and tax.");

            CityEconomyContribution occupied = CityEconomyRules.CalculateContribution(
                CityEconomyRole.OccupiedUnrest, population: 100, adoptedTechCount: 4, totalTechCount: 8,
                distanceFromCapital: 30, slavePopulation: 10, nonCore: true);
            if (occupied.TaxValue >= contribution.TaxValue || occupied.UnrestRisk <= contribution.UnrestRisk)
                throw new Exception("Expected occupied city to pay less tax and carry higher unrest.");

            Console.WriteLine("City economy rule tests passed.");
            return 0;
        }

        private static void ExpectRole(string label, CityEconomyRole expected,
            bool isCapital, int population, int marketBuildings, int farmBuildings,
            int militaryBuildings, int workshopBuildings, int adoptedTechCount,
            int totalTechCount, bool isBorder, bool occupiedUnrest)
        {
            CityEconomyRole actual = CityEconomyRules.SelectRole(isCapital, population,
                marketBuildings, farmBuildings, militaryBuildings, workshopBuildings,
                adoptedTechCount, totalTechCount, isBorder, occupiedUnrest);
            if (actual != expected)
                throw new Exception($"Expected {label} role {expected}, got {actual}.");
        }
    }
}
```

- [ ] **Step 3: Run the failing test**

Run:

```powershell
dotnet run --project Tests\CityEconomyRuleTests\CityEconomyRuleTests.csproj
```

Expected: build fails because `CityEconomyRules`, `CityEconomyRole`, and `CityEconomyContribution` do not exist.

- [ ] **Step 4: Add the minimal pure rules**

Create `Code/core/policy/CityEconomyRules.cs`:

```csharp
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal enum CityEconomyRole
    {
        CapitalAdmin,
        AgrarianGranary,
        MarketTrade,
        FrontierMilitary,
        WorkshopCraft,
        OccupiedUnrest
    }

    internal readonly struct CityEconomyContribution
    {
        public readonly float PolicyPoints;
        public readonly float TechPoints;
        public readonly float TaxValue;
        public readonly float Manpower;
        public readonly float FoodStability;
        public readonly float UnrestRisk;

        public CityEconomyContribution(float policyPoints, float techPoints, float taxValue,
            float manpower, float foodStability, float unrestRisk)
        {
            PolicyPoints = policyPoints;
            TechPoints = techPoints;
            TaxValue = taxValue;
            Manpower = manpower;
            FoodStability = foodStability;
            UnrestRisk = unrestRisk;
        }
    }

    internal static class CityEconomyRules
    {
        public static CityEconomyRole SelectRole(bool isCapital, int population, int marketBuildings,
            int farmBuildings, int militaryBuildings, int workshopBuildings, int adoptedTechCount,
            int totalTechCount, bool isBorder, bool occupiedUnrest)
        {
            if (occupiedUnrest) return CityEconomyRole.OccupiedUnrest;
            if (isCapital) return CityEconomyRole.CapitalAdmin;
            if (isBorder && militaryBuildings >= 1) return CityEconomyRole.FrontierMilitary;
            if (marketBuildings >= farmBuildings && marketBuildings >= workshopBuildings && marketBuildings >= 2)
                return CityEconomyRole.MarketTrade;
            if (workshopBuildings >= 2 || adoptedTechCount >= Mathf.Max(3, totalTechCount / 2))
                return CityEconomyRole.WorkshopCraft;
            return CityEconomyRole.AgrarianGranary;
        }

        public static CityEconomyContribution CalculateContribution(CityEconomyRole role, int population,
            int adoptedTechCount, int totalTechCount, float distanceFromCapital, int slavePopulation, bool nonCore)
        {
            float pop = Mathf.Max(0, population);
            float techFactor = totalTechCount <= 0 ? 0f : Mathf.Clamp01((float)adoptedTechCount / totalTechCount);
            float distanceFactor = Mathf.Clamp(1f - distanceFromCapital / 220f, 0.45f, 1f);
            float slaveFactor = Mathf.Clamp01(slavePopulation / Mathf.Max(1f, pop));
            float nonCoreFactor = nonCore ? 0.72f : 1f;

            float policy = 0.15f + pop * 0.006f + techFactor * 0.35f;
            float tech = 0.10f + pop * 0.004f + techFactor * 0.55f;
            float tax = pop * 0.12f * distanceFactor * nonCoreFactor;
            float manpower = pop * 0.04f;
            float food = pop * 0.03f;
            float unrest = nonCore ? 12f : 2f;

            switch (role)
            {
                case CityEconomyRole.CapitalAdmin:
                    policy *= 1.55f; tax *= 1.15f; unrest -= 1f; break;
                case CityEconomyRole.AgrarianGranary:
                    food *= 1.85f; tax *= 0.95f; break;
                case CityEconomyRole.MarketTrade:
                    tax *= 1.55f; policy *= 1.12f; break;
                case CityEconomyRole.FrontierMilitary:
                    manpower *= 1.85f; tax *= 0.82f; unrest += 2f; break;
                case CityEconomyRole.WorkshopCraft:
                    tech *= 1.55f; tax *= 1.08f; break;
                case CityEconomyRole.OccupiedUnrest:
                    policy *= 0.45f; tech *= 0.55f; tax *= 0.35f; manpower *= 0.55f; food *= 0.75f; unrest += 18f; break;
            }

            tax *= 1f + slaveFactor * 0.12f;
            return new CityEconomyContribution(policy, tech, tax, manpower, food, Mathf.Clamp(unrest, 0f, 100f));
        }
    }
}
```

- [ ] **Step 5: Run tests**

Run:

```powershell
dotnet run --project Tests\CityEconomyRuleTests\CityEconomyRuleTests.csproj
```

Expected: `City economy rule tests passed.`

- [ ] **Step 6: Commit**

```powershell
git add Code/core/policy/CityEconomyRules.cs Tests/CityEconomyRuleTests
git commit -m "test: add city economy rule coverage"
```

---

## Task 2: City Economy Persistence And Yearly Service

**Files:**
- Create: `Code/core/db/CityEconomyStateTableItem.cs`
- Create: `Code/core/policy/CityEconomyService.cs`
- Modify: `Code/patch/AW_KingdomPolicyPatch.cs`
- Modify: `Code/core/policy/KingdomPolicyService.cs`

- [ ] **Step 1: Add the durable table**

Create `Code/core/db/CityEconomyStateTableItem.cs`:

```csharp
using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("CityEconomyState")]
    public class CityEconomyStateTableItem : AbstractTableItem<CityEconomyStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long city_id;
        public string city_name = "";
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public string role = "";
        public string previous_role = "";
        public float policy_points = 0;
        public float tech_points = 0;
        public float tax_value = 0;
        public float manpower = 0;
        public float food_stability = 0;
        public float unrest_risk = 0;
        public int last_year = -1;
        public double updated_time = -1;
    }
}
```

- [ ] **Step 2: Add service API and cheap yearly guard**

Create `Code/core/policy/CityEconomyService.cs` with these public methods:

```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.policy
{
    internal static class CityEconomyService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom)) return;
            if (!Ready) return;
            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.CITY_ECONOMY_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == year) return;
            pKingdom.data.set(LineageKeys.CITY_ECONOMY_LAST_YEAR, year);

            foreach (City city in pKingdom.getCities())
                UpdateCity(pKingdom, city, year);
        }

        public static float GetPolicyContribution(Kingdom pKingdom) => SumContribution(pKingdom, "POLICY_POINTS");
        public static float GetTechContribution(Kingdom pKingdom) => SumContribution(pKingdom, "TECH_POINTS");

        private static void UpdateCity(Kingdom pKingdom, City pCity, int pYear)
        {
            if (pCity?.data == null || pCity.isRekt()) return;
            CityEconomyRole role = SelectRole(pKingdom, pCity);
            CityTechReport tech = CityTechService.GetCityReport(pCity);
            int pop = SafePopulation(pCity);
            CityEconomyContribution contribution = CityEconomyRules.CalculateContribution(role, pop,
                tech.adopted_count, tech.total_count, DistanceFromCapital(pKingdom, pCity),
                CountSlavePopulation(pCity), IsNonCore(pKingdom, pCity));
            Upsert(pKingdom, pCity, role, contribution, pYear);
        }

        private static CityEconomyRole SelectRole(Kingdom pKingdom, City pCity)
        {
            CityTechReport tech = CityTechService.GetCityReport(pCity);
            return CityEconomyRules.SelectRole(pKingdom.capital == pCity, SafePopulation(pCity),
                CountBuildings(pCity, "market"), CountBuildings(pCity, "farm"),
                CountBuildings(pCity, "barracks"), CountBuildings(pCity, "workshop"),
                tech.adopted_count, tech.total_count, IsBorderCity(pKingdom, pCity), IsOccupiedUnrest(pKingdom, pCity));
        }
    }
}
```

Then add the private helpers in the same file:

```csharp
private static int SafePopulation(City pCity) { try { return pCity.getPopulationPeople(); } catch { return 0; } }
private static int CountSlavePopulation(City pCity) { return 0; }
private static bool IsBorderCity(Kingdom pKingdom, City pCity) { return pCity != pKingdom.capital && pKingdom.countCities() > 1; }
private static bool IsOccupiedUnrest(Kingdom pKingdom, City pCity) { return IsNonCore(pKingdom, pCity); }
private static bool IsNonCore(Kingdom pKingdom, City pCity) { return false; }
private static int CountBuildings(City pCity, string pKind) { return 0; }
private static float DistanceFromCapital(Kingdom pKingdom, City pCity) { return pKingdom.capital == pCity ? 0f : 40f; }
```

The first implementation uses conservative helpers. After build passes, replace `IsNonCore` with the existing `WarTerritoryService` core query if it has a public API; otherwise add a small `WarTerritoryService.HasCore(Kingdom, City)` wrapper.

- [ ] **Step 3: Add upsert and contribution query**

Add to `CityEconomyService`:

```csharp
private static void Upsert(Kingdom pKingdom, City pCity, CityEconomyRole pRole,
    CityEconomyContribution pContribution, int pYear)
{
    string role = pRole.ToString();
    string previous = ReadRole(pCity.id);
    bool existed = !string.IsNullOrEmpty(previous);
    var constraints = new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("CITY_ID", pCity.id) };
    if (existed)
    {
        DB.UpdateValue(CityEconomyStateTableItem.GetTableName(), constraints,
            ColumnVal.Create("CITY_NAME", pCity.data.name ?? ""),
            ColumnVal.Create("KINGDOM_ID", pKingdom.id),
            ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
            ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
            ColumnVal.Create("PREVIOUS_ROLE", previous),
            ColumnVal.Create("ROLE", role),
            ColumnVal.Create("POLICY_POINTS", pContribution.PolicyPoints),
            ColumnVal.Create("TECH_POINTS", pContribution.TechPoints),
            ColumnVal.Create("TAX_VALUE", pContribution.TaxValue),
            ColumnVal.Create("MANPOWER", pContribution.Manpower),
            ColumnVal.Create("FOOD_STABILITY", pContribution.FoodStability),
            ColumnVal.Create("UNREST_RISK", pContribution.UnrestRisk),
            ColumnVal.Create("LAST_YEAR", pYear),
            ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()));
    }
    else
    {
        DB.Insert(CityEconomyStateTableItem.GetTableName(),
            ColumnVal.Create("CITY_ID", pCity.id),
            ColumnVal.Create("CITY_NAME", pCity.data.name ?? ""),
            ColumnVal.Create("KINGDOM_ID", pKingdom.id),
            ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
            ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
            ColumnVal.Create("ROLE", role),
            ColumnVal.Create("PREVIOUS_ROLE", ""),
            ColumnVal.Create("POLICY_POINTS", pContribution.PolicyPoints),
            ColumnVal.Create("TECH_POINTS", pContribution.TechPoints),
            ColumnVal.Create("TAX_VALUE", pContribution.TaxValue),
            ColumnVal.Create("MANPOWER", pContribution.Manpower),
            ColumnVal.Create("FOOD_STABILITY", pContribution.FoodStability),
            ColumnVal.Create("UNREST_RISK", pContribution.UnrestRisk),
            ColumnVal.Create("LAST_YEAR", pYear),
            ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()));
    }
    RecordEconomyMilestone(pKingdom, pCity, previous, role, pContribution, existed);
}
```

Add `ReadRole`, `SumContribution`, and `RecordEconomyMilestone` using the existing `SQLiteCommand`, `HistoryWriter.RecordCity`, and `HistoryWriter.RecordKingdom` patterns. Record city history only when the row is new, the role changes, or `TaxValue >= 25`.

- [ ] **Step 4: Add yearly hook**

Modify `Code/patch/AW_KingdomPolicyPatch.cs`:

```csharp
KingdomPolicyService.OnKingdomYear(__instance);
CityTechService.OnKingdomYear(__instance);
CityEconomyService.OnKingdomYear(__instance);
```

Place `CityEconomyService` after `CityTechService`, so it can read city tech reports.

- [ ] **Step 5: Add point contribution to policy gain**

Modify `CalcPoliticalGain` and `CalcTechGain` in `Code/core/policy/KingdomPolicyService.cs`:

```csharp
float cityEconomy = CityEconomyService.GetPolicyContribution(pKingdom);
return Mathf.Clamp(2f + king + CountCities(pKingdom) * 0.25f + CountUnits(pKingdom) * 0.008f + cityEconomy, 1f, 22f);
```

```csharp
float cityEconomy = CityEconomyService.GetTechContribution(pKingdom);
return Mathf.Clamp(1.5f + king + CountCities(pKingdom) * 0.18f + CountUnits(pKingdom) * 0.004f + cityEconomy, 1f, 20f);
```

- [ ] **Step 6: Add lineage key**

Modify `Code/core/lineage/LineageKeys.cs`:

```csharp
public const string CITY_ECONOMY_LAST_YEAR = "aw_city_economy_last_year";
```

- [ ] **Step 7: Build and test**

Run:

```powershell
dotnet run --project Tests\CityEconomyRuleTests\CityEconomyRuleTests.csproj
$env:DOTNET_ROLL_FORWARD='Major'; dotnet build
```

Expected: rule tests pass and build reports `0 Error(s)`.

- [ ] **Step 8: Commit**

```powershell
git add Code/core/db/CityEconomyStateTableItem.cs Code/core/policy/CityEconomyService.cs Code/patch/AW_KingdomPolicyPatch.cs Code/core/policy/KingdomPolicyService.cs Code/core/lineage/LineageKeys.cs
git commit -m "feat: add city economy yearly state"
```

---

## Task 3: Detailed War Target Selection

**Files:**
- Create: `Code/core/lineage/WarTargetSelectionRules.cs`
- Modify: `Code/core/lineage/WarTerritoryService.cs`
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Modify: `Code/ui/windows/WarDecisionTargetWindow.cs`
- Modify: `Code/core/lineage/RoyalClaimService.cs`
- Test: `Tests/WarFabricationRuleTests/Program.cs`

- [ ] **Step 1: Add rule coverage to existing war tests**

Append to `Tests/WarFabricationRuleTests/Program.cs` inside `Main()` before the final `Console.WriteLine`:

```csharp
ExpectTargetScore("core_city", 140, "take_core_city", hasCore: true, hasStrongClaim: false,
    hasWeakClaim: false, restorationStrength: 0, population: 50);
ExpectTargetScore("strong_claim_city", 110, "press_claim_city", hasCore: false, hasStrongClaim: true,
    hasWeakClaim: false, restorationStrength: 0, population: 50);
ExpectTargetScore("restoration", 125, "restore_kingdom", hasCore: false, hasStrongClaim: false,
    hasWeakClaim: false, restorationStrength: 80, population: 50);
```

Add helper:

```csharp
private static void ExpectTargetScore(string label, int expectedMin, string goalType,
    bool hasCore, bool hasStrongClaim, bool hasWeakClaim, int restorationStrength, int population)
{
    int score = WarTargetSelectionRules.ScoreTarget(goalType, hasCore, hasStrongClaim,
        hasWeakClaim, restorationStrength, population);
    if (score < expectedMin)
        throw new Exception($"Expected {label} score >= {expectedMin}, got {score}.");
}
```

- [ ] **Step 2: Run failing test**

```powershell
dotnet run --project Tests\WarFabricationRuleTests\WarFabricationRuleTests.csproj
```

Expected: build fails because `WarTargetSelectionRules` does not exist.

- [ ] **Step 3: Add pure target scoring rules**

Create `Code/core/lineage/WarTargetSelectionRules.cs`:

```csharp
namespace AncientWarfare3.core.lineage
{
    public static class WarTargetSelectionRules
    {
        public static int ScoreTarget(string pGoalType, bool pHasCore, bool pHasStrongClaim,
            bool pHasWeakClaim, int pRestorationStrength, int pPopulation)
        {
            int score = pPopulation / 2;
            switch (pGoalType)
            {
                case WarTerritoryService.GOAL_TAKE_CORE_CITY:
                    if (pHasCore) score += 120;
                    break;
                case WarTerritoryService.GOAL_PRESS_CLAIM_CITY:
                    if (pHasStrongClaim) score += 90;
                    if (pHasWeakClaim) score += 55;
                    break;
                case WarTerritoryService.GOAL_RESTORE_KINGDOM:
                    score += pRestorationStrength + 35;
                    break;
                case WarTerritoryService.GOAL_FORCE_VASSAL:
                    score += 60;
                    break;
                case WarTerritoryService.GOAL_NO_CB:
                    score -= 35;
                    break;
            }
            return score;
        }
    }
}
```

- [ ] **Step 4: Expose detailed target DTOs**

In `WarTerritoryService`, add:

```csharp
public sealed class WarTargetOption
{
    public Kingdom target_kingdom;
    public City target_city;
    public string goal_type = "";
    public string label = "";
    public long source_core_id = -1;
    public long source_claim_id = -1;
    public long restoration_claim_id = -1;
    public long claimant_actor_id = -1;
    public string claimant_name = "";
    public int score;
}
```

Add:

```csharp
public static List<WarTargetOption> BuildTargetOptions(Kingdom pSource, Kingdom pTarget)
```

This method must enumerate:

- source cores currently held by `pTarget`
- source active claims against cities held by `pTarget`
- hosted restoration claims whose old kingdom cores are held by `pTarget`
- force-vassal and no-CB options when existing rules allow them

Use `WarTargetSelectionRules.ScoreTarget` for ordering.

- [ ] **Step 5: Store selected target ids in decision data**

Modify `KingdomPolicyService.StartWarDecision` so it writes the passed target city, claim, core, and claimant ids. Add or reuse these keys in `LineageKeys`:

```csharp
public const string DECISION_WAR_TARGET_CITY_ID = "aw_decision_war_target_city_id";
public const string DECISION_WAR_SOURCE_CLAIM_ID = "aw_decision_war_source_claim_id";
public const string DECISION_WAR_SOURCE_CORE_ID = "aw_decision_war_source_core_id";
public const string DECISION_WAR_RESTORATION_CLAIM_ID = "aw_decision_war_restoration_claim_id";
```

If a key already exists, reuse the existing one and do not duplicate it.

- [ ] **Step 6: Add option-based declaration entry**

Add to `KingdomPolicyService`:

```csharp
public static bool StartWarDecision(Kingdom pKingdom, WarTerritoryService.WarTargetOption pOption)
{
    if (pOption == null || pOption.target_kingdom?.data == null) return false;
    Actor claimant = pOption.claimant_actor_id >= 0 ? World.world.units.get(pOption.claimant_actor_id) : null;
    bool queued = StartWarDecision(pKingdom, pOption.target_kingdom, pOption.goal_type,
        pOption.target_city, WarTypeForGoal(pOption.goal_type), ReasonKeyForGoal(pOption.goal_type),
        pOption.label, claimant);
    if (!queued) return false;
    pKingdom.data.set(LineageKeys.DECISION_WAR_SOURCE_CLAIM_ID, pOption.source_claim_id);
    pKingdom.data.set(LineageKeys.DECISION_WAR_SOURCE_CORE_ID, pOption.source_core_id);
    pKingdom.data.set(LineageKeys.DECISION_WAR_RESTORATION_CLAIM_ID, pOption.restoration_claim_id);
    return true;
}
```

- [ ] **Step 7: Update war target window**

Modify `WarDecisionTargetWindow`:

- Keep the current kingdom rows.
- Clicking a kingdom row expands a detail block under it.
- The detail block lists `BuildTargetOptions(source, target)`.
- Each option has one compact button using existing `AW_UIStyle.ApplyButton`.
- The button tooltip shows reason, target city, claimant, and decision cost.
- Remove duplicated broad action buttons when the detail block is expanded.

- [ ] **Step 8: Run test and build**

```powershell
dotnet run --project Tests\WarFabricationRuleTests\WarFabricationRuleTests.csproj
$env:DOTNET_ROLL_FORWARD='Major'; dotnet build
```

Expected: tests pass and build reports `0 Error(s)`.

- [ ] **Step 9: Commit**

```powershell
git add Code/core/lineage/WarTargetSelectionRules.cs Code/core/lineage/WarTerritoryService.cs Code/core/policy/KingdomPolicyService.cs Code/core/lineage/LineageKeys.cs Code/ui/windows/WarDecisionTargetWindow.cs Code/core/lineage/RoyalClaimService.cs Tests/WarFabricationRuleTests/Program.cs
git commit -m "feat: add detailed war target selection"
```

---

## Task 4: Automatic Peace Settlement And Records

**Files:**
- Create: `Code/core/db/PeaceSettlementTableItem.cs`
- Create: `Code/core/lineage/PeaceSettlementRules.cs`
- Modify: `Code/core/lineage/WarTerritoryService.cs`
- Modify: `Code/core/lineage/WarRecordWriter.cs`
- Test: `Tests/PeaceSettlementRuleTests/PeaceSettlementRuleTests.csproj`
- Test: `Tests/PeaceSettlementRuleTests/Program.cs`

- [ ] **Step 1: Add peace rule test project**

Create `Tests/PeaceSettlementRuleTests/PeaceSettlementRuleTests.csproj` with the same structure as the city economy test project.

- [ ] **Step 2: Add failing peace rule tests**

Create `Tests/PeaceSettlementRuleTests/Program.cs`:

```csharp
using System;
using AncientWarfare3.core.lineage;

namespace PeaceSettlementRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            Expect("core", PeaceSettlementAction.TransferCity, WarTerritoryService.GOAL_TAKE_CORE_CITY, WarWinner.Attackers);
            Expect("claim", PeaceSettlementAction.TransferCity, WarTerritoryService.GOAL_PRESS_CLAIM_CITY, WarWinner.Attackers);
            Expect("vassal", PeaceSettlementAction.ForceVassal, WarTerritoryService.GOAL_FORCE_VASSAL, WarWinner.Attackers);
            Expect("independence", PeaceSettlementAction.ReleaseVassal, WarTerritoryService.GOAL_INDEPENDENCE, WarWinner.Attackers);
            Expect("restore", PeaceSettlementAction.RestoreKingdom, WarTerritoryService.GOAL_RESTORE_KINGDOM, WarWinner.Attackers);
            Expect("white", PeaceSettlementAction.WhitePeace, WarTerritoryService.GOAL_TAKE_CORE_CITY, WarWinner.Peace);
            Expect("defender", PeaceSettlementAction.DefenderVictory, WarTerritoryService.GOAL_PRESS_CLAIM_CITY, WarWinner.Defenders);

            Console.WriteLine("Peace settlement rule tests passed.");
            return 0;
        }

        private static void Expect(string label, PeaceSettlementAction expected, string goal, WarWinner winner)
        {
            PeaceSettlementAction actual = PeaceSettlementRules.ResolveAction(goal, winner);
            if (actual != expected)
                throw new Exception($"Expected {label} action {expected}, got {actual}.");
        }
    }
}
```

- [ ] **Step 3: Run failing tests**

```powershell
dotnet run --project Tests\PeaceSettlementRuleTests\PeaceSettlementRuleTests.csproj
```

Expected: build fails because `PeaceSettlementRules` does not exist.

- [ ] **Step 4: Add pure peace rules**

Create `Code/core/lineage/PeaceSettlementRules.cs`:

```csharp
namespace AncientWarfare3.core.lineage
{
    public enum PeaceSettlementAction
    {
        None,
        TransferCity,
        ForceVassal,
        ReleaseVassal,
        RestoreKingdom,
        ApplyNoCbOutcome,
        WhitePeace,
        DefenderVictory
    }

    public static class PeaceSettlementRules
    {
        public static PeaceSettlementAction ResolveAction(string pGoalType, WarWinner pWinner)
        {
            if (pWinner == WarWinner.Peace) return PeaceSettlementAction.WhitePeace;
            if (pWinner == WarWinner.Defenders) return PeaceSettlementAction.DefenderVictory;
            if (pWinner != WarWinner.Attackers) return PeaceSettlementAction.None;

            switch (pGoalType)
            {
                case WarTerritoryService.GOAL_TAKE_CORE_CITY:
                case WarTerritoryService.GOAL_PRESS_CLAIM_CITY:
                    return PeaceSettlementAction.TransferCity;
                case WarTerritoryService.GOAL_FORCE_VASSAL:
                    return PeaceSettlementAction.ForceVassal;
                case WarTerritoryService.GOAL_INDEPENDENCE:
                    return PeaceSettlementAction.ReleaseVassal;
                case WarTerritoryService.GOAL_RESTORE_KINGDOM:
                    return PeaceSettlementAction.RestoreKingdom;
                case WarTerritoryService.GOAL_NO_CB:
                    return PeaceSettlementAction.ApplyNoCbOutcome;
                default:
                    return PeaceSettlementAction.None;
            }
        }
    }
}
```

- [ ] **Step 5: Add peace settlement table**

Create `Code/core/db/PeaceSettlementTableItem.cs`:

```csharp
using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("PeaceSettlement")]
    public class PeaceSettlementTableItem : AbstractTableItem<PeaceSettlementTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long settlement_id;
        public long war_id = -1;
        public long war_goal_id = -1;
        public string action = "";
        public long winner_kingdom_id = -1;
        public string winner_name = "";
        public long loser_kingdom_id = -1;
        public string loser_name = "";
        public long target_city_id = -1;
        public string target_city_name = "";
        public long claimant_actor_id = -1;
        public string claimant_name = "";
        public string terms_text = "";
        public double world_time = -1;
    }
}
```

- [ ] **Step 6: Use settlement action in `ResolveGoal`**

Modify `WarTerritoryService.ResolveGoal`:

```csharp
PeaceSettlementAction action = PeaceSettlementRules.ResolveAction(pGoal.goal_type, pWinner);
switch (action)
{
    case PeaceSettlementAction.TransferCity:
        TryTransferTargetCity(attacker, targetCity);
        RecordGoalVictory(attacker, defender, targetCity, pGoal);
        break;
    case PeaceSettlementAction.ForceVassal:
        VassalService.SetVassal(defender, attacker, "peace_force_vassal", pWar.data.id);
        RecordGoalVictory(attacker, defender, targetCity, pGoal);
        break;
    case PeaceSettlementAction.ReleaseVassal:
        VassalService.RemoveVassal(attacker, "independence_war_won");
        RecordGoalVictory(attacker, defender, targetCity, pGoal);
        break;
    case PeaceSettlementAction.RestoreKingdom:
        RoyalClaimService.OnRestorationWarWon(attacker, defender, pWar.data.id, pGoal.source_claim_id, targetCity);
        RecordGoalVictory(attacker, defender, targetCity, pGoal);
        break;
    case PeaceSettlementAction.WhitePeace:
        RecordGoalFailure(attacker, defender, targetCity, pGoal, "white_peace");
        break;
    case PeaceSettlementAction.DefenderVictory:
        RecordGoalFailure(attacker, defender, targetCity, pGoal, "defender_victory");
        break;
}
```

Add `InsertPeaceSettlement(pWar, pGoal, action, attacker, defender, targetCity)` after the switch.

- [ ] **Step 7: Localize reason keys in history**

Replace new literal reason strings with stable keys:

- `white_peace`
- `defender_victory`
- `peace_force_vassal`
- `independence_war_won`
- `attacker_goal_enforced`

Use `AW_L10n.Text` in UI-facing text and store stable event keys in DB rows.

- [ ] **Step 8: Run tests and build**

```powershell
dotnet run --project Tests\PeaceSettlementRuleTests\PeaceSettlementRuleTests.csproj
$env:DOTNET_ROLL_FORWARD='Major'; dotnet build
```

Expected: tests pass and build reports `0 Error(s)`.

- [ ] **Step 9: Commit**

```powershell
git add Code/core/db/PeaceSettlementTableItem.cs Code/core/lineage/PeaceSettlementRules.cs Code/core/lineage/WarTerritoryService.cs Code/core/lineage/WarRecordWriter.cs Tests/PeaceSettlementRuleTests
git commit -m "feat: add automatic peace settlements"
```

---

## Task 5: Advanced General Rebellion Without Dedicated UI

**Files:**
- Create: `Code/core/lineage/GeneralRebellionRules.cs`
- Modify: `Code/core/lineage/GeneralRebellionService.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Test: `Tests/GeneralRebellionRuleTests/GeneralRebellionRuleTests.csproj`
- Test: `Tests/GeneralRebellionRuleTests/Program.cs`

- [ ] **Step 1: Add general rebellion rule test project**

Create `Tests/GeneralRebellionRuleTests/GeneralRebellionRuleTests.csproj` with the same structure as the city economy test project.

- [ ] **Step 2: Add failing branch tests**

Create `Tests/GeneralRebellionRuleTests/Program.cs`:

```csharp
using System;
using AncientWarfare3.core.lineage;

namespace GeneralRebellionRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            ExpectBranch("palace", GeneralRebellionBranch.PalaceCoup,
                crisis: 45, personalRisk: 85, hasFief: false, nearCapital: true,
                borderFief: false, strongNeighbor: false, hasRestorationClaim: false);
            ExpectBranch("fief", GeneralRebellionBranch.FiefIndependence,
                crisis: 55, personalRisk: 85, hasFief: true, nearCapital: false,
                borderFief: false, strongNeighbor: false, hasRestorationClaim: false);
            ExpectBranch("defect", GeneralRebellionBranch.DefectToNeighbor,
                crisis: 60, personalRisk: 80, hasFief: true, nearCapital: false,
                borderFief: true, strongNeighbor: true, hasRestorationClaim: false);
            ExpectBranch("restore", GeneralRebellionBranch.SupportRestoration,
                crisis: 70, personalRisk: 75, hasFief: true, nearCapital: false,
                borderFief: true, strongNeighbor: true, hasRestorationClaim: true);
            ExpectBranch("none", GeneralRebellionBranch.None,
                crisis: 15, personalRisk: 40, hasFief: false, nearCapital: false,
                borderFief: false, strongNeighbor: false, hasRestorationClaim: false);

            Console.WriteLine("General rebellion rule tests passed.");
            return 0;
        }

        private static void ExpectBranch(string label, GeneralRebellionBranch expected,
            int crisis, int personalRisk, bool hasFief, bool nearCapital, bool borderFief,
            bool strongNeighbor, bool hasRestorationClaim)
        {
            GeneralRebellionBranch actual = GeneralRebellionRules.SelectBranch(crisis, personalRisk,
                hasFief, nearCapital, borderFief, strongNeighbor, hasRestorationClaim);
            if (actual != expected)
                throw new Exception($"Expected {label} branch {expected}, got {actual}.");
        }
    }
}
```

- [ ] **Step 3: Run failing tests**

```powershell
dotnet run --project Tests\GeneralRebellionRuleTests\GeneralRebellionRuleTests.csproj
```

Expected: build fails because `GeneralRebellionRules` does not exist.

- [ ] **Step 4: Add pure rebellion branch rules**

Create `Code/core/lineage/GeneralRebellionRules.cs`:

```csharp
namespace AncientWarfare3.core.lineage
{
    public enum GeneralRebellionBranch
    {
        None,
        PalaceCoup,
        FiefIndependence,
        DirectMilitaryRebellion,
        DefectToNeighbor,
        SupportRestoration
    }

    public static class GeneralRebellionRules
    {
        public static int CalculateKingdomCrisis(int weakKingScore, bool childOrOldRuler,
            bool successionUnstable, bool recentWarDefeat, bool capitalThreatened,
            int nonCoreCityCount, int disloyalVassalCount, int mandateValue, bool hasRoyalGuard)
        {
            int risk = weakKingScore;
            if (childOrOldRuler) risk += 14;
            if (successionUnstable) risk += 12;
            if (recentWarDefeat) risk += 14;
            if (capitalThreatened) risk += 16;
            risk += nonCoreCityCount * 4;
            risk += disloyalVassalCount * 5;
            if (mandateValue < 20) risk += 10;
            if (hasRoyalGuard) risk -= 10;
            if (risk < 0) return 0;
            if (risk > 100) return 100;
            return risk;
        }

        public static GeneralRebellionBranch SelectBranch(int crisis, int personalRisk,
            bool hasFief, bool nearCapital, bool borderFief, bool strongNeighbor, bool hasRestorationClaim)
        {
            int combined = (crisis + personalRisk) / 2;
            if (combined < 55) return GeneralRebellionBranch.None;
            if (hasRestorationClaim && crisis >= 60) return GeneralRebellionBranch.SupportRestoration;
            if (borderFief && strongNeighbor && personalRisk >= 75) return GeneralRebellionBranch.DefectToNeighbor;
            if (nearCapital && personalRisk >= 80 && crisis >= 35) return GeneralRebellionBranch.PalaceCoup;
            if (hasFief && crisis >= 45 && personalRisk >= 75) return GeneralRebellionBranch.FiefIndependence;
            if (personalRisk >= 88) return GeneralRebellionBranch.DirectMilitaryRebellion;
            return GeneralRebellionBranch.None;
        }
    }
}
```

- [ ] **Step 5: Integrate into `GeneralRebellionService`**

Modify `OnKingdomRiskCheck`:

```csharp
int crisis = CalculateKingdomCrisis(pKingdom);
...
GeneralRebellionBranch branch = GeneralRebellionRules.SelectBranch(crisis, risk,
    FiefService.GetFiefCityId(general) >= 0,
    IsNearCapital(general, pKingdom),
    IsBorderFief(general, pKingdom),
    HasStrongNeighbor(general, pKingdom),
    HasRestorationOpportunity(general, pKingdom));
if (branch != GeneralRebellionBranch.None)
    TryRebel(general, pKingdom, risk, branch);
```

Change `TryRebel` signature:

```csharp
private static bool TryRebel(Actor pGeneral, Kingdom pOldKingdom, int pRisk, GeneralRebellionBranch pBranch)
```

Branch behavior:

- `PalaceCoup`: record coup attempt in person and kingdom history; if successful, call `pOldKingdom.setKing(pGeneral)`; if failure, reduce loyalty/mark risk cooldown.
- `FiefIndependence`: existing `makeOwnKingdom` path.
- `DirectMilitaryRebellion`: existing rebellion path using current city if valid.
- `DefectToNeighbor`: transfer fief city to selected strong neighbor if valid and record city/kingdom/person history.
- `SupportRestoration`: call restoration war setup if a hosted claim is available; otherwise fall back to fief independence.

- [ ] **Step 6: Keep UI surface minimal**

Do not add a window. Add only:

- biography events
- city history events
- major kingdom history events
- optional tooltip line in existing biography role text if `GeneralService.IsGeneral(actor)` is true

- [ ] **Step 7: Run tests and build**

```powershell
dotnet run --project Tests\GeneralRebellionRuleTests\GeneralRebellionRuleTests.csproj
$env:DOTNET_ROLL_FORWARD='Major'; dotnet build
```

Expected: tests pass and build reports `0 Error(s)`.

- [ ] **Step 8: Commit**

```powershell
git add Code/core/lineage/GeneralRebellionRules.cs Code/core/lineage/GeneralRebellionService.cs Code/core/lineage/ChronicleEvents.cs Tests/GeneralRebellionRuleTests
git commit -m "feat: expand general rebellion branches"
```

---

## Task 6: Mandate-Only Temple Names And Double Titles

**Files:**
- Create: `Code/core/db/MandateRulerTitleTableItem.cs`
- Create: `Code/core/lineage/MandateRulerTitleDefs.cs`
- Create: `Code/core/lineage/MandateRulerTitleRules.cs`
- Create: `Code/core/lineage/MandateRulerTitleService.cs`
- Modify: `Code/core/lineage/PosthumousTitleService.cs`
- Modify: `Code/ui/windows/MandateDynastyWindow.cs`
- Test: `Tests/MandateRulerTitleRuleTests/MandateRulerTitleRuleTests.csproj`
- Test: `Tests/MandateRulerTitleRuleTests/Program.cs`

- [ ] **Step 1: Add mandate title rule test project**

Create `Tests/MandateRulerTitleRuleTests/MandateRulerTitleRuleTests.csproj` with the same structure as the city economy test project.

- [ ] **Step 2: Add failing title tests**

Create `Tests/MandateRulerTitleRuleTests/Program.cs`:

```csharp
using System;
using AncientWarfare3.core.lineage;

namespace MandateRulerTitleRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            ExpectTemple("founder", "太祖", founder: true, lowOrigin: false, refounder: false,
                conquestScore: 80, reformScore: 20, reignIndex: 1);
            ExpectTemple("low_origin", "高祖", founder: true, lowOrigin: true, refounder: false,
                conquestScore: 55, reformScore: 20, reignIndex: 1);
            ExpectTemple("refounder", "世祖", founder: true, lowOrigin: false, refounder: true,
                conquestScore: 55, reformScore: 20, reignIndex: 1);
            ExpectTemple("second_reformer", "太宗", founder: false, lowOrigin: false, refounder: false,
                conquestScore: 20, reformScore: 80, reignIndex: 2);

            string pair = MandateRulerTitleRules.SelectDoublePosthumousTitle(civil: 70, war: 65,
                order: 40, disaster: 0);
            if (pair.Length != 2) throw new Exception("Expected two-character mandate posthumous title.");

            string bad = MandateRulerTitleRules.SelectDoublePosthumousTitle(civil: 0, war: 0,
                order: 0, disaster: 80);
            if (bad.Length != 2 || bad == pair) throw new Exception("Expected distinct negative title pair.");

            Console.WriteLine("Mandate ruler title rule tests passed.");
            return 0;
        }

        private static void ExpectTemple(string label, string expected, bool founder, bool lowOrigin,
            bool refounder, int conquestScore, int reformScore, int reignIndex)
        {
            string actual = MandateRulerTitleRules.SelectTempleName(founder, lowOrigin, refounder,
                conquestScore, reformScore, reignIndex);
            if (actual != expected)
                throw new Exception($"Expected {label} temple {expected}, got {actual}.");
        }
    }
}
```

- [ ] **Step 3: Run failing tests**

```powershell
dotnet run --project Tests\MandateRulerTitleRuleTests\MandateRulerTitleRuleTests.csproj
```

Expected: build fails because `MandateRulerTitleRules` does not exist.

- [ ] **Step 4: Add mandate title definitions**

Create `Code/core/lineage/MandateRulerTitleDefs.cs`:

```csharp
namespace AncientWarfare3.core.lineage
{
    internal static class MandateRulerTitleDefs
    {
        public static readonly string[] PositivePairs =
        {
            "文武", "昭烈", "宣德", "孝武", "明德", "成武", "仁宣", "景文"
        };

        public static readonly string[] NegativePairs =
        {
            "幽厉", "灵荒", "炀暴", "悖乱", "昏虐", "哀冲"
        };
    }
}
```

- [ ] **Step 5: Add pure mandate title rules**

Create `Code/core/lineage/MandateRulerTitleRules.cs`:

```csharp
namespace AncientWarfare3.core.lineage
{
    public static class MandateRulerTitleRules
    {
        public static string SelectTempleName(bool founder, bool lowOrigin, bool refounder,
            int conquestScore, int reformScore, int reignIndex)
        {
            if (founder && refounder) return "世祖";
            if (founder && lowOrigin) return "高祖";
            if (founder && conquestScore >= 70) return "太祖";
            if (founder) return "高祖";
            if (reignIndex == 2 && reformScore >= 60) return "太宗";
            if (reignIndex >= 3 && reformScore >= 70) return "世宗";
            if (conquestScore >= 75) return "烈祖";
            return "";
        }

        public static string SelectDoublePosthumousTitle(int civil, int war, int order, int disaster)
        {
            if (disaster >= 60) return MandateRulerTitleDefs.NegativePairs[disaster % MandateRulerTitleDefs.NegativePairs.Length];
            int score = civil + war + order;
            return MandateRulerTitleDefs.PositivePairs[score % MandateRulerTitleDefs.PositivePairs.Length];
        }
    }
}
```

- [ ] **Step 6: Add durable table**

Create `Code/core/db/MandateRulerTitleTableItem.cs`:

```csharp
using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("MandateRulerTitle")]
    public class MandateRulerTitleTableItem : AbstractTableItem<MandateRulerTitleTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long record_id;
        public long period_id = -1;
        public long reign_id = -1;
        public long actor_id = -1;
        public string actor_name = "";
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public string temple_name = "";
        public string double_posthumous = "";
        public string full_title = "";
        public string reason_key = "";
        public string score_detail = "";
        public double decided_time = -1;
    }
}
```

- [ ] **Step 7: Extend `ReignInfo` with fields already stored in `KingdomReign`**

Modify `Code/core/lineage/ReignRecordWriter.cs`:

```csharp
public int ReignIndex;
public double EndTime;
```

Update `ReadOpenReignInfo` so it selects and assigns `REIGN_INDEX` and `END_TIME` from `KingdomReign`. If the current query only reads open reign rows, set `EndTime = World.world.getCurWorldTime()` for open reigns. This keeps mandate title scoring from guessing the reign index.

- [ ] **Step 8: Add mandate title service**

Create `Code/core/lineage/MandateRulerTitleService.cs`:

```csharp
using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateRulerTitleService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static void OnMandateReignEnded(Kingdom pKingdom, Actor pKing,
            ReignRecordWriter.ReignInfo pReign, string pEndReason)
        {
            if (pKingdom?.data == null || pKing?.data == null || !pReign.IsValid || !Ready) return;
            MandateReport report = MandateService.ReadReport();
            if (!report.active && report.period_id < 0) return;
            pKingdom.data.get(LineageKeys.MANDATE_PERIOD_ID, out long periodId, -1L);
            if (periodId < 0) periodId = report.period_id;
            if (periodId < 0) return;

            double endTime = pReign.EndTime > 0 ? pReign.EndTime : World.world.getCurWorldTime();
            var warRecord = WarRecordWriter.GetWarRecord(pKingdom.id, pReign.StartTime, endTime);
            string temple = MandateRulerTitleRules.SelectTempleName(
                founder: pReign.IsFounder == 1 || pReign.ReignIndex <= 1,
                lowOrigin: report.origin_type == "rebel" || report.claimant_kind == "rebel",
                refounder: report.origin_type == "restoration",
                conquestScore: warRecord.wins * 25,
                reformScore: report.dynasty_prestige,
                reignIndex: pReign.ReignIndex <= 0 ? 1 : pReign.ReignIndex);
            string pair = MandateRulerTitleRules.SelectDoublePosthumousTitle(
                civil: report.imperial_authority,
                war: warRecord.wins * 20,
                order: report.mandate_value,
                disaster: pEndReason == "kingdom_fell" ? 90 : 0);
            InsertTitle(pKingdom, pKing, pReign, periodId, temple, pair, pEndReason);
        }
    }
}
```

- [ ] **Step 9: Add insert and history records**

In `MandateRulerTitleService`, add `InsertTitle`:

```csharp
private static void InsertTitle(Kingdom pKingdom, Actor pKing, ReignRecordWriter.ReignInfo pReign,
    long pPeriodId, string pTemple, string pPair, string pEndReason)
{
    long id = TableIdAllocator.Next(DB, MandateRulerTitleTableItem.GetTableName(), "RECORD_ID");
    string full = string.IsNullOrEmpty(pTemple) ? pPair + "皇帝" : pTemple + " " + pPair + "皇帝";
    DB.Insert(MandateRulerTitleTableItem.GetTableName(),
        ColumnVal.Create("RECORD_ID", id),
        ColumnVal.Create("PERIOD_ID", pPeriodId),
        ColumnVal.Create("REIGN_ID", pReign.ReignId),
        ColumnVal.Create("ACTOR_ID", pKing.data.id),
        ColumnVal.Create("ACTOR_NAME", pKing.getName() ?? ""),
        ColumnVal.Create("KINGDOM_ID", pKingdom.id),
        ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
        ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
        ColumnVal.Create("TEMPLE_NAME", pTemple ?? ""),
        ColumnVal.Create("DOUBLE_POSTHUMOUS", pPair ?? ""),
        ColumnVal.Create("FULL_TITLE", full),
        ColumnVal.Create("REASON_KEY", pEndReason ?? ""),
        ColumnVal.Create("SCORE_DETAIL", ""),
        ColumnVal.Create("DECIDED_TIME", LineageService.CurTime()));

    MandateService.RecordMandateEvent("mandate_ruler_title", pKingdom, pKing, null, 0,
        MandateService.ReadReport().mandate_value, full);
    HistoryWriter.RecordKingdom(pKingdom, "mandate_ruler_title",
        HistoryText.Actor(pKing) + HistoryText.PlainText(" 定天命尊号 ") + HistoryText.Colored(full, HistoryColors.FromKingdom(pKingdom)),
        HistoryTarget.Actor(pKing));
    HistoryWriter.RecordPerson(pKing.data.id, pKingdom, pKing.getName(), "mandate_ruler_title",
        HistoryText.Actor(pKing) + HistoryText.PlainText(" 定天命尊号 ") + HistoryText.Colored(full, HistoryColors.FromKingdom(pKingdom)),
        ChronicleCategory.HONOR, HistoryTarget.Kingdom(pKingdom));
}
```

- [ ] **Step 10: Add mandate kingdom helper**

Modify `Code/core/lineage/MandateService.cs`:

```csharp
public static bool IsMandateKingdom(Kingdom pKingdom)
{
    return pKingdom?.data != null && GetCurrentMandateKingdom()?.id == pKingdom.id;
}
```

- [ ] **Step 11: Invoke after ordinary posthumous title**

Modify `PosthumousTitleService.OnReignEnded` after `ReignRecordWriter.SetPosthumous(...)`:

```csharp
if (MandateService.IsMandateKingdom(pKingdom))
    MandateRulerTitleService.OnMandateReignEnded(pKingdom, pKing, pReign, pEndReason);
```

- [ ] **Step 12: Run tests and build**

```powershell
dotnet run --project Tests\MandateRulerTitleRuleTests\MandateRulerTitleRuleTests.csproj
$env:DOTNET_ROLL_FORWARD='Major'; dotnet build
```

Expected: tests pass and build reports `0 Error(s)`.

- [ ] **Step 13: Commit**

```powershell
git add Code/core/db/MandateRulerTitleTableItem.cs Code/core/lineage/MandateRulerTitleDefs.cs Code/core/lineage/MandateRulerTitleRules.cs Code/core/lineage/MandateRulerTitleService.cs Code/core/lineage/PosthumousTitleService.cs Code/core/lineage/ReignRecordWriter.cs Code/core/lineage/MandateService.cs Code/ui/windows/MandateDynastyWindow.cs Tests/MandateRulerTitleRuleTests
git commit -m "feat: add mandate ruler temple titles"
```

---

## Task 7: Localization, README, And Final Verification

**Files:**
- Modify: `Locales/aw3_policy_ui.csv`
- Modify: `Locales/others.csv` or create `Locales/aw3_history_events.csv`
- Modify: `README.md`
- Modify: `docs/AW3_Roadmap.md`

- [ ] **Step 1: Add localization keys**

Add keys for:

- `aw_city_economy_role_capital_admin`
- `aw_city_economy_role_agrarian_granary`
- `aw_city_economy_role_market_trade`
- `aw_city_economy_role_frontier_military`
- `aw_city_economy_role_workshop_craft`
- `aw_city_economy_role_occupied_unrest`
- `aw_war_target_detail_title`
- `aw_war_target_claimant`
- `aw_peace_term_transfer_city`
- `aw_peace_term_force_vassal`
- `aw_peace_term_release_vassal`
- `aw_peace_term_restore_kingdom`
- `aw_general_rebellion_palace_coup`
- `aw_general_rebellion_fief_independence`
- `aw_general_rebellion_defect_neighbor`
- `aw_general_rebellion_support_restoration`
- `aw_mandate_temple_title`
- `aw_mandate_double_posthumous`

Chinese CSV text must use full-width Chinese commas in localized prose where comma separation would break CSV parsing.

- [ ] **Step 2: Update README status table**

Update `README.md`:

- City tax and economy specialization: mark first version complete.
- War reason and peace: mark detailed target and automatic peace complete, manual treaty window remaining.
- General/fief: mark advanced rebellion branches complete, dedicated UI intentionally not planned for this pass.
- Mandate titles: mark mandate temple names and double posthumous titles complete.

- [ ] **Step 3: Update roadmap**

Update `docs/AW3_Roadmap.md` with the same status and leave manual treaty negotiation as future work.

- [ ] **Step 4: Run all rule tests**

```powershell
dotnet run --project Tests\WarFabricationRuleTests\WarFabricationRuleTests.csproj
dotnet run --project Tests\CityEconomyRuleTests\CityEconomyRuleTests.csproj
dotnet run --project Tests\PeaceSettlementRuleTests\PeaceSettlementRuleTests.csproj
dotnet run --project Tests\GeneralRebellionRuleTests\GeneralRebellionRuleTests.csproj
dotnet run --project Tests\MandateRulerTitleRuleTests\MandateRulerTitleRuleTests.csproj
```

Expected: every command prints its `... tests passed.` line.

- [ ] **Step 5: Build**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet build
```

Expected: `0 Error(s)`.

- [ ] **Step 6: Commit**

```powershell
git add Locales README.md docs/AW3_Roadmap.md
git commit -m "docs: update AW3 economy war mandate status"
```

---

## Manual Game Checks

After all tasks build:

- Create a Xia kingdom with multiple cities and confirm city economy rows appear in save DB.
- Confirm non-capital cities produce city economy history only on role change or major event.
- Confirm war target window can choose a specific core city, claim city, and restoration claimant.
- Confirm AI queues `aw_decision_declare_war` with target city metadata.
- Confirm war end transfers the selected city or restores selected old kingdom as a vassal.
- Confirm no dedicated general/fief UI was added.
- Confirm a weak kingdom with high-risk general can produce coup, fief independence, defection, or restoration support depending on state.
- Confirm ordinary rulers keep ordinary single-character titles.
- Confirm mandate rulers receive mandate-only temple/double titles and appear in mandate history.

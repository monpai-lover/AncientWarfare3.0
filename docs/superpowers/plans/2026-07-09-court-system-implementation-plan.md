# AW3 Court System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a low-overhead official court system with primitive court, Hundred Schools factions, real actor offices, city bureaus, AI policy influence, UI, history, benchmark, and zh/en/traditional Chinese localization.

**Architecture:** Build the system around small pure rule classes first, then add cached DB-backed state and a thin yearly service. UI and AI read cached court state; only appointment changes, deaths, defections, and scheduled low-frequency refreshes recalculate faction influence.

**Tech Stack:** C# net48, NeoModLoader/WorldBox publicized API, Unity UI, existing SQLite archive tables, existing AW3 policy/history/benchmark patterns.

---

## File Structure

Create:

- `Code/core/court/CourtIds.cs`: shared court office, layer, school, trait, event, and data-key constants.
- `Code/core/court/CourtRules.cs`: pure office-slot, refresh, unlock, and city-bureau rules.
- `Code/core/court/CourtInfluenceRules.cs`: pure school influence and dominant-faction rules.
- `Code/core/court/CourtAIRules.cs`: pure scoring modifiers for research, decisions, war tendency, and mandate tendency.
- `Code/core/court/CourtTraitRules.cs`: pure trait assignment decisions.
- `Code/core/court/CourtStateCodec.cs`: encode/decode compact faction cache strings for kingdom data and DB snapshots.
- `Code/core/court/CourtService.cs`: WorldBox-facing yearly service, appointment validation, candidate refresh, trait sync, and cache update.
- `Code/core/db/KingdomCourtStateTableItem.cs`: one row per kingdom court snapshot.
- `Code/core/db/CourtOfficerTableItem.cs`: one row per active or former court officer.
- `Code/core/db/CityBureauStateTableItem.cs`: one row per city bureau snapshot.
- `Code/ui/windows/CourtWindow.cs`: original-style scroll window for court overview and manual appointment actions.
- `Tests/CourtSystemRuleTests/CourtSystemRuleTests.csproj`: pure rule test project.
- `Tests/CourtSystemRuleTests/Program.cs`: tests for office slots, influence cache, AI weights, trait rules, and refresh gates.
- `Locales/aw3_court.csv`: zh/en/traditional Chinese text for court UI, tech, offices, schools, events, and tooltips.

Modify:

- `Code/content/policies/KingdomPolicyDefs.cs`: add `aw_tech_official_court` and place it in the tech tree.
- `Code/content/XiaTraits.cs`: register office-only Hundred Schools traits.
- `Code/core/db/LineageArchiveIndexRules.cs`: add court indexes.
- `Code/core/lineage/LineageKeys.cs`: add court keys for compact kingdom/city/actor state.
- `Code/core/lineage/ChronicleKeys.cs`: add court event identifiers.
- `Code/core/lineage/ChronicleEvents.cs`: write important court events to person, kingdom, city, and mandate histories.
- `Code/core/lineage/HistoryLocalizationRules.cs`: add history event display labels.
- `Code/core/policy/KingdomPolicyAI.cs`: apply cached court bias to tech, policy, and decision selection.
- `Code/core/policy/KingdomPolicyService.cs`: call court yearly update after policy initialization and before AI slot filling.
- `Code/core/policy/UpdateAgeBenchmarkRules.cs`: add court benchmark entries under kingdom policy/update-age grouping.
- `Code/ui/AW_LineageWindowIds.cs`: add `COURT`.
- `Code/ui/windows/KingdomWindowAddition.cs`: add one wide court button under the existing four-button policy row.
- `Code/ModClass.cs`: no direct court call needed if all patches and reflected tables are used; only update if a new explicit init is required after testing.

Verification commands:

- `dotnet run --project Tests\CourtSystemRuleTests\CourtSystemRuleTests.csproj`
- `dotnet run --project Tests\CityEconomyRuleTests\CityEconomyRuleTests.csproj`
- `dotnet run --project Tests\WarFabricationRuleTests\WarFabricationRuleTests.csproj`
- `dotnet build AncientWarfare3.csproj`

---

### Task 1: Pure Court Rule Tests And Core Constants

**Files:**
- Create: `Tests/CourtSystemRuleTests/CourtSystemRuleTests.csproj`
- Create: `Tests/CourtSystemRuleTests/Program.cs`
- Create: `Code/core/court/CourtIds.cs`
- Create: `Code/core/court/CourtRules.cs`
- Create: `Code/core/court/CourtInfluenceRules.cs`
- Create: `Code/core/court/CourtAIRules.cs`
- Create: `Code/core/court/CourtTraitRules.cs`

- [ ] **Step 1: Write the failing rule test project**

Create `Tests/CourtSystemRuleTests/CourtSystemRuleTests.csproj`:

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

Create `Tests/CourtSystemRuleTests/Program.cs`:

```csharp
using System;
using AncientWarfare3.core.court;

namespace CourtSystemRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                Expect(CourtRules.IsCourtUnlocked(hasPolicySystem: true, hasOfficialCourtTech: true), "official court unlocks with tech");
                Expect(!CourtRules.IsCourtUnlocked(hasPolicySystem: true, hasOfficialCourtTech: false), "official court stays locked without tech");
                Expect(CourtRules.UsePrimitiveCourt(hasPolicySystem: true, hasOfficialCourtTech: false), "primitive court before tech");
                Expect(!CourtRules.UsePrimitiveCourt(hasPolicySystem: false, hasOfficialCourtTech: true), "unsupported kingdoms do not show full court");

                ExpectEqual(1, CourtRules.CityOfficeSlots(population: 12, zoneCount: 3, isCapital: false), "small city slots");
                ExpectEqual(2, CourtRules.CityOfficeSlots(population: 70, zoneCount: 10, isCapital: false), "middle city slots");
                ExpectEqual(3, CourtRules.CityOfficeSlots(population: 130, zoneCount: 20, isCapital: true), "large capital slots");

                Expect(CourtRules.ShouldRefreshCourt(currentYear: 40, lastRefreshYear: 35, intervalYears: 5), "refresh interval reached");
                Expect(!CourtRules.ShouldRefreshCourt(currentYear: 40, lastRefreshYear: 38, intervalYears: 5), "refresh interval not reached");

                ExpectEqual(CourtSchoolId.Legalist, CourtInfluenceRules.DominantSchool("ru=12;fa=20;dao=3;mo=4", CourtSchoolId.Ru), "dominant legalist");
                ExpectEqual(0.625f, CourtInfluenceRules.Concentration(25f, 40f), "concentration");
                Expect(CourtInfluenceRules.ShouldTriggerStrongEvent(yearsDominant: 8, dominantShare: 0.61f, crisis: false, weakKing: false), "long dominance strong event");
                Expect(CourtInfluenceRules.ShouldTriggerStrongEvent(yearsDominant: 2, dominantShare: 0.48f, crisis: true, weakKing: true), "crisis strong event");
                Expect(!CourtInfluenceRules.ShouldTriggerStrongEvent(yearsDominant: 2, dominantShare: 0.48f, crisis: false, weakKing: false), "no strong event");

                Expect(CourtTraitRules.ShouldHoldSchoolTrait(isOfficer: true, alive: true, defected: false), "active officer holds trait");
                Expect(!CourtTraitRules.ShouldHoldSchoolTrait(isOfficer: false, alive: true, defected: false), "non officer loses trait");
                Expect(!CourtTraitRules.ShouldHoldSchoolTrait(isOfficer: true, alive: false, defected: false), "dead officer loses trait");

                Expect(CourtAIRules.ScoreResearch(CourtSchoolId.Legalist, "aw_policy_early_law", atWar: false, mandateExists: false) > 0, "legalist boosts law");
                Expect(CourtAIRules.ScoreResearch(CourtSchoolId.Mohist, "aw_tech_city_defense", atWar: false, mandateExists: false) > 0, "mohist boosts defense");
                Expect(CourtAIRules.ScoreResearch(CourtSchoolId.Military, "aw_tech_chariot_training", atWar: true, mandateExists: false) > CourtAIRules.ScoreResearch(CourtSchoolId.Military, "aw_tech_chariot_training", atWar: false, mandateExists: false), "military values wartime training");
                Expect(CourtAIRules.ScoreDecision(CourtSchoolId.Diplomat, "aw_decision_declare_war", cities: 4, atWar: false, unstable: false) < CourtAIRules.ScoreDecision(CourtSchoolId.Military, "aw_decision_declare_war", cities: 4, atWar: false, unstable: false), "military favors war more than diplomat");

                Console.WriteLine("Court system rule tests passed.");
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.GetType().FullName + ": " + e.Message);
                return 1;
            }
        }

        private static void Expect(bool value, string label)
        {
            if (!value) throw new Exception("Expected true: " + label);
        }

        private static void ExpectEqual<T>(T expected, T actual, string label)
        {
            if (!Equals(expected, actual))
                throw new Exception($"Expected {label} {expected}, got {actual}.");
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet run --project Tests\CourtSystemRuleTests\CourtSystemRuleTests.csproj
```

Expected: compile failure because `AncientWarfare3.core.court` and its rule types do not exist.

- [ ] **Step 3: Add court identifiers**

Create `Code/core/court/CourtIds.cs`:

```csharp
namespace AncientWarfare3.core.court
{
    public static class CourtOfficeLayer
    {
        public const string Primitive = "primitive";
        public const string Central = "central";
        public const string City = "city";
        public const string Military = "military";
        public const string Censor = "censor";
    }

    public static class CourtOfficeId
    {
        public const string Chancellor = "chancellor";
        public const string Censor = "censor";
        public const string Marshal = "marshal";
        public const string Justice = "justice";
        public const string Steward = "steward";
        public const string Erudite = "erudite";
        public const string Governor = "governor";
        public const string GranaryOfficer = "granary_officer";
        public const string Constable = "constable";
    }

    public static class CourtSchoolId
    {
        public const string None = "";
        public const string PrimitiveMinister = "primitive_minister";
        public const string Warrior = "warrior";
        public const string Elder = "elder";
        public const string Shaman = "shaman";
        public const string Hermit = "hermit";
        public const string Ru = "ru";
        public const string Legalist = "fa";
        public const string Dao = "dao";
        public const string Mohist = "mo";
        public const string Military = "bing";
        public const string Diplomat = "zongheng";
        public const string Agrarian = "nong";
        public const string YinYang = "yinyang";
        public const string Logician = "ming";
    }

    public static class CourtTraitId
    {
        public const string Ru = "aw_school_ru";
        public const string Legalist = "aw_school_fa";
        public const string Dao = "aw_school_dao";
        public const string Mohist = "aw_school_mo";
        public const string Military = "aw_school_bing";
        public const string Diplomat = "aw_school_zongheng";
        public const string Agrarian = "aw_school_nong";
        public const string YinYang = "aw_school_yinyang";
        public const string Logician = "aw_school_ming";
    }

    public static class CourtEvents
    {
        public const string Founded = "court_founded";
        public const string PrimitiveUpgraded = "court_primitive_upgraded";
        public const string OfficerAppointed = "court_officer_appointed";
        public const string OfficerDismissed = "court_officer_dismissed";
        public const string FactionDominant = "court_faction_dominant";
        public const string ReformEvent = "court_reform_event";
        public const string CityBureauChanged = "court_city_bureau_changed";
    }
}
```

- [ ] **Step 4: Add pure rules**

Create `Code/core/court/CourtRules.cs`:

```csharp
namespace AncientWarfare3.core.court
{
    public static class CourtRules
    {
        public const int CentralOfficeCount = 6;
        public const int DefaultRefreshIntervalYears = 5;
        public const int CandidateRefreshIntervalYears = 8;
        public const int StrongEventCooldownYears = 12;

        public static bool IsCourtUnlocked(bool hasPolicySystem, bool hasOfficialCourtTech)
        {
            return hasPolicySystem && hasOfficialCourtTech;
        }

        public static bool UsePrimitiveCourt(bool hasPolicySystem, bool hasOfficialCourtTech)
        {
            return hasPolicySystem && !hasOfficialCourtTech;
        }

        public static int CityOfficeSlots(int population, int zoneCount, bool isCapital)
        {
            int score = population + zoneCount * 4 + (isCapital ? 30 : 0);
            if (score >= 190) return 3;
            if (score >= 85) return 2;
            return 1;
        }

        public static bool ShouldRefreshCourt(int currentYear, int lastRefreshYear, int intervalYears)
        {
            int interval = intervalYears <= 0 ? DefaultRefreshIntervalYears : intervalYears;
            return lastRefreshYear < 0 || currentYear - lastRefreshYear >= interval;
        }

        public static bool ShouldRefreshCandidates(int currentYear, int lastRefreshYear)
        {
            return ShouldRefreshCourt(currentYear, lastRefreshYear, CandidateRefreshIntervalYears);
        }
    }
}
```

Create `Code/core/court/CourtInfluenceRules.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AncientWarfare3.core.court
{
    public static class CourtInfluenceRules
    {
        public static float InfluenceWeight(string layer, bool importantActor, int merit)
        {
            float baseWeight = layer switch
            {
                CourtOfficeLayer.Central => 6f,
                CourtOfficeLayer.Censor => 4.5f,
                CourtOfficeLayer.City => 2.5f,
                CourtOfficeLayer.Military => 3.5f,
                CourtOfficeLayer.Primitive => 1.5f,
                _ => 1f
            };
            if (importantActor) baseWeight += 1.5f;
            if (merit > 0) baseWeight += Math.Min(2f, merit / 25f);
            return baseWeight;
        }

        public static float Concentration(float dominantInfluence, float totalInfluence)
        {
            if (totalInfluence <= 0f || dominantInfluence <= 0f) return 0f;
            return (float)Math.Round(dominantInfluence / totalInfluence, 3);
        }

        public static bool ShouldTriggerStrongEvent(int yearsDominant, float dominantShare, bool crisis, bool weakKing)
        {
            if (dominantShare >= 0.60f && yearsDominant >= 6) return true;
            return crisis && weakKing && dominantShare >= 0.45f;
        }

        public static string DominantSchool(string encoded, string fallback)
        {
            string result = string.IsNullOrEmpty(fallback) ? CourtSchoolId.None : fallback;
            float best = -1f;
            foreach (KeyValuePair<string, float> pair in Decode(encoded))
            {
                if (pair.Value <= best) continue;
                best = pair.Value;
                result = pair.Key;
            }
            return result;
        }

        public static Dictionary<string, float> Decode(string encoded)
        {
            var result = new Dictionary<string, float>();
            if (string.IsNullOrEmpty(encoded)) return result;
            string[] parts = encoded.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                int idx = part.IndexOf('=');
                if (idx <= 0 || idx >= part.Length - 1) continue;
                string key = part.Substring(0, idx);
                string raw = part.Substring(idx + 1);
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) continue;
                result[key] = value;
            }
            return result;
        }
    }
}
```

Create `Code/core/court/CourtAIRules.cs`:

```csharp
namespace AncientWarfare3.core.court
{
    public static class CourtAIRules
    {
        public static int ScoreResearch(string dominantSchool, string nodeId, bool atWar, bool mandateExists)
        {
            int score = 0;
            switch (dominantSchool ?? "")
            {
                case CourtSchoolId.Ru:
                    if (nodeId == "aw_tech_rites_music" || nodeId == "aw_policy_ancestral_rites" || nodeId == "aw_policy_mandate_rites") score += 90;
                    break;
                case CourtSchoolId.Legalist:
                    if (nodeId == "aw_policy_early_law" || nodeId == "aw_policy_abolish_slavery" || nodeId == "aw_policy_xia_law_institutions") score += 95;
                    break;
                case CourtSchoolId.Dao:
                    if (nodeId == "aw_policy_abolish_slavery") score += 35;
                    if (atWar) score -= 20;
                    break;
                case CourtSchoolId.Mohist:
                    if (nodeId == "aw_tech_city_defense" || nodeId == "aw_policy_border_enfeoffment") score += 85;
                    break;
                case CourtSchoolId.Military:
                    if (nodeId == "aw_tech_chariot_training" || nodeId == "aw_policy_military_merit" || nodeId == "aw_policy_slave_army") score += atWar ? 120 : 70;
                    break;
                case CourtSchoolId.Diplomat:
                    if (nodeId == "aw_policy_imperial_court" || nodeId == "aw_policy_mandate_rites") score += mandateExists ? 80 : 45;
                    break;
                case CourtSchoolId.Agrarian:
                    if (nodeId == "aw_tech_iron_plow" || nodeId == "aw_tech_granary_accounting" || nodeId == "aw_policy_corvee_labor") score += 75;
                    break;
                case CourtSchoolId.YinYang:
                    if (nodeId == "aw_policy_mandate_rites" || nodeId == "aw_decision_year_name") score += 70;
                    break;
                case CourtSchoolId.Logician:
                    if (nodeId == "aw_decision_fabricate_weak_claim" || nodeId == "aw_decision_fabricate_strong_claim") score += 60;
                    break;
            }
            return score;
        }

        public static int ScoreDecision(string dominantSchool, string decisionId, int cities, bool atWar, bool unstable)
        {
            int score = 0;
            switch (dominantSchool ?? "")
            {
                case CourtSchoolId.Legalist:
                    if (decisionId == "aw_decision_control_slaves" || decisionId == "aw_decision_fabricate_core") score += 80;
                    break;
                case CourtSchoolId.Mohist:
                    if (decisionId == "aw_decision_change_capital" && unstable) score += 30;
                    break;
                case CourtSchoolId.Military:
                    if (decisionId == "aw_decision_declare_war" && !atWar && cities >= 2) score += 95;
                    break;
                case CourtSchoolId.Diplomat:
                    if (decisionId == "aw_decision_seek_suzerain" || decisionId == "aw_decision_absorb_vassal") score += 85;
                    if (decisionId == "aw_decision_declare_war") score += 30;
                    break;
                case CourtSchoolId.Ru:
                case CourtSchoolId.YinYang:
                    if (decisionId == "aw_decision_claim_mandate" || decisionId == "aw_decision_mandate_ritual") score += 85;
                    break;
                case CourtSchoolId.Dao:
                    if (decisionId == "aw_decision_declare_war") score -= 60;
                    break;
            }
            return score;
        }
    }
}
```

Create `Code/core/court/CourtTraitRules.cs`:

```csharp
namespace AncientWarfare3.core.court
{
    public static class CourtTraitRules
    {
        public static bool ShouldHoldSchoolTrait(bool isOfficer, bool alive, bool defected)
        {
            return isOfficer && alive && !defected;
        }

        public static string TraitForSchool(string schoolId)
        {
            return schoolId switch
            {
                CourtSchoolId.Ru => CourtTraitId.Ru,
                CourtSchoolId.Legalist => CourtTraitId.Legalist,
                CourtSchoolId.Dao => CourtTraitId.Dao,
                CourtSchoolId.Mohist => CourtTraitId.Mohist,
                CourtSchoolId.Military => CourtTraitId.Military,
                CourtSchoolId.Diplomat => CourtTraitId.Diplomat,
                CourtSchoolId.Agrarian => CourtTraitId.Agrarian,
                CourtSchoolId.YinYang => CourtTraitId.YinYang,
                CourtSchoolId.Logician => CourtTraitId.Logician,
                _ => ""
            };
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run:

```powershell
dotnet run --project Tests\CourtSystemRuleTests\CourtSystemRuleTests.csproj
```

Expected: `Court system rule tests passed.`

- [ ] **Step 6: Commit**

```powershell
git add Code\core\court Tests\CourtSystemRuleTests
git commit -m "Add court system rules"
```

---

### Task 2: Court Persistence Tables, Keys, And Cache Codec

**Files:**
- Create: `Code/core/db/KingdomCourtStateTableItem.cs`
- Create: `Code/core/db/CourtOfficerTableItem.cs`
- Create: `Code/core/db/CityBureauStateTableItem.cs`
- Create: `Code/core/court/CourtStateCodec.cs`
- Modify: `Code/core/db/LineageArchiveIndexRules.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Tests/CourtSystemRuleTests/Program.cs`

- [ ] **Step 1: Add failing codec tests**

Append these checks before `Console.WriteLine("Court system rule tests passed.");` in `Tests/CourtSystemRuleTests/Program.cs`:

```csharp
string encoded = CourtStateCodec.EncodeFactionCache(new[] { CourtSchoolId.Ru, CourtSchoolId.Legalist }, new[] { 4.5f, 8f });
ExpectEqual("ru=4.5;fa=8", encoded, "encoded faction cache");
var decoded = CourtStateCodec.DecodeFactionCache(encoded);
ExpectEqual(2, decoded.Count, "decoded faction count");
ExpectEqual(8f, decoded[CourtSchoolId.Legalist], "decoded legalist value");
ExpectEqual("", CourtStateCodec.EncodeFactionCache(new string[0], new float[0]), "empty faction cache");
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet run --project Tests\CourtSystemRuleTests\CourtSystemRuleTests.csproj
```

Expected: compile failure because `CourtStateCodec` does not exist.

- [ ] **Step 3: Add DB table items**

Create `Code/core/db/KingdomCourtStateTableItem.cs`:

```csharp
using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("KingdomCourtState")]
    public class KingdomCourtStateTableItem : AbstractTableItem<KingdomCourtStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long kingdom_id;
        public string kingdom_name;
        public string court_mode;
        public string dominant_school;
        public string secondary_school;
        public double court_efficiency;
        public double faction_concentration;
        public string faction_cache;
        public int last_refresh_year;
        public int last_candidate_refresh_year;
        public int last_strong_event_year;
        public double updated_time;
    }
}
```

Create `Code/core/db/CourtOfficerTableItem.cs`:

```csharp
using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("CourtOfficer")]
    public class CourtOfficerTableItem : AbstractTableItem<CourtOfficerTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long officer_id;
        public long kingdom_id;
        public long actor_id;
        public string actor_name;
        public long city_id;
        public string layer;
        public string office_id;
        public string school_id;
        public double influence;
        public int appointed_year;
        public int active;
        public string end_reason;
        public double updated_time;
    }
}
```

Create `Code/core/db/CityBureauStateTableItem.cs`:

```csharp
using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("CityBureauState")]
    public class CityBureauStateTableItem : AbstractTableItem<CityBureauStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long city_id;
        public long kingdom_id;
        public string city_name;
        public int office_slots;
        public string local_school;
        public double bureau_efficiency;
        public string officer_actor_ids;
        public int last_refresh_year;
        public double updated_time;
    }
}
```

- [ ] **Step 4: Add court keys**

Add these constants near the other policy keys in `Code/core/lineage/LineageKeys.cs`:

```csharp
public const string COURT_MODE = "aw_court_mode";
public const string COURT_DOMINANT_SCHOOL = "aw_court_dominant_school";
public const string COURT_SECONDARY_SCHOOL = "aw_court_secondary_school";
public const string COURT_FACTION_CACHE = "aw_court_faction_cache";
public const string COURT_EFFICIENCY = "aw_court_efficiency";
public const string COURT_CONCENTRATION = "aw_court_concentration";
public const string COURT_LAST_REFRESH_YEAR = "aw_court_last_refresh_year";
public const string COURT_LAST_CANDIDATE_YEAR = "aw_court_last_candidate_year";
public const string COURT_LAST_STRONG_EVENT_YEAR = "aw_court_last_strong_event_year";
public const string COURT_OFFICE_ID = "aw_court_office_id";
public const string COURT_LAYER = "aw_court_layer";
public const string COURT_SCHOOL = "aw_court_school";
public const string COURT_KINGDOM_ID = "aw_court_kingdom_id";
public const string COURT_CITY_ID = "aw_court_city_id";
```

- [ ] **Step 5: Add indexes**

Add these entries before the vassal indexes in `LineageArchiveIndexRules.GetRequiredIndexes()`:

```csharp
Index("idx_KingdomCourtState_kingdom", KingdomCourtStateTableItem.GetTableName(),
    "KINGDOM_ID"),
Index("idx_CourtOfficer_kingdom_active", CourtOfficerTableItem.GetTableName(),
    "KINGDOM_ID, ACTIVE, LAYER, OFFICE_ID"),
Index("idx_CourtOfficer_actor_active", CourtOfficerTableItem.GetTableName(),
    "ACTOR_ID, ACTIVE, KINGDOM_ID"),
Index("idx_CityBureauState_kingdom_city", CityBureauStateTableItem.GetTableName(),
    "KINGDOM_ID, CITY_ID"),
```

- [ ] **Step 6: Add cache codec**

Create `Code/core/court/CourtStateCodec.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;

namespace AncientWarfare3.core.court
{
    public static class CourtStateCodec
    {
        public static string EncodeFactionCache(string[] schools, float[] values)
        {
            if (schools == null || values == null || schools.Length == 0 || values.Length == 0) return "";
            var parts = new List<string>();
            int count = schools.Length < values.Length ? schools.Length : values.Length;
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(schools[i])) continue;
                if (values[i] <= 0f) continue;
                parts.Add(schools[i] + "=" + values[i].ToString("0.###", CultureInfo.InvariantCulture));
            }
            return string.Join(";", parts.ToArray());
        }

        public static Dictionary<string, float> DecodeFactionCache(string raw)
        {
            return CourtInfluenceRules.Decode(raw);
        }
    }
}
```

- [ ] **Step 7: Run tests and build**

Run:

```powershell
dotnet run --project Tests\CourtSystemRuleTests\CourtSystemRuleTests.csproj
dotnet build AncientWarfare3.csproj
```

Expected: rule tests pass and build succeeds.

- [ ] **Step 8: Commit**

```powershell
git add Code\core\db Code\core\lineage\LineageKeys.cs Code\core\court\CourtStateCodec.cs Tests\CourtSystemRuleTests
git commit -m "Add court persistence schema"
```

---

### Task 3: Official Court Tech And School Traits

**Files:**
- Modify: `Code/content/policies/KingdomPolicyDefs.cs`
- Modify: `Code/content/XiaTraits.cs`
- Create: `Locales/aw3_court.csv`
- Modify: `Tests/CourtSystemRuleTests/Program.cs`

- [ ] **Step 1: Add failing policy/trait key tests**

Append these checks before the final success line in `Tests/CourtSystemRuleTests/Program.cs`:

```csharp
ExpectEqual(CourtTraitId.Ru, CourtTraitRules.TraitForSchool(CourtSchoolId.Ru), "ru trait id");
ExpectEqual(CourtTraitId.Legalist, CourtTraitRules.TraitForSchool(CourtSchoolId.Legalist), "legalist trait id");
ExpectEqual("", CourtTraitRules.TraitForSchool("unknown"), "unknown trait id");
```

Run:

```powershell
dotnet run --project Tests\CourtSystemRuleTests\CourtSystemRuleTests.csproj
```

Expected: tests pass because trait mapping exists; this anchors trait IDs before registration is added.

- [ ] **Step 2: Add `aw_tech_official_court` to policy definitions**

Insert this tech after `aw_tech_rites_music` in `KingdomPolicyDefs._all`:

```csharp
new KingdomPolicyDef
{
    Id = "aw_tech_official_court",
    Kind = PolicyNodeKind.Tech,
    NameKey = "aw_tech_official_court",
    DescKey = "aw_tech_official_court_desc",
    FallbackName = "\u5B98\u573A\u5236\u5EA6",
    FallbackDesc = "\u5C06\u539F\u59CB\u671D\u4F1A\u5347\u7EA7\u4E3A\u767E\u5BB6\u5B98\u573A\uFF0C\u5141\u8BB8\u8D24\u4EBA\u5165\u4ED5\u5E76\u5F71\u54CD\u56FD\u5BB6\u8DEF\u7EBF\u3002",
    IconPath = "ui/icons/iconDiplomacy",
    Cost = 90f,
    RequiredTechs = new[] { "aw_tech_writing", "aw_tech_rites_music" },
    Column = 6,
    Row = 2
},
```

Add `"aw_tech_official_court"` to `KingdomPolicyAI.TechOrder` after `"aw_tech_rites_music"`:

```csharp
"aw_tech_rites_music",
"aw_tech_official_court"
```

- [ ] **Step 3: Register office-only school traits**

Add `using AncientWarfare3.core.court;` to `Code/content/XiaTraits.cs`.

Add this block at the end of `XiaTraits.Init()` after social identity traits:

```csharp
RegisterCourtSchoolTrait(CourtTraitId.Ru, "ui/Icons/traits/iconRujia", stewardship: 2f, diplomacy: 1f, warfare: 0f, intelligence: 1f);
RegisterCourtSchoolTrait(CourtTraitId.Legalist, "ui/Icons/traits/iconfajia", stewardship: 2f, diplomacy: 0f, warfare: 1f, intelligence: 1f);
RegisterCourtSchoolTrait(CourtTraitId.Dao, "ui/Icons/traits/icontao", stewardship: 1f, diplomacy: 1f, warfare: -1f, intelligence: 2f);
RegisterCourtSchoolTrait(CourtTraitId.Mohist, "ui/Icons/traits/iconmo", stewardship: 1f, diplomacy: 0f, warfare: 1f, intelligence: 2f);
RegisterCourtSchoolTrait(CourtTraitId.Military, "ui/Icons/traits/iconbinfa", stewardship: 0f, diplomacy: 0f, warfare: 3f, intelligence: 1f);
RegisterCourtSchoolTrait(CourtTraitId.Diplomat, "ui/Icons/traits/iconzonheng", stewardship: 0f, diplomacy: 3f, warfare: 0f, intelligence: 1f);
RegisterCourtSchoolTrait(CourtTraitId.Agrarian, "ui/icons/iconFood", stewardship: 2f, diplomacy: 0f, warfare: 0f, intelligence: 1f);
RegisterCourtSchoolTrait(CourtTraitId.YinYang, "ui/icons/iconCulture", stewardship: 1f, diplomacy: 1f, warfare: 0f, intelligence: 2f);
RegisterCourtSchoolTrait(CourtTraitId.Logician, "ui/icons/iconKnowledge", stewardship: 0f, diplomacy: 2f, warfare: 0f, intelligence: 2f);
```

Add this helper method inside `XiaTraits`:

```csharp
private static ActorTrait RegisterCourtSchoolTrait(string pId, string pIcon, float stewardship, float diplomacy,
    float warfare, float intelligence)
{
    var trait = NewTrait(pId, pIcon, XiaTraitGroups.AW2);
    trait.needs_to_be_explored = false;
    trait.unlocked_with_achievement = false;
    trait.base_stats["stewardship"] = stewardship;
    trait.base_stats["diplomacy"] = diplomacy;
    trait.base_stats["warfare"] = warfare;
    trait.base_stats["intelligence"] = intelligence;
    return trait;
}
```

- [ ] **Step 4: Add court localization file**

Create `Locales/aw3_court.csv`:

```csv
key,cz,en,ch
aw_tech_official_court,官场制度,Official Court,官場制度
aw_tech_official_court_desc,将原始朝会升级为百家官场，允许贤人入仕并影响国家路线,Upgrade primitive council into a court of schools that steers state policy,將原始朝會升級為百家官場，允許賢人入仕並影響國家路線
aw_court_button_primitive,原始朝会,Primitive Council,原始朝會
aw_court_button_official,百家官场,Hundred Schools Court,百家官場
aw_court_button_locked,官场未启用,Court Locked,官場未啟用
aw_court_title,百家官场,Court of the Hundred Schools,百家官場
aw_court_primitive_title,原始朝会,Primitive Council,原始朝會
aw_court_efficiency,官场效率,Court Efficiency,官場效率
aw_court_dominant_school,主流学派,Dominant School,主流學派
aw_court_no_officer,空缺,Vacant,空缺
aw_court_school_ru,儒家,Ru School,儒家
aw_court_school_fa,法家,Legalist School,法家
aw_court_school_dao,道家,Daoist School,道家
aw_court_school_mo,墨家,Mohist School,墨家
aw_court_school_bing,兵家,Military School,兵家
aw_court_school_zongheng,纵横家,Diplomat School,縱橫家
aw_court_school_nong,农家,Agrarian School,農家
aw_court_school_yinyang,阴阳家,Yin-Yang School,陰陽家
aw_court_school_ming,名家,Logician School,名家
aw_court_office_chancellor,丞相,Chancellor,丞相
aw_court_office_censor,御史,Censor,御史
aw_court_office_marshal,司马,Marshal,司馬
aw_court_office_justice,司寇,Justice Minister,司寇
aw_court_office_steward,司徒,Steward,司徒
aw_court_office_erudite,博士祭酒,Erudite Director,博士祭酒
aw_court_layer_central,中央官场,Central Court,中央官場
aw_court_layer_city,地方官署,City Bureau,地方官署
aw_court_layer_military,军府,Military Bureau,軍府
aw_court_layer_censor,监察,Inspection,監察
```

- [ ] **Step 5: Run tests and build**

Run:

```powershell
dotnet run --project Tests\CourtSystemRuleTests\CourtSystemRuleTests.csproj
dotnet build AncientWarfare3.csproj
```

Expected: tests pass and build succeeds.

- [ ] **Step 6: Commit**

```powershell
git add Code\content\policies\KingdomPolicyDefs.cs Code\content\XiaTraits.cs Code\core\policy\KingdomPolicyAI.cs Locales\aw3_court.csv Tests\CourtSystemRuleTests
git commit -m "Add official court tech and school traits"
```

---

### Task 4: Court Service, Appointment Validation, And Cached Influence

**Files:**
- Create: `Code/core/court/CourtService.cs`
- Modify: `Tests/CourtSystemRuleTests/Program.cs`

- [ ] **Step 1: Add rule tests for office validation**

Append:

```csharp
Expect(CourtRules.CanHoldOffice(alive: true, sameKingdom: true, slave: false, madness: false), "valid office holder");
Expect(!CourtRules.CanHoldOffice(alive: true, sameKingdom: false, slave: false, madness: false), "foreign holder rejected");
Expect(!CourtRules.CanHoldOffice(alive: true, sameKingdom: true, slave: true, madness: false), "slave holder rejected");
Expect(!CourtRules.CanHoldOffice(alive: true, sameKingdom: true, slave: false, madness: true), "madness holder rejected");
```

Add this method to `CourtRules`:

```csharp
public static bool CanHoldOffice(bool alive, bool sameKingdom, bool slave, bool madness)
{
    return alive && sameKingdom && !slave && !madness;
}
```

Run:

```powershell
dotnet run --project Tests\CourtSystemRuleTests\CourtSystemRuleTests.csproj
```

Expected: tests pass.

- [ ] **Step 2: Create service shell with cache-only public API**

Create `Code/core/court/CourtService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.court
{
    internal sealed class CourtSnapshot
    {
        public string mode = "";
        public string dominant_school = "";
        public string secondary_school = "";
        public string faction_cache = "";
        public float efficiency;
        public float concentration;
    }

    internal static class CourtService
    {
        private const int CandidateLimit = 24;

        public static bool HasOfficialCourt(Kingdom pKingdom)
        {
            return KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom) &&
                   KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Tech, "aw_tech_official_court");
        }

        public static bool HasPrimitiveCourt(Kingdom pKingdom)
        {
            return KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom) && !HasOfficialCourt(pKingdom);
        }

        public static CourtSnapshot GetSnapshot(Kingdom pKingdom)
        {
            var snapshot = new CourtSnapshot();
            if (pKingdom?.data == null) return snapshot;
            pKingdom.data.get(LineageKeys.COURT_MODE, out snapshot.mode, HasOfficialCourt(pKingdom) ? "official" : "primitive");
            pKingdom.data.get(LineageKeys.COURT_DOMINANT_SCHOOL, out snapshot.dominant_school, "");
            pKingdom.data.get(LineageKeys.COURT_SECONDARY_SCHOOL, out snapshot.secondary_school, "");
            pKingdom.data.get(LineageKeys.COURT_FACTION_CACHE, out snapshot.faction_cache, "");
            pKingdom.data.get(LineageKeys.COURT_EFFICIENCY, out snapshot.efficiency, 0f);
            pKingdom.data.get(LineageKeys.COURT_CONCENTRATION, out snapshot.concentration, 0f);
            return snapshot;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            if (!KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom)) return;
            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.COURT_LAST_REFRESH_YEAR, out int lastYear, -1);
            if (!CourtRules.ShouldRefreshCourt(year, lastYear, CourtRules.DefaultRefreshIntervalYears)) return;
            pKingdom.data.set(LineageKeys.COURT_LAST_REFRESH_YEAR, year);

            ValidateOfficers(pKingdom);
            EnsureMinimumCourt(pKingdom);
            RecalculateFactionCache(pKingdom);
            UpsertCourtSnapshot(pKingdom);
        }

        private static void ValidateOfficers(Kingdom pKingdom)
        {
            foreach (Actor actor in SafeUnits(pKingdom))
            {
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                if (courtKingdomId != pKingdom.id) continue;
                bool valid = CourtRules.CanHoldOffice(
                    alive: actor.isAlive() && !actor.isRekt(),
                    sameKingdom: actor.kingdom == pKingdom,
                    slave: actor.hasTrait(LineageKeys.TRAIT_SLAVE),
                    madness: actor.hasTrait("madness"));
                if (valid) SyncSchoolTrait(actor, active: true);
                else ClearOfficer(actor, "invalid");
            }
        }

        private static void EnsureMinimumCourt(Kingdom pKingdom)
        {
            if (!HasOfficialCourt(pKingdom) && !HasPrimitiveCourt(pKingdom)) return;
            AssignKingIfEmpty(pKingdom);
            if (!HasOfficialCourt(pKingdom)) return;
            FillCentralOffice(pKingdom, CourtOfficeId.Chancellor, CourtSchoolId.Ru);
            FillCentralOffice(pKingdom, CourtOfficeId.Censor, CourtSchoolId.Legalist);
            FillCentralOffice(pKingdom, CourtOfficeId.Marshal, CourtSchoolId.Military);
            FillCentralOffice(pKingdom, CourtOfficeId.Justice, CourtSchoolId.Legalist);
            FillCentralOffice(pKingdom, CourtOfficeId.Steward, CourtSchoolId.Agrarian);
            FillCentralOffice(pKingdom, CourtOfficeId.Erudite, CourtSchoolId.Ru);
        }

        private static void AssignKingIfEmpty(Kingdom pKingdom)
        {
            Actor king = pKingdom.king;
            if (king?.data == null || king.isRekt()) return;
            king.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            if (!string.IsNullOrEmpty(office)) return;
            SetOfficer(king, pKingdom, CourtOfficeLayer.Primitive, "king_council", CourtSchoolId.PrimitiveMinister, null);
        }

        private static void FillCentralOffice(Kingdom pKingdom, string pOfficeId, string pPreferredSchool)
        {
            if (HasActiveOffice(pKingdom, pOfficeId)) return;
            Actor candidate = FindBestCandidate(pKingdom, pOfficeId, pPreferredSchool);
            if (candidate == null) return;
            SetOfficer(candidate, pKingdom, CourtOfficeLayer.Central, pOfficeId, pPreferredSchool, null);
        }

        private static Actor FindBestCandidate(Kingdom pKingdom, string pOfficeId, string pPreferredSchool)
        {
            Actor best = null;
            float bestScore = -1f;
            int seen = 0;
            foreach (Actor actor in SafeUnits(pKingdom))
            {
                if (++seen > CandidateLimit * 8) break;
                if (actor?.data == null || actor.isRekt()) continue;
                if (!CourtRules.CanHoldOffice(actor.isAlive(), actor.kingdom == pKingdom,
                        actor.hasTrait(LineageKeys.TRAIT_SLAVE), actor.hasTrait("madness"))) continue;
                actor.data.get(LineageKeys.COURT_OFFICE_ID, out string currentOffice, "");
                if (!string.IsNullOrEmpty(currentOffice)) continue;
                float score = ScoreCandidate(actor, pOfficeId, pPreferredSchool);
                if (score <= bestScore) continue;
                best = actor;
                bestScore = score;
            }
            return best;
        }

        private static float ScoreCandidate(Actor pActor, string pOfficeId, string pPreferredSchool)
        {
            float stewardship = SafeStat(pActor, "stewardship");
            float diplomacy = SafeStat(pActor, "diplomacy");
            float warfare = SafeStat(pActor, "warfare");
            float intelligence = SafeStat(pActor, "intelligence");
            float score = intelligence + stewardship;
            if (pOfficeId == CourtOfficeId.Marshal) score += warfare * 2f;
            if (pOfficeId == CourtOfficeId.Chancellor || pOfficeId == CourtOfficeId.Censor) score += diplomacy;
            if (ChronicleGate.IsNobleActor(pActor)) score += 4f;
            pActor.data.get(LineageKeys.COURT_SCHOOL, out string naturalSchool, "");
            if (naturalSchool == pPreferredSchool) score += 6f;
            return score;
        }

        private static void SetOfficer(Actor pActor, Kingdom pKingdom, string pLayer, string pOfficeId, string pSchoolId, City pCity)
        {
            if (pActor?.data == null || pKingdom?.data == null) return;
            pActor.data.set(LineageKeys.COURT_KINGDOM_ID, pKingdom.id);
            pActor.data.set(LineageKeys.COURT_LAYER, pLayer ?? "");
            pActor.data.set(LineageKeys.COURT_OFFICE_ID, pOfficeId ?? "");
            pActor.data.set(LineageKeys.COURT_SCHOOL, pSchoolId ?? "");
            pActor.data.set(LineageKeys.COURT_CITY_ID, pCity?.data?.id ?? -1L);
            SyncSchoolTrait(pActor, active: true);
        }

        private static void ClearOfficer(Actor pActor, string pReason)
        {
            if (pActor?.data == null) return;
            SyncSchoolTrait(pActor, active: false);
            pActor.data.set(LineageKeys.COURT_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.COURT_LAYER, "");
            pActor.data.set(LineageKeys.COURT_OFFICE_ID, "");
            pActor.data.set(LineageKeys.COURT_CITY_ID, -1L);
        }

        private static void SyncSchoolTrait(Actor pActor, bool active)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.COURT_SCHOOL, out string school, "");
            foreach (string traitId in AllSchoolTraits())
            {
                if (string.IsNullOrEmpty(traitId)) continue;
                if (active && traitId == CourtTraitRules.TraitForSchool(school))
                {
                    if (!pActor.hasTrait(traitId)) pActor.addTrait(traitId);
                }
                else if (pActor.hasTrait(traitId)) pActor.removeTrait(traitId);
            }
        }

        private static void RecalculateFactionCache(Kingdom pKingdom)
        {
            var values = new Dictionary<string, float>();
            foreach (Actor actor in SafeUnits(pKingdom))
            {
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                if (courtKingdomId != pKingdom.id) continue;
                actor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
                actor.data.get(LineageKeys.COURT_SCHOOL, out string school, "");
                if (string.IsNullOrEmpty(school)) continue;
                float influence = CourtInfluenceRules.InfluenceWeight(layer, ChronicleGate.IsImportant(actor), GeneralService.GetMerit(actor));
                values.TryGetValue(school, out float old);
                values[school] = old + influence;
            }
            string[] schools = values.Keys.ToArray();
            float[] influenceValues = schools.Select(s => values[s]).ToArray();
            string encoded = CourtStateCodec.EncodeFactionCache(schools, influenceValues);
            string dominant = CourtInfluenceRules.DominantSchool(encoded, "");
            float total = influenceValues.Sum();
            float dominantValue = string.IsNullOrEmpty(dominant) || !values.ContainsKey(dominant) ? 0f : values[dominant];
            pKingdom.data.set(LineageKeys.COURT_FACTION_CACHE, encoded);
            pKingdom.data.set(LineageKeys.COURT_DOMINANT_SCHOOL, dominant);
            pKingdom.data.set(LineageKeys.COURT_CONCENTRATION, CourtInfluenceRules.Concentration(dominantValue, total));
            pKingdom.data.set(LineageKeys.COURT_EFFICIENCY, total <= 0f ? 0f : Math.Min(100f, 35f + total * 3f));
            pKingdom.data.set(LineageKeys.COURT_MODE, HasOfficialCourt(pKingdom) ? "official" : "primitive");
        }

        private static void UpsertCourtSnapshot(Kingdom pKingdom)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null || pKingdom?.data == null) return;
            CourtSnapshot s = GetSnapshot(pKingdom);
            pKingdom.data.get(LineageKeys.COURT_LAST_REFRESH_YEAR, out int lastRefresh, -1);
            pKingdom.data.get(LineageKeys.COURT_LAST_CANDIDATE_YEAR, out int lastCandidate, -1);
            pKingdom.data.get(LineageKeys.COURT_LAST_STRONG_EVENT_YEAR, out int lastStrong, -1);
            var values = new[]
            {
                ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                ColumnVal.Create("COURT_MODE", s.mode ?? ""),
                ColumnVal.Create("DOMINANT_SCHOOL", s.dominant_school ?? ""),
                ColumnVal.Create("SECONDARY_SCHOOL", s.secondary_school ?? ""),
                ColumnVal.Create("COURT_EFFICIENCY", (double)s.efficiency),
                ColumnVal.Create("FACTION_CONCENTRATION", (double)s.concentration),
                ColumnVal.Create("FACTION_CACHE", s.faction_cache ?? ""),
                ColumnVal.Create("LAST_REFRESH_YEAR", lastRefresh),
                ColumnVal.Create("LAST_CANDIDATE_REFRESH_YEAR", lastCandidate),
                ColumnVal.Create("LAST_STRONG_EVENT_YEAR", lastStrong),
                ColumnVal.Create("UPDATED_TIME", LineageService.CurTime())
            };
            try
            {
                string table = KingdomCourtStateTableItem.GetTableName();
                if (db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdom.id)))
                    db.UpdateValue(table, new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdom.id) }, values);
                else
                {
                    var insert = new List<ColumnVal> { ColumnVal.Create("KINGDOM_ID", pKingdom.id) };
                    insert.AddRange(values);
                    db.Insert(table, insert.ToArray());
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("KingdomCourtState upsert failed: " + e.Message);
            }
        }

        private static bool HasActiveOffice(Kingdom pKingdom, string pOfficeId)
        {
            foreach (Actor actor in SafeUnits(pKingdom))
            {
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                actor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
                if (courtKingdomId == pKingdom.id && office == pOfficeId) return true;
            }
            return false;
        }

        private static IEnumerable<Actor> SafeUnits(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) yield break;
            IEnumerable<Actor> units;
            try { units = pKingdom.getUnits(); }
            catch { yield break; }
            foreach (Actor unit in units)
                if (unit?.data != null) yield return unit;
        }

        private static IEnumerable<string> AllSchoolTraits()
        {
            yield return CourtTraitId.Ru;
            yield return CourtTraitId.Legalist;
            yield return CourtTraitId.Dao;
            yield return CourtTraitId.Mohist;
            yield return CourtTraitId.Military;
            yield return CourtTraitId.Diplomat;
            yield return CourtTraitId.Agrarian;
            yield return CourtTraitId.YinYang;
            yield return CourtTraitId.Logician;
        }

        private static float SafeStat(Actor pActor, string pStat)
        {
            try { return pActor?.stats?[pStat] ?? 0f; }
            catch { return 0f; }
        }
    }
}
```

- [ ] **Step 3: Run tests and build**

Run:

```powershell
dotnet run --project Tests\CourtSystemRuleTests\CourtSystemRuleTests.csproj
dotnet build AncientWarfare3.csproj
```

Expected: tests pass and build succeeds.

- [ ] **Step 4: Commit**

```powershell
git add Code\core\court Tests\CourtSystemRuleTests
git commit -m "Add court service cache"
```

---

### Task 5: Yearly Integration, AI Bias, And Benchmark

**Files:**
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Modify: `Code/core/policy/KingdomPolicyAI.cs`
- Modify: `Code/core/policy/UpdateAgeBenchmarkRules.cs`
- Modify: `Tests/CourtSystemRuleTests/Program.cs`

- [ ] **Step 1: Add AI bias assertions**

Append:

```csharp
int baseWar = CourtAIRules.ScoreDecision(CourtSchoolId.None, "aw_decision_declare_war", cities: 5, atWar: false, unstable: false);
int militaryWar = CourtAIRules.ScoreDecision(CourtSchoolId.Military, "aw_decision_declare_war", cities: 5, atWar: false, unstable: false);
int daoWar = CourtAIRules.ScoreDecision(CourtSchoolId.Dao, "aw_decision_declare_war", cities: 5, atWar: false, unstable: false);
Expect(militaryWar > baseWar, "military court raises war decision");
Expect(daoWar < baseWar, "dao court lowers war decision");
```

Run:

```powershell
dotnet run --project Tests\CourtSystemRuleTests\CourtSystemRuleTests.csproj
```

Expected: tests pass.

- [ ] **Step 2: Add benchmark entries**

In `UpdateAgeBenchmarkRules`, add these index constants after `KingdomPolicyMapDirtyIndex` and shift later indexes by 7:

```csharp
public const int KingdomCourtYearTickIndex = 25;
public const int KingdomCourtCandidateRefreshIndex = 26;
public const int KingdomCourtOfficerValidateIndex = 27;
public const int KingdomCourtFactionRecalcIndex = 28;
public const int KingdomCourtAiBiasIndex = 29;
public const int KingdomCourtUiBuildIndex = 30;
public const int KingdomCityBureauRefreshIndex = 31;
```

Add these string constants:

```csharp
public const string KingdomCourtYearTick = "aw3_court_year_tick";
public const string KingdomCourtCandidateRefresh = "aw3_court_candidate_refresh";
public const string KingdomCourtOfficerValidate = "aw3_court_officer_validate";
public const string KingdomCourtFactionRecalc = "aw3_court_faction_recalc";
public const string KingdomCourtAiBias = "aw3_court_ai_policy_bias";
public const string KingdomCourtUiBuild = "aw3_court_ui_build";
public const string KingdomCityBureauRefresh = "aw3_city_bureau_refresh";
```

Insert those IDs in `EntryIds` immediately after `KingdomPolicyMapDirty`.

Extend `ParentForIndex`:

```csharp
if (pIndex >= KingdomCourtYearTickIndex && pIndex <= KingdomCityBureauRefreshIndex)
    return KingdomPolicy;
```

- [ ] **Step 3: Call court service from yearly policy service**

In `KingdomPolicyService.OnKingdomYear`, after `EnsureInitialized(pKingdom);` and before reading `POLICY_LAST_YEAR`, add:

```csharp
long courtBenchmark = UpdateAgeBenchmark.Begin();
try { CourtService.OnKingdomYear(pKingdom); }
finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtYearTickIndex, courtBenchmark); }
```

Add `using AncientWarfare3.core.court;` to the file.

- [ ] **Step 4: Apply cached court AI bias**

Add `using AncientWarfare3.core.court;` to `KingdomPolicyAI.cs`.

In `ScoreDecision`, after the `KingdomDecisionPriorityRules.ScoreDecision(...)` call, store the result and add court bias:

```csharp
int baseScore = KingdomDecisionPriorityRules.ScoreDecision(
    pDef.Id,
    MandateService.CanStabilizeMandate(pKingdom),
    RoyalExpansionDecisionService.CanExecute(pKingdom),
    CountCities(pKingdom),
    SlaveService.IsSlaveryEnabled(pKingdom),
    XiaizationService.ScoreResearch(pKingdom, pDef),
    string.IsNullOrEmpty(yearName));
CourtSnapshot court = CourtService.GetSnapshot(pKingdom);
long benchmark = UpdateAgeBenchmark.Begin();
try
{
    return baseScore + CourtAIRules.ScoreDecision(court.dominant_school, pDef.Id,
        CountCities(pKingdom), IsAtWar(pKingdom), court.efficiency < 35f);
}
finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtAiBiasIndex, benchmark); }
```

In `ScoreResearch`, before `return orderScore + context;`, add:

```csharp
CourtSnapshot court = CourtService.GetSnapshot(pKingdom);
long benchmark = UpdateAgeBenchmark.Begin();
try { context += CourtAIRules.ScoreResearch(court.dominant_school, pDef.Id, atWar, MandateService.Exists); }
finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtAiBiasIndex, benchmark); }
```

- [ ] **Step 5: Run tests and build**

Run:

```powershell
dotnet run --project Tests\CourtSystemRuleTests\CourtSystemRuleTests.csproj
dotnet build AncientWarfare3.csproj
```

Expected: tests pass and build succeeds.

- [ ] **Step 6: Commit**

```powershell
git add Code\core\policy Code\core\court Tests\CourtSystemRuleTests
git commit -m "Integrate court yearly AI bias"
```

---

### Task 6: Court History Events

**Files:**
- Modify: `Code/core/lineage/ChronicleKeys.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Code/core/lineage/HistoryLocalizationRules.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Locales/aw3_court.csv`

- [ ] **Step 1: Add chronicle event constants**

In `ChronicleKeys.cs`, add these person events:

```csharp
public const string COURT_OFFICER_APPOINTED = "court_officer_appointed";
public const string COURT_OFFICER_DISMISSED = "court_officer_dismissed";
```

Add these kingdom events:

```csharp
public const string COURT_FOUNDED = "court_founded";
public const string COURT_OFFICER_APPOINTED = "court_officer_appointed";
public const string COURT_FACTION_DOMINANT = "court_faction_dominant";
public const string COURT_REFORM_EVENT = "court_reform_event";
```

Add this city event:

```csharp
public const string COURT_CITY_BUREAU = "court_city_bureau";
```

- [ ] **Step 2: Add history localization labels**

In `HistoryLocalizationRules`, add entries:

```csharp
new Entry("aw_hist_event_court_founded", "官场建立", "Court Founded", "官場建立"),
new Entry("aw_hist_event_court_officer_appointed", "任官", "Court Appointment", "任官"),
new Entry("aw_hist_event_court_officer_dismissed", "罢官", "Court Dismissal", "罷官"),
new Entry("aw_hist_event_court_faction_dominant", "学派主导", "Faction Dominance", "學派主導"),
new Entry("aw_hist_event_court_city_bureau", "地方官署", "City Bureau", "地方官署"),
```

- [ ] **Step 3: Add chronicle writer methods**

Add methods to `ChronicleEvents.cs`:

```csharp
public static void OnCourtFounded(Kingdom pKingdom, bool pOfficial)
{
    if (pKingdom?.data == null) return;
    string text = HistoryText.Kingdom(pKingdom) +
                  HistoryText.PlainText(pOfficial ? " 建立百家官场" : " 形成原始朝会");
    HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.COURT_FOUNDED, text, HistoryTarget.Kingdom(pKingdom));
}

public static void OnCourtOfficerAppointed(Actor pActor, Kingdom pKingdom, string pOfficeName, string pSchoolName)
{
    if (pActor?.data == null || pKingdom?.data == null) return;
    string name = pActor.getName();
    HistoryText text = HistoryText.Actor(pActor, name) + HistoryText.PlainText(" 入朝为" + pOfficeName + "，属" + pSchoolName);
    if (ChronicleGate.IsImportant(pActor) || ChronicleGate.IsNobleActor(pActor))
        HistoryWriter.RecordPerson(pActor, pKingdom, PersonEvent.COURT_OFFICER_APPOINTED, text, ChronicleCategory.HONOR, HistoryTarget.Kingdom(pKingdom));
    if (ChronicleGate.IsImportant(pActor))
        HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.COURT_OFFICER_APPOINTED, text, HistoryTarget.Actor(pActor));
}

public static void OnCourtFactionDominant(Kingdom pKingdom, string pSchoolName)
{
    if (pKingdom?.data == null) return;
    HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.COURT_FACTION_DOMINANT,
        HistoryText.Kingdom(pKingdom) + HistoryText.PlainText(" 官场由" + pSchoolName + "主导"),
        HistoryTarget.Kingdom(pKingdom));
}
```

If `PersonEvent.HONOR` or `ChronicleCategory.SOCIAL` names differ, use the existing honor/social constants in `ChronicleKeys.cs`.

- [ ] **Step 4: Call history from service**

In `CourtService.SetOfficer`, after `SyncSchoolTrait(pActor, active: true);`, add:

```csharp
ChronicleEvents.OnCourtOfficerAppointed(pActor, pKingdom, pOfficeId ?? "", pSchoolId ?? "");
```

In `CourtService.OnKingdomYear`, when court mode changes from empty to primitive or official, call:

```csharp
ChronicleEvents.OnCourtFounded(pKingdom, HasOfficialCourt(pKingdom));
```

Store a boolean data key with `LineageKeys.COURT_MODE` so the event fires once per mode.

- [ ] **Step 5: Run build**

Run:

```powershell
dotnet build AncientWarfare3.csproj
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

```powershell
git add Code\core\lineage Code\core\court Locales\aw3_court.csv
git commit -m "Record court history events"
```

---

### Task 7: Court Window And Kingdom UI Entry

**Files:**
- Create: `Code/ui/windows/CourtWindow.cs`
- Modify: `Code/ui/AW_LineageWindowIds.cs`
- Modify: `Code/ui/windows/KingdomWindowAddition.cs`
- Modify: `Locales/aw3_court.csv`

- [ ] **Step 1: Add court window id**

Add to `AW_LineageWindowIds.cs`:

```csharp
public const string COURT = "aw_court";
```

- [ ] **Step 2: Create simple scroll court window**

Create `Code/ui/windows/CourtWindow.cs`:

```csharp
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal class CourtWindow : AbstractWindow<CourtWindow>
    {
        private static long _kingdomId = -1;

        public static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.COURT);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.COURT,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        protected override void Init()
        {
            var sw = GetComponent<ScrollWindow>();
            if (sw?.titleText != null)
                sw.titleText.text = AW_L10n.Text("aw_court_title", "Court");
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Refresh()
        {
            foreach (Transform child in ContentTransform)
                Destroy(child.gameObject);

            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                AddText("Missing", AW_L10n.Text("aw_policy_no_kingdom", "Kingdom missing"), 0, 22, 12, Color.white);
                return;
            }

            CourtSnapshot snapshot = CourtService.GetSnapshot(kingdom);
            string title = CourtService.HasOfficialCourt(kingdom)
                ? AW_L10n.Text("aw_court_button_official", "Hundred Schools Court")
                : AW_L10n.Text("aw_court_button_primitive", "Primitive Council");

            AddText("Header", kingdom.name + " - " + title, 0, 24, 12, Color.white);
            AddText("Dominant", AW_L10n.Text("aw_court_dominant_school", "Dominant School") + ": " + SchoolName(snapshot.dominant_school), 26, 20, 10, Color.white);
            AddText("Efficiency", AW_L10n.Text("aw_court_efficiency", "Court Efficiency") + ": " + Mathf.FloorToInt(snapshot.efficiency), 48, 20, 10, Color.white);
            AddText("FactionCache", snapshot.faction_cache, 72, 20, 9, new Color(0.85f, 0.85f, 0.85f, 1f));
            AddText("Central", AW_L10n.Text("aw_court_layer_central", "Central Court"), 100, 20, 11, new Color(1f, 0.88f, 0.55f, 1f));
            AddText("CentralDesc", AW_L10n.Text("aw_court_no_officer", "Vacant"), 124, 60, 10, Color.white);
        }

        private void AddText(string pName, string pText, float pY, float pHeight, int pSize, Color pColor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(ContentTransform, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(8f, -pY - pHeight);
            rect.offsetMax = new Vector2(-8f, -pY);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = TextAnchor.UpperLeft;
            text.color = pColor;
            text.supportRichText = true;
            text.text = pText ?? "";
        }

        private static string SchoolName(string pSchool)
        {
            return pSchool switch
            {
                CourtSchoolId.Ru => AW_L10n.Text("aw_court_school_ru", "Ru"),
                CourtSchoolId.Legalist => AW_L10n.Text("aw_court_school_fa", "Legalist"),
                CourtSchoolId.Dao => AW_L10n.Text("aw_court_school_dao", "Dao"),
                CourtSchoolId.Mohist => AW_L10n.Text("aw_court_school_mo", "Mohist"),
                CourtSchoolId.Military => AW_L10n.Text("aw_court_school_bing", "Military"),
                CourtSchoolId.Diplomat => AW_L10n.Text("aw_court_school_zongheng", "Diplomat"),
                CourtSchoolId.Agrarian => AW_L10n.Text("aw_court_school_nong", "Agrarian"),
                CourtSchoolId.YinYang => AW_L10n.Text("aw_court_school_yinyang", "Yin-Yang"),
                CourtSchoolId.Logician => AW_L10n.Text("aw_court_school_ming", "Logician"),
                _ => AW_L10n.Text("aw_policy_idle", "Idle")
            };
        }
    }
}
```

- [ ] **Step 3: Add one wide kingdom UI button under the four policy buttons**

In `KingdomWindowAddition`, add fields:

```csharp
private Text _courtText;
private Image _courtIcon;
private TipButton _courtTip;
```

After the existing `policyRow` creation, add:

```csharp
GameObject courtButton = BuildPolicyIconButton("CourtStatus", new Vector2(114, 16),
    "ui/icons/iconDiplomacy", out _courtText, out _courtIcon, out _courtTip, OpenCourtWindow);
middleBar.AddChild(courtButton);
```

Increase `middleBar` and middle custom height from `36` to `54`, and keep avatar columns at existing height so the new button lives under the four-button row.

Add cache logic in `CacheRefs`:

```csharp
CachePolicyBox(middle.transform.FindRecursive("CourtStatus"), out _courtText, out _courtIcon, out _courtTip, OpenCourtWindow);
```

Add open method:

```csharp
private void OpenCourtWindow()
{
    Kingdom kingdom = _window != null ? _window.meta_object : null;
    if (kingdom == null || kingdom.isRekt()) return;
    CourtWindow.Open(kingdom.id);
}
```

Add refresh method:

```csharp
private void RefreshCourtButton(Kingdom pKingdom)
{
    if (_courtText == null || pKingdom?.data == null) return;
    bool officialCourt = CourtService.HasOfficialCourt(pKingdom);
    _courtText.text = officialCourt
        ? AW_L10n.Text("aw_court_button_official", "\u767E\u5BB6\u5B98\u573A")
        : AW_L10n.Text("aw_court_button_primitive", "\u539F\u59CB\u671D\u4F1A");
    SetPolicyIcon(_courtIcon, officialCourt ? "ui/icons/iconDiplomacy" : "ui/icons/iconKingdomList");
    CourtSnapshot snapshot = CourtService.GetSnapshot(pKingdom);
    SetPolicyTip(_courtTip, _courtText.text,
        AW_L10n.Text("aw_court_dominant_school", "\u4E3B\u6D41\u5B66\u6D3E") + ": " + snapshot.dominant_school + "\n" +
        AW_L10n.Text("aw_court_efficiency", "\u5B98\u573A\u6548\u7387") + ": " + Mathf.FloorToInt(snapshot.efficiency));
}
```

Call `RefreshCourtButton(kingdom);` after `RefreshPolicyBoxes(kingdom);`.

Add `using AncientWarfare3.core.court;` at the top.

- [ ] **Step 4: Run build**

Run:

```powershell
dotnet build AncientWarfare3.csproj
```

Expected: build succeeds. In game, the new button is one row below the four existing buttons and opens the court window.

- [ ] **Step 5: Commit**

```powershell
git add Code\ui Code\core\court Locales\aw3_court.csv
git commit -m "Add court window and kingdom entry"
```

---

### Task 8: Localization Sweep, Existing Test Regression, And README Update

**Files:**
- Modify: `Locales/aw3_court.csv`
- Modify: `README.md`
- Modify: `docs/AW3_Roadmap.md`

- [ ] **Step 1: Add remaining localization rows**

Append rows for appointment actions and event tooltips:

```csv
aw_court_action_appoint,任命,Appoint,任命
aw_court_action_dismiss,罢免,Dismiss,罷免
aw_court_action_transfer,调任,Transfer,調任
aw_court_action_support_school,扶持学派,Support School,扶持學派
aw_court_tooltip_cached,官场数据按低频缓存刷新，打开窗口不会实时扫描全国人物,Court data uses cached refresh and does not scan every actor when opened,官場資料按低頻快取刷新，打開窗口不會即時掃描全國人物
aw_court_event_founded,建立官场,Founded the court,建立官場
aw_court_event_primitive,形成原始朝会,Formed a primitive council,形成原始朝會
aw_court_event_dominant,学派主导官场,A school dominates the court,學派主導官場
```

- [ ] **Step 2: Update README**

Add a short section under the policy/tech feature list:

```markdown
- 官场与诸子百家：国家研究官场制度后，从原始朝会升级为百家官场。中央官、地方官署、军府和监察以缓存方式影响科技、国策、决策、城市治理和战争路线。
```

- [ ] **Step 3: Update roadmap**

Add an entry to `docs/AW3_Roadmap.md`:

```markdown
### 官场与诸子百家

- 已设计并分批实现低开销官场系统：原始朝会、官场制度科技、中央官职、地方官署、军府汇总、监察、百家学派缓存、AI 路线影响、历史记录和 UI 入口。
- 后续扩展保留：门生故吏、官僚家族化、大规模党争、察举/科举路线和更细财政官僚预算。
```

- [ ] **Step 4: Run regression tests**

Run:

```powershell
dotnet run --project Tests\CourtSystemRuleTests\CourtSystemRuleTests.csproj
dotnet run --project Tests\CityEconomyRuleTests\CityEconomyRuleTests.csproj
dotnet run --project Tests\WarFabricationRuleTests\WarFabricationRuleTests.csproj
dotnet build AncientWarfare3.csproj
```

Expected:

- `Court system rule tests passed.`
- `City economy rule tests passed.`
- Existing war fabrication tests pass.
- Build succeeds.

- [ ] **Step 5: Commit**

```powershell
git add Locales\aw3_court.csv README.md docs\AW3_Roadmap.md
git commit -m "Document court system"
```

---

## Manual In-Game Verification

- [ ] Start a Xia kingdom before official court tech is researched.
- [ ] Confirm the kingdom UI shows one wide `原始朝会` button below the four existing policy buttons.
- [ ] Open the court window and confirm it shows primitive council data without heavy scanning spikes.
- [ ] Research or force-complete `官场制度`.
- [ ] Confirm the button text changes to `百家官场`.
- [ ] Confirm court officers receive school traits while in office.
- [ ] Dismiss or invalidate an officer and confirm the school trait is removed.
- [ ] Let 50-100 years pass with multiple kingdoms and check benchmark labels:
  - `aw3_court_year_tick`
  - `aw3_court_candidate_refresh`
  - `aw3_court_officer_validate`
  - `aw3_court_faction_recalc`
  - `aw3_court_ai_policy_bias`
  - `aw3_court_ui_build`
  - `aw3_city_bureau_refresh`
- [ ] Confirm those entries do not become the main updateAge/updateYear cost.
- [ ] Check zh/en/traditional Chinese text in court button, court window, tech node, school names, and tooltips.

## Spec Coverage Review

- Primitive court and official court unlock are covered by Tasks 1, 3, 4, and 7.
- Central offices, local city offices, military bureau summary, and inspection rules are covered by Tasks 1 and 4.
- Hundred Schools school IDs, trait mapping, and office-only trait behavior are covered by Tasks 1, 3, and 4.
- Cached faction composition and AI policy influence are covered by Tasks 1, 2, 4, and 5.
- UI entry below the four policy buttons and court window are covered by Task 7.
- History records are covered by Task 6.
- Benchmark and performance constraints are covered by Tasks 1, 4, 5, and manual verification.
- Localization is covered by Tasks 3 and 8.

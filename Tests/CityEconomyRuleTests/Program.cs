using System;
using AncientWarfare3.core.policy;

namespace CityEconomyRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            try
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

            ExpectRole("fief", CityEconomyRole.FrontierMilitary,
                isCapital: false, population: 80, marketBuildings: 4, farmBuildings: 0,
                militaryBuildings: 0, workshopBuildings: 0, adoptedTechCount: 4,
                totalTechCount: 8, isBorder: false, occupiedUnrest: false,
                activeFief: true);

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

            CityEconomyContribution normalFrontier = CityEconomyRules.CalculateContribution(
                CityEconomyRole.FrontierMilitary, population: 100, adoptedTechCount: 4, totalTechCount: 8,
                distanceFromCapital: 30, slavePopulation: 0, nonCore: false, activeFief: false);
            CityEconomyContribution fiefFrontier = CityEconomyRules.CalculateContribution(
                CityEconomyRole.FrontierMilitary, population: 100, adoptedTechCount: 4, totalTechCount: 8,
                distanceFromCapital: 30, slavePopulation: 0, nonCore: false, activeFief: true);
            if (fiefFrontier.Manpower <= normalFrontier.Manpower)
                throw new Exception("Expected fief military city to produce more manpower than normal frontier.");
            if (fiefFrontier.TaxValue >= normalFrontier.TaxValue)
                throw new Exception("Expected fief military city to trade tax for stronger manpower.");

            ExpectRoleKey(CityEconomyRole.CapitalAdmin, "aw_city_economy_role_capital_admin");
            ExpectRoleKey(CityEconomyRole.AgrarianGranary, "aw_city_economy_role_agrarian_granary");
            ExpectRoleKey(CityEconomyRole.MarketTrade, "aw_city_economy_role_market_trade");
            ExpectRoleKey(CityEconomyRole.FrontierMilitary, "aw_city_economy_role_frontier_military");
            ExpectRoleKey(CityEconomyRole.WorkshopCraft, "aw_city_economy_role_workshop_craft");
            ExpectRoleKey(CityEconomyRole.OccupiedUnrest, "aw_city_economy_role_occupied_unrest");

            ExpectTechMap("none", "tech_0", adoptedScore: 0f, totalTechCount: 8);
            ExpectTechMap("low", "tech_1", adoptedScore: 1f, totalTechCount: 8);
            ExpectTechMap("middle", "tech_4", adoptedScore: 4f, totalTechCount: 8);
            ExpectTechMap("high", "tech_6", adoptedScore: 6f, totalTechCount: 8);
            ExpectTechMap("max", "tech_8", adoptedScore: 8f, totalTechCount: 8);
            ExpectTechDisplayRange("late-world low", "tech_0", rawScore: 0.75f, minScore: 0.75f, maxScore: 1f);
            ExpectTechDisplayRange("late-world middle", "tech_4", rawScore: 0.875f, minScore: 0.75f, maxScore: 1f);
            ExpectTechDisplayRange("late-world max", "tech_8", rawScore: 1f, minScore: 0.75f, maxScore: 1f);
            ExpectTechDisplayRange("early-world keeps absolute", "tech_4", rawScore: 0.50f, minScore: 0.10f,
                maxScore: 1f);
            ExpectHeatHex("tech max green", "#226B3A", CityTechMapRules.HexForColorKey("tech_8"));
            ExpectHeatHex("tech old cyan removed", "#4F8F45", CityTechMapRules.HexForColorKey("tech_7"));
            ExpectDevelopmentScoreOrder();
            ExpectDevelopmentPenalty();
            ExpectDevelopmentColor("low", "development_0", 0.05f);
            ExpectDevelopmentColor("middle", "development_4", 0.45f);
            ExpectDevelopmentColor("high", "development_7", 0.85f);
            ExpectHeatHex("development max green", "#226B3A", CityDevelopmentRules.HexForColorKey("development_8"));
            ExpectDevelopmentAverage();
            ExpectNewCityTechSyncRules();
            ExpectNeighborBonusRule();
            ExpectCityTechUpdateRules();
            ExpectCityEconomyUpdateRules();

            Console.WriteLine("City economy rule tests passed.");
            return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.GetType().FullName + ": " + e.Message);
                return 1;
            }
        }

        private static void ExpectRole(string label, CityEconomyRole expected,
            bool isCapital, int population, int marketBuildings, int farmBuildings,
            int militaryBuildings, int workshopBuildings, int adoptedTechCount,
            int totalTechCount, bool isBorder, bool occupiedUnrest, bool activeFief = false)
        {
            CityEconomyRole actual = CityEconomyRules.SelectRole(isCapital, population,
                marketBuildings, farmBuildings, militaryBuildings, workshopBuildings,
                adoptedTechCount, totalTechCount, isBorder, occupiedUnrest, activeFief);
            if (actual != expected)
                throw new Exception($"Expected {label} role {expected}, got {actual}.");
        }

        private static void ExpectRoleKey(CityEconomyRole role, string expected)
        {
            string actual = CityEconomyRules.RoleNameKey(role);
            if (actual != expected)
                throw new Exception($"Expected {role} localization key {expected}, got {actual}.");
        }

        private static void ExpectTechMap(string label, string expected, float adoptedScore, int totalTechCount)
        {
            float score = CityTechMapRules.CalculateDevelopmentScore(adoptedScore, totalTechCount);
            string actual = CityTechMapRules.ColorKeyForScore(score);
            if (actual != expected)
                throw new Exception($"Expected {label} tech map color {expected}, got {actual}.");
        }

        private static void ExpectTechDisplayRange(string label, string expected, float rawScore, float minScore,
            float maxScore)
        {
            float score = CityTechMapRules.CalculateVisibleScore(rawScore, minScore, maxScore);
            string actual = CityTechMapRules.ColorKeyForScore(score);
            if (actual != expected)
                throw new Exception($"Expected {label} visible tech map color {expected}, got {actual}.");
        }

        private static void ExpectDevelopmentScoreOrder()
        {
            float low = CityDevelopmentRules.CalculateScore(population: 5, zoneCount: 1, buildingCount: 0,
                techScore: 0.05f, economyScore: 0.02f, unrestRisk: 0f, nonCoreOrOccupied: false);
            float high = CityDevelopmentRules.CalculateScore(population: 180, zoneCount: 25, buildingCount: 30,
                techScore: 0.9f, economyScore: 0.85f, unrestRisk: 0f, nonCoreOrOccupied: false);
            if (high <= low)
                throw new Exception($"Expected developed city score {high} to exceed low score {low}.");
        }

        private static void ExpectDevelopmentPenalty()
        {
            float stable = CityDevelopmentRules.CalculateScore(population: 100, zoneCount: 18, buildingCount: 18,
                techScore: 0.6f, economyScore: 0.6f, unrestRisk: 0f, nonCoreOrOccupied: false);
            float unstable = CityDevelopmentRules.CalculateScore(population: 100, zoneCount: 18, buildingCount: 18,
                techScore: 0.6f, economyScore: 0.6f, unrestRisk: 0.8f, nonCoreOrOccupied: true);
            if (unstable >= stable)
                throw new Exception($"Expected unrest/non-core city score {unstable} to be lower than stable {stable}.");
        }

        private static void ExpectDevelopmentColor(string label, string expected, float score)
        {
            string actual = CityDevelopmentRules.ColorKeyForScore(score);
            if (actual != expected)
                throw new Exception($"Expected {label} development color {expected}, got {actual}.");
        }

        private static void ExpectHeatHex(string label, string expected, string actual)
        {
            if (actual != expected)
                throw new Exception($"Expected {label} hex {expected}, got {actual}.");
        }

        private static void ExpectDevelopmentAverage()
        {
            float actual = CityDevelopmentRules.AverageScore(new[] { 0.2f, 0.4f, 0.6f });
            if (Math.Abs(actual - 0.4f) > 0.001f)
                throw new Exception($"Expected development average 0.4, got {actual}.");
        }

        private static void ExpectNewCityTechSyncRules()
        {
            string[] missing = CityTechSyncRules.SelectMissingCompletedTechIds(
                new[] { "aw_tech_iron_plow", "aw_tech_bronze_casting", "aw_tech_roads" },
                pTechId => pTechId == "aw_tech_bronze_casting");
            if (missing.Length != 2 || missing[0] != "aw_tech_iron_plow" || missing[1] != "aw_tech_roads")
                throw new Exception("Expected new city sync to return only completed techs missing from the city.");

            string[] none = CityTechSyncRules.SelectMissingCompletedTechIds(
                new[] { "aw_tech_iron_plow" },
                _ => true);
            if (none.Length != 0)
                throw new Exception("Expected new city sync to skip techs already recorded for the city.");
        }

        private static void ExpectNeighborBonusRule()
        {
            if (!CityTechReportRules.ShouldLoadNeighborBonus(includeNeighborBonus: true, currentTechId: "aw_tech_iron_plow"))
                throw new Exception("Expected full city tech reports to load neighbor bonus.");
            if (CityTechReportRules.ShouldLoadNeighborBonus(includeNeighborBonus: false, currentTechId: "aw_tech_iron_plow"))
                throw new Exception("Expected economy city tech reports to skip neighbor bonus.");
            if (CityTechReportRules.ShouldLoadNeighborBonus(includeNeighborBonus: true, currentTechId: ""))
                throw new Exception("Expected reports without current tech to skip neighbor bonus scans.");
        }

        private static void ExpectCityTechUpdateRules()
        {
            if (!CityTechUpdateRules.ShouldSkipStableAdoptedUpdate(
                    existingAdopted: true,
                    nextAdopted: true,
                    existingAdoption: 100.0,
                    nextAdoption: 100.0,
                    existingExposure: 0.0,
                    nextExposure: 0.0,
                    sameOwner: true))
                throw new Exception("Expected stable adopted city tech rows to skip yearly DB updates.");

            if (CityTechUpdateRules.ShouldSkipStableAdoptedUpdate(
                    existingAdopted: false,
                    nextAdopted: true,
                    existingAdoption: 80.0,
                    nextAdoption: 100.0,
                    existingExposure: 0.0,
                    nextExposure: 0.0,
                    sameOwner: true))
                throw new Exception("Expected newly adopted city tech rows to write DB updates.");

            if (CityTechUpdateRules.ShouldSkipStableAdoptedUpdate(
                    existingAdopted: true,
                    nextAdopted: true,
                    existingAdoption: 100.0,
                    nextAdoption: 100.0,
                    existingExposure: 0.0,
                    nextExposure: 0.0,
                    sameOwner: false))
                throw new Exception("Expected ownership changes to keep DB rows in sync.");
        }

        private static void ExpectCityEconomyUpdateRules()
        {
            var previous = new CityEconomyStoredState
            {
                has_record = true,
                kingdom_id = 12,
                role = CityEconomyRole.MarketTrade.ToString(),
                policy_points = 1.2f,
                tech_points = 0.8f,
                tax_value = 10f,
                manpower = 3f,
                food_stability = 4f,
                unrest_risk = 2f
            };
            var same = new CityEconomyContribution(1.2001f, 0.8001f, 10.0001f, 3.0001f, 4.0001f, 2.0001f);
            if (!CityEconomyUpdateRules.ShouldSkipStableUpdate(previous, 12, CityEconomyRole.MarketTrade.ToString(), same))
                throw new Exception("Expected stable city economy rows to skip yearly DB updates.");

            var changed = new CityEconomyContribution(1.2f, 0.8f, 14f, 3f, 4f, 2f);
            if (CityEconomyUpdateRules.ShouldSkipStableUpdate(previous, 12, CityEconomyRole.MarketTrade.ToString(), changed))
                throw new Exception("Expected changed economy values to keep DB updates.");

            if (CityEconomyUpdateRules.ShouldSkipStableUpdate(previous, 99, CityEconomyRole.MarketTrade.ToString(), same))
                throw new Exception("Expected ownership changes to keep DB updates.");

            if (!CityEconomyUpdateRules.ShouldCountSlavesForEconomy(pSlaveryEnabled: true, pHasCity: true))
                throw new Exception("Enabled slavery cities should count slave population for economy.");
            if (CityEconomyUpdateRules.ShouldCountSlavesForEconomy(pSlaveryEnabled: false, pHasCity: true))
                throw new Exception("Disabled slavery kingdoms should skip city slave scans for economy.");
            if (CityEconomyUpdateRules.ShouldCountSlavesForEconomy(pSlaveryEnabled: true, pHasCity: false))
                throw new Exception("Invalid cities should not count slave population for economy.");

            if (!CityEconomyUpdateRules.ShouldUseContributionCache(pHasCache: true, pCachedYear: 20, pCurrentYear: 20))
                throw new Exception("City economy contribution sums should reuse same-year cache.");
            if (CityEconomyUpdateRules.ShouldUseContributionCache(pHasCache: true, pCachedYear: 19, pCurrentYear: 20))
                throw new Exception("City economy contribution sums must refresh across years.");
            if (CityEconomyUpdateRules.ShouldUseContributionCache(pHasCache: false, pCachedYear: 20, pCurrentYear: 20))
                throw new Exception("Missing contribution cache must read DB.");
        }
    }
}

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

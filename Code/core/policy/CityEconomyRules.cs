using UnityEngine;

namespace AncientWarfare3.core.policy
{
    public enum CityEconomyRole
    {
        CapitalAdmin,
        AgrarianGranary,
        MarketTrade,
        FrontierMilitary,
        WorkshopCraft,
        OccupiedUnrest
    }

    public readonly struct CityEconomyContribution
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

    public static class CityEconomyRules
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
                    policy *= 1.55f;
                    tax *= 1.15f;
                    unrest -= 1f;
                    break;
                case CityEconomyRole.AgrarianGranary:
                    food *= 1.85f;
                    tax *= 0.95f;
                    break;
                case CityEconomyRole.MarketTrade:
                    tax *= 1.55f;
                    policy *= 1.12f;
                    break;
                case CityEconomyRole.FrontierMilitary:
                    manpower *= 1.85f;
                    tax *= 0.82f;
                    unrest += 2f;
                    break;
                case CityEconomyRole.WorkshopCraft:
                    tech *= 1.55f;
                    tax *= 1.08f;
                    break;
                case CityEconomyRole.OccupiedUnrest:
                    policy *= 0.45f;
                    tech *= 0.55f;
                    tax *= 0.35f;
                    manpower *= 0.55f;
                    food *= 0.75f;
                    unrest += 18f;
                    break;
            }

            tax *= 1f + slaveFactor * 0.12f;
            return new CityEconomyContribution(policy, tech, tax, manpower, food, Mathf.Clamp(unrest, 0f, 100f));
        }
    }
}

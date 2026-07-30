using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class WartimeCityMobilizationFacts
    {
        public WartimeCityMobilizationFacts(int population,
            int currentMilitary, int warriorSlots)
        {
            Population = Math.Max(0, population);
            CurrentMilitary = Math.Max(0, currentMilitary);
            WarriorSlots = Math.Max(0, warriorSlots);
        }

        public int Population { get; }
        public int CurrentMilitary { get; }
        public int WarriorSlots { get; }
    }

    public static class WartimeRecruitmentPopulationRules
    {
        public const int MinimumCityPopulation = 20;

        public static int RecruitmentCapacity(int pPopulation,
            int pRequested)
        {
            int available = Math.Max(0, pPopulation -
                MinimumCityPopulation);
            return Math.Min(Math.Max(0, pRequested), available);
        }

        public static int AdditionalMobilizationCapacity(int population,
            int currentMilitary, int warriorSlots)
        {
            int populationMilitaryLimit = Math.Max(0, population -
                MinimumCityPopulation);
            int totalMilitaryLimit = Math.Min(
                Math.Max(0, warriorSlots), populationMilitaryLimit);
            return Math.Max(0, totalMilitaryLimit -
                               Math.Max(0, currentMilitary));
        }

        public static int TotalMilitaryPotential(int currentWarriors,
            IReadOnlyList<WartimeCityMobilizationFacts> cities)
        {
            long total = Math.Max(0, currentWarriors);
            if (cities != null)
                for (int i = 0; i < cities.Count; i++)
                {
                    WartimeCityMobilizationFacts city = cities[i];
                    if (city == null) continue;
                    total += AdditionalMobilizationCapacity(
                        city.Population, city.CurrentMilitary,
                        city.WarriorSlots);
                    if (total >= int.MaxValue) return int.MaxValue;
                }
            return (int)total;
        }

        public static int ScaleSampledMilitaryPotential(int pSampledPotential,
            int pSampledCities, int pTotalCities)
        {
            int sampledCities = Math.Max(0, pSampledCities);
            int totalCities = Math.Max(0, pTotalCities);
            if (sampledCities == 0 || totalCities == 0) return 0;
            long scaled = (long)Math.Max(0, pSampledPotential) * totalCities;
            scaled = (scaled + sampledCities - 1) / sampledCities;
            return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
        }

        public static int RotatingSampleIndex(int pStart, int pOffset,
            int pTotalItems)
        {
            int totalItems = Math.Max(0, pTotalItems);
            if (totalItems == 0) return 0;
            int start = pStart % totalItems;
            if (start < 0) start += totalItems;
            long offset = Math.Max(0, pOffset);
            return (int)((start + offset) % totalItems);
        }

        public static int NextRotatingSampleStart(int pStart,
            int pInspectedItems, int pTotalItems)
        {
            if (pTotalItems <= 0) return 0;
            return RotatingSampleIndex(pStart, pInspectedItems,
                pTotalItems);
        }
    }
}

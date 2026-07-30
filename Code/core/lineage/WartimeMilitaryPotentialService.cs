using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class WartimeMilitaryPotentialService
    {
        private static readonly Dictionary<long, int>
            NextBoundedSampleStartByKingdom = new Dictionary<long, int>();

        public static int CountPotentialWarriors(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return 0;
            var cities = new List<WartimeCityMobilizationFacts>(
                pKingdom.cities?.Count ?? 0);
            if (pKingdom.cities != null)
                for (int i = 0; i < pKingdom.cities.Count; i++)
                {
                    City city = pKingdom.cities[i];
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom) continue;
                    cities.Add(CityFacts(pKingdom, city));
                }
            return WartimeRecruitmentPopulationRules.
                TotalMilitaryPotential(SafeWarriorCount(pKingdom), cities);
        }

        public static int CountPotentialWarriorsBounded(Kingdom pKingdom,
            int pMaximumCityScans)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return 0;
            int maximumScans = Math.Max(0, pMaximumCityScans);
            int totalCityCount = pKingdom.cities?.Count ?? 0;
            var cities = new List<WartimeCityMobilizationFacts>(
                Math.Min(totalCityCount, maximumScans));
            int sampledCurrentMilitary = 0;
            NextBoundedSampleStartByKingdom.TryGetValue(pKingdom.id,
                out int startIndex);
            startIndex = WartimeRecruitmentPopulationRules.
                RotatingSampleIndex(startIndex, 0, totalCityCount);
            int inspected = Math.Min(totalCityCount, maximumScans);
            if (pKingdom.cities != null)
                for (int offset = 0; offset < inspected; offset++)
                {
                    int i = WartimeRecruitmentPopulationRules.
                        RotatingSampleIndex(startIndex, offset,
                            totalCityCount);
                    City city = pKingdom.cities[i];
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom) continue;
                    WartimeCityMobilizationFacts facts = CityFacts(pKingdom,
                        city);
                    sampledCurrentMilitary += facts.CurrentMilitary;
                    cities.Add(facts);
                }
            if (inspected > 0)
                NextBoundedSampleStartByKingdom[pKingdom.id] =
                    WartimeRecruitmentPopulationRules.
                        NextRotatingSampleStart(startIndex, inspected,
                            totalCityCount);
            int sampled = WartimeRecruitmentPopulationRules.
                TotalMilitaryPotential(sampledCurrentMilitary, cities);
            return WartimeRecruitmentPopulationRules.
                ScaleSampledMilitaryPotential(sampled, cities.Count,
                    totalCityCount);
        }

        public static void ClearRuntime()
        {
            NextBoundedSampleStartByKingdom.Clear();
        }

        public static void RemoveKingdom(long pKingdomId)
        {
            if (pKingdomId < 0L) return;
            NextBoundedSampleStartByKingdom.Remove(pKingdomId);
        }

        private static WartimeCityMobilizationFacts CityFacts(
            Kingdom pKingdom, City pCity)
        {
            int population = SafePopulation(pCity);
            int currentMilitary = StandingArmyService.
                CountOrdinaryMilitary(pCity);
            int slots = SafeEffectiveWarriorSlots(pKingdom, pCity);
            return new WartimeCityMobilizationFacts(population,
                currentMilitary, slots);
        }

        private static int SafePopulation(City pCity)
        {
            try { return Math.Max(0, pCity?.getPopulationPeople() ?? 0); }
            catch { return 0; }
        }

        private static int SafeEffectiveWarriorSlots(Kingdom pKingdom,
            City pCity)
        {
            try
            {
                return Math.Max(0, MandateMilitaryPhaseService.
                    EffectiveWarriorSlots(pKingdom,
                        pCity?.status?.warrior_slots ?? 0));
            }
            catch { return 0; }
        }

        private static int SafeWarriorCount(Kingdom pKingdom)
        {
            try { return Math.Max(0, pKingdom?.countTotalWarriors() ?? 0); }
            catch { return 0; }
        }

    }
}

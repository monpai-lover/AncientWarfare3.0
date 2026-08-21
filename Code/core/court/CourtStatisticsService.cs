using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    internal sealed class CourtStatisticsSnapshot
    {
        internal CourtStatisticsScope Scope { get; set; }
        internal string FallbackReason { get; set; } = string.Empty;
        internal int CityCount { get; set; }
        internal int Population { get; set; }
        internal float PolicyPoints { get; set; }
        internal float TechnologyPoints { get; set; }
        internal float TaxValue { get; set; }
        internal float Manpower { get; set; }
        internal float FoodStability { get; set; }
        internal float UnrestRisk { get; set; }
        internal int EconomyRecordCityCount { get; set; }

        internal bool HasEconomyRecord => EconomyRecordCityCount > 0;
    }

    internal static class CourtStatisticsService
    {
        internal static CourtStatisticsSnapshot BuildForCourt(
            Kingdom pKingdom, long pCityId)
        {
            if (pKingdom?.data == null || pKingdom.isRekt())
                return Empty(CourtStatisticsScope.City, "invalid_kingdom");

            if (pCityId < 0L)
                return Aggregate(CourtStatisticsScope.National,
                    LiveCities(pKingdom), string.Empty);

            City city = FindOwnedCity(pKingdom, pCityId);
            if (city == null)
                return Empty(CourtStatisticsScope.City, "invalid_city");

            if (DeJureRegionStore.TryGetBySeat(city.data.id,
                    out DeJureRegion region) && region?.Active == true)
            {
                List<City> members = ResolveOwnedCities(pKingdom,
                    region.MemberCityIds);
                if (members.Count > 0)
                    return Aggregate(CourtStatisticsScope.Region, members,
                        string.Empty);
            }

            return Aggregate(CourtStatisticsScope.City,
                new[] { city }, "region_unavailable");
        }

        internal static CourtStatisticsSnapshot Aggregate(
            CourtStatisticsScope pScope, IEnumerable<City> pCities,
            string pFallbackReason)
        {
            var result = new CourtStatisticsSnapshot
            {
                Scope = pScope,
                FallbackReason = pFallbackReason ?? string.Empty
            };
            var seen = new HashSet<long>();
            float food = 0f;
            float unrest = 0f;
            int economyCount = 0;
            foreach (City city in pCities ?? Array.Empty<City>())
            {
                if (!IsLive(city) || !seen.Add(city.data.id)) continue;
                result.CityCount++;
                result.Population += SafePopulation(city);
                try
                {
                    CityEconomySnapshot economy =
                        CityEconomyService.GetSnapshot(city);
                    if (!economy.has_record) continue;
                    economyCount++;
                    result.PolicyPoints += economy.policy_points;
                    result.TechnologyPoints += economy.tech_points;
                    result.TaxValue += economy.tax_value;
                    result.Manpower += economy.manpower;
                    food += economy.food_stability;
                    unrest += economy.unrest_risk;
                }
                catch
                {
                    // A missing economy record must not hide live population.
                }
            }
            result.EconomyRecordCityCount = economyCount;
            if (economyCount > 0)
            {
                result.FoodStability = food / economyCount;
                result.UnrestRisk = unrest / economyCount;
            }
            return result;
        }

        private static List<City> LiveCities(Kingdom pKingdom)
        {
            var result = new List<City>();
            try
            {
                foreach (City city in pKingdom.getCities() ??
                         Array.Empty<City>())
                    if (IsOwnedLive(city, pKingdom)) result.Add(city);
            }
            catch { }
            return result;
        }

        private static List<City> ResolveOwnedCities(Kingdom pKingdom,
            IEnumerable<long> pCityIds)
        {
            var result = new List<City>();
            foreach (long id in pCityIds ?? Array.Empty<long>())
            {
                City city = FindOwnedCity(pKingdom, id);
                if (city != null) result.Add(city);
            }
            return result;
        }

        private static City FindOwnedCity(Kingdom pKingdom, long pCityId)
        {
            if (pCityId < 0L) return null;
            try
            {
                City city = World.world?.cities?.get(pCityId);
                return IsOwnedLive(city, pKingdom) ? city : null;
            }
            catch { return null; }
        }

        private static CourtStatisticsSnapshot Empty(
            CourtStatisticsScope pScope, string pReason)
        {
            return new CourtStatisticsSnapshot
            {
                Scope = pScope,
                FallbackReason = pReason ?? string.Empty
            };
        }

        private static bool IsOwnedLive(City pCity, Kingdom pKingdom)
        {
            return IsLive(pCity) && pCity.kingdom == pKingdom;
        }

        private static bool IsLive(City pCity)
        {
            return pCity?.data != null && !pCity.isRekt();
        }

        private static int SafePopulation(City pCity)
        {
            try { return Math.Max(0, pCity.getPopulationPeople()); }
            catch { return 0; }
        }
    }
}

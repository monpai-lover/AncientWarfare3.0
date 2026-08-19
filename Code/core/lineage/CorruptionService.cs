using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class CorruptionService
    {
        private const int StreakCap = 10000;

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!CanMutate() || !IsValid(pKingdom)) return;
            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.CORRUPTION_LAST_YEAR,
                out int lastYear, int.MinValue);
            if (lastYear == year) return;

            List<City> cities = LiveCities(pKingdom);
            Dictionary<long, CityBureauView> bureaus = CourtService
                .GetCityBureaus(pKingdom, Math.Max(32, cities.Count + 8))
                .Where(item => item != null && item.city_id >= 0)
                .GroupBy(item => item.city_id)
                .ToDictionary(group => group.Key, group => group.First());
            Dictionary<long, int> officerCounts = CourtService
                .GetActiveOfficers(pKingdom, Math.Max(96, cities.Count * 8))
                .Where(item => item != null &&
                    item.layer == CourtOfficeLayer.City && item.city_id >= 0)
                .GroupBy(item => item.city_id)
                .ToDictionary(group => group.Key, group => group.Count());

            pKingdom.data.get(LineageKeys.CORRUPTION_CLEANUP_ACTIVE,
                out bool cleanupActive, false);
            float cleanupMultiplier = cleanupActive
                ? CorruptionRules.CleanupPressureMultiplier : 1f;

            long weightedScore = 0L;
            long totalPopulation = 0L;
            int highestScore = 0;
            long highestCityId = -1L;
            foreach (City city in cities)
            {
                CityEconomySnapshot economy = CityEconomyService.GetSnapshot(city);
                int population = SafePopulation(city);
                bureaus.TryGetValue(city.id, out CityBureauView bureau);
                officerCounts.TryGetValue(city.id, out int officers);
                CorruptionCityPressure pressure = CalculateCityPressure(city,
                    economy, bureau, officers, population, cleanupMultiplier);
                int cityScore = UpdateCity(city, year, pressure);
                int weight = Math.Max(1, population);
                weightedScore += (long)cityScore * weight;
                totalPopulation += weight;
                if (highestCityId < 0 || cityScore > highestScore)
                {
                    highestScore = cityScore;
                    highestCityId = city.id;
                }
            }

            int average = CorruptionRules.WeightedAverage(weightedScore,
                totalPopulation);
            CourtSnapshot court = CourtService.GetSnapshot(pKingdom);
            float centralPressure = CentralPressure(court) * cleanupMultiplier;
            float fiscalPressure = FiscalPressure(pKingdom, cities) *
                                   cleanupMultiplier;
            pKingdom.data.get(LineageKeys.CORRUPTION_SCORE,
                out int previousScore, 0);
            int score = CorruptionRules.AdvanceInertia(previousScore,
                average + centralPressure + fiscalPressure);
            pKingdom.data.get(LineageKeys.CORRUPTION_HIGH_STREAK_YEARS,
                out int highStreak, 0);
            pKingdom.data.get(LineageKeys.CORRUPTION_VERY_HIGH_STREAK_YEARS,
                out int veryHighStreak, 0);
            highStreak = CorruptionRules.AdvanceStreak(highStreak, score,
                CorruptionRules.HighThreshold, StreakCap);
            veryHighStreak = CorruptionRules.AdvanceStreak(veryHighStreak,
                score, CorruptionRules.VeryHighThreshold, StreakCap);

            pKingdom.data.set(LineageKeys.CORRUPTION_SCORE, score);
            pKingdom.data.set(LineageKeys.CORRUPTION_LAST_YEAR, year);
            pKingdom.data.set(LineageKeys.CORRUPTION_HIGH_STREAK_YEARS,
                highStreak);
            pKingdom.data.set(LineageKeys.CORRUPTION_VERY_HIGH_STREAK_YEARS,
                veryHighStreak);
            pKingdom.data.set(LineageKeys.CORRUPTION_CENTRAL_PRESSURE,
                centralPressure);
            pKingdom.data.set(LineageKeys.CORRUPTION_FISCAL_PRESSURE,
                fiscalPressure);
            pKingdom.data.set(LineageKeys.CORRUPTION_AVERAGE_CITY_SCORE,
                average);
            pKingdom.data.set(LineageKeys.CORRUPTION_HIGHEST_CITY_SCORE,
                highestScore);
            pKingdom.data.set(LineageKeys.CORRUPTION_HIGHEST_CITY_ID,
                highestCityId);
        }

        public static CorruptionCountrySnapshot ReadCountry(Kingdom pKingdom)
        {
            var result = new CorruptionCountrySnapshot();
            if (pKingdom?.data == null) return result;
            pKingdom.data.get(LineageKeys.CORRUPTION_SCORE,
                out result.Score, 0);
            pKingdom.data.get(LineageKeys.CORRUPTION_LAST_YEAR,
                out result.LastYear, int.MinValue);
            pKingdom.data.get(LineageKeys.CORRUPTION_HIGH_STREAK_YEARS,
                out result.HighStreakYears, 0);
            pKingdom.data.get(LineageKeys.CORRUPTION_VERY_HIGH_STREAK_YEARS,
                out result.VeryHighStreakYears, 0);
            pKingdom.data.get(LineageKeys.CORRUPTION_CENTRAL_PRESSURE,
                out result.CentralPressure, 0f);
            pKingdom.data.get(LineageKeys.CORRUPTION_FISCAL_PRESSURE,
                out result.FiscalPressure, 0f);
            pKingdom.data.get(LineageKeys.CORRUPTION_AVERAGE_CITY_SCORE,
                out result.AverageCityScore, 0);
            pKingdom.data.get(LineageKeys.CORRUPTION_HIGHEST_CITY_SCORE,
                out result.HighestCityScore, 0);
            pKingdom.data.get(LineageKeys.CORRUPTION_HIGHEST_CITY_ID,
                out result.HighestCityId, -1L);
            pKingdom.data.get(LineageKeys.CORRUPTION_CLEANUP_ACTIVE,
                out result.CleanupActive, false);
            result.Score = CorruptionRules.ClampScore(result.Score);
            result.Severity = CorruptionRules.GetSeverity(result.Score);
            return result;
        }

        public static CorruptionCitySnapshot ReadCity(City pCity)
        {
            var result = new CorruptionCitySnapshot();
            if (pCity?.data == null) return result;
            pCity.data.get(LineageKeys.CITY_CORRUPTION_SCORE,
                out result.Score, 0);
            pCity.data.get(LineageKeys.CITY_CORRUPTION_LAST_YEAR,
                out result.LastYear, int.MinValue);
            pCity.data.get(LineageKeys.CITY_CORRUPTION_HIGH_STREAK_YEARS,
                out result.HighStreakYears, 0);
            pCity.data.get(LineageKeys.CITY_CORRUPTION_TAX_PRESSURE,
                out result.TaxPressure, 0f);
            pCity.data.get(LineageKeys.CITY_CORRUPTION_OFFICIAL_PRESSURE,
                out result.OfficialPressure, 0f);
            pCity.data.get(LineageKeys.CITY_CORRUPTION_ORDER_PRESSURE,
                out result.OrderPressure, 0f);
            pCity.data.get(LineageKeys.CITY_CORRUPTION_FOOD_PRESSURE,
                out result.FoodPressure, 0f);
            result.Score = CorruptionRules.ClampScore(result.Score);
            result.Severity = CorruptionRules.GetSeverity(result.Score);
            return result;
        }

        public static bool CanStartCleanup(Kingdom pKingdom)
        {
            return IsValid(pKingdom) &&
                   !string.Equals(KingdomPolicyService.GetCurrent(pKingdom,
                       PolicyNodeKind.Decision),
                       "aw_decision_clean_corruption",
                       StringComparison.Ordinal) &&
                   !KingdomPolicyService.IsDecisionQueued(pKingdom,
                       "aw_decision_clean_corruption") &&
                   KingdomPolicyService.GetPoliticalPoints(pKingdom) >=
                   CorruptionRules.CleanupCost &&
                   ReadCountry(pKingdom).Score >=
                   CorruptionRules.CleanupAvailabilityThreshold;
        }

        public static void SetCleanupActive(Kingdom pKingdom, bool pActive)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.CORRUPTION_CLEANUP_ACTIVE, pActive);
        }

        public static bool ApplyCleanup(Kingdom pKingdom)
        {
            if (!CanMutate() || !IsValid(pKingdom)) return false;
            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.CORRUPTION_CLEANUP_COMPLETED_YEAR,
                out int completedYear, int.MinValue);
            if (completedYear == year) return true;

            CorruptionCountrySnapshot country = ReadCountry(pKingdom);
            pKingdom.data.set(LineageKeys.CORRUPTION_SCORE,
                CorruptionRules.ReduceScore(country.Score,
                    CorruptionRules.CleanupCountryReduction));
            foreach (City city in LiveCities(pKingdom)
                         .OrderByDescending(item => ReadCity(item).Score)
                         .ThenBy(item => item.id)
                         .Take(CorruptionRules.CleanupCityCount))
            {
                CorruptionCitySnapshot local = ReadCity(city);
                city.data.set(LineageKeys.CITY_CORRUPTION_SCORE,
                    CorruptionRules.ReduceScore(local.Score,
                        CorruptionRules.CleanupCityReduction));
            }
            pKingdom.data.set(LineageKeys.CORRUPTION_CLEANUP_ACTIVE, false);
            pKingdom.data.set(LineageKeys.CORRUPTION_CLEANUP_COMPLETED_YEAR,
                year);
            return true;
        }

        private static int UpdateCity(City pCity, int pYear,
            CorruptionCityPressure pPressure)
        {
            pCity.data.get(LineageKeys.CITY_CORRUPTION_SCORE,
                out int previous, 0);
            int score = CorruptionRules.AdvanceInertia(previous,
                pPressure.Total);
            pCity.data.get(LineageKeys.CITY_CORRUPTION_HIGH_STREAK_YEARS,
                out int streak, 0);
            streak = CorruptionRules.AdvanceStreak(streak, score,
                CorruptionRules.HighThreshold, StreakCap);
            pCity.data.set(LineageKeys.CITY_CORRUPTION_SCORE, score);
            pCity.data.set(LineageKeys.CITY_CORRUPTION_LAST_YEAR, pYear);
            pCity.data.set(LineageKeys.CITY_CORRUPTION_HIGH_STREAK_YEARS,
                streak);
            pCity.data.set(LineageKeys.CITY_CORRUPTION_TAX_PRESSURE,
                pPressure.Tax);
            pCity.data.set(LineageKeys.CITY_CORRUPTION_OFFICIAL_PRESSURE,
                pPressure.Official);
            pCity.data.set(LineageKeys.CITY_CORRUPTION_ORDER_PRESSURE,
                pPressure.Order);
            pCity.data.set(LineageKeys.CITY_CORRUPTION_FOOD_PRESSURE,
                pPressure.Food);
            return score;
        }

        private static CorruptionCityPressure CalculateCityPressure(City pCity,
            CityEconomySnapshot pEconomy, CityBureauView pBureau,
            int pOfficerCount, int pPopulation, float pCleanupMultiplier)
        {
            float population = Math.Max(1, pPopulation);
            float taxPerCapita = pEconomy?.has_record == true
                ? Math.Max(0f, pEconomy.tax_value) / population : 0f;
            float tax = Clamp((taxPerCapita - 0.08f) * 300f, 0f, 25f);

            int slots = Math.Max(1, pBureau?.office_slots ?? 1);
            float efficiency = Clamp(pBureau?.efficiency ?? 25f, 0f, 100f);
            float vacancyRatio = 1f - Math.Min(1f,
                Math.Max(0, pOfficerCount) / (float)slots);
            float official = Clamp((100f - efficiency) * 0.18f +
                                   vacancyRatio * 10f, 0f, 28f);

            float unrest = pEconomy?.has_record == true
                ? Clamp(pEconomy.unrest_risk, 0f, 100f) : 10f;
            float order = Clamp(unrest * 0.30f, 0f, 30f);

            int hungry = Math.Max(0, pCity.status?.hungry ?? 0);
            float hungryRatio = hungry / population;
            float foodStability = pEconomy?.has_record == true
                ? Math.Max(0f, pEconomy.food_stability) : 0f;
            float foodPerCapita = foodStability / population;
            float food = Clamp(hungryRatio * 35f +
                               Math.Max(0f, 0.02f - foodPerCapita) * 500f,
                0f, 25f);
            return new CorruptionCityPressure(tax * pCleanupMultiplier,
                official * pCleanupMultiplier, order * pCleanupMultiplier,
                food * pCleanupMultiplier);
        }

        private static float CentralPressure(CourtSnapshot pCourt)
        {
            if (pCourt == null) return 12f;
            return Clamp((100f - Clamp(pCourt.efficiency, 0f, 100f)) * 0.12f +
                         (100f - Clamp(pCourt.concentration, 0f, 100f)) * 0.05f,
                0f, 17f);
        }

        private static float FiscalPressure(Kingdom pKingdom,
            IReadOnlyList<City> pCities)
        {
            int population = 0;
            int gold = 0;
            foreach (City city in pCities)
            {
                population += SafePopulation(city);
                try { gold += Math.Max(0, city.getResourcesAmount("gold")); }
                catch { }
            }
            float tax = Math.Max(0f,
                CityEconomyService.GetTaxContribution(pKingdom));
            float taxPerCapita = tax / Math.Max(1, population);
            float goldPerCapita = gold / (float)Math.Max(1, population);
            return Clamp(Math.Max(0f, taxPerCapita - 0.10f) * 80f +
                         Math.Max(0f, 0.30f - goldPerCapita) * 20f,
                0f, 15f);
        }

        private static List<City> LiveCities(Kingdom pKingdom)
        {
            try
            {
                return pKingdom.getCities().Where(city => city?.data != null &&
                    !city.isRekt() && city.kingdom == pKingdom).ToList();
            }
            catch { return new List<City>(); }
        }

        private static int SafePopulation(City pCity)
        {
            try { return Math.Max(0, pCity?.getPopulationPeople() ?? 0); }
            catch { return 0; }
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static bool IsValid(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   pKingdom.isCiv() && !pKingdom.isNeutral();
        }

        private static bool CanMutate()
        {
            return PeasantRebelRouteRules.CanMutateAuthority(
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }

        private readonly struct CorruptionCityPressure
        {
            public readonly float Tax;
            public readonly float Official;
            public readonly float Order;
            public readonly float Food;
            public float Total => Tax + Official + Order + Food;

            public CorruptionCityPressure(float tax, float official,
                float order, float food)
            {
                Tax = tax;
                Official = official;
                Order = order;
                Food = food;
            }
        }
    }
}

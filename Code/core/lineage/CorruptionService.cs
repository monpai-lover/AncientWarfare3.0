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
            // 一次性读取在任官员 city 层计数 + 监察官统计,避免两次 GetActiveOfficers。
            // limit 用较大的 256 下界,保证影响力可能偏低的中央监察官不被截断。
            List<CourtOfficerView> activeOfficers = CourtService
                .GetActiveOfficers(pKingdom,
                    Math.Max(256, cities.Count * 8));
            Dictionary<long, int> officerCounts = activeOfficers
                .Where(item => item != null &&
                    item.layer == CourtOfficeLayer.City && item.city_id >= 0)
                .GroupBy(item => item.city_id)
                .ToDictionary(group => group.Key, group => group.Count());
            Dictionary<long, CityBureauView> bureaus = CourtService
                .GetCityBureaus(pKingdom, Math.Max(32, cities.Count + 8))
                .Where(item => item != null && item.city_id >= 0)
                .GroupBy(item => item.city_id)
                .ToDictionary(group => group.Key, group => group.First());

            pKingdom.data.get(LineageKeys.CORRUPTION_CLEANUP_ACTIVE,
                out bool cleanupActive, false);
            float cleanupMultiplier = cleanupActive
                ? CorruptionRules.CleanupPressureMultiplier : 1f;

            long weightedScore = 0L;
            long totalPopulation = 0L;
            int highestScore = 0;
            long highestCityId = -1L;
            // 地方监察官的逐城统计,先在城层循环前声明,循环内按城压官方压力。
            var localCensorStats = new Dictionary<long, CensorLocalFacts>();
            foreach (City city in cities)
            {
                CityEconomySnapshot economy = CityEconomyService.GetSnapshot(city);
                int population = SafePopulation(city);
                bureaus.TryGetValue(city.id, out CityBureauView bureau);
                officerCounts.TryGetValue(city.id, out int officers);
                localCensorStats.TryGetValue(city.id, out CensorLocalFacts censors);
                CorruptionCityPressure pressure = CalculateCityPressure(city,
                    economy, bureau, officers, population, cleanupMultiplier,
                    censors.Count, censors.Influence);
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
            // 监察官的腐败抑制,分中央/地方两套:
            //  - 中央监察官(在朝廷任职的 censor 层官职,如都察院监察官、御史)
            //    → 压中央腐败压力。
            //  - 地方监察官(在城/县任职但官职定义为 censor 层,如巡按/按察使)
            //    → 压所在城的城层腐败压力,但**反腐难度加大**(折减上限低、
            //    受官府效率制约,官府越腐越难查)。
            // 识别监察性质:layer==censor(中央监察)或官职定义 Layer==censor
            // (覆盖地方监察)或 office_id==censor(内置御史)。
            int censorialCount = 0;
            float censorialInfluence = 0f;
            foreach (CourtOfficerView officer in activeOfficers)
            {
                if (officer == null ||
                    !IsCensorial(pKingdom, officer)) continue;
                bool local = officer.city_id >= 0 &&
                    (string.Equals(officer.layer,
                         CourtOfficeLayer.City,
                         System.StringComparison.Ordinal) ||
                     string.Equals(officer.layer,
                         CourtOfficeLayer.County,
                         System.StringComparison.Ordinal));
                float influence = Math.Max(0f, officer.influence);
                if (local)
                {
                    localCensorStats.TryGetValue(officer.city_id,
                        out CensorLocalFacts facts);
                    localCensorStats[officer.city_id] = new CensorLocalFacts(
                        facts.Count + 1, facts.Influence + influence);
                }
                else
                {
                    censorialCount++;
                    censorialInfluence += influence;
                }
            }
            float centralPressure = CentralPressure(court, censorialCount,
                    censorialInfluence) * cleanupMultiplier;
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
            int pOfficerCount, int pPopulation, float pCleanupMultiplier,
            int pLocalCensorCount = 0, float pLocalCensorInfluence = 0f)
        {
            float population = Math.Max(1, pPopulation);
            float taxPerCapita = pEconomy?.has_record == true
                ? Math.Max(0f, pEconomy.tax_value) / population : 0f;
            float tax = Clamp((taxPerCapita - 0.08f) * 300f, 0f, 25f);

            int slots = Math.Max(1, pBureau?.office_slots ?? 1);
            float efficiency = Clamp(pBureau?.efficiency ?? 25f, 0f, 100f);
            float official = CorruptionRules.LocalOfficialPressure(
                pBureau != null, pOfficerCount, slots, efficiency);
            // 地方监察官反腐:压在城层官方压力上,但**难度加大** ——
            // 折减上限远低于中央,且受官府效率制约(官府越腐,监察越查不动)。
            official = CorruptionRules.ApplyLocalCensorRelief(official,
                pLocalCensorCount, pLocalCensorInfluence, efficiency);

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

        private static float CentralPressure(CourtSnapshot pCourt,
            int pCensorialCount, float pCensorialInfluence)
        {
            if (pCourt == null) return 12f;
            float basePressure = Clamp(
                (100f - Clamp(pCourt.efficiency, 0f, 100f)) * 0.12f +
                (100f - Clamp(pCourt.concentration, 0f, 100f)) * 0.05f,
                0f, 17f);
            // 监察官反腐:每有一位在任监察官,中央腐败压力按影响力折减(非线性,
            // 边际递减),上限为把基础压力压到近零。没有监察官时完全不加成。
            float relief = CorruptionRules.CensorialPressureRelief(
                pCensorialCount, pCensorialInfluence);
            return Clamp(basePressure - relief, 0f, 17f);
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

        /// <summary>
        ///     判断某在任官员是否是监察性质的官职(不论中央还是地方任职)。
        ///     识别三条:layer 直接是 censor(中央监察层)、官职定义 Resolution 的
        ///     Layer 是 censor(覆盖地方监察官职)、office_id 是内置御史 censor。
        /// </summary>
        private static bool IsCensorial(Kingdom pKingdom,
            CourtOfficerView pOfficer)
        {
            if (pOfficer == null) return false;
            if (string.Equals(pOfficer.layer, CourtOfficeLayer.Censor,
                    System.StringComparison.Ordinal)) return true;
            if (string.Equals(pOfficer.office_id, CourtOfficeId.Censor,
                    System.StringComparison.Ordinal)) return true;
            try
            {
                CourtOfficeDefinition definition =
                    CourtProfileRegistry.FindOffice(pKingdom,
                        pOfficer.office_id);
                return definition != null &&
                       string.Equals(definition.Layer,
                           CourtOfficeLayer.Censor,
                           System.StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static bool CanMutate()
        {
            return PeasantRebelRouteRules.CanMutateAuthority(
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }

        private readonly struct CensorLocalFacts
        {
            public readonly int Count;
            public readonly float Influence;
            public CensorLocalFacts(int count, float influence)
            {
                Count = count;
                Influence = influence;
            }
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

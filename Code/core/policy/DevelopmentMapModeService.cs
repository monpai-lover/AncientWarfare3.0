using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class DevelopmentMapModeService
    {
        public const string POWER_ID = "aw_development_mapmode";

        private static readonly Dictionary<long, float> CityScoreCache = new Dictionary<long, float>();
        private static readonly Dictionary<long, float> KingdomAverageCache = new Dictionary<long, float>();
        private static readonly Dictionary<string, ColorAsset> ColorAssetCache = new Dictionary<string, ColorAsset>();

        public static bool IsActive()
        {
            return AWMapModeCoordinator.IsActive(POWER_ID);
        }

        public static string GetCityColorKey(City pCity)
        {
            return CityDevelopmentRules.ColorKeyForScore(GetCityScore(pCity));
        }

        public static float GetCityScore(City pCity)
        {
            if (pCity?.data == null) return 0f;
            if (CityScoreCache.TryGetValue(pCity.id, out float cached)) return cached;

            CityTechReport tech = CityTechService.GetCityReport(pCity, pIncludeNeighborBonus: false);
            float techScore = CityTechMapRules.CalculateDevelopmentScore(tech.adoption_score, tech.total_count);
            CityEconomySnapshot economy = CityEconomyService.GetSnapshot(pCity);
            float economyScore = CalculateEconomyScore(pCity, economy);
            bool nonCore = IsOwnedNonCore(pCity);

            float score = CityDevelopmentRules.CalculateScore(
                SafePopulation(pCity),
                SafeZoneCount(pCity),
                SafeBuildingCount(pCity),
                techScore,
                economyScore,
                economy.unrest_risk / 100f,
                nonCore);
            CityScoreCache[pCity.id] = score;
            return score;
        }

        public static float GetKingdomAverageScore(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0f;
            if (KingdomAverageCache.TryGetValue(pKingdom.id, out float cached)) return cached;

            var scores = new List<float>();
            try
            {
                foreach (City city in pKingdom.getCities())
                    if (city?.data != null && !city.isRekt() && city.isAlive())
                        scores.Add(GetCityScore(city));
            }
            catch
            {
            }

            float average = CityDevelopmentRules.AverageScore(scores);
            KingdomAverageCache[pKingdom.id] = average;
            return average;
        }

        public static ColorAsset GetColorAssetForKey(string pKey)
        {
            string key = string.IsNullOrEmpty(pKey) ? "development_0" : pKey;
            if (ColorAssetCache.TryGetValue(key, out ColorAsset cached)) return cached;
            ColorAsset color = ColorAsset.tryMakeNewColorAsset(CityDevelopmentRules.HexForColorKey(key));
            color?.initColor();
            ColorAssetCache[key] = color;
            return color;
        }

        public static string BuildTooltip(City pCity, Kingdom pKingdom)
        {
            Kingdom kingdom = pKingdom ?? pCity?.kingdom;
            if (pCity?.data == null && kingdom?.data == null) return "";

            string cityLine = "";
            if (pCity?.data != null)
            {
                cityLine = AW_L10n.Text("aw_development_mapmode_city_score", "City development") + ": " +
                           Mathf.RoundToInt(GetCityScore(pCity) * 100f) + "%";
            }

            string kingdomLine = "";
            if (kingdom?.data != null)
            {
                kingdomLine = AW_L10n.Text("aw_development_mapmode_kingdom_average", "Kingdom average") + ": " +
                              Mathf.RoundToInt(GetKingdomAverageScore(kingdom) * 100f) + "%";
            }

            if (string.IsNullOrEmpty(cityLine)) return kingdomLine;
            if (string.IsNullOrEmpty(kingdomLine)) return cityLine;
            return cityLine + "\n" + kingdomLine;
        }

        public static void DirtyMap()
        {
            try
            {
                ClearCache();
                AWMapModeMetaLibrary.ClearDynamicMetaCache();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch
            {
            }
        }

        public static void ClearCache()
        {
            CityScoreCache.Clear();
            KingdomAverageCache.Clear();
        }

        public static void DirtyMapIfActive()
        {
            if (IsActive()) DirtyMap();
        }

        private static float CalculateEconomyScore(City pCity, CityEconomySnapshot pEconomy)
        {
            if (pEconomy != null && pEconomy.has_record)
            {
                float raw = pEconomy.policy_points * 4f + pEconomy.tech_points * 4f +
                            pEconomy.tax_value + pEconomy.manpower + pEconomy.food_stability;
                return Mathf.Clamp01(raw / 55f);
            }

            int population = SafePopulation(pCity);
            int buildings = SafeBuildingCount(pCity);
            return Mathf.Clamp01(population / 180f * 0.65f + buildings / 30f * 0.35f);
        }

        private static bool IsOwnedNonCore(City pCity)
        {
            Kingdom kingdom = pCity?.kingdom;
            if (kingdom?.data == null || pCity?.data == null) return false;
            try
            {
                return WarTerritoryService.GetCoreStatus(kingdom, pCity).status == "owned_non_core";
            }
            catch
            {
                return false;
            }
        }

        private static int SafePopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

        private static int SafeZoneCount(City pCity)
        {
            try { return pCity?.countZones() ?? 0; }
            catch { return 0; }
        }

        private static int SafeBuildingCount(City pCity)
        {
            try { return pCity?.countBuildings() ?? 0; }
            catch { return 0; }
        }
    }
}

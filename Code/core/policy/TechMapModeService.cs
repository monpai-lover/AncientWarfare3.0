using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class TechMapModeService
    {
        public const string POWER_ID = "aw_tech_level_mapmode";
        private const double DIRTY_MIN_INTERVAL = 0.25;
        private static readonly Dictionary<long, string> _cityColorKeyCache = new Dictionary<long, string>();
        private static readonly Dictionary<long, float> _cityRawScoreCache = new Dictionary<long, float>();
        private static readonly Dictionary<string, ColorAsset> _colorAssetCache = new Dictionary<string, ColorAsset>();
        private static double _lastDirtyTime = -1.0;
        private static bool _rangeResolved;
        private static float _rangeMin;
        private static float _rangeMax;

        public static bool IsActive()
        {
            return AWMapModeCoordinator.IsActive(POWER_ID);
        }

        public static TechMapModeLayer GetSelectedLayer()
        {
            int option = 0;
            try { option = AWMapModeMetaLibrary.TechAsset?.getZoneOptionState() ?? 0; }
            catch { option = 0; }
            return TechMapModeOptionRules.ResolveLayer(option);
        }

        public static bool IsDevelopmentLayerSelected()
        {
            return GetSelectedLayer() == TechMapModeLayer.Development;
        }

        public static string GetCityColorKey(City pCity)
        {
            if (pCity?.data == null) return "tech_0";
            if (_cityColorKeyCache.TryGetValue(pCity.id, out string cached)) return cached;

            EnsureVisibleRange();
            float rawScore = GetCityRawScore(pCity);
            float visibleScore = CityTechMapRules.CalculateVisibleScore(rawScore, _rangeMin, _rangeMax);
            string key = CityTechMapRules.ColorKeyForScore(visibleScore);
            _cityColorKeyCache[pCity.id] = key;
            return key;
        }

        public static ColorAsset GetColorAssetForKey(string pKey)
        {
            string key = string.IsNullOrEmpty(pKey) ? "tech_0" : pKey;
            if (_colorAssetCache.TryGetValue(key, out ColorAsset cached)) return cached;
            ColorAsset color = ColorAsset.tryMakeNewColorAsset(CityTechMapRules.HexForColorKey(key));
            color?.initColor();
            _colorAssetCache[key] = color;
            return color;
        }

        public static string BuildTooltip(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            TechLevelReport report = KingdomPolicyService.GetTechLevelReport(pKingdom);
            string current = string.IsNullOrEmpty(report.current_name)
                ? AW_L10n.Text("aw_tech_mapmode_idle", "No current tech")
                : report.current_name + " " + Mathf.RoundToInt(report.current_fraction * 100f) + "%";
            return AW_L10n.Text("aw_tech_mapmode_level", "Tech level") + ": " + report.level + "/" +
                   report.max_level +
                   "\n" + AW_L10n.Text("aw_tech_mapmode_completed", "Completed techs") + ": " +
                   report.completed_count + "/" + report.total_count +
                   "\n" + AW_L10n.Text("aw_tech_mapmode_current", "Current research") + ": " + current +
                   "\n" + AW_L10n.Text("aw_tech_mapmode_points", "Tech points") + ": " +
                   Mathf.FloorToInt(KingdomPolicyService.GetTechPoints(pKingdom));
        }

        public static void DirtyMap()
        {
            try
            {
                ClearCache();
                DevelopmentMapModeService.ClearCache();
                AWMapModeMetaLibrary.ClearDynamicMetaCache();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch
            {
            }
        }

        public static void ClearCache()
        {
            _cityColorKeyCache.Clear();
            _cityRawScoreCache.Clear();
            _rangeResolved = false;
            _rangeMin = 0f;
            _rangeMax = 1f;
        }

        public static void DirtyMapIfActive()
        {
            double now = LineageService.CurTime();
            if (!MapModeDirtyThrottleRules.ShouldDirty(IsActive(), now, _lastDirtyTime, DIRTY_MIN_INTERVAL)) return;
            _lastDirtyTime = now;
            DirtyMap();
        }

        private static void EnsureVisibleRange()
        {
            if (_rangeResolved) return;
            _rangeResolved = true;
            _rangeMin = 1f;
            _rangeMax = 0f;
            bool found = false;

            try
            {
                if (World.world?.kingdoms != null)
                {
                    foreach (Kingdom kingdom in World.world.kingdoms)
                    {
                        if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) continue;
                        if (!KingdomPolicyService.CanUsePolicySystem(kingdom)) continue;
                        foreach (City city in kingdom.getCities())
                        {
                            if (city?.data == null || city.isRekt() || !city.isAlive()) continue;
                            float score = GetCityRawScore(city);
                            if (!found)
                            {
                                _rangeMin = score;
                                _rangeMax = score;
                                found = true;
                                continue;
                            }

                            if (score < _rangeMin) _rangeMin = score;
                            if (score > _rangeMax) _rangeMax = score;
                        }
                    }
                }
            }
            catch
            {
            }

            if (found) return;
            _rangeMin = 0f;
            _rangeMax = 1f;
        }

        private static float GetCityRawScore(City pCity)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null) return 0f;
            if (!KingdomPolicyService.CanUsePolicySystem(pCity.kingdom)) return 0f;
            if (_cityRawScoreCache.TryGetValue(pCity.id, out float cached)) return cached;

            CityTechReport report = CityTechService.GetCityReport(pCity, pIncludeNeighborBonus: false);
            float score = CityTechMapRules.CalculateDevelopmentScore(report.adoption_score, report.total_count);
            _cityRawScoreCache[pCity.id] = score;
            return score;
        }

    }
}

using System.Collections.Generic;
using AncientWarfare3.ui;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class TechMapModeService
    {
        public const string POWER_ID = "aw_tech_level_mapmode";
        private static readonly Dictionary<long, string> _cityColorKeyCache = new Dictionary<long, string>();
        private static readonly Dictionary<string, ColorAsset> _colorAssetCache = new Dictionary<string, ColorAsset>();

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
            string key = CityTechService.GetCityMapColorKey(pCity);
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
                _cityColorKeyCache.Clear();
                DevelopmentMapModeService.ClearCache();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch
            {
            }
        }

        public static void DirtyMapIfActive()
        {
            if (!IsActive()) return;
            DirtyMap();
        }

    }
}

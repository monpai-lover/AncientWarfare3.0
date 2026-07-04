using System.Collections.Generic;
using AncientWarfare3.ui;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class TechMapModeService
    {
        public const string POWER_ID = "aw_tech_level_mapmode";
        private static ColorAsset[] _colors;
        private static TechMapLayer _layer;
        private static readonly Dictionary<long, ColorAsset> _kingdomColorCache = new Dictionary<long, ColorAsset>();
        [System.ThreadStatic] private static int _zoneColorOverrideDepth;

        public static bool IsActive()
        {
            return IsOptionActive() || IsSelectedPower();
        }

        private static bool IsOptionActive()
        {
            try
            {
                return PlayerConfig.optionBoolEnabled(POWER_ID);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSelectedPower()
        {
            try
            {
                return World.world != null && World.world.isSelectedPower(POWER_ID);
            }
            catch
            {
                return false;
            }
        }

        public static ColorAsset GetColor(Kingdom pKingdom, ColorAsset pFallback)
        {
            if (pKingdom?.data == null || !KingdomPolicyService.CanUsePolicySystem(pKingdom)) return pFallback;
            if (_kingdomColorCache.TryGetValue(pKingdom.id, out ColorAsset cached)) return cached ?? pFallback;
            EnsureColors();
            TechLevelReport report = KingdomPolicyService.GetTechLevelReport(pKingdom);
            int index = Mathf.Clamp(report.level - 1, 0, _colors.Length - 1);
            ColorAsset result = _colors[index] ?? pFallback;
            _kingdomColorCache[pKingdom.id] = result;
            return result;
        }

        public static void BeginZoneColorOverride()
        {
            if (!IsActive()) return;
            _zoneColorOverrideDepth++;
        }

        public static void EndZoneColorOverride()
        {
            if (_zoneColorOverrideDepth > 0) _zoneColorOverrideDepth--;
        }

        public static bool ShouldOverrideKingdomZoneColor(Kingdom pKingdom)
        {
            return _zoneColorOverrideDepth > 0 && IsActive() && pKingdom?.data != null &&
                   KingdomPolicyService.CanUsePolicySystem(pKingdom);
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
                EnsureLayer();
                _kingdomColorCache.Clear();
                _layer?.MarkDirty();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch
            {
            }
        }

        public static void EnsureLayer()
        {
            if (World.world == null) return;
            _layer = World.world.GetComponentInChildren<TechMapLayer>();
            if (_layer == null)
            {
                var obj = new GameObject("[layer]AW3 Tech Map", typeof(SpriteRenderer), typeof(TechMapLayer));
                obj.transform.SetParent(World.world.transform, false);
                _layer = obj.GetComponent<TechMapLayer>();
                _layer.create();
            }

            RegisterLayer(_layer);
            _layer?.HideImmediate();
        }

        public static void DirtyMapIfActive()
        {
            if (!IsActive()) return;
            DirtyMap();
        }

        private static void EnsureColors()
        {
            if (_colors != null) return;
            _colors = new[]
            {
                ColorAsset.tryMakeNewColorAsset("#B33A2E"),
                ColorAsset.tryMakeNewColorAsset("#C96B2C"),
                ColorAsset.tryMakeNewColorAsset("#C9A42C"),
                ColorAsset.tryMakeNewColorAsset("#74A84A"),
                ColorAsset.tryMakeNewColorAsset("#2F9B57")
            };
            for (int i = 0; i < _colors.Length; i++)
                _colors[i]?.initColor();
        }

        private static void RegisterLayer(MapLayer pLayer)
        {
            if (pLayer == null || World.world == null) return;
            try
            {
                var list = AccessTools.Field(typeof(MapBox), "_map_layers")?.GetValue(World.world) as List<MapLayer>;
                if (list != null && !list.Contains(pLayer)) list.Add(pLayer);
            }
            catch
            {
            }
        }
    }
}

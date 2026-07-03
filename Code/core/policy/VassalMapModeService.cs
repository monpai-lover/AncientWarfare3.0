using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class VassalMapModeService
    {
        public const string POWER_ID = "aw_vassal_mapmode";

        private static VassalMapLayer _layer;
        [System.ThreadStatic] private static int _zoneColorOverrideDepth;

        public static bool IsActive()
        {
            return IsOptionActive() || IsSelectedPower();
        }

        private static bool IsOptionActive()
        {
            try { return PlayerConfig.optionBoolEnabled(POWER_ID); }
            catch { return false; }
        }

        private static bool IsSelectedPower()
        {
            try { return World.world != null && World.world.isSelectedPower(POWER_ID); }
            catch { return false; }
        }

        public static ColorAsset GetColor(Kingdom pKingdom, ColorAsset pFallback)
        {
            return VassalService.GetMapColor(pKingdom, pFallback);
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
            return _zoneColorOverrideDepth > 0 && IsActive() && pKingdom?.data != null && pKingdom.isCiv();
        }

        public static string BuildTooltip(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            string header = AW_L10n.Text("aw_vassal_mapmode_tooltip", "\u9644\u5EB8\u5730\u56FE");
            string body = VassalService.BuildTooltip(pKingdom);
            return string.IsNullOrEmpty(body) ? header : header + "\n" + body;
        }

        public static void DirtyMap()
        {
            try
            {
                EnsureLayer();
                _layer?.MarkDirty();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch { }
        }

        public static void EnsureLayer()
        {
            if (World.world == null) return;
            _layer = World.world.GetComponentInChildren<VassalMapLayer>();
            if (_layer == null)
            {
                var obj = new GameObject("[layer]AW3 Vassal Map", typeof(SpriteRenderer), typeof(VassalMapLayer));
                obj.transform.SetParent(World.world.transform, false);
                _layer = obj.GetComponent<VassalMapLayer>();
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

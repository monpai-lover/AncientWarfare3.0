using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class MandateDynastyMapModeService
    {
        public const string POWER_ID = "aw_mandate_dynasty_mapmode";

        private static MandateDynastyMapLayer _layer;
        [System.ThreadStatic] private static int _zoneColorOverrideDepth;
        [System.ThreadStatic] private static Dictionary<long, IMetaObject> _zoneDynastyCache;

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
            return MandateService.GetDynastyMapColor(pKingdom, pFallback);
        }

        public static IMetaObject GetDynastyMetaForZone(TileZone pZone)
        {
            City city = pZone?.city;
            Kingdom kingdom = city?.kingdom;
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) return null;

            _zoneDynastyCache ??= new Dictionary<long, IMetaObject>();
            if (_zoneDynastyCache.TryGetValue(kingdom.id, out IMetaObject cached)) return cached;

            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            IMetaObject result = kingdom;
            if (mandate?.data != null &&
                (kingdom == mandate || VassalService.GetRootSuzerain(kingdom) == mandate))
            {
                result = mandate;
            }

            _zoneDynastyCache[kingdom.id] = result;
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
            return _zoneColorOverrideDepth > 0 && IsActive() && pKingdom?.data != null && pKingdom.isCiv();
        }

        public static string BuildTooltip(Kingdom pKingdom)
        {
            return MandateService.BuildDynastyTooltip(pKingdom);
        }

        public static void DirtyMap()
        {
            try
            {
                _zoneDynastyCache?.Clear();
                EnsureLayer();
                _layer?.MarkDirty();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch { }
        }

        public static void EnsureLayer()
        {
            if (World.world == null) return;
            _layer = World.world.GetComponentInChildren<MandateDynastyMapLayer>();
            if (_layer == null)
            {
                var obj = new GameObject("[layer]AW3 Mandate Dynasty Map", typeof(SpriteRenderer), typeof(MandateDynastyMapLayer));
                obj.transform.SetParent(World.world.transform, false);
                _layer = obj.GetComponent<MandateDynastyMapLayer>();
                _layer.create();
            }
            ConfigureRenderer(_layer);
            RegisterLayer(_layer);
            _layer?.HideImmediate();
        }

        public static void DirtyMapIfActive()
        {
            if (IsActive()) DirtyMap();
        }

        private static void RegisterLayer(MapLayer pLayer)
        {
            if (pLayer == null || World.world == null) return;
            try
            {
                var list = AccessTools.Field(typeof(MapBox), "_map_layers")?.GetValue(World.world) as List<MapLayer>;
                if (list != null && !list.Contains(pLayer)) list.Add(pLayer);
            }
            catch { }
        }

        private static void ConfigureRenderer(MandateDynastyMapLayer pLayer)
        {
            if (pLayer == null) return;
            try
            {
                SpriteRenderer renderer = pLayer.GetComponent<SpriteRenderer>();
                if (renderer != null) renderer.sortingOrder = 1;
            }
            catch { }
        }
    }
}

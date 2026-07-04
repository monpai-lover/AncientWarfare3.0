using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.policy
{
    internal static class VassalMapModeService
    {
        public const string POWER_ID = "aw_vassal_mapmode";

        [System.ThreadStatic] private static int _zoneColorOverrideDepth;
        [System.ThreadStatic] private static Dictionary<long, IMetaObject> _zoneRootCache;

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

        public static IMetaObject GetRootMetaForZone(TileZone pZone)
        {
            City city = pZone?.city;
            Kingdom kingdom = city?.kingdom;
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) return null;

            _zoneRootCache ??= new Dictionary<long, IMetaObject>();
            if (_zoneRootCache.TryGetValue(kingdom.id, out IMetaObject cached)) return cached;

            Kingdom root = VassalService.GetRootSuzerain(kingdom);
            IMetaObject result = root?.data == null ? kingdom : root;
            _zoneRootCache[kingdom.id] = result;
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
            if (pKingdom?.data == null) return "";
            string header = AW_L10n.Text("aw_vassal_mapmode_tooltip", "\u9644\u5EB8\u5730\u56FE");
            string body = VassalService.BuildTooltip(pKingdom);
            return string.IsNullOrEmpty(body) ? header : header + "\n" + body;
        }

        public static void DirtyMap()
        {
            try
            {
                _zoneRootCache?.Clear();
                HideLegacyLayer();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch { }
        }

        public static void HideLegacyLayer()
        {
            if (World.world == null) return;
            VassalMapLayer layer = World.world.GetComponentInChildren<VassalMapLayer>();
            layer?.HideImmediate();
        }

        public static void EnsureLayer()
        {
            HideLegacyLayer();
        }

        public static void DirtyMapIfActive()
        {
            if (!IsActive()) return;
            DirtyMap();
        }

    }
}

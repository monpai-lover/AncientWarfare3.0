using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class WarCoreMapModeService
    {
        public const string POWER_ID = "aw_core_mapmode";

        private static long _focusedKingdomId = -1;
        private static readonly Dictionary<string, string> _statusCache = new Dictionary<string, string>();

        public static bool IsActive()
        {
            return AWMapModeCoordinator.IsActive(POWER_ID);
        }

        public static WarMapModeLayer GetSelectedLayer()
        {
            int option = 0;
            try { option = AWMapModeMetaLibrary.WarCoreAsset?.getZoneOptionState() ?? 0; }
            catch { option = 0; }
            return WarMapModeOptionRules.ResolveLayer(option);
        }

        public static bool IsClaimLayerSelected()
        {
            return GetSelectedLayer() == WarMapModeLayer.Claim;
        }

        public static void SetFocus(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral()) return;
            WarClaimMapModeService.SetFocus(pKingdom);
            if (_focusedKingdomId == pKingdom.id) return;
            _focusedKingdomId = pKingdom.id;
            _statusCache.Clear();
            DirtyMap();
        }

        public static Kingdom GetFocusedKingdom()
        {
            Kingdom selected = SelectedMetas.selected_kingdom;
            long selectedId = selected?.data != null && selected.isCiv() && !selected.isNeutral() && !selected.isRekt()
                ? selected.id
                : -1L;
            long focusId = MapModeFocusRules.ResolveFocusId(_focusedKingdomId, selectedId);
            if (focusId < 0) return null;
            try { return World.world?.kingdoms?.get(focusId); }
            catch { return null; }
        }

        public static string BuildTooltip(Kingdom pHover)
        {
            Kingdom focus = GetFocusedKingdom();
            return WarTerritoryService.BuildCoreTooltip(focus, pHover);
        }

        public static string GetColorKeyForZone(TileZone pZone)
        {
            return GetColorKeyForCity(GetFocusedKingdom(), pZone?.city);
        }

        public static string GetColorKeyForCity(Kingdom pFocus, City pCity)
        {
            return WarMapModeColorRules.CoreColorKey(GetCachedStatus(pFocus, pCity));
        }

        private static string GetCachedStatus(Kingdom pFocus, City pCity)
        {
            if (pFocus?.data == null || pCity?.data == null) return "";
            string cacheKey = AWMapModeMetaRules.BuildFocusedCityStatusCacheKey(pFocus.id, pCity.data.id);
            if (string.IsNullOrEmpty(cacheKey)) return "";
            if (_statusCache.TryGetValue(cacheKey, out string status)) return status;
            status = WarTerritoryService.GetCoreStatus(pFocus, pCity).status ?? "";
            _statusCache[cacheKey] = status;
            return status;
        }

        public static void DirtyMap()
        {
            try
            {
                _statusCache.Clear();
                WarClaimMapModeService.ClearCache();
                AWMapModeMetaLibrary.ClearDynamicMetaCache();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch { }
        }

        public static void DirtyMapIfActive()
        {
            if (IsActive()) DirtyMap();
        }

    }
}

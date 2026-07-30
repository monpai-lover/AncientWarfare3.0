using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class WarClaimMapModeService
    {
        public const string POWER_ID = "aw_claim_mapmode";
        private const double DIRTY_MIN_INTERVAL = 0.25;

        private static long _focusedKingdomId = -1;
        private static readonly Dictionary<string, string> _statusCache = new Dictionary<string, string>();
        private static double _lastDirtyTime = -1.0;

        public static bool IsActive()
        {
            return AWMapModeCoordinator.IsActive(POWER_ID);
        }

        public static void SetFocus(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral()) return;
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
            return WarTerritoryService.BuildClaimTooltip(focus, pHover);
        }

        public static string BuildTooltip(City pCity, Kingdom pHover)
        {
            Kingdom focus = GetFocusedKingdom();
            return WarTerritoryService.BuildClaimTooltip(focus, pHover, pCity);
        }

        public static string GetColorKeyForZone(TileZone pZone)
        {
            return GetColorKeyForCity(GetFocusedKingdom(), pZone?.city);
        }

        public static string GetColorKeyForCity(Kingdom pFocus, City pCity)
        {
            return WarMapModeColorRules.ClaimColorKey(GetCachedStatus(pFocus, pCity));
        }

        private static string GetCachedStatus(Kingdom pFocus, City pCity)
        {
            if (pFocus?.data == null || pCity?.data == null) return "";
            string cacheKey = AWMapModeMetaRules.BuildFocusedCityStatusCacheKey(pFocus.id, pCity.data.id);
            if (string.IsNullOrEmpty(cacheKey)) return "";
            if (_statusCache.TryGetValue(cacheKey, out string status)) return status;
            status = WarTerritoryService.GetClaimStatus(pFocus, pCity).status ?? "";
            _statusCache[cacheKey] = status;
            return status;
        }

        public static void DirtyMap()
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                ClearCache();
                AWMapModeMetaLibrary.ClearDynamicMetaCache();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch { }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.MapDirtyIndex, benchmark);
            }
        }

        public static void ClearCache()
        {
            _statusCache.Clear();
        }

        internal static void ResetRuntime()
        {
            _focusedKingdomId = -1L;
            _statusCache.Clear();
            _lastDirtyTime = -1.0;
        }

        public static void DirtyMapIfActive()
        {
            double now = LineageService.CurTime();
            if (!MapModeDirtyThrottleRules.ShouldDirty(IsActive(), now, _lastDirtyTime, DIRTY_MIN_INTERVAL)) return;
            _lastDirtyTime = now;
            DirtyMap();
        }

    }
}

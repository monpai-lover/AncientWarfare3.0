using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class MandateCoreMapModeService
    {
        public const string POWER_ID = "aw_mandate_core_mapmode";
        private const double DIRTY_MIN_INTERVAL = 0.25;

        private static readonly Dictionary<string, string> _statusCache = new Dictionary<string, string>();
        private static double _lastDirtyTime = -1.0;

        public static bool IsActive()
        {
            return AWMapModeCoordinator.IsActive(POWER_ID);
        }

        public static string GetStatusForZone(TileZone pZone)
        {
            return GetStatusForCity(pZone?.city);
        }

        private static string GetStatusForCity(City pCity)
        {
            if (pCity?.data == null) return "none";
            string cacheKey = AWMapModeMetaRules.BuildCityStatusCacheKey(pCity.data.id);
            if (string.IsNullOrEmpty(cacheKey)) return "none";
            if (_statusCache.TryGetValue(cacheKey, out string status)) return status;
            status = MandateService.GetCoreMapStatus(pCity) ?? "none";
            _statusCache[cacheKey] = status;
            return status;
        }

        public static string BuildTooltip(Kingdom pKingdom)
        {
            return MandateService.BuildCoreTooltip(pKingdom);
        }

        public static string BuildTooltip(City pCity, Kingdom pKingdom)
        {
            return MandateService.BuildCoreTooltip(pCity, pKingdom);
        }

        public static void DirtyMap()
        {
            try
            {
                _statusCache.Clear();
                AWMapModeMetaLibrary.ClearDynamicMetaCache();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch { }
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

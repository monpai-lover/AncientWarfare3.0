using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.policy
{
    internal static class MandateDynastyMapModeService
    {
        public const string POWER_ID = "aw_mandate_dynasty_mapmode";
        private const double DIRTY_MIN_INTERVAL = 0.25;
        private static double _lastDirtyTime = -1.0;

        public static bool IsActive()
        {
            return AWMapModeCoordinator.IsActive(POWER_ID);
        }

        public static string BuildTooltip(Kingdom pKingdom)
        {
            return MandateService.BuildDynastyTooltip(pKingdom);
        }

        public static void DirtyMap()
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                AWMapModeMetaLibrary.ClearMandateDynastyStatusCache();
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

        public static void DirtyMapIfActive()
        {
            double now = LineageService.CurTime();
            if (!MapModeDirtyThrottleRules.ShouldDirty(IsActive(), now, _lastDirtyTime, DIRTY_MIN_INTERVAL)) return;
            _lastDirtyTime = now;
            DirtyMap();
        }

        internal static void ResetRuntime()
        {
            _lastDirtyTime = -1.0;
        }

    }
}

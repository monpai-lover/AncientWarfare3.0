using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.policy
{
    internal static class MandateDynastyMapModeService
    {
        public const string POWER_ID = "aw_mandate_dynasty_mapmode";

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
            try
            {
                AWMapModeMetaLibrary.ClearMandateDynastyStatusCache();
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

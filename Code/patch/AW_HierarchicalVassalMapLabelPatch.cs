using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_HierarchicalVassalMapLabelPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static void MapBoxUpdate_Postfix()
        {
            long benchmark = RecentFeatureBenchmark.BeginOutsideFrameStage();
            try
            {
                bool active = HierarchicalVassalMapModeService.IsActive();
                HierarchicalVassalMapModeLabelLayer.ObserveMapModeActive(active);
                if (HierarchicalVassalMapModeLabelLayer.NeedsProcessFrame)
                    HierarchicalVassalMapModeLabelLayer.ProcessFrame();
            }
            finally
            {
                RecentFeatureBenchmark.EndOutsideFrameStage(
                    RecentFeatureBenchmarkRules.HierarchicalLabelsIndex,
                    benchmark);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorld_Prefix()
        {
            HierarchicalVassalMapModeService.Reset();
        }
    }
}

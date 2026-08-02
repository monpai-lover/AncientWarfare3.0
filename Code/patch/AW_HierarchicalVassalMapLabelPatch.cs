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
            HierarchicalVassalMapModeLabelLayer.ProcessFrame();
            HierarchicalVassalMapModeBoundaryLayer.ProcessFrame();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorld_Prefix()
        {
            AW_HierarchicalVassalMapMinimapPatch.ResetSuppression();
            HierarchicalVassalMapModeLabelLayer.Reset();
            HierarchicalVassalMapModeBoundaryLayer.Reset();
        }
    }
}

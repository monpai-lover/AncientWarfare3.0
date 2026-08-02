using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_HierarchicalVassalMapLabelPatch
    {
        // Called by AW_DeferredRuntimeWorkPatch only after capture, worker
        // completion drain, and bounded mesh upload.
        internal static void ProcessLabels()
        {
            HierarchicalVassalMapModeLabelLayer.ProcessFrame();
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorld_Prefix()
        {
            // Cancel the generation before roots/materials/labels are torn
            // down, so a worker completion can never target the old world.
            AW_HierarchicalVassalBoundaryDirtyPatch.CancelGeneration();
            HierarchicalVassalMapModeLabelLayer.Reset();
            HierarchicalVassalMapModeBoundaryLayer.Reset();
        }
    }
}

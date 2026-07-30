using AncientWarfare3.core.presentation;
using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ArmyRtsVisualizationPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), "Update")]
        private static void MapBoxUpdate_Postfix()
        {
            MapBoxFrameStageGuard.Run("army_rts_visualization",
                ArmyRtsVisualizationService.ProcessFrame);
            MapBoxFrameStageGuard.Run("army_rts_attack_speech_bubbles",
                ArmyRtsAttackSpeechBubbleService.ProcessFrame);
            MapBoxFrameStageGuard.Run("army_rts_plan_png",
                ArmyRtsPlanSnapshotService.ProcessFrame);
        }

        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorld_Prefix()
        {
            ArmyRtsVisualizationService.ClearRuntime();
            ArmyMapInformationService.ClearRuntime();
            ArmyRtsAttackSpeechBubbleService.ClearRuntime();
            ArmyRtsPlanSnapshotService.ClearRuntime();
        }
    }
}

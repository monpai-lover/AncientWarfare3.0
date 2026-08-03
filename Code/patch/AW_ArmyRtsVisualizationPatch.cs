using AncientWarfare3.core.presentation;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;
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
            Measure(RecentFeatureBenchmarkRules.RtsVisualizationIndex,
                "army_rts_visualization",
                ArmyRtsVisualizationService.ProcessFrame);
            Measure(RecentFeatureBenchmarkRules.RtsSpeechBubblesIndex,
                "army_rts_attack_speech_bubbles",
                ArmyRtsAttackSpeechBubbleService.ProcessFrame);
            Measure(RecentFeatureBenchmarkRules.RtsPlanSnapshotIndex,
                "army_rts_plan_png", ArmyRtsPlanSnapshotService.ProcessFrame);
        }

        private static void Measure(int pIndex, string pStage,
            System.Action pAction)
        {
            long benchmark = RecentFeatureBenchmark.BeginOutsideFrameStage();
            try { MapBoxFrameStageGuard.Run(pStage, pAction); }
            finally
            {
                RecentFeatureBenchmark.EndOutsideFrameStage(pIndex, benchmark);
            }
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

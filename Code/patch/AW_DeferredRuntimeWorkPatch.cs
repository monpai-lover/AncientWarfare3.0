using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.naming;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.uiquery;
using AncientWarfare3.ui.windows;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_DeferredRuntimeWorkPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), "Update")]
        private static void MapBoxUpdate_Postfix()
        {
            MapBoxFrameStageGuard.Run("family_tree_cleanup",
                DrainFamilyTreeCleanup);
            if (!Config.game_loaded || SmoothLoader.isLoading())
            {
                MapBoxFrameStageGuard.Run("recent_feature_benchmark_flush",
                    RecentFeatureBenchmark.Flush);
                return;
            }

            try
            {
                MapBoxFrameStageGuard.Run("historical_read_completion",
                    DrainPresentationCompletionsMeasured);
                MapBoxFrameStageGuard.Run("localized_name_refresh",
                    AWLocalizedNameRefreshService.ProcessFrame);
                MapBoxFrameStageGuard.Run("school_map_presentation",
                    ProcessSchoolMapPresentationMeasured);
            }
            finally
            {
                MapBoxFrameStageGuard.Run("recent_feature_benchmark_flush",
                    RecentFeatureBenchmark.Flush);
            }
        }

        private static void DrainPresentationCompletions()
        {
            AWHistoricalReadService.DrainMainThread(0.5, 16);
        }

        private static void DrainFamilyTreeCleanup()
        {
            FamilyTreeDeferredCleanupHost.Drain(8);
        }

        private static void DrainPresentationCompletionsMeasured()
        {
            Measure(RecentFeatureBenchmarkRules.AsyncCommitIndex,
                DrainPresentationCompletions);
        }

        private static void ProcessSchoolMapPresentationMeasured()
        {
            Measure(RecentFeatureBenchmarkRules.SchoolMapIndex,
                SchoolMapModeService.ProcessFrame);
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearArmyRtsRuntime_Prefix()
        {
            if (!AWAsyncClearWorldGuard.CleanupAllowed) return;
            MapBoxFrameStageGuard.Reset();
            FamilyTreeDeferredCleanupHost.InvalidateWorld(long.MinValue);
            AWLocalizedNameRefreshService.Clear();
            ArmyCaptainDisposalScope.ClearRuntime();
            ArmyRetreatService.ClearRuntime();
            ArmyRtsControllerService.ClearRuntime();
            ArmyStallWatchdogService.ClearRuntime();
            ArmyLogisticsService.ClearRuntime();
            KingdomWarDirectorService.ClearRuntime();
            ArmyRtsBenchmark.Reset();
        }

        private static void Measure(int pIndex, System.Action pAction)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try { pAction(); }
            finally
            {
                RecentFeatureBenchmark.End(pIndex, benchmark);
            }
        }
    }
}

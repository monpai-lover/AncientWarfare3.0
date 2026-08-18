using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.db;
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
            MeasureOutside(RecentFeatureBenchmarkRules.FamilyTreeCleanupIndex,
                "family_tree_cleanup", DrainFamilyTreeCleanup);
            if (!Config.game_loaded || SmoothLoader.isLoading())
            {
                return;
            }

            MeasureOutside(RecentFeatureBenchmarkRules.AsyncCommitIndex,
                "async_completion_drain", DrainPresentationCompletions);
            MeasureOutside(
                RecentFeatureBenchmarkRules.LocalizedNameRefreshIndex,
                "localized_name_refresh",
                AWLocalizedNameRefreshService.ProcessFrame);
            MeasureOutside(RecentFeatureBenchmarkRules.SchoolMapIndex,
                "school_map_presentation",
                SchoolMapModeService.ProcessFrame);
        }

        private static void DrainPresentationCompletions()
        {
            long deadline = System.Diagnostics.Stopwatch.GetTimestamp() +
                (long)(System.Diagnostics.Stopwatch.Frequency *
                    AWAsyncCompletionDrainRules.FrameBudgetMilliseconds /
                    1000d);

            int pending = AWAsyncRuntime.SnapshotDiagnostics().Completions;
            int itemLimit = AWAsyncCompletionDrainRules.ResolveItemLimit(
                pending);
            if (itemLimit > 0)
                AWAsyncRuntime.DrainMainThread(
                    AWAsyncCompletionDrainRules.RemainingMilliseconds(
                        deadline), itemLimit);

            if (AWAsyncCompletionDrainRules.HasTime(deadline))
                AWAsyncShadowRuntime.DrainMainThread(2);

            int historicalReadPending = AWHistoricalReadService.PendingDrainCount;
            int readLimit = AWAsyncCompletionDrainRules.ResolveItemLimit(
                historicalReadPending);
            if (readLimit > 0 &&
                AWAsyncCompletionDrainRules.HasTime(deadline))
                AWHistoricalReadService.DrainMainThread(
                    AWAsyncCompletionDrainRules.RemainingMilliseconds(
                        deadline), readLimit);

            int historicalWritePending = HistoricalWriteService.PendingCount;
            int writeLimit = AWAsyncCompletionDrainRules.ResolveBatchLimit(
                historicalWritePending);
            if (writeLimit > 0 &&
                AWAsyncCompletionDrainRules.HasTime(deadline))
                HistoricalWriteService.DrainCompletions(
                    AWAsyncCompletionDrainRules.RemainingMilliseconds(
                        deadline), writeLimit);
        }

        private static void DrainFamilyTreeCleanup()
        {
            FamilyTreeDeferredCleanupHost.Drain(8);
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
            ArmyMembershipReconciliationService.ClearRuntime();
            ArmyRetreatService.ClearRuntime();
            ArmyRtsControllerService.ClearRuntime();
            ArmyStallWatchdogService.ClearRuntime();
            ArmyLogisticsService.ClearRuntime();
            KingdomWarDirectorService.ClearRuntime();
            ArmyRtsBenchmark.Reset();
        }

        private static void MeasureOutside(int pIndex, string pStage,
            System.Action pAction)
        {
            long benchmark = RecentFeatureBenchmark.BeginOutsideFrameStage();
            try { MapBoxFrameStageGuard.Run(pStage, pAction); }
            finally
            {
                RecentFeatureBenchmark.EndOutsideFrameStage(pIndex, benchmark);
            }
        }
    }
}

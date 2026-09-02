using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.db;
using AncientWarfare3.core.historyapi;
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
            MeasureOutside(RecentFeatureBenchmarkRules.DeferredWorkIndex,
                "deferred_backlog_drain", DrainDeferredBacklog);
            MeasureOutside(
                RecentFeatureBenchmarkRules.LocalizedNameRefreshIndex,
                "localized_name_refresh",
                AWLocalizedNameRefreshService.ProcessFrame);
            MeasureOutside(RecentFeatureBenchmarkRules.SchoolMapIndex,
                "school_map_presentation",
                SchoolMapModeService.ProcessFrame);
            ShiLineageMapModeService.ProcessFrame();
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

            if (AWAsyncCompletionDrainRules.HasTime(deadline))
                AW3HistoryEventPublisher.Drain(16);
        }

        // 延迟队列此前只有一个出口:权威周期里每次一项(该配额被
        // AuthorityDeferredDrainBudgetTests 明确锁死,不能动)。而 <ordered> 这类
        // 编年史事件按史实入队、无法合并,入队速度长期高于 1 项/周期 —— 实测
        // pending 从 43 单调涨到 123,从不回落。
        //
        // 这里加第二个出口,挂在渲染帧上:不在模拟阶段内,不会抬高每个逻辑
        // tick 的成本(那是倍速的分母),因此对倍速只有好处。仅在积压超过权威
        // 出口的处理能力时才动工,平时 PendingCount 小于阈值直接返回。
        private const int BacklogDrainThreshold = 24;
        private const double BacklogDrainBudgetMilliseconds = 1.5d;
        private const int BacklogDrainMaxItems = 4;

        private static void DrainDeferredBacklog()
        {
            if (DeferredRuntimeWorkService.PendingCount <=
                BacklogDrainThreshold) return;
            DeferredRuntimeWorkService.DrainFrame(
                BacklogDrainBudgetMilliseconds, BacklogDrainMaxItems,
                pIgnoreFrameGate: true);
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
            WarriorArmyMembershipService.ClearRuntime();
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

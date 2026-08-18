using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.performance
{
    internal static class ArmyRtsSchedulingService
    {
        private static readonly ArmyRtsSchedulingGate SharedGate =
            new ArmyRtsSchedulingGate();
        private static long _nativeCycleToken;
        private static bool _sessionStarted;

        public static void ProcessNativeArmyUpdate()
        {
            if (_nativeCycleToken < long.MaxValue)
                _nativeCycleToken++;
            bool paused = World.world == null ||
                          World.world.isPaused();
            ProcessCycle(ArmyRtsSchedulerOwner.NativeArmyManager,
                _nativeCycleToken, paused);
        }

        public static void ProcessLogicalPass(long pCycleToken,
            bool pPaused)
        {
            ProcessCycle(ArmyRtsSchedulerOwner.Aw3Authority,
                pCycleToken, pPaused);
        }

        // Compatibility entry used by the authority-cycle dispatcher. Keep
        // one owner/token path so a logical pass cannot run twice.
        public static void ProcessAw3Authority(long pCycleToken,
            bool pPaused)
        {
            ProcessLogicalPass(pCycleToken, pPaused);
        }

        public static void Reset()
        {
            SharedGate.Reset();
            _sessionStarted = false;
            _nativeCycleToken = 0L;
            CityMilitaryThreatFacts.Reset();
        }

        private static void ProcessCycle(ArmyRtsSchedulerOwner pOwner,
            long pCycleToken, bool pPaused)
        {
            if (!_sessionStarted)
            {
                SharedGate.StartSession(
                    AWPerformanceSettings.UseAw3ArmyRtsScheduler);
                _sessionStarted = true;
            }
            bool allowed = AWFrameSchedulerRules.ShouldRunAuthorityCycle(
                Config.game_loaded, SmoothLoader.isLoading(), pPaused,
                AW3MultiplayerReplicaScope.IsReplicaSession);
            if (!SharedGate.TryEnter(pOwner, pCycleToken, allowed)) return;
            AWSimulationMode simulationMode = AWPerformanceSettings.Mode;
            ArmyRtsExecutionBudget budget =
                ArmyRtsExecutionBudgetRules.Capture(simulationMode,
                    CapturePendingWork());
            CityMilitaryThreatFacts.BeginAuthorityCycle();
            try
            {
                Guard("coalition", () =>
                    Measure(RecentFeatureBenchmarkRules.ArmyRtsCoalitionIndex,
                        CoalitionWarTaskService.ProcessFrame));
                Guard("war_director", () =>
                    Measure(RecentFeatureBenchmarkRules.ArmyRtsDirectorIndex,
                        () => KingdomWarDirectorService.ProcessFrame(
                            budget.FirstOrders)));
                Guard("abstract_battle", () =>
                    ArmyAbstractBattleService.ProcessFrame(
                        budget.AbstractBattles));
                Guard("route_provider", () =>
                    Measure(RecentFeatureBenchmarkRules.PathfindingIndex,
                        ArmyRouteProviderService.ProcessFrame));
                Guard("controller", () =>
                    Measure(RecentFeatureBenchmarkRules.ArmyRtsControllerIndex,
                        () => ArmyRtsControllerService.ProcessFrame(
                            budget.ControllerArmies, budget.ReplenishmentArrivals)));
                Guard("logistics", () =>
                    Measure(RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                        ArmyLogisticsService.ProcessFrame));
                Guard("watchdog", () =>
                    Measure(RecentFeatureBenchmarkRules.ArmyRtsWatchdogIndex,
                        () => ArmyStallWatchdogService.ProcessFrame(
                            budget.WatchdogArmies,
                            pForceSample: simulationMode ==
                                          AWSimulationMode.Large)));
                if (simulationMode == AWSimulationMode.Large)
                {
                    Guard("war_lifecycle", () =>
                        ArmyRtsWarLifecycleService.ProcessAuthorityCycle(
                            budget.LifecycleDiscoveries));
                    Guard("assignment_reconciliation", () =>
                        ArmyRtsAssignmentReconciliationService.
                            ProcessAuthorityCycle(
                                budget.AssignmentReconciliations));
                    Guard("succession_recovery", () =>
                        ArmyRtsSuccessionRecoveryService.
                            ProcessPendingRecoveries(
                                budget.SuccessionRecoveries));
                }
            }
            finally
            {
                CityMilitaryThreatFacts.EndAuthorityCycle();
            }
        }

        private static ArmyRtsPendingWork CapturePendingWork()
        {
            return new ArmyRtsPendingWork(
                controllerArmies:
                    ArmyRtsControllerService.PendingControllerCount,
                firstOrders:
                    KingdomWarDirectorService.PendingFirstOrderCount,
                replenishmentArrivals:
                    ArmyRtsControllerService.
                        PendingReplenishmentArrivalCount,
                watchdogArmies:
                    ArmyStallWatchdogService.PendingArmyCount,
                successionRecoveries:
                    ArmyRtsSuccessionRecoveryService.
                        PendingRecoveryUpperBound,
                lifecycleDiscoveries:
                    ArmyRtsWarLifecycleService.
                        PendingDiscoveryArmyCount,
                assignmentReconciliations:
                    ArmyRtsAssignmentReconciliationService.
                        PendingRecordCount,
                abstractBattles:
                    ArmyAbstractBattleService.PendingWorkUpperBound);
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

        // 隔离单个子服务：某子服务本周期抛异常时，只跳过它并记警告，
        // 不让异常冒泡到 ArmyManager.update 后缀而触发全局静默暂停。
        private static void Guard(string pName, System.Action pAction)
        {
            try
            {
                pAction();
            }
            catch (System.Exception error)
            {
                ModClass.LogWarning(
                    "AW army RTS sub-service '" + pName +
                    "' faulted this cycle; skipped to avoid global pause: " +
                    error);
            }
        }
    }
}

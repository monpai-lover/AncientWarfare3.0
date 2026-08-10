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
                Measure(RecentFeatureBenchmarkRules.ArmyRtsCoalitionIndex,
                    CoalitionWarTaskService.ProcessFrame);
                Measure(RecentFeatureBenchmarkRules.ArmyRtsDirectorIndex,
                    () => KingdomWarDirectorService.ProcessFrame(
                        budget.FirstOrders));
                ArmyAbstractBattleService.ProcessFrame(
                    budget.AbstractBattles);
                Measure(RecentFeatureBenchmarkRules.PathfindingIndex,
                    ArmyRouteProviderService.ProcessFrame);
                Measure(RecentFeatureBenchmarkRules.ArmyRtsControllerIndex,
                    () => ArmyRtsControllerService.ProcessFrame(
                        budget.ControllerArmies, budget.ReplenishmentArrivals));
                Measure(RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                    ArmyLogisticsService.ProcessFrame);
                Measure(RecentFeatureBenchmarkRules.ArmyRtsWatchdogIndex,
                    () => ArmyStallWatchdogService.ProcessFrame(
                        budget.WatchdogArmies,
                        pForceSample: simulationMode ==
                                      AWSimulationMode.Large));
                if (simulationMode == AWSimulationMode.Large)
                {
                    ArmyRtsWarLifecycleService.ProcessAuthorityCycle(
                        budget.LifecycleDiscoveries);
                    ArmyRtsAssignmentReconciliationService.
                        ProcessAuthorityCycle(
                            budget.AssignmentReconciliations);
                    ArmyRtsSuccessionRecoveryService.
                        ProcessPendingRecoveries(
                            budget.SuccessionRecoveries);
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
    }
}

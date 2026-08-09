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
            CityMilitaryThreatFacts.BeginAuthorityCycle();
            try
            {
                Measure(RecentFeatureBenchmarkRules.ArmyRtsCoalitionIndex,
                    CoalitionWarTaskService.ProcessFrame);
                Measure(RecentFeatureBenchmarkRules.ArmyRtsDirectorIndex,
                    KingdomWarDirectorService.ProcessFrame);
                ArmyAbstractBattleService.ProcessFrame();
                Measure(RecentFeatureBenchmarkRules.PathfindingIndex,
                    ArmyRouteProviderService.ProcessFrame);
                Measure(RecentFeatureBenchmarkRules.ArmyRtsControllerIndex,
                    ArmyRtsControllerService.ProcessFrame);
                Measure(RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                    ArmyLogisticsService.ProcessFrame);
                Measure(RecentFeatureBenchmarkRules.ArmyRtsWatchdogIndex,
                    ArmyStallWatchdogService.ProcessFrame);
            }
            finally
            {
                CityMilitaryThreatFacts.EndAuthorityCycle();
            }
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

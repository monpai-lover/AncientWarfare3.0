using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.performance
{
    internal static class ArmyRtsSchedulingService
    {
        private static readonly ArmyRtsSchedulingGate NativeGate =
            new ArmyRtsSchedulingGate();
        private static readonly ArmyRtsSchedulingGate Aw3Gate =
            new ArmyRtsSchedulingGate();
        private static long _nativeCycleToken;

        public static void ProcessNativeArmyUpdate()
        {
            if (_nativeCycleToken < long.MaxValue)
                _nativeCycleToken++;
            bool paused = World.world == null ||
                          World.world.isPaused();
            ProcessCycle(NativeGate,
                ArmyRtsSchedulerOwner.NativeArmyManager,
                _nativeCycleToken, paused);
        }

        public static void ProcessAw3Authority(long pCycleToken,
            bool pCyclePaused)
        {
            ProcessCycle(Aw3Gate, ArmyRtsSchedulerOwner.Aw3Authority,
                pCycleToken, pCyclePaused);
        }

        public static void Reset()
        {
            NativeGate.Reset();
            Aw3Gate.Reset();
            _nativeCycleToken = 0L;
            CityMilitaryThreatFacts.Reset();
        }

        private static void ProcessCycle(ArmyRtsSchedulingGate pGate,
            ArmyRtsSchedulerOwner pOwner, long pCycleToken,
            bool pPaused)
        {
            bool allowed = AWFrameSchedulerRules.ShouldRunAuthorityCycle(
                Config.game_loaded, SmoothLoader.isLoading(), pPaused,
                AW3MultiplayerReplicaScope.IsReplicaSession);
            if (!pGate.TryEnter(ArmyRtsSchedulingMode.Current, pOwner,
                    pCycleToken, allowed)) return;
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

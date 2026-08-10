using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.performance
{
    internal static class AWAuthorityCycleService
    {
        private static readonly AWAuthorityCycleGate CooperativeGate =
            new AWAuthorityCycleGate();
        private static readonly AWAuthorityCycleGate NativeGate =
            new AWAuthorityCycleGate();
        private static long _nativeCycleToken;

        public static void ProcessCooperativeCycle(long pCycleToken,
            bool pCyclePaused)
        {
            ProcessCycle(CooperativeGate, pCycleToken, pCyclePaused);
        }

        public static void ProcessNativeCycle()
        {
            if (_nativeCycleToken < long.MaxValue)
                _nativeCycleToken++;
            bool paused = MapBox.instance == null ||
                          MapBox.instance.isPaused();
            ProcessCycle(NativeGate, _nativeCycleToken, paused);
        }

        public static void Reset()
        {
            CooperativeGate.Reset();
            NativeGate.Reset();
            _nativeCycleToken = 0L;
            ArmyRtsSchedulingService.Reset();
            NobleHeirPregnancyService.Reset();
            RulerHouseholdPregnancyService.Reset();
            DynasticMaleLineContinuityService.Reset();
            EnclosedUnownedZoneRepairService.Reset();
            WarScoreService.ClearPendingCityOccupations();
            CivilServiceExamService.ClearRuntime();
            TemporaryMilitaryReturnService.ClearRuntime();
            CityReservePoolService.ClearRuntime();
            ArmyReplenishmentOperationService.ClearRuntime();
            KingdomDecisionMonthlyService.Reset();
            WarParticipantEntrySourceService.Instance.ClearRuntime();
        }

        private static void ProcessCycle(AWAuthorityCycleGate pGate,
            long pCycleToken, bool pPaused)
        {
            bool allowed = AWFrameSchedulerRules.ShouldRunAuthorityCycle(
                Config.game_loaded, SmoothLoader.isLoading(), pPaused,
                AW3MultiplayerReplicaScope.IsReplicaSession);
            if (!pGate.TryEnter(pCycleToken, allowed)) return;

            DynasticMaleLineContinuityService.ProcessAuthorityCycle();
            NobleHeirPregnancyService.ProcessAuthorityCycle();
            RulerHouseholdPregnancyService.ProcessAuthorityCycle();
            EnclosedUnownedZoneRepairService.ProcessAuthorityCycle();
            TemporaryMilitaryReturnService.ProcessFrame();
            Measure(RecentFeatureBenchmarkRules.PathfindingIndex,
                AWPathfindingBootstrap.ProcessFrame);
            ArmyRtsSchedulingService.ProcessAw3Authority(pCycleToken, pPaused);
            Measure(RecentFeatureBenchmarkRules.SchoolsIndex,
                HistoricalSchoolRuntime.ProcessFrame);
            Measure(RecentFeatureBenchmarkRules.CivilServiceExamRuntimeIndex,
                CivilServiceExamService.ProcessAuthorityCycle);
            Measure(RecentFeatureBenchmarkRules.DiplomacyIndex,
                DiplomacyProposalService.ProcessFrame);
            Measure(RecentFeatureBenchmarkRules.DiplomacyIndex,
                DiplomaticOperationService.ProcessFrame);
            Measure(RecentFeatureBenchmarkRules.KingdomMobilizationIndex,
                TemporaryLevyService.ProcessPreparationMonth);
            Measure(RecentFeatureBenchmarkRules.KingdomMobilizationIndex,
                KingdomDecisionMonthlyService.ProcessAuthorityCycle);
            Measure(RecentFeatureBenchmarkRules.KingdomMobilizationIndex,
                CityReservePoolService.ProcessAuthorityCycle);
            Measure(RecentFeatureBenchmarkRules.KingdomMobilizationIndex,
                ArmyReplenishmentOperationService.ProcessAuthorityCycle);
            Measure(RecentFeatureBenchmarkRules.AsyncCommitIndex,
                DrainAuthorityCompletions);
            Measure(RecentFeatureBenchmarkRules.AsyncCommitIndex,
                FlushPendingWarParticipantSources);
            Measure(RecentFeatureBenchmarkRules.DeferredWorkIndex,
                DrainDeferredAuthorityWork);
            Measure(RecentFeatureBenchmarkRules.CaptureScanIndex,
                SlaveCaptureScanService.DrainFrame);
        }

        private static void DrainAuthorityCompletions()
        {
            AWAsyncRuntime.DrainMainThread(1.0, 32);
            HistoricalWriteService.DrainCompletions(0.5, 16);
        }

        private static void DrainDeferredAuthorityWork()
        {
            int itemLimit = AuthorityDeferredDrainRules.ResolveItemLimit(
                DeferredRuntimeWorkService.PendingCount);
            if (itemLimit <= 0) return;
            DeferredRuntimeWorkService.DrainFrame(pMilliseconds: 1.0,
                pMaxItems: itemLimit);
        }

        private static void FlushPendingWarParticipantSources()
        {
            WarParticipantEntrySourceService.Instance.
                FlushPendingSources(32);
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

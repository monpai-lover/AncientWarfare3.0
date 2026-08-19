using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.naming;
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
            EmptyCityResettlementService.Reset();
            WarScoreService.ClearPendingCityOccupations();
            CivilServiceExamService.ClearRuntime();
            WesternCourtElectionService.Reset();
            AccessionIdentityService.ClearRuntime();
            TemporaryMilitaryReturnService.ClearRuntime();
            WarArmyReturnService.ClearRuntime();
            ArmyRtsAssignmentReconciliationService.Reset();
            AWEnemyPresenceCache.Clear();
            AWStatusSimulationScheduler.ClearRuntime();
            CityReservePoolService.ClearRuntime();
            WarForceEliminationSettlementService.ClearRuntime();
            WarTerminalSettlementCoordinator.ClearRuntime();
            SpecialGovernmentWarParticipationService.ClearRuntime();
            ArmyReplenishmentOperationService.ClearRuntime();
            KingdomDecisionMonthlyService.Reset();
            WarParticipantEntrySourceService.Instance.ClearRuntime();
            ZhuluAgeDirectorService.Reset();
            AWLocalizedNameMigrationService.Reset();
            WesternLineageMigrationService.Reset();
            KingdomInstitutionalXiaizationService.Reset();
            ReigningRoyalLineageIndex.Reset();
            SuccessionDisputePersistenceService.Reset();
            ActorDeathArchiveService.Reset();
            PeasantRebelBanditStrongholdPopulationService.Clear();
            BanditStrongholdCityDisposalService.Clear();
        }

        private static void ProcessCycle(AWAuthorityCycleGate pGate,
            long pCycleToken, bool pPaused)
        {
            bool allowed = AWFrameSchedulerRules.ShouldRunAuthorityCycle(
                Config.game_loaded, SmoothLoader.isLoading(), pPaused,
                AW3MultiplayerReplicaScope.IsReplicaSession) &&
                           !AWWorldInitializationGate.IsPending();
            if (!pGate.TryEnter(pCycleToken, allowed)) return;

            WesternCourtElectionService.ProcessAuthorityCycle();
            AccessionIdentityService.ProcessDeferredInstallations();
            ReigningRoyalLineageIndex.ProcessAuthorityCycle();
            SuccessionDisputePersistenceService.ProcessAuthorityCycle();
            AWLocalizedNameMigrationService.ProcessAuthorityCycle();
            WesternLineageMigrationService.ProcessAuthorityCycle();
            KingdomInstitutionalXiaizationService.ProcessAuthorityCycle();
            DynasticMaleLineContinuityService.ProcessAuthorityCycle();
            NobleHeirPregnancyService.ProcessAuthorityCycle();
            RulerHouseholdPregnancyService.ProcessAuthorityCycle();
            ArmyMembershipReconciliationService.ProcessFrame();
            EnclosedUnownedZoneRepairService.ProcessAuthorityCycle();
            EmptyCityResettlementService.ProcessAuthorityCycle();
            TemporaryMilitaryReturnService.ProcessFrame();
            WarArmyReturnService.ProcessFrame();
            ArmyRtsAssignmentReconciliationService.ProcessAuthorityCycle();
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
            Measure(RecentFeatureBenchmarkRules.DiplomacyIndex,
                ZhuluAgeDirectorService.ProcessAuthorityCycle);
            Measure(RecentFeatureBenchmarkRules.DiplomacyIndex,
                WarTerminalSettlementCoordinator.ProcessAuthorityCycle);
            Measure(RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                SpecialGovernmentWarParticipationService.ProcessAuthorityCycle);
            Measure(RecentFeatureBenchmarkRules.MonthKingdomPolicyIndex,
                KingdomDecisionMonthlyService.ProcessAuthorityCycle);
            TemporaryLevyService.ProcessLegacyMigration();
            Measure(RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                CityReservePoolService.ProcessAuthorityCycle);
            Measure(RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
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
            // Async results are applied once per render frame by
            // AW_DeferredRuntimeWorkPatch. Replaying them inside every
            // simulation pass makes large-step mode pay the same callback
            // cost multiple times in one frame.
            ActorDeathArchiveService.ProcessAuthorityCycle();
        }

        private static void DrainDeferredAuthorityWork()
        {
            PeasantRebelBanditStrongholdPopulationService.
                ProcessAuthorityCycle();
            BanditStrongholdCityDisposalService.ProcessAuthorityCycle();
            int itemLimit = DeferredRuntimeWorkRules.
                ResolveItemsPerAuthorityFrame(
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

        // The cooperative runner is retained as the owner of the staged
        // simulation. This compatibility surface delegates to the existing
        // authority pass and never reintroduces removed migration stages.
        public static bool ProcessCooperativeStep(long pCycleToken,
            bool pCyclePaused)
        {
            ProcessCooperativeCycle(pCycleToken, pCyclePaused);
            return true;
        }

        public static void AbortCooperativeCycle()
        {
            CooperativeGate.Reset();
        }

        public static string GetCooperativePhaseName()
        {
            return "aw3.authority";
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

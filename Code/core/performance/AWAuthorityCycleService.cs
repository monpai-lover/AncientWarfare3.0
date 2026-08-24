using System;
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
            SyntheticMobilizationLedgerService.ClearRuntime();
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

            MeasureAuthority("court_election",
                WesternCourtElectionService.ProcessAuthorityCycle);
            MeasureAuthority("accession_installations",
                AccessionIdentityService.ProcessDeferredInstallations);
            MeasureAuthority("royal_lineage_index",
                ReigningRoyalLineageIndex.ProcessAuthorityCycle);
            MeasureAuthority("succession_dispute_persistence",
                SuccessionDisputePersistenceService.ProcessAuthorityCycle);
            MeasureAuthority("localized_name_migration",
                AWLocalizedNameMigrationService.ProcessAuthorityCycle);
            MeasureAuthority("western_lineage_migration",
                WesternLineageMigrationService.ProcessAuthorityCycle);
            MeasureAuthority("institutional_xiaization",
                KingdomInstitutionalXiaizationService.ProcessAuthorityCycle);
            MeasureAuthority("dynastic_continuity",
                DynasticMaleLineContinuityService.ProcessAuthorityCycle);
            MeasureAuthority("noble_heir_pregnancy",
                NobleHeirPregnancyService.ProcessAuthorityCycle);
            MeasureAuthority("household_pregnancy",
                RulerHouseholdPregnancyService.ProcessAuthorityCycle);
            MeasureAuthority("army_membership_reconciliation",
                ArmyMembershipReconciliationService.ProcessFrame);
            MeasureAuthority("unowned_zone_repair",
                EnclosedUnownedZoneRepairService.ProcessAuthorityCycle);
            MeasureAuthority("empty_city_resettlement",
                EmptyCityResettlementService.ProcessAuthorityCycle);
            MeasureAuthority("temporary_military_return",
                TemporaryMilitaryReturnService.ProcessFrame);
            MeasureAuthority("war_army_return",
                WarArmyReturnService.ProcessFrame);
            MeasureAuthority("rts_assignment_reconciliation",
                ArmyRtsAssignmentReconciliationService.ProcessAuthorityCycle);
            Measure(RecentFeatureBenchmarkRules.PathfindingIndex,
                AWPathfindingBootstrap.ProcessFrame);
            MeasureAuthority("army_rts_authority", () =>
                ArmyRtsSchedulingService.ProcessAw3Authority(
                    pCycleToken, pPaused));
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
            MeasureAuthority("temporary_levy_migration",
                TemporaryLevyService.ProcessLegacyMigration);
            Measure(RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                CityReservePoolService.ProcessAuthorityCycle);
            Measure(RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                ArmyReplenishmentOperationService.ProcessAuthorityCycle);
            MeasureAuthority("war_refugee",
                () => WarRefugeeService.ProcessAuthorityCycle(
                    pCycleToken, pPaused));
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
            int pending = DeferredRuntimeWorkService.PendingCount;
            int itemLimit = DeferredRuntimeWorkRules.
                ResolveItemsPerAuthorityFrame(
                pending);
            if (itemLimit <= 0) return;
            // Keep the ordinary one-item authority budget. If large-step
            // simulation has produced a backlog, allow a tiny bounded
            // catch-up window so persistent work cannot grow forever.
            int catchUpLimit = DeferredRuntimeWorkRules.
                ResolveCatchUpItemsPerAuthorityFrame(pending);
            itemLimit = Math.Max(itemLimit, catchUpLimit);
            DeferredRuntimeWorkService.DrainFrame(
                pMilliseconds: DeferredRuntimeWorkRules.
                    ResolveDrainMillisecondsPerAuthorityFrame(pending),
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
            long authorityStage = RuntimePerformanceDiagnostic.
                BeginAuthorityStage();
            long benchmark = RecentFeatureBenchmark.Begin();
            try { pAction(); }
            finally
            {
                RecentFeatureBenchmark.End(pIndex, benchmark);
                RuntimePerformanceDiagnostic.EndAuthorityStage(
                    RecentFeatureBenchmarkRules.IdForIndex(pIndex),
                    authorityStage);
            }
        }

        private static void MeasureAuthority(string pId,
            System.Action pAction)
        {
            long started = RuntimePerformanceDiagnostic.BeginAuthorityStage();
            try { pAction(); }
            finally
            {
                RuntimePerformanceDiagnostic.EndAuthorityStage(pId, started);
            }
        }
    }
}

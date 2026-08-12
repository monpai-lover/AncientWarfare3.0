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
        private enum CooperativeAuthorityStage
        {
            SuccessionRelationships,
            ReigningRoyalLineages,
            SuccessionDisputePersistence,
            WesternCourtElection,
            AccessionInstallations,
            ArmyRtsSuccessionRecovery,
            LocalizedNameMigration,
            WesternLineageMigration,
            KingdomInstitutionalXiaization,
            DynasticMaleLineContinuity,
            NobleHeirPregnancy,
            RulerHouseholdPregnancy,
            ArmyMembershipReconciliation,
            EnclosedUnownedZoneRepair,
            EmptyCityResettlement,
            TemporaryMilitaryReturn,
            WarArmyReturn,
            ArmyRtsWarLifecycle,
            ArmyRtsAssignmentReconciliation,
            Pathfinding,
            ArmyRts,
            Schools,
            CivilServiceExam,
            DiplomacyProposal,
            DiplomaticOperation,
            ZhuluAge,
            WarForceEliminationSettlement,
            KingdomDecisionMonthly,
            CityReservePool,
            SyntheticMobilizationLedger,
            ArmyReplenishment,
            ActorDeathArchive,
            AsyncMainThreadDrain,
            HistoricalWriteCompletions,
            WarParticipantSources,
            DeferredAuthorityWork,
            SlaveCaptureScan,
            Complete
        }

        private static readonly string[] CooperativePhaseNames =
        {
            "aw3.authority.succession_relationships",
            "aw3.authority.reigning_royal_lineages",
            "aw3.authority.succession_dispute_persistence",
            "aw3.authority.western_court_election",
            "aw3.authority.accession_installations",
            "aw3.authority.army_rts_succession_recovery",
            "aw3.authority.localized_name_migration",
            "aw3.authority.western_lineage_migration",
            "aw3.authority.kingdom_institutional_xiaization",
            "aw3.authority.dynastic_male_line_continuity",
            "aw3.authority.noble_heir_pregnancy",
            "aw3.authority.ruler_household_pregnancy",
            "aw3.authority.army_membership_reconciliation",
            "aw3.authority.enclosed_unowned_zone_repair",
            "aw3.authority.empty_city_resettlement",
            "aw3.authority.temporary_military_return",
            "aw3.authority.war_army_return",
            "aw3.authority.army_rts_war_lifecycle",
            "aw3.authority.army_rts_assignment_reconciliation",
            "aw3.authority.pathfinding",
            "aw3.authority.army_rts",
            "aw3.authority.schools",
            "aw3.authority.civil_service_exam",
            "aw3.authority.diplomacy_proposal",
            "aw3.authority.diplomatic_operation",
            "aw3.authority.zhulu_age",
            "aw3.authority.war_force_elimination_settlement",
            "aw3.authority.kingdom_decision_monthly",
            "aw3.authority.city_reserve_pool",
            "aw3.authority.synthetic_mobilization_ledger",
            "aw3.authority.army_replenishment",
            "aw3.authority.actor_death_archive",
            "aw3.authority.async_main_thread_drain",
            "aw3.authority.historical_write_completions",
            "aw3.authority.war_participant_sources",
            "aw3.authority.deferred_authority_work",
            "aw3.authority.slave_capture_scan"
        };

        private static readonly AWAuthorityCycleGate CooperativeGate =
            new AWAuthorityCycleGate();
        private static readonly AWAuthorityCycleGate NativeGate =
            new AWAuthorityCycleGate();
        private static long _nativeCycleToken;
        private static bool _cooperativeActive;
        private static long _cooperativeCycleToken;
        private static bool _cooperativeCyclePaused;
        private static CooperativeAuthorityStage _cooperativeStage =
            CooperativeAuthorityStage.Complete;

        public static string GetCooperativePhaseName()
        {
            CooperativeAuthorityStage stage = _cooperativeActive
                ? _cooperativeStage
                : CooperativeAuthorityStage.SuccessionRelationships;
            return CooperativePhaseNames[(int)stage];
        }

        public static bool ProcessCooperativeStep(long pCycleToken,
            bool pCyclePaused)
        {
            if (!_cooperativeActive)
            {
                bool allowed = CanRunAuthorityCycle(pCyclePaused);
                if (!CooperativeGate.TryEnter(pCycleToken, allowed))
                    return true;
                _cooperativeActive = true;
                _cooperativeCycleToken = pCycleToken;
                _cooperativeCyclePaused = pCyclePaused;
                _cooperativeStage =
                    CooperativeAuthorityStage.SuccessionRelationships;
            }
            else if (_cooperativeCycleToken != pCycleToken)
            {
                throw new System.InvalidOperationException(
                    "AW authority cycle token changed before completion.");
            }

            CooperativeAuthorityStage stage = _cooperativeStage;
            string phase = CooperativePhaseNames[(int)stage];
            long diagnostic =
                RuntimePerformanceDiagnostic.BeginContinuousScope();
            try
            {
                ExecuteStage(stage, _cooperativeCycleToken,
                    _cooperativeCyclePaused);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndContinuousStage(
                    phase, diagnostic);
            }

            _cooperativeStage = (CooperativeAuthorityStage)((int)stage + 1);
            if (_cooperativeStage != CooperativeAuthorityStage.Complete)
                return false;
            ResetCooperativeState();
            return true;
        }

        public static void AbortCooperativeCycle()
        {
            ResetCooperativeState();
        }

        public static void ProcessNativeCycle()
        {
            if (_nativeCycleToken < long.MaxValue)
                _nativeCycleToken++;
            bool paused = World.world == null ||
                          World.world.isPaused();
            long diagnostic = RuntimePerformanceDiagnostic.
                BeginContinuousScope();
            try { ProcessCycle(NativeGate, _nativeCycleToken, paused); }
            finally
            {
                RuntimePerformanceDiagnostic.EndContinuousStage(
                    "authority_cycle", diagnostic);
            }
        }

        public static void Reset()
        {
            CooperativeGate.Reset();
            NativeGate.Reset();
            ResetCooperativeState();
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
            ArmyRtsSuccessionRecoveryService.Reset();
            TemporaryMilitaryReturnService.ClearRuntime();
            WarArmyReturnService.ClearRuntime();
            ArmyRtsAssignmentReconciliationService.Reset();
            AWStatusSimulationScheduler.ClearRuntime();
            CityReservePoolService.ClearRuntime();
            SyntheticMobilizationLedgerService.ClearRuntime();
            WarForceEliminationSettlementService.ClearRuntime();
            ArmyReplenishmentOperationService.ClearRuntime();
            MonthlyKingdomSnapshotService.Reset();
            KingdomDecisionMonthlyService.Reset();
            WarParticipantEntrySourceService.Instance.ClearRuntime();
            ZhuluAgeDirectorService.Reset();
            AWLocalizedNameMigrationService.Reset();
            WesternLineageMigrationService.Reset();
            KingdomInstitutionalXiaizationService.Reset();
            ActorDeathArchiveService.Reset();
            SuccessionRelationshipIndex.Reset();
            ReigningRoyalLineageIndex.Reset();
            AuthoritativeSuccessionService.Reset();
            SuccessionDisputePersistenceService.Reset();
        }

        private static void ProcessCycle(AWAuthorityCycleGate pGate,
            long pCycleToken, bool pPaused)
        {
            bool allowed = CanRunAuthorityCycle(pPaused);
            if (!pGate.TryEnter(pCycleToken, allowed)) return;

            for (CooperativeAuthorityStage stage =
                     CooperativeAuthorityStage.SuccessionRelationships;
                 stage < CooperativeAuthorityStage.Complete;
                 stage++)
                ExecuteStage(stage, pCycleToken, pPaused);
        }

        private static bool CanRunAuthorityCycle(bool pPaused)
        {
            return AWFrameSchedulerRules.ShouldRunAuthorityCycle(
                       Config.game_loaded, SmoothLoader.isLoading(), pPaused,
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AWWorldInitializationGate.IsPending();
        }

        private static void ResetCooperativeState()
        {
            _cooperativeActive = false;
            _cooperativeCycleToken = 0L;
            _cooperativeCyclePaused = false;
            _cooperativeStage = CooperativeAuthorityStage.Complete;
        }

        private static void ExecuteStage(CooperativeAuthorityStage pStage,
            long pCycleToken, bool pPaused)
        {
            // 隔离单个权限阶段：某阶段本周期抛异常时，只跳过它并记警告，
            // 不让异常冒泡到 RunNativeAuthorityAfterSimulation 而触发全局
            // 静默暂停（学派讲学、王国/外交/历史等阶段就在这条链上）。
            try
            {
                ExecuteStageCore(pStage, pCycleToken, pPaused);
            }
            catch (System.Exception error)
            {
                ModClass.LogWarning(
                    "AW authority stage '" + pStage +
                    "' faulted this cycle; skipped to avoid global pause: " +
                    error);
            }
        }

        private static void ExecuteStageCore(CooperativeAuthorityStage pStage,
            long pCycleToken, bool pPaused)
        {
            switch (pStage)
            {
                case CooperativeAuthorityStage.SuccessionRelationships:
                    SuccessionRelationshipIndex.ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.ReigningRoyalLineages:
                    ReigningRoyalLineageIndex.ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.SuccessionDisputePersistence:
                    SuccessionDisputePersistenceService.ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.WesternCourtElection:
                    WesternCourtElectionService.ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.AccessionInstallations:
                    AccessionIdentityService.ProcessDeferredInstallations();
                    break;
                case CooperativeAuthorityStage.ArmyRtsSuccessionRecovery:
                    if (AWPerformanceSettings.Mode !=
                        AWSimulationMode.Large)
                        ArmyRtsSuccessionRecoveryService.
                            ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.LocalizedNameMigration:
                    AWLocalizedNameMigrationService.ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.WesternLineageMigration:
                    WesternLineageMigrationService.ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.KingdomInstitutionalXiaization:
                    KingdomInstitutionalXiaizationService.
                        ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.DynasticMaleLineContinuity:
                    DynasticMaleLineContinuityService.ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.NobleHeirPregnancy:
                    NobleHeirPregnancyService.ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.RulerHouseholdPregnancy:
                    RulerHouseholdPregnancyService.ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.ArmyMembershipReconciliation:
                    ArmyMembershipReconciliationService.ProcessFrame();
                    break;
                case CooperativeAuthorityStage.EnclosedUnownedZoneRepair:
                    EnclosedUnownedZoneRepairService.ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.EmptyCityResettlement:
                    EmptyCityResettlementService.ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.TemporaryMilitaryReturn:
                    TemporaryMilitaryReturnService.ProcessFrame();
                    break;
                case CooperativeAuthorityStage.WarArmyReturn:
                    WarArmyReturnService.ProcessFrame();
                    break;
                case CooperativeAuthorityStage.ArmyRtsWarLifecycle:
                    if (AWPerformanceSettings.Mode !=
                        AWSimulationMode.Large)
                        ArmyRtsWarLifecycleService.ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.ArmyRtsAssignmentReconciliation:
                    if (AWPerformanceSettings.Mode !=
                        AWSimulationMode.Large)
                        ArmyRtsAssignmentReconciliationService.
                            ProcessAuthorityCycle();
                    break;
                case CooperativeAuthorityStage.Pathfinding:
                    Measure(RecentFeatureBenchmarkRules.PathfindingIndex,
                        AWPathfindingBootstrap.ProcessFrame);
                    break;
                case CooperativeAuthorityStage.ArmyRts:
                    break;
                case CooperativeAuthorityStage.Schools:
                    Measure(RecentFeatureBenchmarkRules.SchoolsIndex,
                        HistoricalSchoolRuntime.ProcessFrame);
                    break;
                case CooperativeAuthorityStage.CivilServiceExam:
                    Measure(RecentFeatureBenchmarkRules.
                            CivilServiceExamRuntimeIndex,
                        CivilServiceExamService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.DiplomacyProposal:
                    Measure(RecentFeatureBenchmarkRules.DiplomacyIndex,
                        DiplomacyProposalService.ProcessFrame);
                    break;
                case CooperativeAuthorityStage.DiplomaticOperation:
                    Measure(RecentFeatureBenchmarkRules.DiplomacyIndex,
                        DiplomaticOperationService.ProcessFrame);
                    break;
                case CooperativeAuthorityStage.ZhuluAge:
                    Measure(RecentFeatureBenchmarkRules.DiplomacyIndex,
                        ZhuluAgeDirectorService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.WarForceEliminationSettlement:
                    Measure(RecentFeatureBenchmarkRules.DiplomacyIndex,
                        WarForceEliminationSettlementService.
                            ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.KingdomDecisionMonthly:
                    Measure(RecentFeatureBenchmarkRules.
                            MonthKingdomPolicyIndex,
                        KingdomDecisionMonthlyService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.CityReservePool:
                    Measure(RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                        () =>
                        {
                            TemporaryLevyService.ProcessLegacyMigration();
                            CityReservePoolService.ProcessAuthorityCycle();
                        });
                    break;
                case CooperativeAuthorityStage.SyntheticMobilizationLedger:
                    Measure(RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                        SyntheticMobilizationLedgerService.
                            ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.ArmyReplenishment:
                    Measure(RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                        ArmyReplenishmentOperationService.
                            ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.ActorDeathArchive:
                    Measure(RecentFeatureBenchmarkRules.AsyncCommitIndex,
                        ActorDeathArchiveService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.AsyncMainThreadDrain:
                    Measure(RecentFeatureBenchmarkRules.AsyncCommitIndex,
                        () => AWAsyncRuntime.DrainMainThread(1.0, 32));
                    break;
                case CooperativeAuthorityStage.HistoricalWriteCompletions:
                    Measure(RecentFeatureBenchmarkRules.AsyncCommitIndex,
                        () => HistoricalWriteService.
                            DrainCompletions(0.5, 16));
                    break;
                case CooperativeAuthorityStage.WarParticipantSources:
                    Measure(RecentFeatureBenchmarkRules.AsyncCommitIndex,
                        FlushPendingWarParticipantSources);
                    break;
                case CooperativeAuthorityStage.DeferredAuthorityWork:
                    Measure(RecentFeatureBenchmarkRules.DeferredWorkIndex,
                        DrainDeferredAuthorityWork);
                    break;
                case CooperativeAuthorityStage.SlaveCaptureScan:
                    Measure(RecentFeatureBenchmarkRules.CaptureScanIndex,
                        SlaveCaptureScanService.DrainFrame);
                    break;
                case CooperativeAuthorityStage.Complete:
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(
                        nameof(pStage));
            }
        }

        private static void DrainDeferredAuthorityWork()
        {
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

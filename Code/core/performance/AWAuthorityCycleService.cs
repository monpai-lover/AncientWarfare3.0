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
        // Cooperative mode executes exactly one service per scheduler step.
        // Native mode still executes the whole list in one authority cycle.
        private enum CooperativeAuthorityStage
        {
            SuccessionRelationships,
            CourtElection,
            AccessionInstallations,
            ReigningRoyalLineages,
            SuccessionDisputePersistence,
            LocalizedNameMigration,
            WesternLineageMigration,
            InstitutionalXiaization,
            DynasticContinuity,
            NobleHeirPregnancy,
            HouseholdPregnancy,
            ArmyMembershipReconciliation,
            WarriorArmyMembership,
            UnownedZoneRepair,
            EmptyCityResettlement,
            TemporaryMilitaryReturn,
            WarArmyReturn,
            RtsAssignmentReconciliation,
            Pathfinding,
            ArmyRtsAuthority,
            Schools,
            CivilServiceExam,
            DiplomacyProposal,
            DiplomaticOperation,
            ZhuluAge,
            WarTerminalSettlement,
            SpecialGovernmentWarParticipation,
            KingdomDecisionMonthly,
            TemporaryLevyMigration,
            SyntheticMobilization,
            ArmyReplenishment,
            MandateMilitaryStrength,
            WarRefugee,
            ActorDeathArchive,
            WarParticipantSources,
            DeferredAuthorityWork,
            SlaveCaptureScan,
            Complete
        }

        private static readonly string[] CooperativePhaseNames =
        {
            "aw3.authority.succession_relationships",
            "aw3.authority.court_election",
            "aw3.authority.accession_installations",
            "aw3.authority.reigning_royal_lineages",
            "aw3.authority.succession_dispute_persistence",
            "aw3.authority.localized_name_migration",
            "aw3.authority.western_lineage_migration",
            "aw3.authority.institutional_xiaization",
            "aw3.authority.dynastic_continuity",
            "aw3.authority.noble_heir_pregnancy",
            "aw3.authority.household_pregnancy",
            "aw3.authority.army_membership_reconciliation",
            "aw3.authority.warrior_army_membership",
            "aw3.authority.unowned_zone_repair",
            "aw3.authority.empty_city_resettlement",
            "aw3.authority.temporary_military_return",
            "aw3.authority.war_army_return",
            "aw3.authority.rts_assignment_reconciliation",
            "aw3.authority.pathfinding",
            "aw3.authority.army_rts",
            "aw3.authority.schools",
            "aw3.authority.civil_service_exam",
            "aw3.authority.diplomacy_proposal",
            "aw3.authority.diplomatic_operation",
            "aw3.authority.zhulu_age",
            "aw3.authority.war_terminal_settlement",
            "aw3.authority.special_government_war_participation",
            "aw3.authority.kingdom_decision_monthly",
            "aw3.authority.temporary_levy_migration",
            "aw3.authority.synthetic_mobilization",
            "aw3.authority.army_replenishment",
            "aw3.authority.mandate_military_strength",
            "aw3.authority.war_refugee",
            "aw3.authority.actor_death_archive",
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
                _cooperativeStage = CooperativeAuthorityStage.SuccessionRelationships;
            }
            else if (_cooperativeCycleToken != pCycleToken)
            {
                // A pause/save/replica boundary may abandon the current
                // cursor. Restart the next token instead of throwing into
                // the vanilla simulation loop.
                ResetCooperativeState();
                CooperativeGate.Reset();
                bool allowed = CanRunAuthorityCycle(pCyclePaused);
                if (!CooperativeGate.TryEnter(pCycleToken, allowed))
                    return true;
                _cooperativeActive = true;
                _cooperativeCycleToken = pCycleToken;
                _cooperativeCyclePaused = pCyclePaused;
                _cooperativeStage = CooperativeAuthorityStage.SuccessionRelationships;
            }

            CooperativeAuthorityStage stage = _cooperativeStage;
            string phase = CooperativePhaseNames[(int)stage];
            long diagnostic = RuntimePerformanceDiagnostic.
                BeginContinuousScope();
            try
            {
                try
                {
                    ExecuteStage(stage, _cooperativeCycleToken,
                        _cooperativeCyclePaused);
                }
                catch (Exception error)
                {
                    // A single optional AW3 stage must not pause the whole
                    // world or strand the RTS/P0 owner.
                    ModClass.LogError("AW authority stage '" + phase +
                        "' failed and was skipped: " + error);
                }
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

        // Kept for compatibility with old callers. The cooperative runner
        // must use ProcessCooperativeStep so the stage cursor remains active.
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
            TemporaryMilitaryReturnService.ClearRuntime();
            WarArmyReturnService.ClearRuntime();
            ArmyRtsAssignmentReconciliationService.Reset();
            CityReservePoolService.ClearRuntime();
            SyntheticMobilizationLedgerService.ClearRuntime();
            WarForceEliminationSettlementService.ClearRuntime();
            WarTerminalSettlementCoordinator.ClearRuntime();
            ZhuluWarService.ClearRuntime();
            MandateMilitaryStrengthService.ClearRuntime();
            SpecialGovernmentWarParticipationService.ClearRuntime();
            ArmyReplenishmentOperationService.ClearRuntime();
            WarriorArmyMembershipService.ClearRuntime();
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
            CityLeaderVacancyRepairService.ClearRuntime();
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

        private static void ProcessCycle(AWAuthorityCycleGate pGate,
            long pCycleToken, bool pPaused)
        {
            if (!pGate.TryEnter(pCycleToken, CanRunAuthorityCycle(pPaused)))
                return;

            for (CooperativeAuthorityStage stage =
                     CooperativeAuthorityStage.SuccessionRelationships;
                 stage < CooperativeAuthorityStage.Complete; stage++)
                ExecuteStage(stage, pCycleToken, pPaused);
        }

        private static void ExecuteStage(CooperativeAuthorityStage pStage,
            long pCycleToken, bool pPaused)
        {
            switch (pStage)
            {
                case CooperativeAuthorityStage.SuccessionRelationships:
                    MeasureAuthority("succession_relationships",
                        SuccessionRelationshipIndex.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.CourtElection:
                    MeasureAuthority("court_election",
                        WesternCourtElectionService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.AccessionInstallations:
                    MeasureAuthority("accession_installations",
                        AccessionIdentityService.ProcessDeferredInstallations);
                    break;
                case CooperativeAuthorityStage.ReigningRoyalLineages:
                    MeasureAuthority("royal_lineage_index",
                        ReigningRoyalLineageIndex.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.SuccessionDisputePersistence:
                    MeasureAuthority("succession_dispute_persistence",
                        SuccessionDisputePersistenceService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.LocalizedNameMigration:
                    MeasureAuthority("localized_name_migration",
                        AWLocalizedNameMigrationService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.WesternLineageMigration:
                    MeasureAuthority("western_lineage_migration",
                        WesternLineageMigrationService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.InstitutionalXiaization:
                    MeasureAuthority("institutional_xiaization",
                        KingdomInstitutionalXiaizationService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.DynasticContinuity:
                    MeasureAuthority("dynastic_continuity",
                        DynasticMaleLineContinuityService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.NobleHeirPregnancy:
                    MeasureAuthority("noble_heir_pregnancy",
                        NobleHeirPregnancyService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.HouseholdPregnancy:
                    MeasureAuthority("household_pregnancy",
                        RulerHouseholdPregnancyService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.ArmyMembershipReconciliation:
                    MeasureAuthority("army_membership_reconciliation",
                        ArmyMembershipReconciliationService.ProcessFrame);
                    break;
                case CooperativeAuthorityStage.WarriorArmyMembership:
                    MeasureAuthority("warrior_army_membership",
                        WarriorArmyMembershipService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.UnownedZoneRepair:
                    MeasureAuthority("unowned_zone_repair",
                        EnclosedUnownedZoneRepairService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.EmptyCityResettlement:
                    MeasureAuthority("empty_city_resettlement",
                        EmptyCityResettlementService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.TemporaryMilitaryReturn:
                    MeasureAuthority("temporary_military_return",
                        TemporaryMilitaryReturnService.ProcessFrame);
                    break;
                case CooperativeAuthorityStage.WarArmyReturn:
                    MeasureAuthority("war_army_return",
                        WarArmyReturnService.ProcessFrame);
                    break;
                case CooperativeAuthorityStage.RtsAssignmentReconciliation:
                    MeasureAuthority("rts_assignment_reconciliation",
                        ArmyRtsAssignmentReconciliationService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.Pathfinding:
                    Measure(RecentFeatureBenchmarkRules.PathfindingIndex,
                        AWPathfindingBootstrap.ProcessFrame);
                    break;
                case CooperativeAuthorityStage.ArmyRtsAuthority:
                    MeasureAuthority("army_rts_authority", () =>
                        ArmyRtsSchedulingService.ProcessAw3Authority(
                            pCycleToken, pPaused));
                    break;
                case CooperativeAuthorityStage.Schools:
                    Measure(RecentFeatureBenchmarkRules.SchoolsIndex,
                        HistoricalSchoolRuntime.ProcessFrame);
                    break;
                case CooperativeAuthorityStage.CivilServiceExam:
                    Measure(RecentFeatureBenchmarkRules.CivilServiceExamRuntimeIndex,
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
                case CooperativeAuthorityStage.WarTerminalSettlement:
                    Measure(RecentFeatureBenchmarkRules.DiplomacyIndex,
                        WarTerminalSettlementCoordinator.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.SpecialGovernmentWarParticipation:
                    Measure(RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                        SpecialGovernmentWarParticipationService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.KingdomDecisionMonthly:
                    Measure(RecentFeatureBenchmarkRules.MonthKingdomPolicyIndex,
                        KingdomDecisionMonthlyService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.TemporaryLevyMigration:
                    MeasureAuthority("temporary_levy_migration",
                        TemporaryLevyService.ProcessLegacyMigration);
                    break;
                case CooperativeAuthorityStage.SyntheticMobilization:
                    MeasureAuthority("synthetic_mobilization",
                        SyntheticMobilizationLedgerService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.ArmyReplenishment:
                    Measure(RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                        ArmyReplenishmentOperationService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.MandateMilitaryStrength:
                    MeasureAuthority("mandate_military_strength",
                        MandateMilitaryStrengthService.ProcessAuthorityCycle);
                    break;
                case CooperativeAuthorityStage.WarRefugee:
                    MeasureAuthority("war_refugee", () =>
                        WarRefugeeService.ProcessAuthorityCycle(
                            pCycleToken, pPaused));
                    break;
                case CooperativeAuthorityStage.ActorDeathArchive:
                    Measure(RecentFeatureBenchmarkRules.AsyncCommitIndex,
                        DrainAuthorityCompletions);
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
                    throw new ArgumentOutOfRangeException(nameof(pStage));
            }
        }

        public static void AbortCooperativeCycle()
        {
            ResetCooperativeState();
            CooperativeGate.Reset();
        }

        private static void DrainAuthorityCompletions()
        {
            // Async results are applied once per render frame by the
            // presentation patch. Replaying them in every large-step pass
            // would multiply callback cost without changing state.
            ActorDeathArchiveService.ProcessAuthorityCycle();
        }

        private static void DrainDeferredAuthorityWork()
        {
            PeasantRebelBanditStrongholdPopulationService.
                ProcessAuthorityCycle();
            BanditStrongholdCityDisposalService.ProcessAuthorityCycle();
            int pending = DeferredRuntimeWorkService.PendingCount;
            int itemLimit = DeferredRuntimeWorkRules.
                ResolveItemsPerAuthorityFrame(pending);
            if (itemLimit <= 0) return;
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

        private static void Measure(int pIndex, Action pAction)
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

        private static void MeasureAuthority(string pId, Action pAction)
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

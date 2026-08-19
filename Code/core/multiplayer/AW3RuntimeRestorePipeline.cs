using System;
using System.Collections.Generic;
#if !AW3_RULES_TESTS
using AncientWarfare3.content;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.naming;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui.windows;
#endif

namespace AncientWarfare3.core.multiplayer
{
    internal sealed class AW3RestoreStage
    {
        internal AW3RestoreStage(string name, Action execute,
            bool stopOnFailure = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Restore stage name is required.",
                    nameof(name));

            Name = name;
            Execute = execute ?? throw new ArgumentNullException(nameof(execute));
            StopOnFailure = stopOnFailure;
        }

        internal string Name { get; }

        internal Action Execute { get; }

        internal bool StopOnFailure { get; }
    }

    internal sealed class AW3RestoreResult
    {
        private AW3RestoreResult(bool success, string failedStage,
            string detail)
        {
            Success = success;
            FailedStage = failedStage ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        internal bool Success { get; }

        internal string FailedStage { get; }

        internal string Detail { get; }

        internal static AW3RestoreResult Succeeded()
        {
            return new AW3RestoreResult(true, string.Empty, string.Empty);
        }

        internal static AW3RestoreResult Failed(string failedStage,
            string detail)
        {
            return new AW3RestoreResult(false, failedStage, detail);
        }
    }

    internal enum AW3ArchiveRecoveryAction
    {
        Fail,
        InitializeFresh
    }

    internal static class AW3ArchiveRestoreRules
    {
        internal static AW3ArchiveRecoveryAction Resolve(bool strict,
            bool archiveMissing)
        {
            return !strict && archiveMissing
                ? AW3ArchiveRecoveryAction.InitializeFresh
                : AW3ArchiveRecoveryAction.Fail;
        }
    }

    internal static class AW3RuntimeRestoreStageRunner
    {
        internal static AW3RestoreResult Run(
            IEnumerable<AW3RestoreStage> stages, bool strict)
        {
            if (stages == null) throw new ArgumentNullException(nameof(stages));

            AW3RestoreResult firstFailure = null;
            foreach (AW3RestoreStage stage in stages)
            {
                if (stage == null)
                    throw new ArgumentException(
                        "Restore stages cannot contain null entries.",
                        nameof(stages));

                try
                {
                    stage.Execute();
                }
                catch (Exception error)
                {
                    firstFailure ??= AW3RestoreResult.Failed(stage.Name,
                        error.Message);
                    if (strict || stage.StopOnFailure) return firstFailure;
                }
            }

            return firstFailure ?? AW3RestoreResult.Succeeded();
        }
    }

#if !AW3_RULES_TESTS
    internal static class AW3RuntimeRestorePipeline
    {
        internal static AW3RestoreResult TryRestoreFromDirectory(
            string directory, bool strict)
        {
            CityTechService.ClearRuntime();
            LineageArchiveManager archive = LineageArchiveManager.Instance;
            if (!archive.TryLoadFromSaveDirectory(directory,
                    out string archiveError))
            {
                bool archiveMissing = LineageArchiveManager
                    .IsMissingArchiveError(archiveError);
                if (AW3ArchiveRestoreRules.Resolve(strict, archiveMissing) !=
                    AW3ArchiveRecoveryAction.InitializeFresh)
                {
                    archive.DisableRuntimeArchive();
                    return AW3RestoreResult.Failed("archive", archiveError);
                }

                archive.CreateDataBase();
                if (!archive.IsOperational)
                    return AW3RestoreResult.Failed("archive_create",
                        "Fresh lineage archive initialization failed.");
                ModClass.LogWarning(
                    "AW3 save has no lineage archive; initialized a fresh archive.");
            }

            return TryRebuildAfterReplicationInstall(strict, directory);
        }

        internal static AW3RestoreResult TryRebuildAfterReplicationInstall(
            bool strict, string directory = null)
        {
            var stages = new List<AW3RestoreStage>
            {
                new AW3RestoreStage("mandate_projection",
                    MandateService.RebuildRuntimeMarkerProjection),
                new AW3RestoreStage("mandate_projection_resume", () =>
                    MandateService.ResumePendingProjections(2)),
                new AW3RestoreStage("bandit_strongholds",
                    PeasantRebelBanditStrongholdService.RestoreRuntime),
                new AW3RestoreStage("peasant_rebel_routes",
                    PeasantRebelRouteService.RebuildRuntime),
                new AW3RestoreStage("bandit_great_uprising",
                    BanditGreatUprisingService.RebuildRuntime),
                new AW3RestoreStage("world_traits", () =>
                    XiaSubspeciesRepair.EnsureWorldTraits()),
                new AW3RestoreStage("figure_state", FigureStateStore.Load),
                new AW3RestoreStage("kingdom_archive",
                    KingdomArchiveWriter.BackfillAll),
                new AW3RestoreStage("vassal_projection",
                    VassalService.RebuildRuntimeProjections),
                new AW3RestoreStage("ruler_cache",
                    RulerAppellationService.RebuildLivingCache),
                new AW3RestoreStage("year_names",
                    YearNameService.RebuildCommittedProjections),
                new AW3RestoreStage("localized_name_projection",
                    AWLocalizedNameMigrationService.
                        RebuildVisibleProjections),
                new AW3RestoreStage("lineage_family_archive_migration",
                    LineageFamilyArchiveMigrationService.Run,
                    stopOnFailure: true),
                new AW3RestoreStage("western_lineage_migration", () =>
                    WesternLineageMigrationService.Request()),
                new AW3RestoreStage("runtime_cache_reset", () =>
                {
                    AW3RestoreResult reset = ResetRuntimeCaches(strict);
                    if (!reset.Success)
                        throw new InvalidOperationException(
                            reset.FailedStage + ": " + reset.Detail);
                }),
                new AW3RestoreStage("war_refugees",
                    WarRefugeeService.RebuildRuntime),
                new AW3RestoreStage("western_court_office_migration", () =>
                    OfficialCareerPersistence.MigrateWesternOfficeIds(
                        LineageArchiveManager.Instance?.OperatingDB)),
                new AW3RestoreStage("official_career_repair", () =>
                    OfficialCareerPersistence.
                        RepairDuplicateFormalAppointments(
                            LineageArchiveManager.Instance?.OperatingDB)),
                new AW3RestoreStage("official_career_projection",
                    CourtService.RebuildOfficialCareerRuntimeProjections),
                new AW3RestoreStage("city_tech_zone_cache",
                    CityTechService.RebuildZoneExpansionCache),
                new AW3RestoreStage("feudatories",
                    FeudatoryService.LoadActiveCache),
                new AW3RestoreStage("mandate_phase",
                    MandatePhaseService.RebuildRuntime),
                new AW3RestoreStage("zhulu_age_director",
                    ZhuluAgeDirectorService.RebuildRuntime),
                new AW3RestoreStage("royal_asylum",
                    RoyalAsylumService.LoadRuntimeState),
                new AW3RestoreStage("school_runtime",
                    HistoricalSchoolRuntime.LoadState),
                new AW3RestoreStage("civil_service_exam_sessions",
                    CivilServiceExamService.RebuildRuntime),
                new AW3RestoreStage("civil_service_qualifications",
                    CivilServiceQualificationService.RebuildRuntimeProjections),
                new AW3RestoreStage("zhulu_native_war_migration",
                    ZhuluWarMigrationService.RebuildRuntime),
                new AW3RestoreStage("special_armies",
                    AWArmyService.RepairSpecialArmiesAfterLoad),
                new AW3RestoreStage("army_strategic_index",
                    ArmyStrategicIndexService.RebuildRuntime),
                new AW3RestoreStage("coalition_war_tasks",
                    CoalitionWarTaskService.RebuildRuntime),
                new AW3RestoreStage("war_military_facts",
                    WarMilitaryFactsService.RebuildRuntime),
                new AW3RestoreStage("kingdom_war_director",
                    KingdomWarDirectorService.RebuildRuntime),
                new AW3RestoreStage("army_rts_missions",
                    ArmyMissionPersistence.RebuildRuntime),
                new AW3RestoreStage("army_rts_logistics",
                    ArmyLogisticsService.RebuildRuntime),
                new AW3RestoreStage("army_rts_watchdog",
                    ArmyStallWatchdogService.RebuildRuntime),
                new AW3RestoreStage("army_rts_controllers",
                    ArmyRtsControllerService.RebuildRuntime),
                new AW3RestoreStage("army_rts_lifecycle_discovery",
                    ArmyRtsWarLifecycleService.RebuildDiscovery),
                new AW3RestoreStage("army_war_return",
                    WarArmyReturnService.RebuildRuntime),
                new AW3RestoreStage("military_readiness",
                    KingdomMilitaryReadinessService.RebuildRuntime),
                new AW3RestoreStage("military_emergency",
                    MilitaryEmergencyService.RebuildRuntime),
                new AW3RestoreStage("city_reserve_pools",
                    CityReservePoolService.ClearRuntime),
                new AW3RestoreStage("synthetic_mobilization_ledger", () =>
                {
                    bool restored = false;
                    string snapshotError = string.Empty;
                    if (!string.IsNullOrWhiteSpace(directory))
                        restored = SyntheticMobilizationLedgerService.
                            TryRestoreSnapshot(directory, out snapshotError);
                    if (!restored && !string.IsNullOrEmpty(snapshotError))
                        ModClass.LogWarning("Synthetic mobilization snapshot " +
                            "reconciliation fallback: " + snapshotError);
                }),
                new AW3RestoreStage("army_replenishment_operations",
                    ArmyReplenishmentOperationService.RebuildRuntime),
                new AW3RestoreStage("war_notices",
                    WarNoticeService.RebuildRuntime),
                new AW3RestoreStage("temporary_levies",
                    TemporaryLevyService.RebuildRuntime),
                new AW3RestoreStage("wartime_garrisons",
                    WartimeGarrisonService.RebuildRuntime),
                new AW3RestoreStage("slave_vanguard",
                    TemporarySlaveVanguardService.RebuildRuntime),
                new AW3RestoreStage("autonomous_restoration",
                    AutonomousRestorationService.RebuildRuntime),
                new AW3RestoreStage("succession_disputes",
                    SuccessionDisputeService.RebuildRuntime),
                new AW3RestoreStage("war_plot_redirect",
                    WarPlotRedirectService.SweepExistingPlots),
                new AW3RestoreStage("active_war_archive",
                    WarRecordWriter.BackfillActive),
                new AW3RestoreStage("military_governorates",
                    MilitaryGovernorateStore.EnqueueRuntimeRestore)
            };

            return AW3RuntimeRestoreStageRunner.Run(stages, strict);
        }

        internal static void RefreshCurrentWindows()
        {
            SchoolWindow.ResetWorldCache();
            SchoolRosterWindow.ResetWorldCache();
            HistoryListWindow.ResetWorldCache();
            KingdomRosterWindow.ResetWorldCache(pRefreshIfCurrent: true);
        }

        internal static AW3RestoreResult TryInitializeGeneratedWorld(
            bool strict)
        {
            CityTechService.ClearRuntime();
            var stages = new List<AW3RestoreStage>
            {
                new AW3RestoreStage("archive_create",
                    LineageArchiveManager.Instance.CreateDataBase),
                new AW3RestoreStage("mandate_projection",
                    MandateService.RebuildRuntimeMarkerProjection),
                new AW3RestoreStage("mandate_projection_resume", () =>
                    MandateService.ResumePendingProjections(2)),
                new AW3RestoreStage("bandit_strongholds",
                    PeasantRebelBanditStrongholdService.RestoreRuntime),
                new AW3RestoreStage("peasant_rebel_routes",
                    PeasantRebelRouteService.RebuildRuntime),
                new AW3RestoreStage("bandit_great_uprising",
                    BanditGreatUprisingService.RebuildRuntime),
                new AW3RestoreStage("war_plot_redirect",
                    WarPlotRedirectService.SweepExistingPlots),
                new AW3RestoreStage("world_traits", () =>
                    XiaSubspeciesRepair.EnsureWorldTraits()),
                new AW3RestoreStage("figure_state", FigureStateStore.Load),
                new AW3RestoreStage("runtime_cache_reset", () =>
                {
                    AW3RestoreResult reset = ResetRuntimeCaches(strict);
                    if (!reset.Success)
                        throw new InvalidOperationException(
                            reset.FailedStage + ": " + reset.Detail);
                }),
                new AW3RestoreStage("war_refugees",
                    WarRefugeeService.RebuildRuntime),
                new AW3RestoreStage("western_court_office_migration", () =>
                    OfficialCareerPersistence.MigrateWesternOfficeIds(
                        LineageArchiveManager.Instance?.OperatingDB)),
                new AW3RestoreStage("official_career_repair", () =>
                    OfficialCareerPersistence.
                        RepairDuplicateFormalAppointments(
                            LineageArchiveManager.Instance?.OperatingDB)),
                new AW3RestoreStage("official_career_projection",
                    CourtService.RebuildOfficialCareerRuntimeProjections),
                new AW3RestoreStage("city_tech_zone_cache",
                    CityTechService.RebuildZoneExpansionCache),
                new AW3RestoreStage("mandate_phase",
                    MandatePhaseService.RebuildRuntime),
                new AW3RestoreStage("zhulu_age_director",
                    ZhuluAgeDirectorService.RebuildRuntime),
                new AW3RestoreStage("royal_asylum",
                    RoyalAsylumService.LoadRuntimeState),
                new AW3RestoreStage("school_runtime",
                    HistoricalSchoolRuntime.LoadState),
                new AW3RestoreStage("civil_service_exam_sessions",
                    CivilServiceExamService.RebuildRuntime),
                new AW3RestoreStage("civil_service_qualifications",
                    CivilServiceQualificationService.RebuildRuntimeProjections),
                new AW3RestoreStage("succession_disputes",
                    SuccessionDisputeService.RebuildRuntime),
                new AW3RestoreStage("army_strategic_index",
                    ArmyStrategicIndexService.RebuildRuntime),
                new AW3RestoreStage("kingdom_war_director",
                    KingdomWarDirectorService.RebuildRuntime),
                new AW3RestoreStage("army_rts_missions",
                    ArmyMissionPersistence.RebuildRuntime),
                new AW3RestoreStage("army_rts_logistics",
                    ArmyLogisticsService.RebuildRuntime),
                new AW3RestoreStage("army_rts_watchdog",
                    ArmyStallWatchdogService.RebuildRuntime),
                new AW3RestoreStage("army_rts_controllers",
                    ArmyRtsControllerService.RebuildRuntime),
                new AW3RestoreStage("army_rts_lifecycle_discovery",
                    ArmyRtsWarLifecycleService.RebuildDiscovery),
                new AW3RestoreStage("army_war_return",
                    WarArmyReturnService.RebuildRuntime),
                new AW3RestoreStage("ruler_cache",
                    RulerAppellationService.RebuildLivingCache),
                new AW3RestoreStage("localized_name_projection",
                    AWLocalizedNameMigrationService.
                        RebuildVisibleProjections),
                new AW3RestoreStage("lineage_family_archive_migration",
                    LineageFamilyArchiveMigrationService.Run,
                    stopOnFailure: true),
                new AW3RestoreStage("western_lineage_migration", () =>
                    WesternLineageMigrationService.Request()),
                new AW3RestoreStage("military_governorates",
                    MilitaryGovernorateStore.EnqueueRuntimeRestore)
            };

            return AW3RuntimeRestoreStageRunner.Run(stages, strict);
        }

        private static AW3RestoreResult ResetRuntimeCaches(bool strict)
        {
            var stages = new List<AW3RestoreStage>
            {
                new AW3RestoreStage("god_power_runtime",
                    GodPowerLibrary.ClearRuntime),
                new AW3RestoreStage("deferred_runtime_work",
                    DeferredRuntimeWorkService.ClearRuntimeState),
                new AW3RestoreStage("kingdom_annual_work",
                    KingdomAnnualWorkService.ClearRuntimeState),
                new AW3RestoreStage("political_point_reservations",
                    PoliticalPointReservationService.Clear),
                new AW3RestoreStage("kingdom_policy",
                    KingdomPolicyService.ClearRuntime),
                new AW3RestoreStage("kingdom_policy_inheritance",
                    KingdomPolicyInheritanceService.ClearRuntime),
                new AW3RestoreStage("city_economy",
                    CityEconomyService.ClearRuntime),
                new AW3RestoreStage("city_tech", CityTechService.ClearRuntime),
                new AW3RestoreStage("virtual_titles", VirtualNobleTitleService.ClearRuntime),
                new AW3RestoreStage("court_aristocratic_groups",
                    CourtAristocraticGroupService.ClearRuntime),
                new AW3RestoreStage("court_peace",
                    CourtPeaceService.ClearRuntime),
                new AW3RestoreStage("conferred_posthumous_titles",
                    ConferredPosthumousTitleService.ClearRuntime),
                new AW3RestoreStage("diplomatic_operations",
                    DiplomaticOperationService.ResetRuntime),
                new AW3RestoreStage("diplomacy_proposals",
                    DiplomacyProposalService.ClearRuntime),
                new AW3RestoreStage("diplomatic_relation_modifiers",
                    DiplomaticRelationModifierService.ClearRuntime),
                new AW3RestoreStage("war_notices",
                    WarNoticeService.ClearRuntime),
                new AW3RestoreStage("military_emergency",
                    MilitaryEmergencyService.ClearRuntime),
                new AW3RestoreStage("city_reserve_pools",
                    CityReservePoolService.ClearRuntime),
                new AW3RestoreStage("synthetic_mobilization_ledger",
                    SyntheticMobilizationLedgerService.ClearRuntime),
                new AW3RestoreStage("army_replenishment_operations",
                    ArmyReplenishmentOperationService.ClearRuntime),
                new AW3RestoreStage("temporary_levies",
                    TemporaryLevyService.ClearRuntime),
                new AW3RestoreStage("wartime_garrisons",
                    WartimeGarrisonService.ClearRuntime),
                new AW3RestoreStage("slave_vanguard",
                    TemporarySlaveVanguardService.ClearRuntime),
                new AW3RestoreStage("autonomous_restoration",
                    AutonomousRestorationService.ClearRuntime),
                new AW3RestoreStage("royal_claims",
                    RoyalClaimService.ClearRuntime),
                new AW3RestoreStage("succession_disputes",
                    SuccessionDisputeService.ClearRuntime),
                new AW3RestoreStage("feudatories",
                    FeudatoryService.ResetRuntime),
                new AW3RestoreStage("military_readiness",
                    KingdomMilitaryReadinessService.ClearRuntime),
                new AW3RestoreStage("city_occupation",
                    CityOccupationAccelerationService.ClearRuntime),
                new AW3RestoreStage("mandate_phase",
                    MandatePhaseService.ClearRuntime),
                new AW3RestoreStage("peasant_rebel_routes",
                    PeasantRebelRouteService.ClearRuntime),
                new AW3RestoreStage("bandit_great_uprising",
                    BanditGreatUprisingService.ClearRuntime),
                new AW3RestoreStage("army_retreat",
                    ArmyRetreatService.ClearRuntime),
                new AW3RestoreStage("army_strategic_index",
                    ArmyStrategicIndexService.ClearRuntime),
                new AW3RestoreStage("kingdom_war_director",
                    KingdomWarDirectorService.ClearRuntime),
                new AW3RestoreStage("army_rts_missions",
                    ArmyMissionPersistence.ClearRuntime),
                new AW3RestoreStage("army_rts_logistics",
                    ArmyLogisticsService.ClearRuntime),
                new AW3RestoreStage("army_rts_watchdog",
                    ArmyStallWatchdogService.ClearRuntime),
                new AW3RestoreStage("army_rts_controllers",
                    ArmyRtsControllerService.ClearRuntime),
                new AW3RestoreStage("army_rts_supply",
                    ArmyRtsAbstractSupplyService.ClearRuntime),
                new AW3RestoreStage("army_war_return",
                    WarArmyReturnService.ClearRuntime),
                new AW3RestoreStage("aw_armies",
                    AWArmyService.ClearRuntimeCaches),
                new AW3RestoreStage("army_cleanup",
                    ArmyInvalidCleanupQueue.ClearRuntime),
                new AW3RestoreStage("royal_asylum",
                    RoyalAsylumService.ClearRuntime),
                new AW3RestoreStage("ruler_appellations",
                    RulerAppellationService.ClearRuntime),
                new AW3RestoreStage("slave_capture_scan",
                    SlaveCaptureScanService.Clear),
                new AW3RestoreStage("royal_guard",
                    RoyalGuardService.ClearRuntimeCaches),
                new AW3RestoreStage("slave",
                    SlaveService.ClearRuntimeCaches),
                new AW3RestoreStage("slave_king_abdication",
                    SlaveKingAbdicationService.ClearRuntime),
                new AW3RestoreStage("war_records",
                    WarRecordWriter.ClearRuntime),
                new AW3RestoreStage("war_territory",
                    WarTerritoryService.ClearRuntime),
                new AW3RestoreStage("zhulu_age_director",
                    ZhuluAgeDirectorService.Reset),
                new AW3RestoreStage("xia_contacts",
                    XiaContactService.ClearRuntime),
                new AW3RestoreStage("school_map_bar",
                    SchoolMapBottomBarController.Hide),
                new AW3RestoreStage("map_mode_meta",
                    AWMapModeMetaLibrary.ClearRuntimeCaches),
                new AW3RestoreStage("school_window",
                    SchoolWindow.ResetWorldCache),
                new AW3RestoreStage("school_roster_window",
                    SchoolRosterWindow.ResetWorldCache),
                new AW3RestoreStage("historical_schools",
                    HistoricalSchoolRuntime.ClearRuntime),
                new AW3RestoreStage("history_window",
                    HistoryListWindow.ResetWorldCache),
                new AW3RestoreStage("family_tree_window",
                    FamilyTreeWindow.ResetWorldState),
                new AW3RestoreStage("kingdom_roster_window", () =>
                    KingdomRosterWindow.ResetWorldCache(
                        pRefreshIfCurrent: true))
            };

            return AW3RuntimeRestoreStageRunner.Run(stages, strict);
        }
    }
#endif
}

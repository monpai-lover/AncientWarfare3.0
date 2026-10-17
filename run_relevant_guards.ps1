$ErrorActionPreference = 'Stop'
# Historical selected checks. For the current full inventory use run_all_checks.ps1.
$guards = @(
  'ActorRuntimePerformanceSourceGuardTests','ArmyLifecycleRulesTests','ArmyMapInformationMinimapSourceGuardTests',
  'ArmyReplenishmentCompletionSourceGuardTests','ArmyReplenishmentOperationSourceGuardTests','ArmyRtsControllerPerformanceSourceGuard',
  'ArmyRtsSchedulingSourceGuardTests','ArmyRtsSourceGuardTests','ArmyRtsTransportRouteChoiceSourceGuard',
  'ArmyRtsTransportSourceGuardTests','ArmySharedCaptainPathSourceGuardTests','AsyncHistoricalReadSourceGuardTests',
  'AsyncPerformanceSourceGuardTests','CityArmyReinforcementSourceGuard','CityReserveRecruitmentSourceGuardTests',
  'CultiwayPerfSchedulerCompletionSourceGuard','DynastyAppellationProjectionSourceGuard',
  'FamilyTreeBranchProjectionSourceGuardTests','FamilyTreeDedicatedRevisionSourceGuardTests','FamilyTreeInlineBranchExpansionTests',
  'FamilyTreeMaterializationLifecycleTests','FamilyTreeStructureRefreshSourceGuardTests','FamilyTreeUiIntegration.Tests',
  'FamilyTreeWorldResetSourceGuardTests','KingdomWarDirectorPerformanceSourceGuard','KingdomWarDirectorTargetOrderingTests',
  'LocalArmyReplenishmentSourceGuardTests','MilitaryLifecycleSourceGuardTests','PathfindingModeSourceGuardTests',
  'ReplacementArmyCommandSourceGuardTests','RoyalGuardTaskPresentationSourceGuard','RtsOccupiedTargetHandoffSourceGuard',
  'RtsWartimeLifecycleSourceGuard','SchedulerIntegrationFixSourceGuardTests','WarRegressionTests',
  'WartimeZeroArmyRecoverySourceGuard','WesternBilateralFamilyTreeSourceGuard','WesternLineageAdmissionSourceGuard',
  'XiaExpansionAndCivMonkeyNamingTests','AWDockTransportDiagnosticSourceGuard',
  'AWTemporaryShoreTransportSourceGuard','ArmyRtsTransportRetrySourceGuard',
  'RtsPathRequestDeduplicationSourceGuard','FramePriorityRecoverySourceGuard',
  'MilitaryPriorityRefreshSourceGuard',
  'RtsTransportDiagnosticsSourceGuard'
  ,'PathFinderSubmitNewSourceGuard'
  ,'PathTransportRouteReuseSourceGuard'
  ,'PathFinderWorkerLoopSourceGuard'
  ,'SlaveStateAsyncPersistenceSourceGuard'
  ,'SlaveStateBatchPersistenceSourceGuard'
  ,'SchoolStandingPromotionAsyncSourceGuard'
  ,'CityLeaderVacancyBoundedScanSourceGuard'
  ,'CityLeaderVacancyScanBudgetSourceGuard'
  ,'CityBureauImmediateReconcileCaptureSourceGuard'
)
& (Join-Path $PSScriptRoot 'run_all_guards.ps1') -CheckNames $guards
exit $LASTEXITCODE

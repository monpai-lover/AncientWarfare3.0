$ErrorActionPreference = 'Continue'
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
$fail = @()
foreach ($g in $guards) {
  $p = Join-Path $PSScriptRoot "Tests\$g.ps1"
  if (-not (Test-Path $p)) { Write-Host "MISSING $g"; continue }
  try { $global:LASTEXITCODE = 0; & $p *> $null; if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "exit $LASTEXITCODE" }; }
  catch { $fail += ,@($g, $_.Exception.Message) }
}
Write-Host "=== RAN $($guards.Count) guards ==="
if ($fail.Count -eq 0) { Write-Host "ALL PASS" }
else { foreach ($f in $fail) { Write-Host "FAIL $($f[0]): $($f[1])" } }

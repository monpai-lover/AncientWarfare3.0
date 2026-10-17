$ErrorActionPreference = 'Stop'
# Historical selected checks. For the current full inventory use run_all_checks.ps1.
$guards = @(
 'ArmyCaptainCareerSourceGuardTests',
 'ArmyFollowerLocalPathFallbackTests',
 'ArmyLifecycleRulesTests',
 'ArmyReplenishmentOperationSourceGuardTests',
 'ArmyRtsAttackSpeechBubbleSourceGuard',
 'ArmyRtsDoctrineBoundarySourceGuard',
 'ArmyRtsPlanGifSourceGuard',
 'ArmyRtsSchedulingSourceGuardTests',
 'ArmyRtsSourceGuardTests',
 'ArmyRtsStateTaskPresentationSourceGuard',
 'ArmyRtsTransportRouteChoiceSourceGuard',
 'ArmyRtsTransportSourceGuardTests',
 'ArmyRtsVisualizationSourceGuardTests',
 'RtsOccupiedTargetHandoffSourceGuard',
 'CityArmyReinforcementSourceGuard',
 'CityReservePoolLifecycleSourceGuardTests',
 'CityReservePoolLoadGateSourceGuardTests',
 'CityReserveRecruitmentSourceGuardTests',
 'LocalArmyReplenishmentSourceGuardTests',
 'MilitaryLifecycleSourceGuardTests',
 'KingdomWarDirectorTargetOrderingTests'
)
& (Join-Path $PSScriptRoot 'run_all_guards.ps1') -CheckNames $guards
exit $LASTEXITCODE

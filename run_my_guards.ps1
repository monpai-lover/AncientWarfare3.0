$ErrorActionPreference = 'Continue'
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
foreach ($g in $guards) {
  $p = Join-Path $PSScriptRoot "Tests\$g.ps1"
  if (-not (Test-Path $p)) { Write-Host "MISSING $g"; continue }
  $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $p 2>&1
  $txt = ($out | Out-String)
  $line = ($out | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | Select-Object -First 1)
  if ($LASTEXITCODE -eq 0) { Write-Host "PASS  $g" }
  else {
    $m = if ($line) { $line.Exception.Message } else { ($txt -split "`n" | Select-Object -Last 1) }
    Write-Host "FAIL  $g :: $m"
  }
}

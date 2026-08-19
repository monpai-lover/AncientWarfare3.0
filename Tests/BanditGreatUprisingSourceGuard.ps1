$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw (Join-Path $root 'Code/core/lineage/BanditGreatUprisingService.cs')
$annual = Get-Content -Raw (Join-Path $root 'Code/core/policy/KingdomAnnualWorkService.cs')

foreach ($required in @(
        'MANDATE_REBEL_GREAT_UPRISING_LAST_YEAR',
        'RebuildIndexIfNeeded',
        'ConversionBudgetPerYear',
        'PeasantRebelRouteService.ConvertBanditToFounding',
        'AW3MultiplayerReplicaScope.IsReplicaSession',
        'AW3MultiplayerReplicaScope.IsApplying',
        'BanditGreatUprisingService.OnKingdomYear(pKingdom)')) {
    if (-not ($service + "`n" + $annual).Contains($required)) {
        throw "Bandit great uprising integration is missing: $required"
    }
}

if ($service.Contains('World.world.kingdoms.removeObject') -or
    $service.Contains('World.world?.kingdoms?.removeObject')) {
    throw 'Bandit great uprising coordinator must not remove kingdoms directly.'
}

if ($service.Contains('UpdateAge') -or $service.Contains('Update')) {
    throw 'Bandit great uprising coordinator must remain annual, not frame-driven.'
}

$convertCount = ([regex]::Matches(
    $service, 'PeasantRebelRouteService\.ConvertBanditToFounding')).Count
if ($convertCount -ne 1) {
    throw 'Bandit conversion must have one bounded route-service call site.'
}

Write-Host 'Bandit great uprising source guard passed.'

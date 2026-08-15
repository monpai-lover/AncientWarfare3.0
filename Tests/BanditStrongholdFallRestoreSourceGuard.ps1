$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdService.cs'
$occupationPath = Join-Path $root 'Code/patch/AW_CityOccupationAccelerationPatch.cs'
$restorePath = Join-Path $root 'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs'
$modelsPath = Join-Path $root 'Code/api/multiplayer/AW3MultiplayerStrategicStateModels.cs'
$strategicPath = Join-Path $root 'Code/core/multiplayer/AW3MultiplayerStrategicStateCoordinator.cs'

$service = Get-Content -Raw -Encoding UTF8 $servicePath
$occupation = Get-Content -Raw -Encoding UTF8 $occupationPath
$restore = Get-Content -Raw -Encoding UTF8 $restorePath
$models = Get-Content -Raw -Encoding UTF8 $modelsPath
$strategic = Get-Content -Raw -Encoding UTF8 $strategicPath

foreach ($token in @('TryHandleCapture(', 'BanditStrongholdPhase.Falling',
        'BanditStrongholdPhase.Completed', 'joinCity(', 'addZone(',
        'recalculateNeighbourZones', 'World.world.cities.removeObject')) {
    if ($service -notmatch [regex]::Escape($token)) {
        throw "Stronghold fall is missing $token"
    }
}
$fallingIndex = $service.IndexOf('BanditStrongholdPhase.Falling')
$removeIndex = $service.IndexOf('World.world.cities.removeObject')
if ($fallingIndex -lt 0 -or $removeIndex -lt 0 -or
    $fallingIndex -gt $removeIndex) {
    throw 'Falling phase must be persisted before native city removal'
}

$finishIndex = $occupation.IndexOf('public static bool FinishCapture_Prefix')
$acquireIndex = $occupation.IndexOf(
    'PeasantRebelRouteService.CanAcquireCity', $finishIndex)
$strongholdIndex = $occupation.IndexOf(
    'PeasantRebelBanditStrongholdService.TryHandleCapture', $finishIndex)
if ($strongholdIndex -lt 0 -or $strongholdIndex -gt $acquireIndex) {
    throw 'Stronghold fall must intercept before ordinary acquisition checks'
}

foreach ($token in @('PeasantRebelBanditStrongholdService.RestoreRuntime',
        '"bandit_strongholds"')) {
    if ($restore -notmatch [regex]::Escape($token)) {
        throw "Runtime restore is missing $token"
    }
}
if ($service -match 'RestoreRuntime[\s\S]{0,3000}TryCreate\(') {
    throw 'Runtime restore must not replay stronghold creation'
}
foreach ($token in @('AW3MultiplayerBanditStrongholdProjection',
        'BanditStrongholds', 'StateJson')) {
    if (($models + $strategic) -notmatch [regex]::Escape($token)) {
        throw "Strategic stronghold replication is missing $token"
    }
}
$applyStart = $strategic.IndexOf('public void ApplyBanditStronghold(')
if ($applyStart -lt 0) {
    throw 'Replica store has no bandit stronghold apply method'
}
$applyBody = $strategic.Substring($applyStart,
    [Math]::Min(1500, $strategic.Length - $applyStart))
if ($applyBody -match 'TryCreate\(|TryHandleCapture\(|RestoreRuntime\(') {
    throw 'Replica stronghold apply invokes authoritative lifecycle work'
}

Write-Output 'Bandit stronghold fall and restore source guard passed.'

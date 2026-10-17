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
$disposal = Get-Content -Raw -Encoding UTF8 (Join-Path $root `
    'Code/core/lineage/BanditStrongholdCityDisposalService.cs')

foreach ($token in @('TryHandleCapture(', 'BanditStrongholdPhase.Falling',
        'BanditStrongholdPhase.Completed', 'joinCity(', 'addZone(',
        'recalculateNeighbourZones', 'BanditStrongholdCityDisposalService.Schedule(')) {
    if ($service -notmatch [regex]::Escape($token)) {
        throw "Stronghold fall is missing $token"
    }
}
$fallStart = $service.IndexOf('private static bool CompleteFall(')
$fallingIndex = $service.IndexOf('pState.Phase = BanditStrongholdPhase.Falling', $fallStart)
$fallWriteIndex = $service.IndexOf('PeasantRebelBanditStateStore.Write(', $fallingIndex)
$transferIndex = $service.IndexOf('actor.joinCity(', $fallStart)
$completedIndex = $service.IndexOf('pState.Phase = BanditStrongholdPhase.Completed', $fallStart)
$completedWriteIndex = $service.IndexOf('PeasantRebelBanditStateStore.Write(', $completedIndex)
$removeIndex = $service.IndexOf('BanditStrongholdCityDisposalService.Schedule(', $fallStart)
if ($fallStart -lt 0 -or $fallingIndex -lt 0 -or
    $fallWriteIndex -le $fallingIndex -or $transferIndex -le $fallWriteIndex -or
    $completedIndex -le $transferIndex -or $completedWriteIndex -le $completedIndex -or
    $removeIndex -le $completedWriteIndex) {
    throw 'Fall must persist Falling before transfers and Completed before scheduling disposal'
}
if ($service.Contains('World.world.cities.removeObject')) {
    throw 'Stronghold lifecycle must not remove cities during native collection mutation'
}
foreach ($token in @('CityManagerMutationScope.IsCityUpdateActive',
        'CanDisposeStrongholdCity(', 'World.world.cities.removeObject(city)',
        'AW3MultiplayerReplicaScope.IsReplicaSession',
        'AW3MultiplayerReplicaScope.IsApplying')) {
    if (-not $disposal.Contains($token)) {
        throw "Deferred native city disposal is missing $token"
    }
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

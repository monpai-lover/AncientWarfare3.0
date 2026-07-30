$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot

function Read-Source([string]$relativePath) {
    $path = Join-Path $repo $relativePath
    if (-not [IO.File]::Exists($path)) {
        throw "Missing source file: $relativePath"
    }
    return [IO.File]::ReadAllText($path)
}

function Require([string]$source, [string]$needle, [string]$message) {
    if (-not $source.Contains($needle)) { throw $message }
}

function Forbid([string]$source, [string]$needle, [string]$message) {
    if ($source.Contains($needle)) { throw $message }
}

$service = Read-Source `
    'Code/core/lineage/RebellionDirectTerritoryTransferService.cs'
$patch = Read-Source 'Code/patch/AW_CityOccupationAccelerationPatch.cs'
$bridge = Read-Source `
    'Code/core/lineage/WarScoreRebellionDirectTransferBridge.cs'

Require $service 'foreach (War war in pCapturer.getWars())' `
    'direct rebellion resolution must inspect only the capturer active-war index'
Require $service 'war.getAsset()?.rebellion == true' `
    'direct transfer must use the authoritative rebellion asset flag'
Require $service 'war.isInWarWith(owner, pCapturer)' `
    'the exact war must place current owner and capturer on opposing sides'
Forbid $service 'foreach (War war in World.world.wars)' `
    'direct transfer must not scan the global war collection'
Forbid $service '.endWar(' `
    'direct transfer must leave war completion to dedicated rebellion services'

$finishStart = $patch.IndexOf('public static bool FinishCapture_Prefix(')
$finishEnd = $patch.IndexOf('[HarmonyPostfix]', $finishStart)
if ($finishStart -lt 0 -or $finishEnd -le $finishStart) {
    throw 'Cannot isolate FinishCapture_Prefix.'
}
$finish = $patch.Substring($finishStart, $finishEnd - $finishStart)
$directIndex = $finish.IndexOf(
    'RebellionDirectTerritoryTransferService.TryResolve(')
$redirectIndex = $finish.IndexOf(
    'VassalCaptureService.ResolveCaptureRecipient(')
$freezeIndex = $finish.IndexOf('WarScoreService.TryFreezeCityOccupation(')
if ($directIndex -lt 0 -or $redirectIndex -lt 0 -or $freezeIndex -lt 0 -or
    $directIndex -ge $redirectIndex -or $directIndex -ge $freezeIndex) {
    throw 'direct rebellion transfer must branch before redirection and freezing'
}
Require $finish 'RebellionDirectCaptureState' `
    'finishCapture must carry exact transfer authority through Harmony state'
Forbid $patch 'static RebellionDirectCaptureState' `
    'capture authority must not leak through process-wide static state'

$joinStart = $patch.IndexOf('public static void JoinCapturedCity_Prefix(')
if ($joinStart -lt 0) { throw 'Cannot isolate JoinCapturedCity_Prefix.' }
$join = $patch.Substring($joinStart)
$joinDirect = $join.IndexOf(
    'RebellionDirectTerritoryTransferService.TryResolve(')
$joinRedirect = $join.IndexOf(
    'VassalCaptureService.ResolveCaptureRecipient(')
if ($joinDirect -lt 0 -or $joinRedirect -lt 0 -or
    $joinDirect -ge $joinRedirect) {
    throw 'nested captured-city join must bypass suzerain redirection first'
}

Require $patch 'WarScoreService.ClearDirectRebellionTransferState(' `
    'committed direct transfer must clear only matching stale frozen state'
Require $bridge 'PendingCityOccupations.Remove(pCityId)' `
    'direct transfer must clear a stale pending row for the captured city'
Require $bridge 'runtime.TryGetFrozenCityControl(pWarId, pCityId' `
    'direct cleanup must read only the matching war and city row'
Require $bridge 'ClearGoalControlForCity(runtime, state, pCityId' `
    'direct cleanup must use the existing scoped war-score cleanup path'

Write-Output 'Rebellion direct territory transfer source guards passed.'

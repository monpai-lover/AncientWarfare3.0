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
$peaceRuntime = Read-Source `
    'Code/core/lineage/WarPeaceSettlementRuntime.cs'
$peaceService = Read-Source `
    'Code/core/lineage/WarPeaceSettlementService.cs'
$diplomacy = Read-Source `
    'Code/core/lineage/DiplomacyProposalService.cs'
$decisive = Read-Source `
    'Code/core/lineage/WarScoreDecisiveSettlementService.cs'
$goals = Read-Source `
    'Code/core/lineage/WarGoalSettlementRuntimeService.cs'
$exhaustion = Read-Source `
    'Code/core/lineage/WarExhaustionSettlementRuntimeService.cs'
$controller = Read-Source `
    'Code/ui/windows/WarPeaceNegotiationController.cs'
$conversation = Read-Source `
    'Code/ui/windows/DiplomacyConversationWindow.cs'
$locale = Read-Source 'locales/aw3_diplomacy.csv'

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

Require $peaceRuntime 'BlocksOrdinarySettlement(war)' `
    'authoritative settlement context must reject direct-transfer rebellions'
Require $peaceRuntime 'SettlementBlockedReason' `
    'authoritative settlement rejection must use the stable reason'
Require $peaceService 'SettlementBlockedReason' `
    'settlement recovery must recognize stale rebellion proposals'
Require $peaceService 'Cancel(proposal.DetailId,' `
    'pending or accepted stale rebellion proposals must be cancelled'
Require $diplomacy `
    'pAuthoritativeRebellion: authoritativeRebellion' `
    'AI protected-war selection must use the authoritative asset flag'
Require $decisive 'BlocksOrdinarySettlement(pWar)' `
    'decisive score must not force an ordinary rebellion treaty'
Require $goals 'BlocksOrdinarySettlement(pWar)' `
    'war goals must not force an ordinary rebellion treaty'
Require $exhaustion 'BlocksOrdinarySettlement(pWar)' `
    'exhaustion must not force an ordinary rebellion treaty'
Require $controller 'BlocksOrdinarySettlement(war)' `
    'the player negotiation window must remain closed for rebellions'
Require $conversation 'rebellion_uses_direct_territory_transfer' `
    'the stable rebellion rejection reason must have player feedback'
Require $locale 'aw_diplomacy_failure_rebellion_direct_transfer,' `
    'direct rebellion transfer feedback must be localized'

Write-Output 'Rebellion direct territory transfer source guards passed.'

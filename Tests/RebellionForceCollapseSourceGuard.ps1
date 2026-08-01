$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    $path = Join-Path $root $relativePath
    if (-not [IO.File]::Exists($path)) {
        $failures.Add("missing source file: $relativePath")
        return ''
    }
    return [IO.File]::ReadAllText($path)
}

function Require-Text([string]$source, [string]$needle,
    [string]$label) {
    if (-not $source.Contains($needle)) {
        $failures.Add("${label}: missing '$needle'")
    }
}

$reserve = Read-Source 'Code/core/lineage/CityReservePoolService.cs'
foreach ($required in @(
        'internal static void RefreshCapturedCity(City city)',
        'Kingdom kingdom = city?.kingdom;',
        'KingdomPoolState state = State(kingdom);',
        'CityPool pool = Pool(state, city.id);',
        'CityReservePoolRules.FullReconciliationBudget(',
        'city.units?.Count ?? 0, pool.ActorIds.Count)',
        'allowFrozenAddition: true')) {
    Require-Text $reserve $required "captured-city reserve refresh"
}

$settlement = Read-Source `
    'Code/core/lineage/RebellionCollapseSettlementService.cs'
foreach ($required in @(
        'internal static class RebellionCollapseSettlementService',
        'AW3MultiplayerReplicaScope.IsReplicaSession',
        'DeferredRuntimeWorkService.EnqueueCoalesced(',
        'DeferredWorkClass.Runtime',
        'WarPeaceSettlementWorld.FindWar(pWarId)',
        'war.getMainAttacker()',
        'war.isAttacker(rebel)',
        'war.countAttackersWarriors()',
        'CountAvailable(rebel);',
        'RebellionForceCollapseRules.ShouldCollapse(',
        'World.world?.wars?.endWar(war, WarWinner.Defenders)')) {
    Require-Text $settlement $required "authority rebellion collapse"
}

$bridge = Read-Source 'Code/core/lineage/WarScoreRuntimeBridge.cs'
Require-Text $bridge `
    'RebellionCollapseSettlementService.QueueIfCollapsed(pWar);' `
    'combat settlement boundary'

$capture = Read-Source 'Code/patch/AW_CityOccupationAccelerationPatch.cs'
foreach ($required in @(
        'WarPeaceSettlementWorld.FindWar(__state.WarId)',
        'RebellionDirectTerritoryTransferService.',
        'BlocksOrdinarySettlement(war)',
        'CityReservePoolService.RefreshCapturedCity(__instance);',
        'RebellionCollapseSettlementService.QueueIfCollapsed(war);')) {
    Require-Text $capture $required "direct rebellion capture integration"
}
$clearIndex = $capture.IndexOf(
    'WarScoreService.ClearDirectRebellionTransferState(')
$refreshIndex = $capture.IndexOf(
    'CityReservePoolService.RefreshCapturedCity(__instance);')
$collapseIndex = $capture.IndexOf(
    'RebellionCollapseSettlementService.QueueIfCollapsed(war);')
if ($clearIndex -lt 0 -or $refreshIndex -le $clearIndex -or
    $collapseIndex -le $refreshIndex) {
    $failures.Add('direct rebellion capture must clear state, refresh ' +
        'the reserve pool, then queue collapse in that order')
}

if ($failures.Count -gt 0) {
    throw "Rebellion force-collapse source guard failures:`n - " +
        ($failures -join "`n - ")
}

Write-Output 'Rebellion force-collapse source guards passed.'

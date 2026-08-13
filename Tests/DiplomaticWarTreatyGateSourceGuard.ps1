$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proposal = Get-Content -Raw -LiteralPath `
    (Join-Path $root 'Code/core/lineage/DiplomacyProposalService.cs')
$declaration = Get-Content -Raw -LiteralPath `
    (Join-Path $root 'Code/core/lineage/DiplomaticWarDeclarationService.cs')
$warDecision = Get-Content -Raw -LiteralPath `
    (Join-Path $root 'Code/core/lineage/WarDecisionService.cs')

foreach ($pattern in @(
        'ReconcilePendingDeclarationsForActiveTreaty\s*\(\s*pFirst,\s*pSecond\s*\);',
        'ClearPendingForPair\s*\(\s*pFirst,\s*pSecond,\s*"active_war_blocker"\s*\);',
        'ClearPendingForPair\s*\(\s*pSecond,\s*pFirst,\s*"active_war_blocker"\s*\);')) {
    if ($proposal -notmatch $pattern) {
        throw "missing truce declaration reconciliation: $pattern"
    }
}

$methodStart = $proposal.IndexOf(
    'private static bool RegisterTrucePair(long pWarId, Kingdom pFirst,')
$methodEnd = $proposal.IndexOf(
    'private static int SettlementTruceStartYear(', $methodStart)
if ($methodStart -lt 0 -or $methodEnd -le $methodStart) {
    throw 'could not isolate RegisterTrucePair'
}
$method = $proposal.Substring($methodStart, $methodEnd - $methodStart)
$existing = $method.IndexOf('if (existing.ExecuteScalar() != null)')
$insert = $method.IndexOf(
    'DB.Insert(DiplomacyProposalTableItem.GetTableName()')
$notify = $method.IndexOf('NotifyPair(pFirst.id, pSecond.id);')
$calls = [regex]::Matches($method,
    'ReconcilePendingDeclarationsForActiveTreaty\s*\(\s*pFirst,\s*pSecond\s*\);')
if ($existing -lt 0 -or $insert -lt 0 -or $notify -lt 0 -or
    $calls.Count -ne 2) {
    throw 'truce registration must reconcile existing and inserted rows'
}
if ($calls[0].Index -lt $existing -or $calls[0].Index -gt $insert) {
    throw 'an existing authoritative truce must reconcile before insert path'
}
if ($calls[1].Index -lt $notify) {
    throw 'a newly inserted truce must reconcile after notification'
}
if (-not $declaration.Contains(
        'ShouldBlockWarWithActiveTreaty(activeTreaty,') -or
    -not $warDecision.Contains(
        'ShouldBlockWarWithActiveTreaty(activeTreaty, pSystemWar,')) {
    throw 'declaration issue and execution must both use the live treaty gate'
}

Write-Host 'Diplomatic war treaty gate source guard passed.'

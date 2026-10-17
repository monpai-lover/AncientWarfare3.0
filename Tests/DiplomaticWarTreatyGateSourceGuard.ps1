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
foreach ($pattern in @(
        'existing\.Contains\(pairKey\)[\s\S]{0,220}ReconcilePendingDeclarationsForActiveTreaty',
        'existing\.Contains\(TreatyPairKey\(requesterId, responderId\)\)[\s\S]{0,220}ReconcilePendingDeclarationsForActiveTreaty')) {
    if ($proposal -notmatch $pattern) {
        throw "authoritative truce path lacks declaration cleanup: $pattern"
    }
}
$breakStart = $proposal.IndexOf(
    'private static bool TryBreakNonAggression(')
$breakEnd = $proposal.IndexOf(
    'private static bool EnsureAllianceWithdrawalTruce(', $breakStart)
$withdrawalEnd = $proposal.IndexOf(
    'private static string TypeId(', $breakEnd)
if ($breakStart -lt 0 -or $breakEnd -le $breakStart -or
    $withdrawalEnd -le $breakEnd) {
    throw 'could not isolate treaty-breaking methods'
}
$breakMethod = $proposal.Substring($breakStart, $breakEnd - $breakStart)
$withdrawalMethod = $proposal.Substring(
    $breakEnd, $withdrawalEnd - $breakEnd)
if ($breakMethod -notmatch
        'BreakNonAggression\([\s\S]*ReconcilePendingDeclarationsForActiveTreaty') {
    throw 'breaking non-aggression must reconcile pending declarations'
}
if ($withdrawalMethod -notmatch
        'EnsureProposalTruce\([\s\S]*ReconcilePendingDeclarationsForActiveTreaty') {
    throw 'alliance withdrawal truce must reconcile pending declarations'
}
if (-not $declaration.Contains(
        'ShouldBlockWarWithActiveTreaty(activeTreaty,') -or
    -not $warDecision.Contains(
        'ShouldBlockWarWithActiveTreaty(activeTreaty,')) {
    throw 'declaration issue and execution must both use the live treaty gate'
}
if (-not $warDecision.Contains('TryStartInternalSystemWar(') -or
    -not $warDecision.Contains('pTreatyExemptInternalWar: true')) {
    throw 'internal system wars must use an explicit treaty exemption'
}

$territory = Get-Content -Raw -LiteralPath `
    (Join-Path $root 'Code/core/lineage/WarTerritoryService.cs')
$zhulu = Get-Content -Raw -LiteralPath `
    (Join-Path $root 'Code/core/lineage/ZhuluWarService.cs')
foreach ($externalNeedle in @(
        'TryStartSystemWar(pAttacker,',
        'MandateService.WAR_TIANMING, "tianming"',
        '"mandate_conquest"')) {
    if (-not $territory.Contains($externalNeedle)) {
        throw "external mandate war lost its treaty-gated route: $externalNeedle"
    }
}
if (-not $zhulu.Contains('TryStartSystemWar(attacker, defender,')) {
    throw 'external zhulu war must retain the treaty-gated system route'
}
function Get-SourceSection([string] $source, [string] $startToken,
    [string] $endToken) {
    $start = $source.IndexOf($startToken)
    if ($start -lt 0) { throw "Missing method: $startToken" }
    $end = $source.IndexOf($endToken, $start + $startToken.Length)
    if ($end -le $start) { throw "Missing method boundary: $endToken" }
    return $source.Substring($start, $end - $start)
}
$issue = Get-SourceSection $declaration 'internal static bool CanIssue(' `
    'internal static List<DiplomaticWarTargetAvailability>'
$startWar = Get-SourceSection $warDecision 'private static War StartWar(' `
    'internal static Kingdom ResolveWarMainDefender('
foreach ($body in @($issue, $startWar)) {
    $resolve = $body.IndexOf('ResolveWarMainDefender(')
    $treaty = [regex]::Match($body,
        'HasActiveWarBlocker\s*\(\s*pAttacker,\s*mainDefender\s*\)')
    $gate = $body.IndexOf('ShouldBlockWarWithActiveTreaty(activeTreaty,')
    if ($resolve -lt 0 -or -not $treaty.Success -or
        $treaty.Index -le $resolve -or $gate -le $treaty.Index -or
        $body -notmatch 'HasActiveWarBlocker\s*\(\s*pAttacker,\s*pDefender\s*\)') {
        throw 'declaration issue and execution must gate both declared and actual defenders on live treaties'
    }
}
if ($startWar.IndexOf('ShouldBlockWarWithActiveTreaty(activeTreaty,') -gt
    $startWar.IndexOf('World.world.diplomacy.startWar(')) {
    throw 'the live treaty gate must run before native war creation'
}
$clearPending = Get-SourceSection $declaration 'public static void ClearPendingForPair(' `
    'public static Kingdom TargetKingdom('
if ($clearPending -notmatch ('ResolveWarMainDefender\s*\(\s*declaredDefender\s*,\s*pAttacker\s*\)' +
        '[\s\S]*record\?\.DefenderId != pDefender\.id\s*&&\s*' +
        'mainDefender != pDefender\) continue;')) {
    throw 'truce cleanup must match pending governorate declarations by actual defender'
}

$rebellion = Get-Content -Raw -LiteralPath `
    (Join-Path $root 'Code/core/lineage/GeneralRebellionService.cs')
$defectionStart = $rebellion.IndexOf(
    'private static bool TryDefectToNeighbor(')
$defectionEnd = $rebellion.IndexOf(
    'private static bool TrySupportRestoration(', $defectionStart)
if ($defectionStart -lt 0 -or $defectionEnd -le $defectionStart) {
    throw 'could not isolate foreign general defection'
}
$defection = $rebellion.Substring(
    $defectionStart, $defectionEnd - $defectionStart)
$blocker = [regex]::Match($defection,
    'HasActiveWarBlocker\s*\(\s*neighbor,\s*pOldKingdom\s*\)').Index
$transfer = $defection.IndexOf('baseCity.joinAnotherKingdom(neighbor);')
if ($blocker -lt 0 -or $transfer -lt 0 -or $blocker -gt $transfer -or
    $defection -notmatch
        'TryStartSystemWar\s*\(\s*neighbor,\s*pOldKingdom' -or
    $defection -match 'TryStartInternalSystemWar\s*\(\s*neighbor,') {
    throw 'foreign-backed defection must validate treaties before transfer'
}

$restorationStart = $territory.IndexOf(
    'TryDeclareAutonomousRestorationCoreWar(')
$restorationEnd = $territory.IndexOf(
    'TryDeclareMandateWar(', $restorationStart)
if ($restorationStart -lt 0 -or $restorationEnd -le $restorationStart) {
    throw 'could not isolate autonomous restoration core war'
}
$restoration = $territory.Substring(
    $restorationStart, $restorationEnd - $restorationStart)
if ($restoration -notmatch
        'TryStartSystemWar\s*\(\s*pAttacker,\s*defender' -or
    $restoration -match
        'TryStartInternalSystemWar\s*\(\s*pAttacker,') {
    throw 'follow-up restoration core wars must obey external treaties'
}

Write-Host 'Diplomatic war treaty gate source guard passed.'

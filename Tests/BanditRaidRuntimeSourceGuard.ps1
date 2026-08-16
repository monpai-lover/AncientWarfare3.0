$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$raidPath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditRaidService.cs'
$routePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditRoute.cs'
$authorityPath = Join-Path $root 'Code/core/performance/AWAuthorityCycleService.cs'

if (-not (Test-Path -LiteralPath $raidPath)) {
    throw 'Missing PeasantRebelBanditRaidService.cs'
}

$raid = Get-Content -Raw -Encoding UTF8 $raidPath
$route = Get-Content -Raw -Encoding UTF8 $routePath
$authority = Get-Content -Raw -Encoding UTF8 $authorityPath

foreach ($token in @(
        'ScheduleYear(', 'ProcessAuthorityCycle(',
        'PeasantRebelBanditStateStore.TryResolveActive',
        'PeasantRebelBanditRaidRules.NeedsRaid',
        'PeasantRebelBanditRaidRules.PartySize',
        'PeasantRebelBanditRaidRules.RankTargets',
        'reachableFrom(', 'getAlliance()', 'hasKingdom(',
        'GeneralService.IsGeneral', 'HeirService.IsCurrentHeir',
        'BanditRaidStage.Outbound', 'BanditRaidStage.Returning',
        'MemberActorIds', 'LeaderActorId', 'TargetCityId',
        'TargetX', 'TargetY', 'PeasantRebelBanditStateStore.Write',
        'goTo(', 'pLimitPathfindingRegions: 6')) {
    if ($raid -notmatch [regex]::Escape($token)) {
        throw "Bandit raid runtime is missing $token"
    }
}

if ($raid -match 'startWar\(|newWar\(|joinAnotherKingdom\(|finishCapture') {
    throw 'Raid movement must not declare war or invoke occupation'
}
if ($raid -notmatch 'Stage\s*!=\s*BanditRaidStage\.None' -or
    $raid -notmatch 'Stage\s*==\s*BanditRaidStage\.Cooldown') {
    throw 'Raid scheduling does not enforce one active mission per stronghold'
}
if ($raid -notmatch 'PeasantRebelBanditRaidRules\.CanJoinRaid' -or
    $raid -notmatch 'actor\.isWarrior\(\)' -or
    $raid -notmatch 'actor\.isKing\(\)' -or
    $raid -notmatch 'HeirService\.IsCurrentHeir' -or
    $raid -notmatch 'actor\.isCarryingResources\(\)') {
    throw 'Raid party must use warriors without forcing ruler or heir'
}
if ($raid -notmatch 'OrderByDescending\([\s\S]{0,160}GeneralService\.IsGeneral') {
    throw 'Raid party does not prefer generals'
}

$outboundIndex = $raid.IndexOf('Stage = BanditRaidStage.Outbound')
$outboundWriteIndex = $raid.IndexOf('PeasantRebelBanditStateStore.Write',
    $outboundIndex)
$firstGoToIndex = $raid.IndexOf('.goTo(', $outboundIndex)
if ($outboundIndex -lt 0 -or $outboundWriteIndex -lt 0 -or
    $firstGoToIndex -lt 0 -or $outboundWriteIndex -gt $firstGoToIndex) {
    throw 'Outbound mission must be persisted before native actor movement'
}

if ($route -notmatch 'PeasantRebelBanditRaidService\.ScheduleYear\(pKingdom\)') {
    throw 'Bandit annual route does not schedule shortage raids'
}
foreach ($token in @('BanditRaids', 'aw3.authority.bandit_raids',
        'PeasantRebelBanditRaidService.ProcessAuthorityCycle')) {
    if ($authority -notmatch [regex]::Escape($token)) {
        throw "Authority cycle is missing bounded bandit raid stage: $token"
    }
}

Write-Output 'Bandit raid runtime source guard passed.'

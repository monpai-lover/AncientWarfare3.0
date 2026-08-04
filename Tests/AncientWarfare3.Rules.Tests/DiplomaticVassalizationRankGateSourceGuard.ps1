$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$proposalPath = Join-Path $repo 'Code/core/lineage/DiplomacyProposalService.cs'
$warRulesPath = Join-Path $repo 'Code/core/lineage/WarAiGoalSelectionRules.cs'
$warTestsPath = Join-Path $repo 'Tests/AncientWarfare3.Rules.Tests/WarAiGoalSelectionRulesTests.cs.txt'

$proposal = Get-Content -Raw -Encoding UTF8 -LiteralPath $proposalPath
$warRules = Get-Content -Raw -Encoding UTF8 -LiteralPath $warRulesPath
$warTests = Get-Content -Raw -Encoding UTF8 -LiteralPath $warTestsPath

$demand = [regex]::Match(
    $proposal,
    '(?s)if \(detailId !=\s*DiplomacyProposalOpportunityRules\.VassalizeDemandDetail\).*?result\.Allowed = true;')
if (-not $demand.Success) {
    throw 'vassalize-demand assessment block was not found'
}
if ($demand.Value -notmatch 'VassalService\.CanSetVassal\(pResponder, pRequester') {
    throw 'diplomatic vassalization must retain the shared title and adjacency gate'
}
if ($demand.Value -match 'pRequester\.power' -or
    $demand.Value -match 'pResponder\.power' -or
    $demand.Value -match 'insufficient_power') {
    throw 'diplomatic vassalization must not require a two-times-power preflight'
}

if ($warRules -notmatch 'CanAiForceVassal\(' -or
    $warRules -notmatch 'attackerTitleRank == 2 && targetTitleRank == 0') {
    throw 'AI forced-vassal war title restrictions changed unexpectedly'
}
if ($warTests -notmatch 'a marquis cannot force-vassalize a baron through AI' -or
    $warTests -notmatch 'a duke may force-vassalize a baron through AI') {
    throw 'AI forced-vassal war regression cases are missing'
}

Write-Output 'Diplomatic vassalization rank-gate source guard passed.'

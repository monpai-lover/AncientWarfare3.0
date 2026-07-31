$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$vassalAi = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/VassalAIService.cs'))
$proposal = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/DiplomacyProposalService.cs'))

$activeStart = $vassalAi.IndexOf(
    'private static bool TryActiveVassal')
$activeEnd = $vassalAi.IndexOf(
    'private static bool TryVassalWar', $activeStart)
if ($activeStart -lt 0 -or $activeEnd -le $activeStart) {
    throw 'TryActiveVassal source range is unavailable.'
}
$activeBody = $vassalAi.Substring($activeStart,
    $activeEnd - $activeStart)
if ($activeBody.Contains('aw_decision_seek_suzerain') -or
    $activeBody.Contains('StartDecisionWithTarget')) {
    throw 'TryActiveVassal must not bypass diplomacy proposals.'
}
if (-not $activeBody.Contains(
        'DiplomacyProposalService.TryCreateAiProtectionProposal')) {
    throw 'TryActiveVassal must create a formal protection proposal.'
}
if (-not $proposal.Contains('TryJoinProtectorToDefensiveWar') -or
    -not $proposal.Contains('protection_war_entry_failed') -or
    -not $proposal.Contains('diplomatic_protection')) {
    throw 'Protection execution needs guarded war entry and compensation.'
}

Write-Output 'Vassal AI proposal-chain source guards passed.'

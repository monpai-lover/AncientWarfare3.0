$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$war = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\WarDecisionService.cs')
$vassal = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\VassalService.cs')
$declaration = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\DiplomaticWarDeclarationService.cs')

foreach ($token in @(
    'Kingdom declaredDefender = pDefender;',
    'ResolveMilitaryGovernorateMainDefender(declaredDefender)',
    'mainDefender, asset);',
    'ConsumeClaim(pAttacker, declaredDefender, type)',
    'RecordWarDecision(pAttacker, declaredDefender'
)) {
    if (-not $war.Contains($token)) { throw "Missing war redirect: $token" }
}
if (-not $war.Contains(
        'CanPassAllianceWarRules(pAttacker, queueDefender')) {
    throw 'Queued war does not validate the resolved main defender.'
}
if (-not $declaration.Contains('target_kingdom = pDefender')) {
    throw 'War goal no longer preserves the originally targeted governorate.'
}
if (-not $vassal.Contains(
        'MilitaryGovernorateRules.MustJoinSuzerainWar(')) {
    throw 'Military governorate war obligation is not deterministic.'
}
Write-Output 'Military governorate war source guard passed.'

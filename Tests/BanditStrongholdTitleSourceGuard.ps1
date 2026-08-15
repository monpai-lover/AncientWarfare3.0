$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rulerPath = Join-Path $root 'Code/core/lineage/RulerAppellationService.cs'
$heirPath = Join-Path $root 'Code/core/lineage/HeirTitleRules.cs'
$ceremonialPath = Join-Path $root 'Code/core/lineage/CeremonialTitleResolver.cs'
$strongholdPath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdService.cs'

$ruler = Get-Content -Raw -Encoding UTF8 $rulerPath
$heir = Get-Content -Raw -Encoding UTF8 $heirPath
$ceremonial = Get-Content -Raw -Encoding UTF8 $ceremonialPath
$stronghold = Get-Content -Raw -Encoding UTF8 $strongholdPath

if ($ruler -notmatch 'ComposeCeremonialTitle\(pKingdom,\s*false\)') {
    throw 'Living bandit ruler does not use the shared stronghold title composer'
}
if ($heir -notmatch 'ComposeCeremonialTitle\(pKingdom,\s*true\)') {
    throw 'Living bandit heir does not use the shared stronghold title composer'
}
foreach ($token in @('MANDATE_REBEL_NAME_ROOT',
        'PeasantRebelBanditStrongholdRules.',
        'ComposeCeremonialTitle(root, role)',
        'aw_bandit_ruler_title', 'aw_bandit_heir_title')) {
    if ($stronghold -notmatch [regex]::Escape($token)) {
        throw "Shared kingdom title composer is missing $token"
    }
}
foreach ($token in @('RulerAppellationService.GetFullLivingAppellation',
        'HeirTitleRules.BuildSocialTitle')) {
    if ($ceremonial -notmatch [regex]::Escape($token)) {
        throw "Ceremonial archive boundary is missing $token"
    }
}

Write-Output 'Bandit stronghold title source guard passed.'

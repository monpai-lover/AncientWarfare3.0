$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$powersPath = Join-Path $root 'Code/content/GodPowerLibrary.cs'
$tabPath = Join-Path $root 'Code/ui/AW_LineageTab.cs'
$servicePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdService.cs'
$localePath = Join-Path $root 'Locales/others.csv'

$powers = Get-Content -Raw -Encoding UTF8 $powersPath
$tab = Get-Content -Raw -Encoding UTF8 $tabPath
$service = Get-Content -Raw -Encoding UTF8 $servicePath
$locale = Get-Content -Raw -Encoding UTF8 $localePath

foreach ($token in @('SPAWN_BANDIT_STRONGHOLD',
        'RegisterBanditStrongholdPower', 'BanditStrongholdClick',
        'pTile?.zone?.city', 'PeasantRebelBanditStrongholdService.TryCreateDirect',
        'ui/wars/war_rebellion')) {
    if ($powers -notmatch [regex]::Escape($token)) {
        throw "Bandit god power is missing $token"
    }
}
if ($powers -match 'getClosestCity|findClosestCity|nearestCity') {
    throw 'Bandit god power must not search for a nearby city'
}
if ($tab -notmatch 'GodPowerLibrary\.SPAWN_BANDIT_STRONGHOLD') {
    throw 'Lineage tab is missing the bandit stronghold button'
}
foreach ($token in @('TryCreateDirect(', 'makeNewCivKingdom',
        'copyMetasFromOtherKingdom', 'TryCreate(',
        'RemoveBanditOnFailure', 'SelectDirectRuler(',
        'IsOrdinaryResident(actor, origin)')) {
    if ($service -notmatch [regex]::Escape($token)) {
        throw "Direct stronghold service is missing $token"
    }
}
if ($service.Contains('Actor leader = pMother?.leader;')) {
    throw 'Direct stronghold creation must not conscript the mother-city leader'
}
foreach ($key in @('aw_spawn_bandit_stronghold,',
        'aw_spawn_bandit_stronghold_description,',
        'aw_bandit_stronghold_invalid_city,',
        'aw_bandit_stronghold_tower_failed,',
        'aw_bandit_stronghold_success,')) {
    if ($locale -notmatch [regex]::Escape($key)) {
        throw "Bandit god power localization is missing $key"
    }
}

Write-Output 'Bandit stronghold god power source guard passed.'

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$patch = [IO.File]::ReadAllText((Join-Path $root `
    'Code/patch/AW_ArmyMapInformationMinimapPatch.cs'))
$service = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/presentation/ArmyMapInformationService.cs'))
$locale = [IO.File]::ReadAllText((Join-Path $root `
    'Locales/aw3_army_rts.csv'))

if ($patch.Contains('drawKings')) {
    throw 'Army information must extend the original army flag, not draw a second king/minimap marker.'
}
if (-not $patch.Contains('drawArmies')) {
    throw 'Army information must hook the original army-flag renderer.'
}
if ($patch.Contains('.getNext()')) {
    throw 'Army information must not allocate another QuantumSprite flag.'
}
if (-not $patch.Contains('getAll()') -or
    -not $patch.Contains('QuantumSpriteWithText')) {
    throw 'Army information must reuse the native army flag and its text child.'
}
if (-not $patch.Contains(
        'ArmyMapInformationService.TryPopulateNativeFlagText')) {
    throw 'Army flag rendering must use the shared RTS information formatter.'
}
if (-not $service.Contains('ResolvePendingState(')) {
    throw 'Missionless armies must derive a visible pending state.'
}
if (-not $service.Contains('PendingOperationLocalizationKey(')) {
    throw 'Missionless army text must expose a localized pending operation.'
}
if ($service.Contains(
        '!ArmyRtsControllerService.TryGetProjection(pArmy,') -and
    $service.Contains(
        '!ArmyRtsControllerService.TryGetMission(pArmy,')) {
    throw 'Army information must not disappear solely because RTS publication is pending.'
}
if (-not $locale.Contains('aw_army_rts_state_awaiting_orders,')) {
    throw 'Army information needs an awaiting-orders localization key.'
}

Write-Output 'Army flag information source guards passed.'

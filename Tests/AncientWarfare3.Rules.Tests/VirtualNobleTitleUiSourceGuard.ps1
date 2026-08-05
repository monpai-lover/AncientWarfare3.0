$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$kingdom = Get-Content -Raw (Join-Path $root 'Code/ui/windows/KingdomWindowAddition.cs')
$unit = Get-Content -Raw (Join-Path $root 'Code/patch/AW_UnitTabPatch.cs')
$grant = Get-Content -Raw (Join-Path $root 'Code/ui/windows/VirtualNobleTitleGrantWindow.cs')
$roster = Get-Content -Raw (Join-Path $root 'Code/ui/windows/VirtualNobleTitleRosterWindow.cs')
$locale = Get-Content -Raw (Join-Path $root 'Locales/aw3_virtual_titles.csv')
foreach ($needle in @('VirtualNobleTitleRosterWindow.Open', 'GetActiveForKingdom', 'VirtualNobleTitleGrantWindow.Open')) {
    if (-not ($kingdom.Contains($needle) -or $unit.Contains($needle))) { throw "missing UI entry: $needle" }
}
foreach ($needle in @('GrantVirtualNobleTitle', 'DispatchFromUi', 'characterLimit')) {
    if (-not $grant.Contains($needle)) { throw "missing grant UI contract: $needle" }
}
if (-not $roster.Contains('ActionLibrary.openUnitWindow')) { throw 'missing roster actor navigation' }
foreach ($key in @(
    'aw_virtual_titles',
    'aw_virtual_titles_short',
    'aw_virtual_titles_none',
    'aw_virtual_title_grant',
    'aw_virtual_title_grant_desc',
    'aw_virtual_title_grant_action',
    'aw_virtual_title_prompt',
    'aw_virtual_title_placeholder',
    'aw_virtual_noble_title',
    'aw_unknown_actor',
    'aw_virtual_title_error_generic',
    'aw_virtual_title_error_not_ready',
    'aw_virtual_title_error_invalid_target',
    'aw_virtual_title_error_invalid_text',
    'aw_virtual_title_error_duplicate',
    'aw_virtual_title_error_persistence'
)) {
    if (-not $locale.Contains($key + ',')) { throw "missing locale key: $key" }
}
if ($grant.Contains('result.Error')) { throw 'raw command error leaked into UI' }
Write-Output 'virtual noble title UI source guard passed'

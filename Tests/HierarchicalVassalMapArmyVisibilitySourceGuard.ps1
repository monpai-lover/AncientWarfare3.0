$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$rules = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\HierarchicalVassalMapModeRules.cs')
$map = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\patch\AW_HierarchicalVassalMapMinimapPatch.cs')
$info = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\patch\AW_ArmyMapInformationMinimapPatch.cs')

if (-not $rules.Contains('pAssetId == "armies"')) {
    throw 'Army asset is not retained'
}
if ($map.Contains('private static bool SkipArmyFlags')) {
    throw 'native drawArmies is still skipped'
}
if ($map.Contains('MinimapArmyFlagSortingOrder')) {
    throw 'native Army sorting is still overridden'
}
if ($info -match
    'HierarchicalVassalMapModeService\.IsActive\(\)\) return;') {
    throw 'Army information is still disabled in hierarchical mode'
}

Write-Output 'HierarchicalVassalMapArmyVisibilitySourceGuard: PASS'

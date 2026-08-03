$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$patch = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\patch\AW_PowerButtonVisualPatch.cs')

if (-not $patch.Contains(
    'bool hasValidSourceIcon = pButton?.icon != null;')) {
    throw 'Vanilla icon validity is not checked before clearing an override'
}
if (-not $patch.Contains('ShouldClearOwnedCancelIconOverride')) {
    throw 'Cancel icon clearing is not guarded by AW3 ownership'
}
if (-not $patch.Contains('_ownedCancelButton = __instance')) {
    throw 'AW3 cancel-button ownership is not recorded'
}
if (-not $patch.Contains('_ownedCancelSprite = sprite')) {
    throw 'AW3 cancel-sprite ownership is not recorded'
}

Write-Output 'CancelButtonIconOverrideSourceGuard: PASS'

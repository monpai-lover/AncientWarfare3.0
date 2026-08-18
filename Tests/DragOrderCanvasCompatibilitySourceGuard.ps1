$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$source = Get-Content -Raw (Join-Path $root `
    'Code/patch/AW_DragOrderElementPatch.cs')

foreach ($required in @(
        '[HarmonyPatch(typeof(DragOrderElement), "Start")]',
        'GetOrAddCanvas',
        'GetOrAddGraphicRaycaster',
        'GetComponent<Canvas>()',
        'GetComponent<GraphicRaycaster>()')) {
    if (-not $source.Contains($required)) {
        throw "DragOrderElement compatibility patch is missing: $required"
    }
}

if ($source.Contains('sh_toolkit_')) {
    throw 'DragOrderElement compatibility must not depend on toolkit object names.'
}

if ($source -notmatch 'canvasMatches\s*!=\s*1' -or
    $source -notmatch 'raycasterMatches\s*!=\s*1') {
    throw 'DragOrderElement transpiler must fail closed when the vanilla IL shape changes.'
}

Write-Host 'DragOrderElement Canvas compatibility source guard passed.'

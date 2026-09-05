$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'Code\ui\windows\HistoricalFigureDrawWindow.cs'
$source = Get-Content -Raw -Encoding UTF8 -LiteralPath $path

$required = @(
    'private enum DetailsReturnTarget',
    'DetailsReturnTarget.CrateSelection',
    'DetailsReturnTarget.CrateContents',
    'DetailsReturnTarget.Inventory',
    'DetailsReturnTarget.Recycle',
    '_detailsReturnInventoryPage = _inventoryPage',
    '_inventoryPage = _detailsReturnInventoryPage',
    'private void BackToPreviousPage()',
    'if (_state == DrawState.Idle)',
    '_detailsReturnTarget = DetailsReturnTarget.CrateSelection;',
    'private ScrollRect _revealBiographyScroll;',
    'private Scrollbar _revealBiographyScrollbar;',
    'typeof(RectMask2D)',
    '_revealBiography.preferredHeight',
    '_revealBiographyScroll.verticalNormalizedPosition = 1f'
)

foreach ($needle in $required) {
    if (-not $source.Contains($needle)) {
        throw "Historical figure details navigation guard missing: $needle"
    }
}

$backStart = $source.IndexOf('private void BackToPreviousPage()')
$backEnd = $source.IndexOf('protected override void Init()', $backStart)
if ($backStart -lt 0 -or $backEnd -le $backStart) {
    throw 'Could not isolate BackToPreviousPage.'
}
$back = $source.Substring($backStart, $backEnd - $backStart)
if (-not $back.Contains('if (_state == DrawState.Idle)')) {
    throw 'Idle inventory/crate pages do not return to crate selection.'
}

if ($source.Contains('private static bool _returnToRecycle;')) {
    throw 'Details navigation still uses the old recycle-only return flag.'
}
if ($source.Contains('_revealBiography.verticalOverflow = VerticalWrapMode.Truncate')) {
    throw 'Biography text is still clipped instead of scrollable.'
}

Write-Output 'Historical figure details navigation guard passed.'

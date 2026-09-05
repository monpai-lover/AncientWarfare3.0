$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'Code\ui\windows\HistoricalFigureRecycleWindow.cs'
$source = Get-Content -Raw -Encoding UTF8 -LiteralPath $path

$required = @(
    'new Vector2(480f, 350f)',
    'nativeScrollComponent.horizontal = false',
    'nativeScrollComponent.vertical = false',
    'nativeViewport.sizeDelta = new Vector2(width, height)',
    'nativeContent.sizeDelta = new Vector2(width, height)',
    'Mathf.Min(286f, width - 216f)',
    '_listViewport.rect.width - 12f',
    'for (int i = 0; i < 10; i++)',
    'int slotColumn = i == 9 ? 1 : i % SlotColumns',
    'float slotButtonTop = -height + 28f'
)

foreach ($needle in $required) {
    if (-not $source.Contains($needle)) {
        throw "Historical recycle layout guard missing: $needle"
    }
}

foreach ($size in @(@(550, 410), @(480, 350), @(760, 620))) {
    $width = $size[0] - 42
    $height = $size[1] - 40
    $listWidth = [Math]::Max(220, [Math]::Min(286, $width - 216))
    $rightX = 14 + $listWidth + 4 + 14
    $rightWidth = [Math]::Max(120, $width - $rightX - 14)
    $listHeight = [Math]::Max(140, $height - 150)
    $slotSize = [Math]::Max(32, [Math]::Min(50,
        [Math]::Min(($rightWidth - 24) / 3, ($listHeight - 29) / 4)))
    $slotRight = 7 + 2 * ($slotSize + 5) + $slotSize
    $slotBottom = 7 + 3 * ($slotSize + 5) + $slotSize
    $buttonBottom = ($height - 28) + 26
    if ($slotRight -gt $rightWidth -or $slotBottom -gt $listHeight) {
        throw "Selection grid overflows at $($size[0])x$($size[1])"
    }
    if ($buttonBottom -gt $height) {
        throw "Footer buttons overflow at $($size[0])x$($size[1])"
    }
}

Write-Output 'Historical figure recycle layout guard passed.'

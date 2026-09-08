$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$modelPath = Join-Path $repo 'Code\content\figures\HistoricalFigureCardModels.cs'
$source = Get-Content -LiteralPath $modelPath -Raw

$expected = @{
    Gold = '0.0030f'
    Red = '0.0075f'
    Pink = '0.0350f'
    Purple = '0.1750f'
    Blue = '0.7795f'
}

foreach ($entry in $expected.GetEnumerator()) {
    $pattern = 'HistoricalFigureCardRarity\("' +
        $entry.Key.ToLowerInvariant() + '"[\s\S]*?' +
        [regex]::Escape($entry.Value) + '\);'
    if ($source -notmatch $pattern) {
        throw "Expected $($entry.Key) probability $($entry.Value)"
    }
}

$total = 0.0030 + 0.0075 + 0.0350 + 0.1750 + 0.7795
if ([math]::Abs($total - 1.0) -gt 0.0000001) {
    throw "Expected rarity probabilities to total 1.0, got $total"
}

Write-Output 'Historical figure card probability source guard passed.'

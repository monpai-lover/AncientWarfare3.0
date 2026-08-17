$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$path = Join-Path $repo 'Code/core/court/MinisterialPowerService.cs'
$source = [IO.File]::ReadAllText($path)

foreach ($needle in @(
    'HistoricalFigureService.TRAIT_FIGURE',
    'HistoricalFigureService.TRAIT_FIRST',
    'historicalFigure: historicalFigure')) {
    if ($source.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) {
        throw "Ministerial historical-figure usurpation integration missing: $needle"
    }
}

if ($source.IndexOf('pActor.addTrait("ambitious")',
        [StringComparison]::Ordinal) -ge 0 -or
    $source.IndexOf('pActor.removeTrait("content")',
        [StringComparison]::Ordinal) -ge 0) {
    throw 'Ministerial coup eligibility must not mutate historical figure personality traits.'
}

Write-Host 'Historical figure ministerial usurpation source guard passed.'

$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$source = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\patch\AW_KingdomAgeSafePatch.cs')

if (-not $source.Contains('private static readonly List<Kingdom> Snapshot')) {
    throw 'Kingdom annual update must reuse its snapshot buffer.'
}

if ($source.Contains('new List<Kingdom>(__instance.list)')) {
    throw 'Kingdom annual update must not allocate a full list every year.'
}

$annual = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\policy\KingdomAnnualWorkService.cs')
$flushCount = ([regex]::Matches($annual,
    'UpdateAgeBenchmark\.Flush\(\);')).Count
if ($flushCount -ne 1 -or
    -not $annual.Contains('FlushBenchmarkIfIdle()')) {
    throw 'Annual benchmark metrics must flush once when the batch becomes idle.'
}

Write-Output 'Annual kingdom performance source guard passed.'

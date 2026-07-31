$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'Code/core/lineage/HeirService.cs'
if (-not (Test-Path -LiteralPath $path)) {
    throw "HeirService source is missing: $path"
}

$source = [IO.File]::ReadAllText($path)
$requirements = @(
    @('Actor directDescendant = null;', 'direct descendants are tracked separately'),
    @('Actor fullBrother = PickEldestEligibleFullBrother(', 'full brothers are selected explicitly'),
    @('if (directDescendant != null)', 'direct descendants retain precedence'),
    @('if (fullBrother != null)', 'full brothers precede collateral succession'),
    @('HeirFullBrotherRules.SelectEldestEligibleId(', 'pure full-brother ordering is used'),
    @('CollectFullSiblingIds(', 'both parental child lists are combined'),
    @('HasSameParentPair(', 'half brothers are excluded')
)

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($requirement in $requirements) {
    if (-not $source.Contains($requirement[0])) {
        $failures.Add("$($requirement[1]): missing '$($requirement[0])'")
    }
}

if ($failures.Count -gt 0) {
    throw "Full-brother succession source guard failures:`n - " +
        ($failures -join "`n - ")
}

Write-Output 'Full-brother succession source guard passed.'

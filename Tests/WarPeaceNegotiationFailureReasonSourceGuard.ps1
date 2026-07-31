$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'Code/ui/windows/WarPeaceNegotiationController.cs'
if (-not [IO.File]::Exists($path)) {
    throw "war peace negotiation controller missing: $path"
}

$source = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
$start = $source.IndexOf(
    'private static bool TryGetNegotiationContext(',
    [StringComparison]::Ordinal)
$end = if ($start -lt 0) { -1 } else {
    $source.IndexOf(
        'private static bool TryResolveSettlementScope(',
        $start,
        [StringComparison]::Ordinal)
}
if ($start -lt 0 -or $end -le $start) {
    throw 'unable to inspect TryGetNegotiationContext'
}

$body = $source.Substring($start, $end - $start)
$pattern = 'if \(!WarScoreService\.TryGetSnapshot\([\s\S]*?' +
    'out pScore\)\)\s*\{\s*pReason = "war_score_unavailable";\s*' +
    'return false;\s*\}'
if (-not [Regex]::IsMatch($body, $pattern)) {
    throw 'war-score snapshot failure does not return war_score_unavailable'
}

Write-Output 'War-peace negotiation failure reason source guard passed.'

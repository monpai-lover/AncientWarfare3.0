$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $repoRoot 'Code/core/lineage/WarRefugeeService.cs'
$source = [System.IO.File]::ReadAllText($servicePath).Replace("`r`n", "`n")

$methodMatch = [regex]::Match(
    $source,
    'internal static void OnActorJoinedCity\(.*?(?=\n\s*internal static void OnActorBorn\()',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $methodMatch.Success) {
    throw 'OnActorJoinedCity source boundary was not found.'
}

$methodSource = $methodMatch.Value
$keyRead = $methodSource.IndexOf(
    'pActor.data.get(LineageKeys.WAR_REFUGEE_JOURNEY_ID',
    [System.StringComparison]::Ordinal)
$negativeGate = $methodSource.IndexOf(
    'if (journeyId < 0L) return;',
    [System.StringComparison]::Ordinal)
$databaseAccess = $methodSource.IndexOf(
    'LineageArchiveManager archive',
    [System.StringComparison]::Ordinal)

if ($keyRead -lt 0 -or $negativeGate -lt 0) {
    throw 'Actor.joinCity must reject actors without a refugee journey before database access.'
}
if ($databaseAccess -lt 0 -or $keyRead -gt $databaseAccess -or
    $negativeGate -gt $databaseAccess) {
    throw 'Actor.joinCity refugee ownership gate must run before database access.'
}

Write-Host 'War refugee joinCity hot-path source guard passed.'

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$service = [IO.File]::ReadAllText((Join-Path $repoRoot `
    'Code/core/lineage/ActorDeathArchiveService.cs'))
$writer = [IO.File]::ReadAllText((Join-Path $repoRoot `
    'Code/core/lineage/LineageArchiveWriter.cs'))
$historical = [IO.File]::ReadAllText((Join-Path $repoRoot `
    'Code/core/db/HistoricalWriteService.cs'))
$failures = [System.Collections.Generic.List[string]]::new()

if (-not $service.Contains('InFlightSequence')) {
    $failures.Add('death archive commit tracking is missing InFlightSequence')
}
$capturedStart = $writer.IndexOf(
    'internal static bool TryQueueCapturedDeath(',
    [StringComparison]::Ordinal)
$synchronousStart = $writer.IndexOf(
    'internal static bool WriteCapturedDeathSynchronously(',
    $capturedStart, [StringComparison]::Ordinal)
$capturedBody = if ($capturedStart -ge 0 -and
    $synchronousStart -gt $capturedStart) {
    $writer.Substring($capturedStart, $synchronousStart - $capturedStart)
} else {
    ''
}
foreach ($token in @(
        'ActorDeathArchiveService.OnWriteAccepted',
        'ActorDeathArchiveService.OnWriteCommitted',
        'ActorDeathArchiveService.OnWriteFailed')) {
    if (-not $capturedBody.Contains($token)) {
        $failures.Add("captured death callback is missing $token")
    }
}

$acceptedBranch = [regex]::Match($service,
    'if\s*\(queueAccepted\)\s*\{(?<body>.*?)\}',
    [Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $acceptedBranch.Success) {
    $failures.Add('death archive service has no accepted-write branch')
} elseif ($acceptedBranch.Groups['body'].Value.Contains(
        'Pending.Remove(actorId)')) {
    $failures.Add('accepted async deaths must remain pending until commit')
}

if (-not $service.Contains('HistoricalWriteService.EnsureRequiredWorker(')) {
    $failures.Add('save drain must recover the required historical writer')
}
if (-not $service.Contains('HistoricalWriteService.FlushForSave(')) {
    $failures.Add('death save drain must wait for accepted writes to commit')
}
if (-not $service.Contains('completion_no_progress')) {
    $failures.Add('death save drain must stop when commit callbacks make no progress')
}
if (-not $historical.Contains(
        'HistoricalWriteModeRules.ShouldRequireWorkerForFlush(')) {
    $failures.Add('historical flush must reject a missing required worker')
}

if ($failures.Count -gt 0) {
    Write-Host "Actor death archive commit tracking failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Actor death archive commit tracking guard passed.'

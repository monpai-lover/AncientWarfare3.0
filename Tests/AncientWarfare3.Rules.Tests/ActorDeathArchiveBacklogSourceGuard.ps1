param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$service = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/ActorDeathArchiveService.cs'))
$savePatch = [IO.File]::ReadAllText((Join-Path $root `
    'Code/patch/AW_SavePatch.cs'))
$failures = [System.Collections.Generic.List[string]]::new()

if ($service -notmatch `
        'ActorDeathArchiveRules\.\s*ResolveAuthorityMilliseconds\(Pending\.Count\)') {
    $failures.Add('authority processing must scale its time slice with the death backlog')
}
if ($service -notmatch `
        'ActorDeathArchiveRules\.\s*ResolveAuthorityItemLimit\(Pending\.Count\)') {
    $failures.Add('authority processing must scale its item limit with the death backlog')
}
if ($savePatch -notmatch `
        'ActorDeathArchiveRules\.\s*ResolveSaveTimeoutSeconds\s*\(') {
    $failures.Add('save preparation must use a bounded backlog-aware death timeout')
}
if (-not $savePatch.Contains('ActorDeathArchiveService.PendingCount')) {
    $failures.Add('save preparation must size the death timeout from the live backlog')
}
if (-not $savePatch.Contains('ActorDeathArchiveService.FlushForSave(')) {
    $failures.Add('save safety must still require the death queue to flush')
}

if ($failures.Count -gt 0) {
    Write-Host "Actor death archive backlog failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Actor death archive backlog guard passed.'

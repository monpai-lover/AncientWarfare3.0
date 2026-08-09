$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$runnerPath = Join-Path $repoRoot `
    'Code/core/performance/AWCooperativeActorPostRunner.cs'
$runner = Get-Content -Raw -Encoding UTF8 $runnerPath
$rulesProject = Get-Content -Raw -Encoding UTF8 (Join-Path $repoRoot `
    'Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj')

$runnerRequirements = [ordered]@{
    'action-landed job id is recognized' =
        'ActionLandedJobId = "update_action_landed"'
    'action-landed uses a dedicated runner' =
        'actorsChecked = RunActionLandedJob(batch, job.container);'
    'action-landed runner reports checked actors' =
        'private static int RunActionLandedJob('
}

$methodMatch = [regex]::Match(
    $runner,
    '(?s)private static int RunActionLandedJob\(.+?' +
    'private void PrepareActiveBehaviorPartitions')
$method = if ($methodMatch.Success) { $methodMatch.Value } else { '' }
$methodRequirements = [ordered]@{
    'dedicated runner refreshes pending container changes' =
        'container.checkAddRemove();'
    'dedicated runner rejects null actors' =
        'actor == null'
    'dedicated runner rejects disposed actor data' =
        'actor.data == null'
    'dedicated runner rejects stale batch membership' =
        '!ReferenceEquals(actor.batch, batch)'
    'dedicated runner removes rejected container entries' =
        'container.Remove(actor);'
    'valid actors retain the original landing callback' =
        'actor.actionLanded();'
}

$failures = [System.Collections.Generic.List[string]]::new()
if (-not $rulesProject.Contains(
        'ActionLandedPostJobSafetySourceGuard.ps1')) {
    $failures.Add('action-landed guard is registered in the rules build')
}
foreach ($entry in $runnerRequirements.GetEnumerator()) {
    if (-not $runner.Contains($entry.Value)) {
        $failures.Add([string]$entry.Key)
    }
}
foreach ($entry in $methodRequirements.GetEnumerator()) {
    if (-not $method.Contains($entry.Value)) {
        $failures.Add([string]$entry.Key)
    }
}

if ($method.Contains('!actor.isAlive()')) {
    $failures.Add('dead actors that still own their batch retain native cleanup')
}
if ($method.Contains('catch (NullReferenceException')) {
    $failures.Add('action-landed safety must not swallow NullReferenceException')
}

if ($failures.Count -gt 0) {
    Write-Host "Action-landed safety failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Action-landed post-job safety guard passed.'

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$path = Join-Path $repo 'Code/core/performance/AWCooperativeWorldMaintenanceRunner.cs'
if (-not [IO.File]::Exists($path)) {
    throw 'Cooperative world maintenance runner is missing.'
}

$source = [IO.File]::ReadAllText($path)
foreach ($needle in @(
    'private void ProcessActorMetaBatch()',
    'kingdom == null || kingdom.data == null',
    'ActorKingdomSafetyService.QueueRepair(actor)',
    '_world.units.units_only_dying.Add(actor)',
    'if (kingdom.wild)')) {
    if (-not $source.Contains($needle)) {
        throw "Actor meta null-kingdom quarantine is missing '$needle'."
    }
}

Write-Output 'Actor meta null-kingdom source guard passed.'

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source = Get-Content -Raw (Join-Path $root 'Code\core\pathfinding\AWPathMovementBridge.cs')
if (-not $source.Contains('AWPathFinder finder = AWPathfindingBootstrap.Finder;')) {
    throw 'smooth movement must snapshot the shared pathfinder'
}
if (-not $source.Contains('else if (finder != null)')) {
    throw 'smooth movement must guard pathfinder teardown before OpenReadyCursor'
}
Write-Output 'SmoothMovementPathfinderBoundarySourceGuard passed.'

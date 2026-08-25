$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$bridge = Get-Content -Raw (Join-Path $repoRoot 'Code/core/pathfinding/AWPathMovementBridge.cs')
$runner = Get-Content -Raw (Join-Path $repoRoot 'Code/core/performance/AWCooperativeActorPostRunner.cs')
$globalPatch = Get-Content -Raw (Join-Path $repoRoot 'Code/patch/AW_GlobalPathfindingPatch.cs')
$safetyPatch = Get-Content -Raw (Join-Path $repoRoot 'Code/patch/AW_PathfindingSafetyPatch.cs')

function Assert-Contains([string] $Text, [string] $Pattern, [string] $Message) {
    if ($Text -notmatch $Pattern) { throw $Message }
}

Assert-Contains $bridge 'internal long ActorId \{ get; \}' 'prepared native path must capture actor id'
Assert-Contains $bridge 'internal int CurrentTileId \{ get; \}' 'prepared native path must capture current tile id'
Assert-Contains $bridge 'internal int TargetTileId \{ get; \}' 'prepared native path must capture target tile id'
Assert-Contains $bridge 'PreparedNativePathCommitRules\.Decide' 'serial native commit must classify the live fingerprint'
Assert-Contains $bridge 'PreparedNativePathCommitDecision\.RetryLater' 'prepared native path must retain retryable actors'
Assert-Contains $bridge 'PreparedNativePathCommitDecision\.Commit' 'prepared native path must distinguish successful commits'
Assert-Contains $globalPatch '__instance\.current_tile\?\.data != null' 'native military goTo must validate the current tile'
Assert-Contains $globalPatch 'pTile\?\.data != null' 'native military goTo must validate the target tile'
Assert-Contains $globalPatch '__instance\.current_tile\.region != null' 'native military goTo must validate the current region'
Assert-Contains $globalPatch 'pTile\.region != null' 'native military goTo must validate the target region'

if ($safetyPatch -match 'PathfindingOwnershipService\.IsAw3Owner\) return __exception') {
    throw 'AW3-owned global path failures must pass through narrow classification'
}
Assert-Contains $safetyPatch 'LogConvertedGlobalPathFailure' 'converted global path failures must emit a rate-limited diagnostic'

Write-Output 'Prepared native path runtime source guard passed.'

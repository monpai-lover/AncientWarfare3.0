$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$failures = [Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    return [IO.File]::ReadAllText(
        (Join-Path $root $relativePath), [Text.Encoding]::UTF8)
}

function Require-Contains(
    [string]$text,
    [string]$needle,
    [string]$message) {
    if (-not $text.Contains($needle)) {
        $failures.Add($message)
    }
}

function Forbid-Contains(
    [string]$text,
    [string]$needle,
    [string]$message) {
    if ($text.Contains($needle)) {
        $failures.Add($message)
    }
}

function Get-MethodBlock([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) {
        throw "Could not locate method '$signature'."
    }

    $open = $source.IndexOf('{', $start)
    if ($open -lt 0) {
        throw "Could not locate opening brace for '$signature'."
    }

    $depth = 0
    for ($index = $open; $index -lt $source.Length; $index++) {
        if ($source[$index] -eq '{') {
            $depth++
        }
        elseif ($source[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) {
                return $source.Substring($start, $index - $start + 1)
            }
        }
    }

    throw "Could not locate closing brace for '$signature'."
}

$runner = Read-Source `
    'Code\core\performance\AWCooperativeSimulationRunner.cs'
$pathBridge = Read-Source `
    'Code\core\pathfinding\AWPathMovementBridge.cs'
$workerPool = Read-Source `
    'Code\core\performance\AWSimulationWorkerPool.cs'
$actorPost = Read-Source `
    'Code\core\performance\AWCooperativeActorPostRunner.cs'
$frameSchedulerPatch = Read-Source `
    'Code\patch\AW_FramePrioritySchedulerPatch.cs'
$armySchedulerPatch = Read-Source `
    'Code\patch\AW_ArmyRtsSchedulerPatch.cs'

Require-Contains $runner 'Aw3RtsLogicalPulse' `
    'Cultiway extraction must retain the AW3 RTS logical pulse.'

$hasOwnership = Get-MethodBlock $pathBridge `
    'internal static bool HasOwnership(Actor pActor)'
Forbid-Contains $hasOwnership '.Poll(' `
    'HasOwnership must not consume path results.'
Forbid-Contains $hasOwnership 'OpenReadyCursor(' `
    'HasOwnership must not open a path result cursor.'

$describeRuntimeState = Get-MethodBlock $pathBridge `
    'internal static string DescribeRuntimeState(Actor pActor)'
Forbid-Contains $describeRuntimeState '.Poll(' `
    'DescribeRuntimeState must not consume path results.'
Forbid-Contains $describeRuntimeState 'OpenReadyCursor(' `
    'DescribeRuntimeState must not open a path result cursor.'
Require-Contains $describeRuntimeState 'finder.ReadState(actorId)' `
    'DescribeRuntimeState must use a read-only finder snapshot.'

Require-Contains $workerPool 'AWSimulationWorkerDispatchGate' `
    'Persistent workers must retain generation-gated dispatch.'
Require-Contains $workerPool '_dispatchGate.Assign(' `
    'Worker dispatch must publish an operation generation.'
Require-Contains $workerPool '_dispatchGate.Consume(' `
    'Worker wakeups must consume exactly one generation token.'
Require-Contains $workerPool '_activeGeneration' `
    'Worker participation must remain bound to the active generation.'
Require-Contains $workerPool `
    'result.ExecutedItems != result.ScheduledItems' `
    'Persistent workers must reject incomplete operations.'
Require-Contains $workerPool `
    'Simulation worker did not execute all scheduled work:' `
    'Incomplete worker operations must remain a hard failure.'

$workerLoop = Get-MethodBlock $workerPool `
    'private void WorkerLoop(object pState)'
Require-Contains $workerLoop '_dispatchGate.Consume(workerIndex)' `
    'A signaled worker must consume a generation before executing work.'

Forbid-Contains $actorPost 'tileActionWorkItemAction' `
    'Tile-action classification must not retain a worker delegate.'
Forbid-Contains $actorPost 'tileActionTicket' `
    'Tile-action classification must not retain a worker ticket.'
Forbid-Contains $actorPost 'PostStage.ScheduleTileAction' `
    'Tile-action work must proceed directly to bounded main-thread commits.'
Forbid-Contains $actorPost 'PostStage.AwaitTileAction' `
    'Tile-action work must not await a worker that reads live actors.'

$tileCommit = Get-MethodBlock $actorPost `
    'private void CommitTileActionWorkItem(int index)'
Require-Contains $tileCommit 'CanSkipSafeGroundTileAction(' `
    'Tile-action classification must occur in the main-thread commit.'

$tileSafety = Get-MethodBlock $actorPost `
    'private static bool CanSkipSafeGroundTileAction('
Require-Contains $tileSafety 'actor == null' `
    'Tile-action classification must reject a missing actor.'
Require-Contains $tileSafety 'actor.current_tile' `
    'Tile-action classification must validate the current tile.'
Require-Contains $tileSafety 'actor.asset' `
    'Tile-action classification must validate the actor asset.'
Require-Contains $tileSafety 'tile.tile_id < fires.Length' `
    'Tile-action classification must bounds-check the fire array.'

foreach ($message in @(
    'AW MapBox.Update failed; scheduler stopped and game paused:',
    'AW native authority cycle failed; game paused:',
    'AW background simulation/presentation boundary failed;')) {
    $messageIndex = $frameSchedulerPatch.IndexOf(
        $message, [StringComparison]::Ordinal)
    if ($messageIndex -lt 0) {
        $failures.Add("Missing forced-pause scheduler message: $message")
        continue
    }
    $prefixStart = [Math]::Max(0, $messageIndex - 160)
    $prefix = $frameSchedulerPatch.Substring(
        $prefixStart, $messageIndex - $prefixStart)
    if (-not $prefix.Contains('ModClass.LogError(')) {
        $failures.Add("Forced-pause scheduler fault is not LogError: $message")
    }
}

$rtsMessage = 'AW native Army RTS scheduling failed; game paused:'
$rtsIndex = $armySchedulerPatch.IndexOf(
    $rtsMessage, [StringComparison]::Ordinal)
if ($rtsIndex -lt 0) {
    $failures.Add('Missing forced-pause native RTS scheduling message.')
}
else {
    $rtsPrefixStart = [Math]::Max(0, $rtsIndex - 160)
    $rtsPrefix = $armySchedulerPatch.Substring(
        $rtsPrefixStart, $rtsIndex - $rtsPrefixStart)
    if (-not $rtsPrefix.Contains('ModClass.LogError(')) {
        $failures.Add(
            'Forced-pause native RTS scheduling fault must use LogError.')
    }
}

$chunkMembership = Read-Source `
    'Code\core\performance\AWIncrementalChunkActorMembership.cs'
Require-Contains $chunkMembership 'internal static void Validate(' `
    'Incremental chunk membership must retain exact validation.'
Require-Contains $chunkMembership `
    'totalUnits = container.units_all.Count;' `
    'Chunk totals must be repaired from the canonical units list.'

$zonePatch = Read-Source 'Code\patch\AW_SimObjectsZonesPatch.cs'
Require-Contains $zonePatch 'FallBackToVanilla(' `
    'Spatial optimization faults must preserve a vanilla rebuild fallback.'
Require-Contains $zonePatch `
    'AWIncrementalSimObjectZoneUnits.Invalidate();' `
    'Spatial fallback must invalidate incremental membership.'
Require-Contains $zonePatch `
    'AWParallelSimObjectZoneUnits.Invalidate();' `
    'Spatial fallback must invalidate parallel rebuild state.'

if ($failures.Count -gt 0) {
    Write-Output "Cultiway advanced extraction guard failures: $($failures.Count)"
    foreach ($failure in $failures) {
        Write-Output " - $failure"
    }
    exit 1
}

Write-Output 'Cultiway advanced extraction source guard passed.'

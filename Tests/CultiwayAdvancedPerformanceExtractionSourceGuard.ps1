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

Require-Contains $runner 'Aw3RtsLogicalPulse' `
    'Cultiway extraction must retain the AW3 RTS logical pulse.'

$hasOwnership = Get-MethodBlock $pathBridge `
    'internal static bool HasOwnership(Actor pActor)'
Forbid-Contains $hasOwnership '.Poll(' `
    'HasOwnership must not consume path results.'
Forbid-Contains $hasOwnership 'OpenReadyCursor(' `
    'HasOwnership must not open a path result cursor.'

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

if ($failures.Count -gt 0) {
    Write-Output "Cultiway advanced extraction guard failures: $($failures.Count)"
    foreach ($failure in $failures) {
        Write-Output " - $failure"
    }
    exit 1
}

Write-Output 'Cultiway advanced extraction source guard passed.'

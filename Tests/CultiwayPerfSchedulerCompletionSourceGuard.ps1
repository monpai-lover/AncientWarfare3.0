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

function Require-Match(
    [string]$text,
    [string]$pattern,
    [string]$message) {
    if (-not [Text.RegularExpressions.Regex]::IsMatch(
            $text,
            $pattern,
            [Text.RegularExpressions.RegexOptions]::Singleline)) {
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

$coordinator = Read-Source `
    'Code\core\performance\AWSimulationCoordinatorThread.cs'
Require-Contains $coordinator 'private long _completedOperations' `
    'Coordinator must retain completed operation count for scheduler diagnostics.'
Require-Contains $coordinator 'private long _completedWallTicks' `
    'Coordinator must retain completed wall time for scheduler diagnostics.'
Require-Contains $coordinator 'private long _completedWaitTicks' `
    'Coordinator must retain main-thread wait time for scheduler diagnostics.'
Require-Contains $coordinator 'internal string GetDiagnostics()' `
    'Coordinator must expose diagnostics like the Cultiway perf baseline.'
Require-Contains $coordinator 'Interlocked.Increment(ref _completedOperations)' `
    'Coordinator must record completed operations.'
Require-Contains $coordinator 'Interlocked.Add(ref _completedWallTicks' `
    'Coordinator must record completed wall time.'
Require-Contains $coordinator 'Interlocked.Add(ref _completedWaitTicks' `
    'Coordinator must record main-thread wait time.'

$runtimeDiagnostic = Read-Source `
    'Code\core\policy\RuntimePerformanceDiagnostic.cs'
Require-Contains $runtimeDiagnostic 'AWSimulationCoordinatorThread.Instance.GetDiagnostics()' `
    'Runtime performance diagnostics must include coordinator wait statistics.'
$runFrame = Get-MethodBlock $runner `
    'public void RunFrame(MapBox pMap, bool pAllowNewCycles = true)'
Require-Match $runFrame 'bool\s+actorBackgroundPending\s*=\s*_actorRunner\.WaitingForBackgroundWork\s*&&\s*!_actorRunner\.IsBackgroundWorkCompleted' `
    'Completed Actor post work must return to Step() for consumption.'
Require-Match $runFrame 'bool\s+buildingBackgroundPending\s*=\s*_buildingRunner\.WaitingForBackgroundWork\s*&&\s*!_buildingRunner\.IsBackgroundWorkCompleted' `
    'Completed building background work must return to Step() for consumption.'

$batchRunner = Read-Source `
    'Code\core\performance\AWCooperativeBatchRunner.cs'
Require-Contains $batchRunner `
    'private int FindNextMainThreadBatchIndex(RunnerStage pJobStage)' `
    'Batch phase naming must identify the next concrete main-thread batch.'
Require-Contains $batchRunner `
    '".batch_group." + nextBatchIndex' `
    'Batch phase naming must identify the concrete parallel job group.'
Require-Contains $batchRunner `
    '".parallel.batch." +' `
    'Native batch phase naming must identify the concrete batch index.'

$actorReadBoundary = Get-MethodBlock $runner `
    'public bool EnsureActorReadBoundary(string pReason)'
Require-Contains $actorReadBoundary @'
if (_actorRunner.HasParallelPresentationWorkInFlight)
'@ 'Actor read boundaries must commit mutating presentation work.'
Require-Contains $actorReadBoundary @'
if (!_actorRunner.WaitingForBackgroundWork)
'@ 'Actor read boundaries must also inspect Actor post worker work.'
Require-Contains $actorReadBoundary @'
_actorRunner.WaitForBackgroundWork();
'@ 'Actor read boundaries must wait for Actor post worker work before live reads.'
Require-Contains $actorReadBoundary @'
Stopwatch.GetTimestamp() - waitStartedAt;
'@ 'Actor post read-boundary waits must remain observable in diagnostics.'

$schedulerPatch = Read-Source `
    'Code\patch\AW_FramePrioritySchedulerPatch.cs'
$takeOver = Get-MethodBlock $schedulerPatch `
    'private static bool TakeOverMainSimulation(MapBox __instance,'
Require-Match $takeOver '!AWPerformanceSettings\.EnableFramePriorityScheduler\s*&&\s*!runner\.Active' `
    'Disabling the scheduler may release control only at a cycle boundary.'
Require-Match $takeOver 'runner\.RunFrame\(__instance,\s*AWPerformanceSettings\.EnableFramePriorityScheduler\)' `
    'The scheduler setting must control new-cycle admission, not an active cycle.'
Forbid-Contains $takeOver 'runner.RunFrame(__instance);' `
    'TakeOverMainSimulation must pass the new-cycle admission decision explicitly.'

$disabledGate = $takeOver.IndexOf(
    'if (!AWPerformanceSettings.EnableFramePriorityScheduler',
    [StringComparison]::Ordinal)
foreach ($boundary in @(
        'if (SmoothLoader.isLoading())',
        'if (AW3MultiplayerReplicaScope.IsReplicaSession)',
        'if (runner.RequiresControl &&')) {
    $boundaryIndex = $takeOver.IndexOf(
        $boundary, [StringComparison]::Ordinal)
    if ($boundaryIndex -lt 0 -or $boundaryIndex -gt $disabledGate) {
        $failures.Add(
            "Scheduler abort boundary '$boundary' must remain explicit before disabled admission handling.")
    }
    else {
        $boundaryBody = Get-MethodBlock $takeOver $boundary
        Require-Contains $boundaryBody 'ResetSchedulerState(' `
            "Scheduler abort boundary '$boundary' must explicitly reset an active cycle."
    }
}

foreach ($method in @(
        'private static bool PrepareBuildingPresentationFrame(',
        'private static bool PreparePresentationFrame(')) {
    $body = Get-MethodBlock $schedulerPatch $method
    Require-Match $body '!AWPerformanceSettings\.EnableFramePriorityScheduler\s*&&\s*!runner\.Active\s*&&\s*!runner\.HasMutatingPresentationWorkInFlight' "$method must keep read-boundary protection until active and mutating work finishes."
    Forbid-Contains $body @'
if (!AWPerformanceSettings.EnableFramePriorityScheduler)
'@ "$method must not return to native presentation while scheduler work is active."
}

$boatPatch = Read-Source 'Code\patch\AW_ActorBoatLifecyclePatch.cs'
foreach ($method in @(
        'private static void EmbarkIntoPostfix(Actor __instance)',
        'private static void ExitBoatPostfix(Actor __instance)',
        'private static void ClearManagersPostfix(Actor __instance)',
        'private static void DisposePrefix(Actor __instance)')) {
    $body = Get-MethodBlock $boatPatch $method
    Require-Contains $body 'AWInsideBoatActorIndex.Notify(' `
        "$method must keep the boat index current in native mode."
    Forbid-Contains $body 'EnableFramePriorityScheduler' `
        "$method must not gate boat-index lifecycle notifications."
}

foreach ($method in @(
        'private static bool CheckInsideBatchPrefix(',
        'private static bool CheckInsidePrefix(Actor __instance)')) {
    $body = Get-MethodBlock $boatPatch $method
    Require-Contains $body 'EnableFramePriorityScheduler' `
        "$method must leave the native checkInside path enabled when the scheduler is off."
}

$clearWorld = Get-MethodBlock $boatPatch `
    'private static void ClearWorldPrefix()'
Require-Contains $clearWorld 'AWInsideBoatActorIndex.Reset();' `
    'MapBox.clearWorld must reset the boat index.'

$actorParallel = Read-Source `
    'Code\core\performance\AWCooperativeActorParallelJobRunner.cs'
$timerActor = Get-MethodBlock $actorParallel `
    'private void RunTimerActor(Actor pActor)'
Require-Contains $timerActor @'
if (pActor._precalc_movement_speed_skips > 0)
'@ 'Moving Actor speed calculation must preserve the native skip counter.'
Require-Contains $timerActor @'
pActor._precalc_movement_speed_skips--;
'@ 'Moving Actor speed skips must count down instead of recalculating every tick.'
Require-Contains $actorParallel `
    'internal static void RefreshFrameVisibility()' `
    'Skipped visibility jobs require one explicit visibility refresh per render frame.'
Require-Contains $actorParallel `
    'lastVisibilityFrame == UnityEngine.Time.frameCount' `
    'Actor visibility refresh must be idempotent within one render frame.'

$beforeMapUpdate = Get-MethodBlock $schedulerPatch `
    'private static void BeforeMapBoxUpdate(MapBox __instance,'
Require-Match $beforeMapUpdate 'EnsureActorReadBoundary\("mapbox\.frame_begin"\);\s*EnsureBuildingReadBoundary\("mapbox\.frame_begin"\);' 'Frame visibility refresh must follow Actor and Building read barriers.'
Require-Contains $beforeMapUpdate `
    'AWCooperativeActorParallelJobRunner.RefreshFrameVisibility();' `
    'MapBox frame start must refresh Actor visibility before timer workers consume it.'
$actorBoundaryIndex = $beforeMapUpdate.IndexOf(
    'EnsureActorReadBoundary("mapbox.frame_begin");',
    [StringComparison]::Ordinal)
$visibilityRefreshIndex = $beforeMapUpdate.IndexOf(
    'AWCooperativeActorParallelJobRunner.RefreshFrameVisibility();',
    [StringComparison]::Ordinal)
if ($visibilityRefreshIndex -lt 0 -or
    $actorBoundaryIndex -lt 0 -or
    $visibilityRefreshIndex -lt $actorBoundaryIndex) {
    $failures.Add(
        'Actor visibility refresh must run after the frame-start Actor read barrier.')
}

$workerPoolPath = Join-Path $root `
    'Code\core\performance\AWSimulationWorkerPool.cs'
if (-not [IO.File]::Exists($workerPoolPath)) {
    $failures.Add('AW3 must own a persistent simulation worker pool.')
}
else {
    $workerPool = [IO.File]::ReadAllText(
        $workerPoolPath, [Text.Encoding]::UTF8)
    foreach ($contract in @(
            'internal WorkResult RunIndexed(',
            'internal WorkTicket BeginIndexed(',
            'internal bool TryAssistActiveOperation()',
            'private void WorkerLoop(',
            'if (_operationActive)',
            'IsBackground = true')) {
        Require-Contains $workerPool $contract `
            "Simulation worker pool is missing contract: $contract"
    }

    $workerDiscard = Get-MethodBlock $workerPool `
        'internal void WaitAndDiscard(WorkTicket pTicket)'
    Require-Contains $workerDiscard 'TryWait(pTicket,' `
        'Worker-pool teardown must use bounded diagnostic waits.'
    Require-Contains $workerDiscard 'catch (Exception error)' `
        'Worker-pool teardown must retain the discarded operation error.'
    Require-Contains $workerDiscard 'ModClass.LogInfo(' `
        'Worker-pool teardown must report stalls and discarded errors.'
}

$coordinator = Read-Source `
    'Code\core\performance\AWSimulationCoordinatorThread.cs'
Require-Match $coordinator `
    'AWSimulationWorkerPool\.Instance\s*\.TryAssistActiveOperation\(\)' `
    'Coordinator waits must assist unfinished persistent-worker items.'
$coordinatorDiscard = Get-MethodBlock $coordinator `
    'internal void WaitAndDiscard(WorkTicket pTicket)'
Require-Contains $coordinatorDiscard 'TryWait(pTicket,' `
    'Coordinator teardown must use bounded diagnostic waits.'
Require-Contains $coordinatorDiscard 'catch (Exception error)' `
    'Coordinator teardown must retain the discarded operation error.'
Require-Contains $coordinatorDiscard 'ModClass.LogInfo(' `
    'Coordinator teardown must report stalls and discarded errors.'
$coordinatorTryWait = Get-MethodBlock $coordinator `
    'internal bool TryWait(WorkTicket pTicket,'
Require-Contains $coordinatorTryWait `
    'TryAssistActiveOperationUntil(deadline)' `
    'Coordinator timed waits must not overrun their assistance deadline.'

$actorPost = Read-Source `
    'Code\core\performance\AWCooperativeActorPostRunner.cs'
Require-Contains $actorPost `
    'AWSimulationWorkerPool.Instance.BeginIndexed(' `
    'Actor enemy search must be admitted directly to the simulation pool.'
Forbid-Contains $actorPost 'AWSimulationCoordinatorThread' `
    'Actor post work must not consume the presentation coordinator.'
foreach ($contract in @(
        'AWDeferredPathRequestBatch.StartCycle();',
        'AWDeferredPathRequestBatch.BeginCapture();',
        'AWDeferredPathRequestBatch.EndCapture();',
        'AWDeferredPathRequestBatch.CompleteCycle();',
        'AWDeferredPathRequestBatch.AbortCycle();')) {
    Require-Contains $actorPost $contract `
        "Actor post path batching is missing lifecycle contract: $contract"
}

$stageDiagnostics = Read-Source `
    'Code\core\performance\AWSchedulerStageDiagnostics.cs'
foreach ($contract in @(
        'internal enum AWSchedulerStageBucket',
        'internal static long BeginSchedulerFrame()',
        'internal static void EndSchedulerFrame(long pStarted)',
        'internal static long Begin(',
        'internal static void BeginFrame(bool pSampling)',
        'internal static void End(',
        'internal static AWSchedulerStageDiagnosticSnapshot TakeSnapshot()',
        'internal long TotalTicks',
        'internal long UnaccountedTicks')) {
    Require-Contains $stageDiagnostics $contract `
        "Scheduler stage diagnostics are missing contract: $contract"
}
Require-Contains $runner 'AWSchedulerStageDiagnostics.Begin(' `
    'Each cooperative scheduler stage must enter an exclusive wall-time bucket.'
Require-Contains $runner 'AWSchedulerStageDiagnostics.End(' `
    'Each cooperative scheduler stage must close its exclusive wall-time bucket.'

$runtimeDiagnostics = Read-Source `
    'Code\core\policy\RuntimePerformanceDiagnostic.cs'
Require-Contains $runtimeDiagnostics `
    'AWSchedulerStageDiagnostics.BeginFrame(_sampling);' `
    'Exclusive scheduler buckets must share the sampled-frame boundary.'
foreach ($contract in @(
        'scheduler_wall_ms=',
        'scheduler_stage_ms={',
        'scheduler_stage_unaccounted_ms=',
        'host_unaccounted_ms=')) {
    Require-Contains $runtimeDiagnostics $contract `
        "Runtime diagnostics are missing exclusive-stage output: $contract"
}

$deferredPathBatch = Read-Source `
    'Code\core\performance\AWDeferredPathRequestBatch.cs'
foreach ($contract in @(
        'DefaultCapacity',
        'PendingSlots',
        'internal static bool TryCapture(',
        'internal static int FlushAtFrameStart()',
        'AWPathMovementBridge.Submit(')) {
    Require-Contains $deferredPathBatch $contract `
        "Deferred path batching is missing contract: $contract"
}
$pathPatch = Read-Source 'Code\patch\AW_GlobalPathfindingPatch.cs'
Require-Contains $pathPatch 'AWDeferredPathRequestBatch.TryCapture(' `
    'Actor.goTo must use deferred path capture while a scheduler cycle owns it.'
$frameStart = Get-MethodBlock $schedulerPatch `
    'private static void BeforeMapBoxUpdate(MapBox __instance,'
$frameBarrier = $frameStart.IndexOf(
    'EnsureActorReadBoundary("mapbox.frame_begin");',
    [StringComparison]::Ordinal)
$pathFlush = $frameStart.IndexOf(
    'AWDeferredPathRequestBatch.FlushAtFrameStart();',
    [StringComparison]::Ordinal)
if ($frameBarrier -lt 0 -or $pathFlush -le $frameBarrier) {
    $failures.Add(
        'Deferred path requests must flush after the frame-start read barrier.')
}

$spatialPatch = Read-Source 'Code\patch\AW_SimObjectsZonesPatch.cs'
foreach ($relativePath in @(
        'Code\core\performance\AWActorZoneMembershipDirtyIndex.cs',
        'Code\core\performance\AWIncrementalChunkActorMembership.cs',
        'Code\core\performance\AWParallelIslandActorMembership.cs',
        'Code\core\performance\AWParallelSimObjectZoneUnits.cs',
        'Code\core\performance\AWIncrementalSimObjectZoneUnits.cs')) {
    $spatialSource = Read-Source $relativePath
    Require-Contains $spatialSource 'AW' `
        "Spatial membership source is missing: $relativePath"
}
Require-Contains $spatialPatch 'TrySkipRedundantCheckUnits' `
    'SimObjectsZones must skip only a validated current membership rebuild.'
Require-Contains $spatialPatch 'fullClear' `
    'Spatial membership must invalidate on full world clear.'
Require-Contains $spatialPatch 'HasPending' `
    'SimObjectsZones skip must reject pending actor spatial dirty records.'
Require-Contains $spatialSource 'HasPending' `
    'Actor spatial dirty index must expose a non-consuming pending check.'
Require-Match $spatialPatch `
    '!AWActorZoneMembershipDirtyIndex\.HasPending\(\)' `
    'Redundant checkUnits suppression must require an empty dirty index.'

$chunkMembership = Read-Source `
    'Code\core\performance\AWIncrementalChunkActorMembership.cs'
$changeKingdom = Get-MethodBlock $chunkMembership `
    'internal static void ChangeKingdom('
Require-Contains $changeKingdom 'RemoveActorFromKingdomLists(' `
    'ChangeKingdom must remove stale and duplicate kingdom memberships before insertion.'
Require-Contains $changeKingdom 'EnsureKingdom(' `
    'ChangeKingdom must recreate a missing destination kingdom list.'
Require-Contains $changeKingdom 'InsertActorAtRank(' `
    'ChangeKingdom must preserve deterministic World.units rank ordering.'
Forbid-Contains $changeKingdom 'throw new InvalidOperationException' `
    'ChangeKingdom must not pause the simulation for a recoverable stale membership.'
$removeKingdomMembership = Get-MethodBlock $chunkMembership `
    'private static int RemoveActorFromKingdomLists('
Require-Contains $removeKingdomMembership 'oldKingdomId' `
    'Kingdom migration must use the old kingdom as the fast-path lookup.'
$removeActorReferences = Get-MethodBlock $chunkMembership `
    'private static int RemoveActorReferences('
Require-Contains $removeActorReferences 'ReferenceEquals(' `
    'Stale kingdom membership cleanup must remove by Actor identity.'
Require-Contains $removeActorReferences 'RemoveAt(' `
    'Stale kingdom membership cleanup must remove every duplicate entry.'

$pathMovement = Read-Source `
    'Code\core\pathfinding\AWPathMovementBridge.cs'
foreach ($contract in @(
        'internal enum AWParallelPathMovementResult',
        'internal static AWParallelPathMovementResult',
        'TryRunParallelSafePathMovement(',
        'CommitPreparedPathMovement(',
        'TryRunParallelSafeSmoothMovement(',
        'CommitPreparedSmoothMovement(')) {
    Require-Contains $pathMovement $contract `
        "Path movement bridge is missing Cultiway prepare/commit contract: $contract"
}
$setMoveStepTile = Get-MethodBlock $pathMovement `
    'private static bool SetMoveStepTile('
Require-Contains $setMoveStepTile 'SetCurrentTile(pActor, pTile)' `
    'Path movement must route direct tile changes through the Cultiway tile helper.'
$setCurrentTile = Get-MethodBlock $pathMovement `
    'private static void SetCurrentTile('
Require-Contains $setCurrentTile 'AWActorZoneMembershipDirtyIndex.Mark(' `
    'Tile changes must publish spatial dirty state after committing.'

$actorPostPath = Get-MethodBlock $actorPost `
    'private void CommitPathMovementWorkItem(int index)'
Require-Contains $actorPostPath 'work.Fallback' `
    'Path movement commit must use the fallback only for dirty/unpartitioned containers.'
Require-Contains $actorPostPath 'CommitPreparedPathMovement(' `
    'Path movement commit must apply prepared worker results on the simulation thread.'
Forbid-Contains $actorPostPath 'job.job_updater();' `
    'Path movement commit must not rerun the entire vanilla job after worker preparation.'

$actorPostSmooth = Get-MethodBlock $actorPost `
    'private void CommitSmoothMovementWorkItem(int index)'
Require-Contains $actorPostSmooth 'CommitPreparedSmoothMovement(' `
    'Smooth movement commit must apply prepared worker results on the simulation thread.'
Forbid-Contains $actorPostSmooth 'job.job_updater();' `
    'Smooth movement commit must not rerun the entire vanilla job after worker preparation.'

foreach ($relativePath in @(
        'Code\core\performance\AWDirtyMetaActorIndex.cs',
        'Code\core\performance\AWActorPresentationSnapshot.cs')) {
    $parallelSource = Read-Source $relativePath
    Forbid-Contains $parallelSource 'Parallel.For(' `
        "$relativePath must use the persistent simulation worker pool."
    Require-Contains $parallelSource 'AWSimulationWorkerPool.Instance.RunIndexed(' `
        "$relativePath is missing persistent worker-pool admission."
}

$actorSpatialPatch = Read-Source `
    'Code\patch\AW_ActorSpatialMembershipPatch.cs'
$setTilePrefix = Get-MethodBlock $actorSpatialPatch `
    'private static void SetCurrentTile_Prefix('
Require-Contains $setTilePrefix 'out WorldTile __state' `
    'Spatial dirty tracking must capture the previous tile before setCurrentTile.'
$setTilePostfix = Get-MethodBlock $actorSpatialPatch `
    'private static void SetCurrentTile_Postfix('
Require-Contains $setTilePostfix 'ReferenceEquals(__state, pTile)' `
    'Spatial dirty tracking must only mark an actual tile change.'
Forbid-Contains $actorSpatialPatch 'setCurrentTilePosition' `
    'setCurrentTilePosition must not dirty spatial membership for same-tile movement.'
Forbid-Contains $actorSpatialPatch '[HarmonyPatch(typeof(Actor), "dispose")]' `
    'Actor spatial membership must not target the unavailable Actor.dispose method.'

foreach ($relativePath in @(
        'Code\core\performance\AWCooperativeBatchRunner.cs',
        'Code\core\performance\AWCooperativeActorParallelJobRunner.cs',
        'Code\core\performance\AWCooperativeActorPostRunner.cs',
        'Code\core\performance\AWCooperativeWorldMaintenanceRunner.cs')) {
    $schedulerSource = Read-Source $relativePath
    Forbid-Contains $schedulerSource 'Parallel.For(' `
        "$relativePath must use the persistent simulation worker pool instead of TPL."
}

if ($failures.Count -gt 0) {
    Write-Output "Cultiway scheduler completion source guard failures: $($failures.Count)"
    foreach ($failure in $failures) {
        Write-Output " - $failure"
    }
    exit 1
}

Write-Output 'Cultiway scheduler completion source guard passed.'

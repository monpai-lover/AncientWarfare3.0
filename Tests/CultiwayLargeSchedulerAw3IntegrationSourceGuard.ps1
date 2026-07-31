$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot `
    'CultiwayLargeSchedulerSourceGuardCommon.ps1')

$sources = [ordered]@{
    Runner = Read-Source `
        'Code/core/performance/AWCooperativeSimulationRunner.cs'
    Patch = Read-Source `
        'Code/patch/AW_FramePrioritySchedulerPatch.cs'
    Ownership = Read-Source `
        'Code/core/performance/AWSchedulerResourceOwnership.cs'
    Batch = Read-Source `
        'Code/core/performance/AWCooperativeBatchRunner.cs'
    Coordinator = Read-Source `
        'Code/core/performance/AWSimulationCoordinatorThread.cs'
    Snapshot = Read-Source `
        'Code/core/performance/AWActorPresentationSnapshot.cs'
    ActorRenderer = Read-Source `
        'Code/core/performance/AWActorPresentationRenderer.cs'
    WorldRenderer = Read-Source `
        'Code/core/performance/AWWorldObjectPresentationRenderer.cs'
    Overlays = Read-Source `
        'Code/core/performance/AWActorPresentationOverlays.cs'
    Transient = Read-Source `
        'Code/core/performance/AWActorTransientPresentationFrame.cs'
    Commands = Read-Source `
        'Code/core/performance/AWPresentationCommandQueue.cs'
    Visibility = Read-Source `
        'Code/core/performance/AWPresentationVisibility.cs'
    Interpolator = Read-Source `
        'Code/core/performance/AWPresentationInterpolator.cs'
    Cursor = Read-Source `
        'Code/core/performance/AWCursorPresentationLifecycle.cs'
    CursorPatch = Read-Source `
        'Code/patch/AW_CursorPresentationLifecyclePatch.cs'
    Governor = Read-Source `
        'Code/core/performance/AWFramePriorityGovernor.cs'
    Settings = Read-Source `
        'Code/core/performance/AWPerformanceSettings.cs'
    StepContext = Read-Source `
        'Code/core/performance/AWSimulationStepContext.cs'
    SimulationTime = Read-Source `
        'Code/core/performance/AWSimulationTime.cs'
    StatusClock = Read-Source `
        'Code/core/performance/AWStatusPresentationAnimationClock.cs'
    TimeRate = Read-Source `
        'Code/core/performance/AWWorldTimeRateTracker.cs'
    InsideBoat = Read-Source `
        'Code/core/performance/AWInsideBoatActorIndex.cs'
    Rules = Read-Source `
        'Code/core/performance/AWFrameSchedulerRules.cs'
    PathfindingBootstrap = Read-Source `
        'Code/core/pathfinding/AWPathfindingBootstrap.cs'
    ArmyRoutes = Read-Source `
        'Code/core/pathfinding/ArmyRouteProvider.cs'
}

function Compress-Source([string]$Text) {
    return [regex]::Replace($Text, '\s+', '')
}

function Get-SourceRegion([string]$Name, [string]$Text,
    [string]$StartNeedle, [string]$EndNeedle) {
    $startIndex = $Text.IndexOf(
        $StartNeedle, [System.StringComparison]::Ordinal)
    if ($startIndex -lt 0) {
        $script:GuardFailures.Add(
            "${Name}: missing start marker '$StartNeedle'")
        return ''
    }

    $endIndex = $Text.IndexOf(
        $EndNeedle,
        $startIndex + $StartNeedle.Length,
        [System.StringComparison]::Ordinal)
    if ($endIndex -lt 0) {
        $script:GuardFailures.Add(
            "${Name}: missing end marker '$EndNeedle'")
        return ''
    }

    return $Text.Substring($startIndex, $endIndex - $startIndex)
}

function Require-Regex([string]$Name, [string]$Text,
    [string]$Pattern) {
    if (-not [regex]::IsMatch(
            $Text,
            $Pattern,
            [System.Text.RegularExpressions.RegexOptions]::Singleline)) {
        $script:GuardFailures.Add(
            "${Name}: pattern not found '$Pattern'")
    }
}

function Require-Sequence([string]$Name, [string]$Text,
    [string[]]$Needles) {
    $cursor = 0
    foreach ($needle in $Needles) {
        $index = $Text.IndexOf(
            $needle,
            $cursor,
            [System.StringComparison]::Ordinal)
        if ($index -lt 0) {
            $script:GuardFailures.Add(
                "${Name}: missing or out of order '$needle'")
            return
        }
        $cursor = $index + $needle.Length
    }
}

function Test-IntegrationSources(
    [System.Collections.IDictionary]$SourceSet) {
    $runner = $SourceSet['Runner']
    $patchSource = $SourceSet['Patch']
    $ownership = $SourceSet['Ownership']
    $batch = $SourceSet['Batch']
    $snapshot = $SourceSet['Snapshot']
    $rules = $SourceSet['Rules']
    $settings = $SourceSet['Settings']

    $runnerCompact = Compress-Source $runner
    $patchCompact = Compress-Source $patchSource
    $rulesCompact = Compress-Source $rules
    $settingsCompact = Compress-Source $settings
    $ownershipCompact = Compress-Source $ownership
    $batchCompact = Compress-Source $batch
    $snapshotCompact = Compress-Source $snapshot

    $takeOver = Compress-Source (Get-SourceRegion `
        'scheduler takeover' $patchSource `
        'private static bool TakeOverMainSimulation(' `
        'private static void RunNativeAuthorityAfterSimulation(')
    $nativeAuthority = Compress-Source (Get-SourceRegion `
        'native authority postfix' $patchSource `
        'private static void RunNativeAuthorityAfterSimulation(' `
        'private static bool DeferAutoSaveUntilCycleBoundary(')
    $beforeMapUpdate = Compress-Source (Get-SourceRegion `
        'map update prefix' $patchSource `
        'private static void BeforeMapBoxUpdate(' `
        'private static void AfterMapBoxUpdate(')
    $saveBoundary = Compress-Source (Get-SourceRegion `
        'save boundary' $patchSource `
        'internal static void DrainSimulationToSaveBoundary()' `
        'private static void AbortBeforeWorldLoad()')
    $loadReset = Compress-Source (Get-SourceRegion `
        'world load reset' $patchSource `
        'private static void AbortBeforeWorldLoad()' `
        'private static void AbortBeforeWorldClear()')
    $clearReset = Compress-Source (Get-SourceRegion `
        'world clear reset' $patchSource `
        'private static void AbortBeforeWorldClear()' `
        'private static void ResetAfterWorldCreation(')
    $worldCreation = Compress-Source (Get-SourceRegion `
        'world creation reset' $patchSource `
        'private static void ResetAfterWorldCreation(' `
        'private static void EnsureSimulationTimeBound(')
    $clockBinding = Compress-Source (Get-SourceRegion `
        'lazy clock binding' $patchSource `
        'private static void EnsureSimulationTimeBound(' `
        'private static void ResetSchedulerState(')
    $cleanup = Compress-Source (Get-SourceRegion `
        'scheduler cleanup' $patchSource `
        'private static void ResetSchedulerState(' `
        'private static void RunCleanup(')
    $buildingPrepare = Compress-Source (Get-SourceRegion `
        'building snapshot prepare' $patchSource `
        'private static bool PrepareBuildingPresentationFrame(' `
        'private static bool PreparePresentationFrame(')
    $actorPrepare = Compress-Source (Get-SourceRegion `
        'actor snapshot prepare' $patchSource `
        'private static bool PreparePresentationFrame(' `
        'private static void UseSnapshotUnitCount(')
    $runFrame = Compress-Source (Get-SourceRegion `
        'runner frame' $runner `
        'public void RunFrame(' `
        'public bool TryBeginActorPresentationOverlap()')
    $stageCore = Compress-Source (Get-SourceRegion `
        'runner stage core' $runner `
        'private void ExecuteCurrentStageCore()' `
        'private void CompleteCycle()')
    $pausedRules = Compress-Source (Get-SourceRegion `
        'replica paused rules' $rules `
        'public static AWPausedFrameAction ResolvePausedFrameAction(' `
        'public static bool ShouldRunAuthorityCycle(')

    Require-Count 'one cooperative authority dispatch' $runnerCompact `
        'AWAuthorityCycleService.ProcessCooperativeCycle(' 1
    Require-Count 'one native authority dispatch' $patchCompact `
        'AWAuthorityCycleService.ProcessNativeCycle()' 1
    Require-Text 'cooperative authority stays in AW3 stage' $stageCore `
        'AWAuthorityCycleService.ProcessCooperativeCycle('

    Require-Text 'loading takeover freezes simulation' $takeOver `
        'if(SmoothLoader.isLoading()){ResetSchedulerState(pUnbindSimulationTime:false);returnfalse;}'
    Require-Text 'replica takeover freezes simulation' $takeOver `
        'if(AW3MultiplayerReplicaScope.IsReplicaSession){ResetSchedulerState(pUnbindSimulationTime:false);returnfalse;}'
    Require-Text 'disabled scheduler returns native without force' $takeOver `
        'if(!AWPerformanceSettings.EnableFramePriorityScheduler){ResetSchedulerState(pUnbindSimulationTime:false);__state=true;returntrue;}'
    Require-Sequence 'scheduler takeover boundary order' $takeOver @(
        'if(SmoothLoader.isLoading())',
        'if(AW3MultiplayerReplicaScope.IsReplicaSession)',
        'if(!AWPerformanceSettings.EnableFramePriorityScheduler)',
        'if(!runner.RequiresControl)',
        'EnsureSimulationTimeBound(__instance);',
        'runner.RunFrame(__instance);'
    )
    Require-Sequence 'native authority postfix gates' $nativeAuthority @(
        'if(AW3MultiplayerReplicaScope.IsReplicaSession)',
        'return;',
        'if(!__state)',
        'return;',
        'AWAuthorityCycleService.ProcessNativeCycle();'
    )

    Require-Text 'active replica resolves to abort' $pausedRules `
        'if(replicaSession)returnmodCycleActive?AWPausedFrameAction.AbortReplicaCycle:AWPausedFrameAction.RefreshPresentation;'
    Require-Text 'runner aborts an active replica cycle' $runFrame `
        'if(pausedAction==AWPausedFrameAction.AbortReplicaCycle){Abort();return;}'
    Require-Text 'runner freezes an idle replica frame' $runFrame `
        'if(pausedAction==AWPausedFrameAction.RefreshPresentation){_presentationRefresh.Request(pMap,UnityEngine.Time.frameCount);RestoreNativeParallelism();return;}'
    Require-Sequence 'replica exits precede runner admission' $runFrame @(
        'boolreplicaSession=AW3MultiplayerReplicaScope.IsReplicaSession;',
        'AWFrameSchedulerRules.ResolvePausedFrameAction(',
        'if(pausedAction==AWPausedFrameAction.AbortReplicaCycle)',
        'Abort();',
        'return;',
        'if(pausedAction==AWPausedFrameAction.RefreshPresentation)',
        'RestoreNativeParallelism();',
        'return;',
        '_resourceOwnership.Acquire(',
        'PrepareAdmissionCredits(',
        'CanAdmitCycle(',
        '_startAdmissionCycleAction'
    )

    Require-Count 'one frame snapshot request' $patchCompact `
        'AWActorPresentationSnapshots.RequestCapture()' 1
    Require-Text 'snapshot request excludes disabled and replica frames' `
        $beforeMapUpdate `
        'if(Config.game_loaded&&!SmoothLoader.isLoading()&&AWPerformanceSettings.EnableFramePriorityScheduler&&runner.RequiresControl&&!replicaSession){AWActorPresentationSnapshots.RequestCapture();}'
    Require-Count 'one snapshot capture point' $runnerCompact `
        'AWActorPresentationSnapshots.CaptureIfRequested(' 1
    Require-Count 'actor preparation acquires current snapshots' `
        $actorPrepare 'AWActorPresentationSnapshots.AcquireLatest()' 2
    Require-Count 'building preparation acquires current snapshots' `
        $buildingPrepare 'AWActorPresentationSnapshots.AcquireLatest()' 2
    Require-Sequence 'world creation unbinds before rebinding' `
        $worldCreation @(
            'ResetSchedulerState(',
            'pUnbindSimulationTime:true',
            'pForce:true',
            'if(__instance?.map_stats!=null)',
            'AWSimulationTime.BindWorld(__instance);'
        )
    Require-Text 'lazy clock binding is world aware' $clockBinding `
        'if(!AWSimulationTime.IsBound&&pMap?.map_stats!=null){AWSimulationTime.BindWorld(pMap);}'
    Require-Text 'world load unbinds scheduler clock' $loadReset `
        'ResetSchedulerState(pUnbindSimulationTime:true,pForce:true);'
    Require-Text 'world clear unbinds scheduler clock' $clearReset `
        'ResetSchedulerState(pUnbindSimulationTime:true,pForce:true);'
    Require-Text 'save drains to a cycle boundary' $saveBoundary `
        'AWCooperativeSimulationRunner.Instance.DrainToBoundary();'
    Require-Sequence 'scheduler cleanup dependency order' $cleanup @(
        'runner.Abort()',
        'AWPresentationCommandQueue.Clear()',
        'AWActorPresentationSnapshots.Reset()',
        'AWActorPresentationRenderer.Reset()',
        'AWWorldObjectPresentationRenderer.Reset()',
        'AWActorTransientPresentationFrame.Reset()',
        'AWPresentationInterpolator.Reset()',
        'AWCursorPresentationLifecycle.Reset()',
        'AWSimulationTime.',
        'AWAuthorityCycleService.Reset()',
        'AWFramePriorityGovernor.ResetFault()'
    )

    foreach ($prepare in @(
            @('actor disabled prepare', $actorPrepare),
            @('building disabled prepare', $buildingPrepare))) {
        Require-Sequence $prepare[0] $prepare[1] @(
            'if(!AWPerformanceSettings.EnableFramePriorityScheduler)',
            'returntrue;',
            'AWActorPresentationSnapshots.AcquireLatest()'
        )
    }

    Require-Text 'worker allocation exposes actor reservation' `
        $rulesCompact 'publicintActorPathWorkers{get;}'
    Require-Text 'army route allocation remains reserved' `
        $rulesCompact 'publicintArmyRouteWorkers{get;}'
    Require-Text 'worker allocation exposes foreground budget' `
        $rulesCompact 'publicintForegroundParallelism{get;}'
    Require-Text 'pathfinding budget remains bounded' $rulesCompact `
        'returnMath.Min(4,Math.Max(1,total/4));'
    Require-Text 'army route keeps one reserved path worker' `
        $rulesCompact `
        'intarmyRouteWorkers=pathWorkers>=2?1:0;'
    Require-Text 'actor path workers exclude army reservation' `
        $rulesCompact `
        'intactorPathWorkers=pathWorkers-armyRouteWorkers;'
    Require-Text 'foreground workers exclude all path workers' `
        $rulesCompact `
        'intforegroundParallelism=total-pathWorkers;'
    Require-Text 'allocation returns all worker partitions' `
        $rulesCompact `
        'returnnewAWPathWorkerAllocation(total,actorPathWorkers,armyRouteWorkers,foregroundParallelism);'
    Require-Count 'one shared processor allocation' `
        ($rulesCompact + $settingsCompact) `
        'AWFrameSchedulerRules.AllocateWorkers(Environment.ProcessorCount)' 1
    Require-Text 'settings preserve actor worker partition' `
        $settingsCompact `
        'publicstaticintActorPathfindingWorkerCount=>WorkerAllocation.ActorPathWorkers;'
    Require-Text 'settings preserve army worker partition' `
        $settingsCompact `
        'publicstaticintArmyRouteWorkerCount=>WorkerAllocation.ArmyRouteWorkers;'
    Require-Text 'AW3 pathfinding consumes actor reservation' `
        (Compress-Source $SourceSet['PathfindingBootstrap']) `
        'AWPerformanceSettings.ActorPathfindingWorkerCount'
    Require-Text 'AW3 army routes consume army reservation' `
        (Compress-Source $SourceSet['ArmyRoutes']) `
        'AWPerformanceSettings.ArmyRouteWorkerCount'

    Require-Text 'resource ownership accepts shared foreground budget' `
        $ownershipCompact `
        'publicvoidAcquire(TWorldpWorld,intpSchedulerParallelism)'
    Require-Sequence 'resource ownership saves and restores native budget' `
        $ownershipCompact @(
            '_parallelOwnership.Acquire(pWorld,_readParallelism(pWorld));',
            '_writeParallelism(pWorld,pSchedulerParallelism);',
            'publicvoidRelease()',
            'ReleaseParallelBudget();',
            'outintnativeParallelism',
            '_writeParallelism((TWorld)rawWorld,nativeParallelism);',
            '_parallelOwnership.Release(rawWorld);'
        )
    foreach ($partition in @(
            'ActorPathWorkers',
            'ArmyRouteWorkers',
            'ForegroundParallelism')) {
        Forbid-Text 'ownership does not duplicate worker partitions' `
            $ownershipCompact $partition
    }
    Require-Text 'snapshot parallel options use foreground budget' `
        $snapshotCompact `
        'privatereadonlyParallelOptionsdynamicCaptureParallelOptions=new(){MaxDegreeOfParallelism=AWPerformanceSettings.ForegroundParallelism};'
    Require-Text 'batch sizing uses foreground budget' $batchCompact `
        'AWPerformanceSettings.ForegroundParallelism*4'
    Require-Text 'batch execution keeps shared parallel options' `
        $batchCompact '_parallelOptions=pCycleParallelOptions;'

    $changedSourceNames = @(
        'Runner', 'Patch', 'Ownership', 'Batch', 'Coordinator',
        'Snapshot', 'ActorRenderer', 'WorldRenderer', 'Overlays',
        'Transient', 'Commands', 'Visibility', 'Interpolator',
        'Cursor', 'CursorPatch', 'Governor', 'Settings',
        'StepContext', 'SimulationTime', 'StatusClock', 'TimeRate',
        'InsideBoat'
    )
    $changedSources = ($changedSourceNames | ForEach-Object {
            $SourceSet[$_]
        }) -join "`n"
    $changedCompact = Compress-Source $changedSources
    foreach ($forbidden in @(
            'Task.Run(',
            'Cultiway.',
            'SimulationWorkerPool',
            'PathFinder.Instance')) {
        Forbid-Text "changed scheduler sources forbid $forbidden" `
            $changedCompact $forbidden
    }
    Require-Count 'one coordinator thread' $changedCompact `
        'newThread(CoordinatorLoop)' 1
    Require-Count 'no other raw scheduler thread' $changedCompact `
        'newThread(' 1
    Require-Count 'no independent processor budget' $changedCompact `
        'Environment.ProcessorCount' 1
    Require-Count 'processor budget flows through allocation rules' `
        $changedCompact `
        'AllocateWorkers(Environment.ProcessorCount)' 1
}

function Copy-SourceSet(
    [System.Collections.IDictionary]$SourceSet) {
    $copy = [ordered]@{}
    foreach ($entry in $SourceSet.GetEnumerator()) {
        $copy[$entry.Key] = $entry.Value
    }
    return $copy
}

function Get-MutatedSources(
    [System.Collections.IDictionary]$SourceSet,
    [string]$Mutation) {
    $copy = Copy-SourceSet $SourceSet
    switch ($Mutation) {
        'duplicate_cooperative_authority' {
            $needle = 'AWAuthorityCycleService.ProcessCooperativeCycle('
            $copy['Runner'] = $copy['Runner'].Replace(
                $needle,
                $needle + "`n" + $needle)
        }
        'replica_return_true' {
            $pattern = [regex]::new(
                '(?s)(if\s*\(\s*AW3MultiplayerReplicaScope\.IsReplicaSession\s*\)\s*\{.*?return\s+)false(\s*;)')
            $copy['Patch'] = $pattern.Replace(
                $copy['Patch'], '${1}true${2}', 1)
        }
        'disabled_force_true' {
            $pattern = [regex]::new(
                '(?s)(if\s*\(\s*!AWPerformanceSettings\.EnableFramePriorityScheduler\s*\)\s*\{\s*ResetSchedulerState\(\s*pUnbindSimulationTime\s*:\s*false)(\s*\);)')
            $copy['Patch'] = $pattern.Replace(
                $copy['Patch'], '${1}, pForce: true${2}', 1)
        }
        'delete_army_route_workers' {
            $copy['Rules'] = $copy['Rules'].Replace(
                'public int ArmyRouteWorkers { get; }', '')
        }
        'add_task_run' {
            $copy['Interpolator'] += "`nTask.Run("
        }
        'add_second_thread' {
            $needle = 'new Thread(CoordinatorLoop)'
            $copy['Coordinator'] = $copy['Coordinator'].Replace(
                $needle,
                $needle + "`n" + $needle)
        }
        default {
            throw "unknown integration guard mutation: $Mutation"
        }
    }
    return $copy
}

function Get-IntegrationFailures(
    [System.Collections.IDictionary]$SourceSet) {
    $previousFailures = $script:GuardFailures
    $script:GuardFailures =
        [System.Collections.Generic.List[string]]::new()
    try {
        Test-IntegrationSources $SourceSet
        return @($script:GuardFailures.ToArray())
    }
    finally {
        $script:GuardFailures = $previousFailures
    }
}

$baselineFailures = @(Get-IntegrationFailures $sources)
foreach ($failure in $baselineFailures) {
    $script:GuardFailures.Add($failure)
}

if ($script:GuardFailures.Count -eq 0) {
    $mutationExpectations = [ordered]@{
        duplicate_cooperative_authority =
            'cooperative authority dispatch'
        replica_return_true = 'replica takeover freezes'
        disabled_force_true = 'disabled scheduler returns native'
        delete_army_route_workers = 'army route allocation'
        add_task_run = 'Task.Run'
        add_second_thread = 'one coordinator thread'
    }
    foreach ($entry in $mutationExpectations.GetEnumerator()) {
        $mutatedSources = Get-MutatedSources $sources $entry.Key
        $sourceChanged = $false
        foreach ($name in $sources.Keys) {
            if ($sources[$name] -ne $mutatedSources[$name]) {
                $sourceChanged = $true
                break
            }
        }
        if (-not $sourceChanged) {
            $script:GuardFailures.Add(
                "mutation self-test did not mutate source: $($entry.Key)")
            continue
        }

        $mutationFailures = @(
            Get-IntegrationFailures $mutatedSources)
        $expectedFailures = @($mutationFailures | Where-Object {
                $_ -like "*$($entry.Value)*"
            })
        if ($expectedFailures.Count -eq 0) {
            $script:GuardFailures.Add(
                'mutation self-test was not rejected by expected rule: ' +
                "$($entry.Key) -> $($entry.Value)")
        }
    }
}

Complete-Guard 'AW3 scheduler integration guard' `
    'Cultiway large scheduler AW3 integration guard passed.'

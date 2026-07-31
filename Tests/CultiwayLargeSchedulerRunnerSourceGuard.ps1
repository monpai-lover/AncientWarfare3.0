$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot `
    'CultiwayLargeSchedulerSourceGuardCommon.ps1')

$runner = Read-Source `
    'Code/core/performance/AWCooperativeSimulationRunner.cs'
$stackEffectsUpdater = Read-Source `
    'Code/core/performance/AWActiveStackEffectsUpdater.cs'

function Compress-Source([string]$Text) {
    return [regex]::Replace($Text, '\s+', '')
}

function Get-SourceRegion([string]$Name, [string]$Text,
    [string]$StartNeedle, [string]$EndNeedle) {
    $startIndex = $Text.IndexOf(
        $StartNeedle, [System.StringComparison]::Ordinal)
    $endIndex = $Text.IndexOf(
        $EndNeedle, [System.StringComparison]::Ordinal)
    if ($startIndex -lt 0 -or $endIndex -le $startIndex) {
        $script:GuardFailures.Add(
            "${Name}: source region '$StartNeedle' -> '$EndNeedle' is missing")
        return ''
    }

    return $Text.Substring($startIndex, $endIndex - $startIndex)
}

function Test-RunnerSource([string]$Source) {
    $compact = Compress-Source $Source
    $runFrame = Compress-Source (Get-SourceRegion 'run frame' $Source `
        'public void RunFrame(' `
        'public bool TryBeginActorPresentationOverlap()')
    $burstEntry = Compress-Source (Get-SourceRegion 'burst entry' $Source `
        'private void ExecuteCurrentStageBurst()' `
        'private void ExecuteVanillaStageBurstCore()')
    $burstCore = Compress-Source (Get-SourceRegion 'burst core' $Source `
        'private void ExecuteVanillaStageBurstCore()' `
        'private void ExecuteCurrentStageCore()')
    $stageCore = Compress-Source (Get-SourceRegion 'stage core' $Source `
        'private void ExecuteCurrentStageCore()' `
        'private void CompleteCycle()')
    $eagerDeferred = Compress-Source (Get-SourceRegion 'eager deferred work' `
        $Source 'private bool TryBeginDeferredParallelWorkEagerly()' `
        'private static bool CanRunDeferredParallelWorkSynchronously(')
    $abortReset = Compress-Source (Get-SourceRegion 'abort reset' $Source `
        'private void ResetAfterAbort()' `
        'public void ReleaseControl()')

    @(
        @('maximum stages value',
            'privateconstintMaximumStagesPerBurst=256;'),
        @('minimum burst value',
            'privateconstdoubleMinimumBurstMilliseconds=0.25d;'),
        @('maximum burst value',
            'privateconstdoubleMaximumBurstMilliseconds=2d;'),
        @('target frame burst ratio value',
            'privateconstdoubleTargetFrameBurstRatio=0.01d;'),
        @('all burst stop reasons',
            'privateenumStageBurstStopReason{None,Completed,AsyncBoundary,DomainBoundary,Deadline,StageLimit}'),
        @('animation stage transition',
            'caseSimulationStage.AnimationTime:Advance(SimulationStage.EnemyCache);break;'),
        @('actor worker and presentation options',
            'privatereadonlyAWCooperativeBatchRunner<BatchActors,Actor>_actorRunner=newAWCooperativeBatchRunner<BatchActors,Actor>("vanilla.actors",pAllowWorkerParallelism:true,pDeferParallelToPresentation:true);'),
        @('building worker and presentation options',
            'privatereadonlyAWCooperativeBatchRunner<BatchBuildings,Building>_buildingRunner=newAWCooperativeBatchRunner<BatchBuildings,Building>("vanilla.buildings",pAllowWorkerParallelism:true,pDeferParallelToPresentation:true);'),
        @('eager deferred work',
            'TryBeginDeferredParallelWorkEagerly()'),
        @('actor overlap', 'TryBeginActorPresentationOverlap()'),
        @('building overlap', 'TryBeginBuildingPresentationOverlap()'),
        @('actor read boundary',
            'EnsureActorReadBoundary(stringpReason)'),
        @('building read boundary',
            'EnsureBuildingReadBoundary(stringpReason)')
    ) | ForEach-Object {
        Require-Text $_[0] $compact $_[1]
    }

    Require-Before 'delayed actions precede authority' $compact `
        'DelayedActions,' 'Aw3Authority,'
    Require-Before 'authority precedes completion' $compact `
        'Aw3Authority,' 'Complete}'
    Require-Before 'delayed actions switch precedes authority transition' `
        $stageCore 'caseSimulationStage.DelayedActions:' `
        'Advance(SimulationStage.Aw3Authority);'
    Require-Before 'authority transition reaches authority case' $stageCore `
        'Advance(SimulationStage.Aw3Authority);' `
        'caseSimulationStage.Aw3Authority:'
    Require-Before 'authority processing precedes tick completion stage' `
        $stageCore 'AWAuthorityCycleService.ProcessCooperativeCycle(' `
        'Advance(SimulationStage.Complete);'

    Require-Count 'one cooperative authority call' $compact `
        'AWAuthorityCycleService.ProcessCooperativeCycle(' 1
    Require-Count 'one scheduler tick begin' $compact `
        'AWSimulationTime.BeginTick(' 1
    Require-Count 'one scheduler tick complete' $compact `
        'AWSimulationTime.CompleteTick(' 1
    Require-Count 'one snapshot capture' $compact `
        'AWActorPresentationSnapshots.CaptureIfRequested(' 1

    Require-Text 'dispatch waits before burst execution' $runFrame `
        'if((_stage==SimulationStage.Actors&&_actorRunner.WaitingForPresentationDispatch)||(_stage==SimulationStage.Buildings&&_buildingRunner.WaitingForPresentationDispatch))'
    Require-Text 'eager dispatch executes deferred work' $runFrame `
        'TryBeginDeferredParallelWorkEagerly()'
    Require-Text 'background join uses governor admission only' $runFrame `
        'if(!AWFramePriorityGovernor.CanRun(AWSimulationDomain.Vanilla,awaitPhase))'
    Require-Text 'background join absorbs remaining budget' $runFrame `
        'doublejoinMilliseconds=Math.Max(AWPerformanceSettings.BackgroundJoinMilliseconds,remainingMilliseconds);'
    Require-Text 'actor background join' $runFrame `
        '_actorRunner.TryJoinBackgroundWork(joinMilliseconds)'
    Require-Text 'building background join' $runFrame `
        '_buildingRunner.TryJoinBackgroundWork(joinMilliseconds)'
    Require-Text 'completed actor background work is completed' $runFrame `
        'if(_actorRunner.HasParallelPresentationWorkInFlight&&_actorRunner.IsBackgroundWorkCompleted){CompleteActorPresentationWork(true,"run_frame.completed");continue;}'
    Require-Text 'completed building background work is completed' `
        $runFrame `
        'if(_buildingRunner.HasParallelPresentationWorkInFlight&&_buildingRunner.IsBackgroundWorkCompleted){CompleteBuildingPresentationWork(true,"run_frame.completed");continue;}'
    Require-Text 'joined actor background work is completed' $runFrame `
        'if(_actorRunner.HasParallelPresentationWorkInFlight)CompleteActorPresentationWork(false,"run_frame.join");'
    Require-Text 'joined building background work is completed' `
        $runFrame `
        'elseif(_buildingRunner.HasParallelPresentationWorkInFlight)CompleteBuildingPresentationWork(false,"run_frame.join");'
    Forbid-Text 'no background join remaining-budget threshold' $runFrame `
        'remainingMilliseconds<AWPerformanceSettings.BackgroundJoinMilliseconds'
    Forbid-Text 'no background join minimum wait clamp' $runFrame `
        'doublejoinMilliseconds=Math.Min('

    Require-Text 'actor seeds first snapshot without budget gate' `
        $eagerDeferred `
        'if(!AWActorPresentationSnapshots.HasPublishedSnapshot||CanRunDeferredParallelWorkSynchronously(_actorParallelStageEstimateMilliseconds)){longstartedAt=Stopwatch.GetTimestamp();if(_actorRunner.RunDeferredParallelWorkSynchronously())'
    Require-Text 'building seeds first snapshot without budget gate' `
        $eagerDeferred `
        'if(!AWActorPresentationSnapshots.HasPublishedSnapshot||CanRunDeferredParallelWorkSynchronously(_buildingParallelStageEstimateMilliseconds)){longstartedAt=Stopwatch.GetTimestamp();if(_buildingRunner.RunDeferredParallelWorkSynchronously())'

    Require-Text 'target frame burst formula' $burstEntry `
        'doubletargetFrameMilliseconds=1000d/AWPerformanceSettings.TargetRenderFps;'
    Require-Text 'desired burst formula' $burstEntry `
        'doubledesiredBurstMilliseconds=Math.Max(MinimumBurstMilliseconds,Math.Min(MaximumBurstMilliseconds,targetFrameMilliseconds*TargetFrameBurstRatio));'
    Require-Text 'remaining simulation budget' $burstEntry `
        'AWFramePriorityGovernor.GetRemainingSimulationBudgetMilliseconds();'
    Require-Text 'remaining-budget burst clamp' $burstEntry `
        'doubleburstMilliseconds=remainingMilliseconds>0d?Math.Min(desiredBurstMilliseconds,Math.Max(MinimumBurstMilliseconds,remainingMilliseconds)):MinimumBurstMilliseconds;'
    Require-Text 'burst deadline from budget' $burstEntry `
        '_activeStageBurstDeadline=burstStartedAt+Math.Max(1L,(long)(burstMilliseconds*Stopwatch.Frequency/1000d));'

    Require-Text 'burst remains in vanilla domain' $burstCore `
        'if(GetCurrentDomain()!=AWSimulationDomain.Vanilla){_activeStageBurstStopReason=StageBurstStopReason.DomainBoundary;return;}'
    Require-Text 'burst stops at actor or building async boundary' $burstCore `
        'if((_stage==SimulationStage.Actors&&(_actorRunner.WaitingForPresentationDispatch||_actorRunner.WaitingForBackgroundWork))||(_stage==SimulationStage.Buildings&&(_buildingRunner.WaitingForPresentationDispatch||_buildingRunner.WaitingForBackgroundWork))){_activeStageBurstStopReason=StageBurstStopReason.AsyncBoundary;return;}'
    Require-Text 'burst enforces stage limit' $burstCore `
        'if(_activeStageBurstSteps>=MaximumStagesPerBurst){_activeStageBurstStopReason=StageBurstStopReason.StageLimit;return;}'
    Require-Text 'burst enforces deadline' $burstCore `
        'if((_activeStageBurstSteps&3)==0&&Stopwatch.GetTimestamp()>=_activeStageBurstDeadline){_activeStageBurstStopReason=StageBurstStopReason.Deadline;return;}'

    Require-Text 'abort resets all scheduler-owned work' $abortReset `
        '_actorRunner.Abort();_buildingRunner.Abort();_maintenanceRunner.Abort();AWSimulationTime.CancelTick();AWActorPresentationSnapshots.Reset();AWPresentationCommandQueue.Clear();'

    Require-Text 'stack effects use active updater' $stageCore `
        'caseSimulationStage.StackEffects:AWActiveStackEffectsUpdater.Update(_world.stack_effects,_cycleElapsed);Advance(SimulationStage.ResourceThrows);break;'
    Forbid-Text 'no direct stack effects update' $stageCore `
        '_world.stack_effects.update('

    Forbid-Text 'no Cultiway namespace' $compact 'Cultiway.'
    Forbid-Text 'no Task.Run' $compact 'Task.Run('
    Forbid-Text 'no extra raw thread' $compact 'newThread('
    Forbid-Text 'no competing worker pool' $compact `
        'SimulationWorkerPool'
}

function Test-StackEffectsUpdaterSource([string]$Source) {
    $compact = Compress-Source $Source
    $update = Compress-Source (Get-SourceRegion 'stack effects update' `
        $Source 'internal static void Update(' `
        'internal static string GetDiagnostics()')

    Require-Text 'AW stack effects updater class' $compact `
        'internalstaticclassAWActiveStackEffectsUpdater'
    Require-Text 'scheduler-disabled stack effects fallback' $update `
        'if(!AWPerformanceSettings.EnableFramePriorityScheduler){effects.update(elapsed);return;}'
    Require-Text 'initialization stack effects fallback' $update `
        'if(AssetManager.effects_library.list.Count>effects.list.Count){effects.update(elapsed);Interlocked.Increment(refinitializationFallbacks);return;}'
    Require-Count 'two complete stack effects fallback paths' $update `
        'effects.update(elapsed);' 2
    Require-Text 'stack effects benchmark begin' $update `
        'Bench.bench("stack_effects","game_total");'
    Require-Text 'stack effects controller iteration' $update `
        'for(inti=0;i<effects.list.Count;i++){BaseEffectControllercontroller=effects.list[i];'
    Require-Text 'only inactive exact base controllers are skipped' $update `
        'if(controller.getActiveIndex()==0&&controller.GetType()==typeof(BaseEffectController)){skipped++;continue;}'
    Require-Text 'active or specialized controller update' $update `
        'controller.update(elapsed);updated++;'
    Require-Text 'stack effects benchmark finally' $update `
        'finally{Bench.benchEnd("stack_effects","game_total",pSaveCounter:false,0L);}'
    Require-Text 'optional stack effects diagnostics counters' $update `
        'if(Bench.bench_enabled){Interlocked.Increment(refupdatePasses);Interlocked.Add(refcontrollersUpdated,updated);Interlocked.Add(refinactiveControllersSkipped,skipped);}'
    Require-Text 'stack effects diagnostics output' $compact `
        'internalstaticstringGetDiagnostics()'

    Require-Before 'stack effects benchmark wraps controller loop' $update `
        'Bench.bench("stack_effects","game_total");' 'try{'
    Require-Before 'stack effects controller loop precedes finally' $update `
        'for(inti=0;i<effects.list.Count;i++)' 'finally{'
    Forbid-Text 'no Cultiway stack effects namespace' $compact 'Cultiway.'
}

function Test-MutationRejected([string]$Name, [string]$OriginalSource,
    [string]$MutatedSource, [scriptblock]$Verifier) {
    if ($MutatedSource -eq $OriginalSource) {
        $script:GuardFailures.Add(
            "${Name}: mutation target was not found")
        return
    }

    $failureStart = $script:GuardFailures.Count
    & $Verifier $MutatedSource
    $mutationRejected = $script:GuardFailures.Count -gt $failureStart
    while ($script:GuardFailures.Count -gt $failureStart) {
        $script:GuardFailures.RemoveAt($script:GuardFailures.Count - 1)
    }

    if (-not $mutationRejected) {
        $script:GuardFailures.Add(
            "${Name}: guard accepted mutation")
    }
}

function Test-NoSnapshotSeedMutation([string]$Source) {
    $mutated = [regex]::Replace(
        $Source,
        '!\s*AWActorPresentationSnapshots\s*\.\s*HasPublishedSnapshot\s*\|\|\s*(?=CanRunDeferredParallelWorkSynchronously)',
        '')
    if ($mutated -eq $Source) {
        $script:GuardFailures.Add(
            'no-snapshot mutation: eager seed conjunct was not found')
        return
    }

    $failureStart = $script:GuardFailures.Count
    Test-RunnerSource $mutated
    $mutationRejected = $script:GuardFailures.Count -gt $failureStart
    while ($script:GuardFailures.Count -gt $failureStart) {
        $script:GuardFailures.RemoveAt($script:GuardFailures.Count - 1)
    }

    if (-not $mutationRejected) {
        $script:GuardFailures.Add(
            'no-snapshot mutation: guard accepted removal of eager seed conjunct')
    }
}

function Test-RuntimeParityMutations([string]$RunnerSource,
    [string]$UpdaterSource) {
    $minimumJoin = [regex]::Replace(
        $RunnerSource,
        'double\s+joinMilliseconds\s*=\s*Math\.Max\s*\(',
        'double joinMilliseconds = Math.Min(',
        1)
    Test-MutationRejected 'minimum-join mutation' $RunnerSource `
        $minimumJoin { param($source) Test-RunnerSource $source }

    $thresholdJoin = [regex]::Replace(
        $RunnerSource,
        'if\s*\(\s*!\s*AWFramePriorityGovernor\s*\.\s*CanRun\s*\(\s*AWSimulationDomain\s*\.\s*Vanilla\s*,\s*awaitPhase\s*\)\s*\)',
        'if (remainingMilliseconds < AWPerformanceSettings.BackgroundJoinMilliseconds || !AWFramePriorityGovernor.CanRun(AWSimulationDomain.Vanilla, awaitPhase))',
        1)
    Test-MutationRejected 'remaining-threshold mutation' $RunnerSource `
        $thresholdJoin { param($source) Test-RunnerSource $source }

    $actorCompletedCompletion = [regex]::Replace(
        $RunnerSource,
        'CompleteActorPresentationWork\(\s*true\s*,\s*"run_frame\.completed"\s*\)\s*;',
        '',
        1)
    Test-MutationRejected 'actor completed completion mutation' `
        $RunnerSource $actorCompletedCompletion `
        { param($source) Test-RunnerSource $source }

    $buildingCompletedCompletion = [regex]::Replace(
        $RunnerSource,
        'CompleteBuildingPresentationWork\(\s*true\s*,\s*"run_frame\.completed"\s*\)\s*;',
        '',
        1)
    Test-MutationRejected 'building completed completion mutation' `
        $RunnerSource $buildingCompletedCompletion `
        { param($source) Test-RunnerSource $source }

    $actorJoinedCompletion = [regex]::Replace(
        $RunnerSource,
        '(?s)(if\s*\(\s*_actorRunner\s*\.\s*HasParallelPresentationWorkInFlight\s*\)\s*)CompleteActorPresentationWork\(\s*false\s*,\s*"run_frame\.join"\s*\)\s*;',
        '${1}{ }',
        1)
    Test-MutationRejected 'actor joined completion mutation' `
        $RunnerSource $actorJoinedCompletion `
        { param($source) Test-RunnerSource $source }

    $buildingJoinedCompletion = [regex]::Replace(
        $RunnerSource,
        '(?s)(else\s+if\s*\(\s*_buildingRunner\s*\.\s*HasParallelPresentationWorkInFlight\s*\)\s*)CompleteBuildingPresentationWork\(\s*false\s*,\s*"run_frame\.join"\s*\)\s*;',
        '${1}{ }',
        1)
    Test-MutationRejected 'building joined completion mutation' `
        $RunnerSource $buildingJoinedCompletion `
        { param($source) Test-RunnerSource $source }

    $untypedSkip = [regex]::Replace(
        $UpdaterSource,
        'controller\s*\.\s*getActiveIndex\(\)\s*==\s*0\s*&&\s*controller\s*\.\s*GetType\(\)\s*==\s*typeof\s*\(\s*BaseEffectController\s*\)',
        'controller.getActiveIndex() == 0',
        1)
    Test-MutationRejected 'exact-controller-type mutation' $UpdaterSource `
        $untypedSkip { param($source) Test-StackEffectsUpdaterSource $source }

    $directStackUpdate = [regex]::Replace(
        $RunnerSource,
        'AWActiveStackEffectsUpdater\s*\.\s*Update\s*\(\s*_world\s*\.\s*stack_effects\s*,\s*_cycleElapsed\s*\)\s*;',
        '_world.stack_effects.update(_cycleElapsed);',
        1)
    Test-MutationRejected 'direct-stack-update mutation' $RunnerSource `
        $directStackUpdate { param($source) Test-RunnerSource $source }
}

Test-RunnerSource $runner
Test-StackEffectsUpdaterSource $stackEffectsUpdater
if ($script:GuardFailures.Count -eq 0) {
    Test-NoSnapshotSeedMutation $runner
    Test-RuntimeParityMutations $runner $stackEffectsUpdater
}

Complete-Guard 'runner guard' `
    'Cultiway large scheduler runner guard passed.'

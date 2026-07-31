. (Join-Path $PSScriptRoot `
    'CultiwayLargeSchedulerSourceGuardCommon.ps1')

$batch = Read-Source 'Code/core/performance/AWCooperativeBatchRunner.cs'
$coordinator = Read-Source `
    'Code/core/performance/AWSimulationCoordinatorThread.cs'
$ownership = Read-Source `
    'Code/core/performance/AWSchedulerResourceOwnership.cs'

function Compress-Source([string]$Text) {
    return [regex]::Replace($Text, '\s+', '')
}

function Test-BatchRunnerSource([string]$Source) {
    $compact = Compress-Source $Source

    @(
        @('parallel job index',
            'privateint_parallelJobIndex;'),
        @('active parallel batch buffer',
            'privateint[]_activeParallelBatchIndices=Array.Empty<int>();'),
        @('active parallel batch count',
            'privateint_activeParallelBatchCount;'),
        @('parallel job action',
            '_parallelJobAction=RunCurrentParallelJob;'),
        @('parallel job group helper',
            'privateboolTryRunNextParallelJobGroup()'),
        @('parallel work filter',
            'privateboolHasParallelJobWork('),
        @('first batch defines parallel job count',
            'intjobCount=_batches.Count==0?0:_batches[0].jobs_parallel.Count;'),
        @('advance completed parallel job',
            'if(_batchIndex>=_batches.Count){_parallelJobIndex++;_batchIndex=0;continue;}'),
        @('active batch capacity',
            'EnsureActiveParallelBatchCapacity(scannedCount);'),
        @('active batch scan',
            '_activeParallelBatchIndices[_activeParallelBatchCount++]=_batchIndex;'),
        @('same-job parallel range',
            'Parallel.For(0,_activeParallelBatchCount,_parallelOptions,_parallelJobAction);'),
        @('active index maps to real batch',
            'RunParallelJob(_activeParallelBatchIndices[pActiveIndex],_parallelJobIndex);'),
        @('abort uses shared reset',
            'Reset();_batchIndex=0;'),
        @('single parallel job updater',
            'Job<TObject>job=batch.jobs_parallel[pJobListIndex];batch._elapsed=_elapsed;batch._cur_container=job.container;job.job_updater();'),
        @('null or populated or dirty parallel container',
            'returncontainer==null||container.Count>0||container.isDirtyContainer();'),
        @('parallel stage preserves native order when disabled',
            'if(_parallelEnabled?TryRunNextParallelJobGroup():TryRunNextParallelBatch())returnfalse;'),
        @('native parallel batch helper',
            'privateboolTryRunNextParallelBatch()'),
        @('native parallel batch updater',
            'TBatchbatch=_batches[_batchIndex++];batch._elapsed=_elapsed;batch.updateJobsParallel(_elapsed);returntrue;'),
        @('background stage loops job groups',
            'while(TryRunNextParallelJobGroup()){}'),
        @('deferred synchronous path uses job-group loop',
            'RunDeferredParallelWorkSynchronously(){if(!WaitingForPresentationDispatch)returnfalse;RunParallelStageInBackground();returntrue;}'),
        @('main-thread no-benchmark fast path',
            'if(!_collectJobBenchmarks)RunMainThreadJobsWithoutBenchmark(batch,jobs);'),
        @('main-thread fast path method',
            'privatevoidRunMainThreadJobsWithoutBenchmark('),
        @('main-thread skip countdown',
            'if(job.current_skips>0){job.current_skips--;continue;}'),
        @('main-thread random skip reset',
            'if(job.random_tick_skips>0)job.current_skips=Randy.randomInt(0,job.random_tick_skips);'),
        @('benchmark pre path remains vanilla',
            'elseif(pJobStage==RunnerStage.Pre)batch.updateJobsPre(_elapsed);'),
        @('benchmark post path remains vanilla',
            'elsebatch.updateJobsPost(_elapsed);')
    ) | ForEach-Object {
        Require-Text $_[0] $compact $_[1]
    }

    Require-Count 'parallel job increment' $compact `
        '_parallelJobIndex++;' 1
    Require-Count 'start and reset parallel job index' $compact `
        '_parallelJobIndex=0;' 2
    Require-Count 'start and reset active batch count' $compact `
        '_activeParallelBatchCount=0;' 3
    Require-Count 'parallel for uses active range' $compact `
        'Parallel.For(0,_activeParallelBatchCount,_parallelOptions,' 1
    Require-Count 'parallel job updater executes once per helper' $compact `
        'job.job_updater();' 2
    Require-Count 'native parallel batch updater count' $compact `
        'batch.updateJobsParallel(_elapsed);' 1

    Forbid-Text 'no parallel batch-group helper' $compact `
        'TryRunNextParallelBatchGroup'
    Forbid-Text 'no all-batch parallel range' $compact `
        'Parallel.For(0,_batches.Count,_parallelOptions,'
}

function Get-BatchRunnerFailures([string]$Source) {
    $previousFailures = $script:GuardFailures
    $script:GuardFailures =
        [System.Collections.Generic.List[string]]::new()
    try {
        Test-BatchRunnerSource $Source
        return @($script:GuardFailures.ToArray())
    }
    finally {
        $script:GuardFailures = $previousFailures
    }
}

function Get-MutatedBatchRunner([string]$Source, [string]$Mutation) {
    switch ($Mutation) {
        'whole_batch_updater' {
            $pattern = [regex]::new(
                '(?s)(private void RunParallelJob\(.*?)(job\.job_updater\(\);)')
            return $pattern.Replace(
                $Source,
                '${1}batch.updateJobsParallel(_elapsed);',
                1)
        }
        'additive_whole_batch_updater' {
            $pattern = [regex]::new(
                '(?s)(private void RunParallelJob\(.*?job\.job_updater\(\);)')
            return $pattern.Replace(
                $Source,
                '${1}' + [Environment]::NewLine +
                '            batch.updateJobsParallel(_elapsed);',
                1)
        }
        'delete_job_advance' {
            return $Source.Replace('_parallelJobIndex++;', '')
        }
        'delete_batch_reset' {
            $pattern = [regex]::new(
                '(?s)(if \(_batchIndex >= _batches\.Count\).*?_parallelJobIndex\+\+;)\s*_batchIndex = 0;')
            return $pattern.Replace($Source, '${1}', 1)
        }
        'all_batches_parallel_range' {
            return $Source.Replace(
                'Parallel.For(0, _activeParallelBatchCount,',
                'Parallel.For(0, _batches.Count,')
        }
        'unconditional_parallel_job_groups' {
            $pattern = [regex]::new(
                '(?s)_parallelEnabled\s*\?\s*TryRunNextParallelJobGroup\(\)\s*:\s*TryRunNextParallelBatch\(\)')
            return $pattern.Replace(
                $Source,
                'TryRunNextParallelJobGroup()',
                1)
        }
        default {
            throw "unknown batch guard mutation: $Mutation"
        }
    }
}

$baselineBatchFailures = @(Get-BatchRunnerFailures $batch)
foreach ($failure in $baselineBatchFailures) {
    $script:GuardFailures.Add($failure)
}

if ($baselineBatchFailures.Count -eq 0) {
    $mutationExpectations = [ordered]@{
        whole_batch_updater = 'single parallel job updater'
        additive_whole_batch_updater =
            'native parallel batch updater count'
        delete_job_advance = 'parallel job increment'
        delete_batch_reset = 'advance completed parallel job'
        all_batches_parallel_range = 'same-job parallel range'
        unconditional_parallel_job_groups =
            'parallel stage preserves native order when disabled'
    }
    foreach ($entry in $mutationExpectations.GetEnumerator()) {
        $mutatedBatch = Get-MutatedBatchRunner $batch $entry.Key
        if ($mutatedBatch -eq $batch) {
            $script:GuardFailures.Add(
                "mutation self-test did not mutate source: $($entry.Key)")
            continue
        }

        $mutationFailures = @(
            Get-BatchRunnerFailures $mutatedBatch)
        $expectedFailure = @(
            $mutationFailures | Where-Object {
                $_ -like "*$($entry.Value)*"
            })
        if ($expectedFailure.Count -eq 0) {
            $script:GuardFailures.Add(
                'mutation self-test was not rejected by expected rule: ' +
                "$($entry.Key) -> $($entry.Value)")
        }
    }
}

@(
    @('waiting dispatch', $batch,
        'WaitingForPresentationDispatch'),
    @('in-flight state', $batch,
        'HasParallelPresentationWorkInFlight'),
    @('begin presentation work', $batch,
        'BeginParallelPresentationWork()'),
    @('complete presentation work', $batch,
        'CompleteParallelPresentationWork()'),
    @('synchronous deferred work', $batch,
        'RunDeferredParallelWorkSynchronously()'),
    @('wait and discard on abort', $batch,
        'WaitAndDiscard('),
    @('coordinator begin', $coordinator, 'WorkTicket Begin('),
    @('coordinator wait', $coordinator,
        'void Wait(WorkTicket pTicket)'),
    @('coordinator complete', $coordinator, 'WorkResult Complete('),
    @('single background coordinator', $coordinator,
        'new Thread(CoordinatorLoop)'),
    @('coordinator background flag', $coordinator,
        'IsBackground = true'),
    @('scheduler parallel ownership', $ownership,
        'pSchedulerParallelism')
) | ForEach-Object {
    Require-Text $_[0] $_[1] $_[2]
}

foreach ($source in @($batch, $ownership)) {
    Forbid-Text 'no competing worker pool' $source `
        'new SimulationWorkerPool('
    Forbid-Text 'no extra raw thread' $source 'new Thread('
    Forbid-Text 'no independent processor budget' $source `
        'Environment.ProcessorCount - 2'
}

Require-Count 'one coordinator thread' $coordinator `
    'new Thread(CoordinatorLoop)' 1

Complete-Guard 'batch guard' `
    'Cultiway large scheduler batch guard passed.'

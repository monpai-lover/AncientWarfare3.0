using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AncientWarfare3.core.performance
{
    internal sealed class AWCooperativeBatchRunner<TBatch, TObject>
        where TBatch : Batch<TObject>, new()
    {
        private enum RunnerStage
        {
            Idle,
            Pre,
            ClearParallelResults,
            Parallel,
            ApplyParallelResults,
            Post,
            Finish
        }

        private readonly List<TBatch> _batches = new List<TBatch>();
        private readonly string _phasePrefix;
        private readonly bool _allowWorkerParallelism;
        private readonly bool _deferParallelToPresentation;
        private readonly IAWCooperativeBatchPostRunner<TBatch, TObject>
            _postRunner;
        private readonly IAWCooperativeBatchParallelJobRunner<TBatch, TObject>
            _parallelJobRunner;
        private readonly Action<int> _parallelJobAction;
        private readonly Action _runParallelStageInBackground;
        private int[] _activeParallelBatchIndices = Array.Empty<int>();
        private JobManagerBase<TBatch, TObject> _manager;
        private RunnerStage _stage;
        private float _elapsed;
        private int _batchIndex;
        private int _parallelJobIndex;
        private int _activeParallelBatchCount;
        private bool _parallelEnabled;
        private int _parallelGroupSize;
        private ParallelOptions _parallelOptions;
        private bool _collectJobBenchmarks;
        private bool _useCustomPostRunner;
        private bool _deferParallelToPresentationForCycle;
        private bool _parallelStageFinishedInBackground;
        private AWSimulationCoordinatorThread.WorkTicket
            _parallelStageTicket;

        public AWCooperativeBatchRunner(string pPhasePrefix,
            bool pAllowWorkerParallelism,
            bool pDeferParallelToPresentation = false,
            IAWCooperativeBatchPostRunner<TBatch, TObject> pPostRunner = null,
            IAWCooperativeBatchParallelJobRunner<TBatch, TObject>
                pParallelJobRunner = null)
        {
            _phasePrefix = pPhasePrefix ??
                throw new ArgumentNullException(nameof(pPhasePrefix));
            _allowWorkerParallelism = pAllowWorkerParallelism;
            _deferParallelToPresentation =
                pDeferParallelToPresentation;
            _postRunner = pPostRunner;
            _parallelJobRunner = pParallelJobRunner;
            _parallelJobAction = RunCurrentParallelJob;
            _runParallelStageInBackground =
                RunParallelStageInBackground;
        }

        public bool Active => _stage != RunnerStage.Idle;
        public bool WaitingForPresentationDispatch =>
            _deferParallelToPresentationForCycle &&
            _parallelEnabled &&
            _stage == RunnerStage.Parallel &&
            !_parallelStageTicket.IsValid &&
            !_parallelStageFinishedInBackground;
        public bool HasParallelPresentationWorkInFlight =>
            _stage == RunnerStage.Parallel &&
            _parallelStageTicket.IsValid;
        public bool WaitingForBackgroundWork =>
            HasParallelPresentationWorkInFlight ||
            (_stage == RunnerStage.Post && _useCustomPostRunner &&
             _postRunner.WaitingForBackgroundWork);
        public bool IsBackgroundWorkCompleted =>
            HasParallelPresentationWorkInFlight
                ? AWSimulationCoordinatorThread.Instance.IsCompleted(
                    _parallelStageTicket)
                : _stage == RunnerStage.Post && _useCustomPostRunner &&
                  _postRunner.IsBackgroundWorkCompleted;

        public void Start(JobManagerBase<TBatch, TObject> pJobManager,
            IEnumerable<TBatch> pActiveBatches, float pCycleElapsed,
            ParallelOptions pCycleParallelOptions,
            Comparison<TBatch> pComparison = null)
        {
            _manager = pJobManager ??
                throw new ArgumentNullException(nameof(pJobManager));
            _elapsed = pCycleElapsed;
            _parallelEnabled = AWFrameSchedulerRules
                .ShouldParallelizeBatchRunner(Config.parallel_jobs_updater,
                    _allowWorkerParallelism);
            _parallelGroupSize = _parallelEnabled
                ? Math.Max(1,
                    AWPerformanceSettings.ForegroundParallelism * 4)
                : 1;
            _parallelOptions = pCycleParallelOptions;
            _useCustomPostRunner = _parallelEnabled && _postRunner != null;
            if (_parallelEnabled && _parallelOptions == null)
                throw new InvalidOperationException(
                    "AW scheduler parallel batch is missing ParallelOptions.");

            _batches.Clear();
            _batches.AddRange(pActiveBatches);
            if (pComparison != null) _batches.Sort(pComparison);
            _deferParallelToPresentationForCycle =
                _deferParallelToPresentation &&
                CanDeferActorParallelStageToPresentation();

            _collectJobBenchmarks = AWSimulationTickBenchmark.IsCapturing;
            if (_collectJobBenchmarks) _manager.clearJobBenchmarks();

            _batchIndex = 0;
            _parallelJobIndex = 0;
            _activeParallelBatchCount = 0;
            _parallelStageFinishedInBackground = false;
            _parallelStageTicket = default;
            _stage = RunnerStage.Pre;
        }

        public string GetNextPhaseName()
        {
            if (HasParallelPresentationWorkInFlight)
                return _phasePrefix + ".parallel.presentation.await";
            if (WaitingForPresentationDispatch)
                return _phasePrefix + ".parallel.presentation.dispatch";
            if (_stage == RunnerStage.Post && _useCustomPostRunner)
                return _postRunner.GetNextPhaseName(_phasePrefix);

            if (_stage == RunnerStage.Parallel)
            {
                if (_parallelEnabled)
                {
                    string parallelPhase = FindNextParallelJobPhase();
                    if (parallelPhase != null) return parallelPhase;
                }
                else if (_batchIndex < _batches.Count)
                {
                    return _phasePrefix + ".parallel.batch." +
                           _batchIndex;
                }

                return _phasePrefix + ".apply_parallel_results";
            }

            if (_stage == RunnerStage.Pre || _stage == RunnerStage.Post)
            {
                int nextBatchIndex = FindNextMainThreadBatchIndex(_stage);
                if (nextBatchIndex >= 0)
                {
                    return _phasePrefix + "." +
                           (_stage == RunnerStage.Pre ? "pre" : "post") +
                           ".batch." + nextBatchIndex;
                }

                return _stage == RunnerStage.Pre
                    ? _phasePrefix + ".clear_parallel_results"
                    : _phasePrefix + ".finish";
            }

            switch (_stage)
            {
                case RunnerStage.Idle:
                    return _phasePrefix + ".idle";
                case RunnerStage.Pre:
                    return _phasePrefix + ".pre";
                case RunnerStage.ClearParallelResults:
                    return _phasePrefix + ".clear_parallel_results";
                case RunnerStage.Parallel:
                    return _phasePrefix + ".parallel";
                case RunnerStage.ApplyParallelResults:
                    return _phasePrefix + ".apply_parallel_results";
                case RunnerStage.Post:
                    return _phasePrefix + ".post";
                case RunnerStage.Finish:
                    return _phasePrefix + ".finish";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public bool Step()
        {
            while (true)
            {
                switch (_stage)
                {
                    case RunnerStage.Idle:
                        return true;
                    case RunnerStage.Pre:
                        if (TryRunNextMainThreadBatch(RunnerStage.Pre))
                            return false;
                        _stage = RunnerStage.ClearParallelResults;
                        _batchIndex = 0;
                        continue;
                    case RunnerStage.ClearParallelResults:
                        _manager.clearParallelResults();
                        _stage = RunnerStage.Parallel;
                        return false;
                    case RunnerStage.Parallel:
                        if (_parallelStageTicket.IsValid)
                        {
                            if (!AWSimulationCoordinatorThread.Instance
                                    .IsCompleted(_parallelStageTicket))
                                return false;
                            CompleteParallelPresentationWork();
                            continue;
                        }

                        if (_parallelStageFinishedInBackground)
                        {
                            _stage = RunnerStage.ApplyParallelResults;
                            _batchIndex = 0;
                            continue;
                        }

                        if (_deferParallelToPresentationForCycle &&
                            _parallelEnabled)
                            return false;

                        if (_parallelEnabled
                                ? TryRunNextParallelJobGroup()
                                : TryRunNextParallelBatch())
                            return false;
                        _stage = RunnerStage.ApplyParallelResults;
                        _batchIndex = 0;
                        continue;
                    case RunnerStage.ApplyParallelResults:
                        _manager.applyParallelResults();
                        _stage = RunnerStage.Post;
                        if (_useCustomPostRunner)
                            _postRunner.Start(_batches, _elapsed,
                                _parallelOptions);
                        return false;
                    case RunnerStage.Post:
                        if (_useCustomPostRunner
                                ? !_postRunner.Step()
                                : TryRunNextMainThreadBatch(RunnerStage.Post))
                            return false;
                        _stage = RunnerStage.Finish;
                        continue;
                    case RunnerStage.Finish:
                        if (_collectJobBenchmarks)
                            AWSimulationTickBenchmark.RecordBatchJobs<TBatch,
                                TObject>(_manager.benchmark_id, _batches);
                        Reset();
                        return true;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public bool TryJoinBackgroundWork(double pMaximumMilliseconds)
        {
            if (HasParallelPresentationWorkInFlight)
                return AWSimulationCoordinatorThread.Instance.TryWait(
                    _parallelStageTicket, pMaximumMilliseconds);
            return _stage != RunnerStage.Post || !_useCustomPostRunner ||
                   _postRunner.TryJoinBackgroundWork(pMaximumMilliseconds);
        }

        public void WaitForBackgroundWork()
        {
            if (HasParallelPresentationWorkInFlight)
                AWSimulationCoordinatorThread.Instance.Wait(
                    _parallelStageTicket);
            else if (_stage == RunnerStage.Post && _useCustomPostRunner &&
                     _postRunner.WaitingForBackgroundWork)
                _postRunner.WaitForBackgroundWork();
        }

        public bool BeginParallelPresentationWork()
        {
            if (!WaitingForPresentationDispatch) return false;
            _parallelStageTicket =
                AWSimulationCoordinatorThread.Instance.Begin(
                    _phasePrefix + ".parallel.presentation",
                    _runParallelStageInBackground);
            return true;
        }

        public bool RunDeferredParallelWorkSynchronously()
        {
            if (!WaitingForPresentationDispatch) return false;
            RunParallelStageInBackground();
            return true;
        }

        public AWSimulationCoordinatorThread.WorkResult
            CompleteParallelPresentationWork()
        {
            if (!_parallelStageTicket.IsValid) return default;

            AWSimulationCoordinatorThread.WorkTicket ticket =
                _parallelStageTicket;
            AWSimulationCoordinatorThread.Instance.Wait(ticket);
            try
            {
                return AWSimulationCoordinatorThread.Instance.Complete(
                    ticket);
            }
            finally
            {
                _parallelStageTicket = default;
            }
        }

        public void Abort()
        {
            if (_parallelStageTicket.IsValid)
            {
                AWSimulationCoordinatorThread.Instance.WaitAndDiscard(
                    _parallelStageTicket);
                _parallelStageTicket = default;
            }

            _postRunner?.Abort();
            Reset();
            _batchIndex = 0;
        }

        private void RunParallelStageInBackground()
        {
            while (TryRunNextParallelJobGroup())
            {
            }

            _parallelStageFinishedInBackground = true;
        }

        private void Reset()
        {
            _batches.Clear();
            _manager = null;
            _parallelOptions = null;
            _parallelEnabled = false;
            _parallelGroupSize = 0;
            _batchIndex = 0;
            _parallelJobIndex = 0;
            _activeParallelBatchCount = 0;
            _collectJobBenchmarks = false;
            _useCustomPostRunner = false;
            _deferParallelToPresentationForCycle = false;
            _parallelStageFinishedInBackground = false;
            _parallelStageTicket = default;
            _stage = RunnerStage.Idle;
        }

        private bool TryRunNextMainThreadBatch(RunnerStage pJobStage)
        {
            while (_batchIndex < _batches.Count)
            {
                TBatch batch = _batches[_batchIndex++];
                List<Job<TObject>> jobs = GetJobs(batch, pJobStage);
                if (jobs.Count == 0) continue;
                if (!_collectJobBenchmarks)
                    RunMainThreadJobsWithoutBenchmark(batch, jobs);
                else if (pJobStage == RunnerStage.Pre)
                    batch.updateJobsPre(_elapsed);
                else
                    batch.updateJobsPost(_elapsed);
                return true;
            }

            return false;
        }

        private void RunMainThreadJobsWithoutBenchmark(TBatch pBatch,
            List<Job<TObject>> pJobs)
        {
            pBatch._elapsed = _elapsed;
            for (int i = 0; i < pJobs.Count; i++)
            {
                Job<TObject> job = pJobs[i];
                pBatch._cur_container = job.container;
                if (job.current_skips > 0)
                {
                    job.current_skips--;
                    continue;
                }

                job.job_updater();
                if (job.random_tick_skips > 0)
                    job.current_skips = Randy.randomInt(0,
                        job.random_tick_skips);
            }
        }

        private bool TryRunNextParallelJobGroup()
        {
            int jobCount = _batches.Count == 0
                ? 0
                : _batches[0].jobs_parallel.Count;
            while (_parallelJobIndex < jobCount)
            {
                // The maintenance stage has already prepared every active
                // container before the actor/building runner starts. Cultiway
                // skips this redundant wake-up job for the same reason: it
                // can otherwise reopen container mutation windows during a
                // large-step parallel pass.
                if (_batchIndex == 0 &&
                    ((_parallelJobRunner?.TrySkipAllBatches(
                         _batches[0].jobs_parallel[_parallelJobIndex],
                         _batches.Count, _elapsed) ?? false) ||
                     ShouldSkipParallelJob(_batches[0].jobs_parallel[
                         _parallelJobIndex])))
                {
                    _parallelJobIndex++;
                    continue;
                }

                if (_batchIndex >= _batches.Count)
                {
                    _parallelJobIndex++;
                    _batchIndex = 0;
                    continue;
                }

                int scannedCount = Math.Min(_parallelGroupSize,
                    _batches.Count - _batchIndex);
                EnsureActiveParallelBatchCapacity(scannedCount);
                _activeParallelBatchCount = 0;
                int endIndex = _batchIndex + scannedCount;
                for (; _batchIndex < endIndex; _batchIndex++)
                {
                    if (!HasParallelJobWork(_batchIndex,
                            _parallelJobIndex))
                        continue;
                    _activeParallelBatchIndices[
                        _activeParallelBatchCount++] = _batchIndex;
                }

                bool handledAsGroup = _activeParallelBatchCount > 0 &&
                    (_parallelJobRunner?.TryRunGroup(_batches,
                        _parallelJobIndex, _activeParallelBatchIndices,
                        _activeParallelBatchCount, _elapsed,
                        _parallelOptions) ?? false);
                if (handledAsGroup)
                {
                }
                else if (_parallelEnabled &&
                         _activeParallelBatchCount > 1)
                    AWSimulationWorkerPool.Instance.RunIndexed(0,
                        _activeParallelBatchCount, _parallelJobAction);
                else
                    for (int i = 0;
                         i < _activeParallelBatchCount;
                         i++)
                        RunCurrentParallelJob(i);

                return true;
            }

            return false;
        }

        private string FindNextParallelJobPhase()
        {
            int nextJobIndex = _parallelJobIndex;
            int nextBatchIndex = _batchIndex;
            int jobCount = _batches.Count == 0
                ? 0
                : _batches[0].jobs_parallel.Count;
            while (nextJobIndex < jobCount &&
                   nextBatchIndex >= _batches.Count)
            {
                nextJobIndex++;
                nextBatchIndex = 0;
            }

            while (nextJobIndex < jobCount &&
                   ShouldSkipParallelJob(_batches[0].jobs_parallel[
                       nextJobIndex]))
            {
                nextJobIndex++;
                nextBatchIndex = 0;
            }

            if (nextJobIndex >= jobCount) return null;
            Job<TObject> job = _batches[0].jobs_parallel[nextJobIndex];
            return _phasePrefix + ".parallel." + job.id +
                   ".batch_group." + nextBatchIndex;
        }

        private const string PrepareJobId = "prepare";
        private const string UpdateVisibilityJobId = "update_visibility";
        private const string UpdateStatsJobId = "update_stats";

        private bool CanDeferActorParallelStageToPresentation()
        {
            if (typeof(TBatch) != typeof(BatchActors)) return true;
            for (int batchIndex = 0; batchIndex < _batches.Count;
                 batchIndex++)
            {
                List<Job<TObject>> jobs = _batches[batchIndex].jobs_parallel;
                for (int jobIndex = 0; jobIndex < jobs.Count; jobIndex++)
                    if (string.Equals(jobs[jobIndex].id, UpdateStatsJobId,
                            StringComparison.Ordinal))
                        return false;
            }
            return true;
        }

        private bool ShouldSkipParallelJob(Job<TObject> pJob)
        {
            if (typeof(TBatch) != typeof(BatchActors) || pJob == null)
                return false;

            // Cultiway owns both container preparation and presentation
            // visibility when the frame-priority scheduler is active. The
            // render-frame snapshot path refreshes visibility once per frame,
            // so running the same scan once per simulation tick is redundant.
            return string.Equals(pJob.id, PrepareJobId,
                       StringComparison.Ordinal) ||
                   (AWPerformanceSettings.EnableFramePriorityScheduler &&
                    string.Equals(pJob.id, UpdateVisibilityJobId,
                        StringComparison.Ordinal));
        }

        private bool HasParallelJobWork(int pBatchListIndex,
            int pJobListIndex)
        {
            Job<TObject> job = _batches[pBatchListIndex]
                .jobs_parallel[pJobListIndex];
            ObjectContainer<TObject> container = job.container;
            return container == null ||
                   container.Count > 0 ||
                   container.isDirtyContainer();
        }

        private void EnsureActiveParallelBatchCapacity(int pCapacity)
        {
            if (_activeParallelBatchIndices.Length < pCapacity)
                Array.Resize(ref _activeParallelBatchIndices, pCapacity);
        }

        private bool TryRunNextParallelBatch()
        {
            if (_batchIndex >= _batches.Count) return false;

            TBatch batch = _batches[_batchIndex++];
            batch._elapsed = _elapsed;
            batch.updateJobsParallel(_elapsed);
            return true;
        }

        private void RunCurrentParallelJob(int pActiveIndex)
        {
            RunParallelJob(
                _activeParallelBatchIndices[pActiveIndex],
                _parallelJobIndex);
        }

        private void RunParallelJob(int pBatchListIndex,
            int pJobListIndex)
        {
            TBatch batch = _batches[pBatchListIndex];
            Job<TObject> job = batch.jobs_parallel[pJobListIndex];
            batch._elapsed = _elapsed;
            batch._cur_container = job.container;
            if (_parallelJobRunner == null ||
                !_parallelJobRunner.TryRun(batch, job, _elapsed))
                job.job_updater();
        }

        private static List<Job<TObject>> GetJobs(TBatch pBatch,
            RunnerStage pJobStage)
        {
            switch (pJobStage)
            {
                case RunnerStage.Pre:
                    return pBatch.jobs_pre;
                case RunnerStage.Parallel:
                    return pBatch.jobs_parallel;
                case RunnerStage.Post:
                    return pBatch.jobs_post;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pJobStage));
            }
        }

        private int FindNextMainThreadBatchIndex(RunnerStage pJobStage)
        {
            for (int index = _batchIndex; index < _batches.Count; index++)
                if (GetJobs(_batches[index], pJobStage).Count > 0)
                    return index;
            return -1;
        }
    }
}

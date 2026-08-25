using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AncientWarfare3.core.performance
{
    // Cultiway-master style batch lifecycle. AW3-specific work stays in the
    // post runner; no second presentation/coordinator simulation channel.
    internal sealed class AWCooperativeBatchRunner<TBatch, TObject>
        where TBatch : Batch<TObject>, new()
    {
        private enum RunnerStage
        {
            Idle, Pre, ClearParallelResults, Parallel,
            ApplyParallelResults, Post, Finish
        }

        private static readonly string[] StagePhaseNames =
        {
            "idle", "pre", "clear_parallel_results", "parallel",
            "apply_parallel_results", "post", "finish"
        };

        private readonly List<TBatch> _batches = new List<TBatch>();
        private readonly string _phasePrefix;
        private readonly bool _allowWorkerParallelism;
        private readonly IAWCooperativeBatchPostRunner<TBatch, TObject>
            _postRunner;
        private readonly Action<int> _parallelJobAction;
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

        public AWCooperativeBatchRunner(
            string pPhasePrefix,
            bool pAllowWorkerParallelism = true,
            bool pDeferParallelToPresentation = false,
            IAWCooperativeBatchPostRunner<TBatch, TObject> pPostRunner = null)
        {
            _phasePrefix = pPhasePrefix ??
                throw new ArgumentNullException(nameof(pPhasePrefix));
            _allowWorkerParallelism = pAllowWorkerParallelism;
            _postRunner = pPostRunner;
            _parallelJobAction = RunCurrentParallelJob;
            _ = pDeferParallelToPresentation;
        }

        public bool Active => _stage != RunnerStage.Idle;
        public bool WaitingForPresentationDispatch => false;
        public bool HasParallelPresentationWorkInFlight => false;
        public bool WaitingForBackgroundWork =>
            _stage == RunnerStage.Post && _useCustomPostRunner &&
            _postRunner.WaitingForBackgroundWork;
        public bool IsBackgroundWorkCompleted =>
            WaitingForBackgroundWork && _postRunner.IsBackgroundWorkCompleted;

        public void Start(
            JobManagerBase<TBatch, TObject> pJobManager,
            IEnumerable<TBatch> pActiveBatches,
            float pCycleElapsed,
            ParallelOptions pCycleParallelOptions,
            Comparison<TBatch> pComparison = null)
        {
            _manager = pJobManager ??
                throw new ArgumentNullException(nameof(pJobManager));
            _elapsed = pCycleElapsed;
            _parallelEnabled = Config.parallel_jobs_updater &&
                _allowWorkerParallelism;
            _parallelGroupSize = _parallelEnabled
                ? Math.Max(1, AWPerformanceSettings.ForegroundParallelism * 4)
                : 1;
            _parallelOptions = pCycleParallelOptions;
            _useCustomPostRunner = _parallelEnabled && _postRunner != null;
            if (_parallelEnabled && _parallelOptions == null)
                throw new InvalidOperationException(
                    "AW scheduler parallel batch is missing ParallelOptions.");

            _batches.Clear();
            if (pActiveBatches != null) _batches.AddRange(pActiveBatches);
            if (pComparison != null) _batches.Sort(pComparison);
            _collectJobBenchmarks = AWSimulationTickBenchmark.IsCapturing;
            if (_collectJobBenchmarks) _manager.clearJobBenchmarks();
            _batchIndex = 0;
            _parallelJobIndex = 0;
            _activeParallelBatchCount = 0;
            _stage = RunnerStage.Pre;
        }

        public string GetNextPhaseName()
        {
            if (_stage == RunnerStage.Post && _useCustomPostRunner)
                return _postRunner.GetNextPhaseName(_phasePrefix);

            if (_stage == RunnerStage.Parallel && _parallelEnabled)
            {
                int nextJob = _parallelJobIndex;
                int nextBatch = _batchIndex;
                int jobCount = _batches.Count == 0
                    ? 0 : _batches[0].jobs_parallel.Count;
                while (nextJob < jobCount && nextBatch >= _batches.Count)
                {
                    nextJob++;
                    nextBatch = 0;
                }
                if (nextJob < jobCount)
                    return _phasePrefix + ".parallel." +
                        _batches[0].jobs_parallel[nextJob].id +
                        ".batch_group." + nextBatch;
            }
            else if (_stage == RunnerStage.Pre ||
                     _stage == RunnerStage.Post)
            {
                int nextBatch = FindNextMainThreadBatchIndex(_stage);
                if (nextBatch >= 0)
                    return _phasePrefix + "." +
                        StagePhaseNames[(int)_stage] +
                        ".batch." + nextBatch;
            }

            switch (_stage)
            {
                case RunnerStage.Idle: return _phasePrefix + ".idle";
                case RunnerStage.Pre:
                case RunnerStage.ClearParallelResults:
                    return _phasePrefix + ".clear_parallel_results";
                case RunnerStage.Parallel:
                    return _phasePrefix + ".parallel";
                case RunnerStage.ApplyParallelResults:
                    return _phasePrefix + ".apply_parallel_results";
                case RunnerStage.Post:
                case RunnerStage.Finish:
                    return _phasePrefix + ".finish";
                default: throw new ArgumentOutOfRangeException();
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
                        _parallelJobIndex = 0;
                        continue;
                    case RunnerStage.ClearParallelResults:
                        _manager.clearParallelResults();
                        _stage = RunnerStage.Parallel;
                        return false;
                    case RunnerStage.Parallel:
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
                            AWSimulationTickBenchmark.RecordBatchJobs<
                                TBatch, TObject>(_manager.benchmark_id,
                                _batches);
                        Reset();
                        return true;
                    default: throw new ArgumentOutOfRangeException();
                }
            }
        }

        public bool TryJoinBackgroundWork(double pMaximumMilliseconds)
        {
            return !WaitingForBackgroundWork ||
                _postRunner.TryJoinBackgroundWork(pMaximumMilliseconds);
        }

        public void WaitForBackgroundWork()
        {
            if (WaitingForBackgroundWork) _postRunner.WaitForBackgroundWork();
        }

        public bool BeginParallelPresentationWork() => false;
        public bool RunDeferredParallelWorkSynchronously() => false;

        public void Abort()
        {
            _postRunner?.Abort();
            Reset();
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
            _stage = RunnerStage.Idle;
        }

        private bool TryRunNextMainThreadBatch(RunnerStage pJobStage)
        {
            while (_batchIndex < _batches.Count)
            {
                TBatch batch = _batches[_batchIndex++];
                List<Job<TObject>> jobs = GetJobs(batch, pJobStage);
                if (jobs.Count == 0) continue;
                if (pJobStage == RunnerStage.Pre)
                    batch.updateJobsPre(_elapsed);
                else
                    batch.updateJobsPost(_elapsed);
                return true;
            }
            return false;
        }

        private bool TryRunNextParallelJobGroup()
        {
            int jobCount = _batches.Count == 0
                ? 0 : _batches[0].jobs_parallel.Count;
            while (_parallelJobIndex < jobCount)
            {
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
                    if (HasParallelJobWork(_batchIndex,
                            _parallelJobIndex))
                        _activeParallelBatchIndices[
                            _activeParallelBatchCount++] = _batchIndex;
                }

                if (_activeParallelBatchCount > 1)
                    AWSimulationWorkerPool.Instance.RunIndexed(
                        0, _activeParallelBatchCount,
                        _parallelJobAction);
                else if (_activeParallelBatchCount == 1)
                    RunCurrentParallelJob(0);
                return true;
            }
            return false;
        }

        private bool HasParallelJobWork(int pBatchListIndex,
            int pJobListIndex)
        {
            Job<TObject> job = _batches[pBatchListIndex]
                .jobs_parallel[pJobListIndex];
            ObjectContainer<TObject> container = job.container;
            return container == null || container.Count > 0 ||
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
            int batchIndex = _activeParallelBatchIndices[pActiveIndex];
            TBatch batch = _batches[batchIndex];
            Job<TObject> job = batch.jobs_parallel[_parallelJobIndex];
            batch._elapsed = _elapsed;
            batch._cur_container = job.container;
            job.job_updater();
        }

        private static List<Job<TObject>> GetJobs(TBatch pBatch,
            RunnerStage pJobStage)
        {
            switch (pJobStage)
            {
                case RunnerStage.Pre: return pBatch.jobs_pre;
                case RunnerStage.Parallel: return pBatch.jobs_parallel;
                case RunnerStage.Post: return pBatch.jobs_post;
                default: throw new ArgumentOutOfRangeException(
                    nameof(pJobStage));
            }
        }

        private int FindNextMainThreadBatchIndex(RunnerStage pJobStage)
        {
            for (int i = _batchIndex; i < _batches.Count; i++)
                if (GetJobs(_batches[i], pJobStage).Count > 0) return i;
            return -1;
        }
    }
}

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
        private readonly string _idlePhase;
        private readonly string _prePhase;
        private readonly string _clearParallelResultsPhase;
        private readonly string _parallelPhase;
        private readonly string _applyParallelResultsPhase;
        private readonly string _postPhase;
        private readonly string _finishPhase;
        private readonly Action<int> _parallelBatchAction;
        private readonly bool _allowWorkerParallelism;
        private JobManagerBase<TBatch, TObject> _manager;
        private RunnerStage _stage;
        private float _elapsed;
        private int _batchIndex;
        private bool _parallelEnabled;
        private int _parallelGroupSize;
        private ParallelOptions _parallelOptions;
        private bool _collectJobBenchmarks;

        public AWCooperativeBatchRunner(string pPhasePrefix,
            bool pAllowWorkerParallelism)
        {
            _allowWorkerParallelism = pAllowWorkerParallelism;
            _idlePhase = pPhasePrefix + ".idle";
            _prePhase = pPhasePrefix + ".pre";
            _clearParallelResultsPhase =
                pPhasePrefix + ".clear_parallel_results";
            _parallelPhase = pPhasePrefix + ".parallel";
            _applyParallelResultsPhase =
                pPhasePrefix + ".apply_parallel_results";
            _postPhase = pPhasePrefix + ".post";
            _finishPhase = pPhasePrefix + ".finish";
            _parallelBatchAction = RunParallelBatch;
        }

        public bool Active => _stage != RunnerStage.Idle;

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
                ? AWPerformanceSettings.ForegroundParallelism
                : 1;
            _parallelOptions = pCycleParallelOptions;
            if (_parallelEnabled && _parallelOptions == null)
                throw new InvalidOperationException(
                    "AW scheduler parallel batch is missing ParallelOptions.");

            _batches.Clear();
            _batches.AddRange(pActiveBatches);
            if (pComparison != null) _batches.Sort(pComparison);

            _collectJobBenchmarks = AWSimulationTickBenchmark.IsCapturing;
            if (_collectJobBenchmarks)
                _manager.clearJobBenchmarks();

            _batchIndex = 0;
            _stage = RunnerStage.Pre;
        }

        public string GetNextPhaseName()
        {
            switch (_stage)
            {
                case RunnerStage.Idle: return _idlePhase;
                case RunnerStage.Pre: return _prePhase;
                case RunnerStage.ClearParallelResults:
                    return _clearParallelResultsPhase;
                case RunnerStage.Parallel: return _parallelPhase;
                case RunnerStage.ApplyParallelResults:
                    return _applyParallelResultsPhase;
                case RunnerStage.Post: return _postPhase;
                case RunnerStage.Finish: return _finishPhase;
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
                        continue;
                    case RunnerStage.ClearParallelResults:
                        _manager.clearParallelResults();
                        _stage = RunnerStage.Parallel;
                        return false;
                    case RunnerStage.Parallel:
                        if (TryRunNextParallelBatchGroup()) return false;
                        _stage = RunnerStage.ApplyParallelResults;
                        _batchIndex = 0;
                        continue;
                    case RunnerStage.ApplyParallelResults:
                        _manager.applyParallelResults();
                        _stage = RunnerStage.Post;
                        return false;
                    case RunnerStage.Post:
                        if (TryRunNextMainThreadBatch(RunnerStage.Post))
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

        public void Abort()
        {
            Reset();
            _batchIndex = 0;
        }

        private void Reset()
        {
            _batches.Clear();
            _manager = null;
            _parallelOptions = null;
            _parallelEnabled = false;
            _parallelGroupSize = 0;
            _collectJobBenchmarks = false;
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

        private bool TryRunNextParallelBatchGroup()
        {
            if (_batchIndex >= _batches.Count) return false;

            int startIndex = _batchIndex;
            int groupSize = Math.Min(_parallelGroupSize,
                _batches.Count - startIndex);
            int endIndex = startIndex + groupSize;
            if (_parallelEnabled && groupSize > 1)
                Parallel.For(startIndex, endIndex, _parallelOptions,
                    _parallelBatchAction);
            else
                RunParallelBatch(startIndex);

            _batchIndex = endIndex;
            return true;
        }

        private void RunParallelBatch(int pIndex)
        {
            TBatch batch = _batches[pIndex];
            batch._elapsed = _elapsed;
            batch.updateJobsParallel(_elapsed);
        }

        private static List<Job<TObject>> GetJobs(TBatch pBatch,
            RunnerStage pJobStage)
        {
            switch (pJobStage)
            {
                case RunnerStage.Pre: return pBatch.jobs_pre;
                case RunnerStage.Parallel: return pBatch.jobs_parallel;
                case RunnerStage.Post: return pBatch.jobs_post;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pJobStage));
            }
        }
    }
}

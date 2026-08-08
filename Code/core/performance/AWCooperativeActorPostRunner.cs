using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AncientWarfare3.core.performance
{
    internal sealed class AWCooperativeActorPostRunner :
        IAWCooperativeBatchPostRunner<BatchActors, Actor>
    {
        private const string EnemySearchJobId = "b3_findEnemyTarget";

        private enum PostStage
        {
            Idle,
            PrepareBatches,
            ScheduleEnemySearch,
            AwaitEnemySearch,
            CommitEnemySearch,
            AfterBatches,
            Finish
        }

        private readonly List<SearchWorkItem> _workItems =
            new List<SearchWorkItem>();
        private readonly List<SearchWorkItem> _workItemPool =
            new List<SearchWorkItem>();
        private readonly Action _runEnemySearch;
        private List<BatchActors> _batches;
        private ParallelOptions _parallelOptions;
        private PostStage _stage;
        private AWSimulationCoordinatorThread.WorkTicket _searchTicket;
        private float _elapsed;
        private int _enemySearchJobIndex;
        private int _batchIndex;
        private int _commitIndex;
        private int _workGroupSize;

        private static long _workerTicks;
        private static long _commitTicks;
        private static long _enemySearchCalls;
        private static long _enemySearchCandidates;
        private static long _enemySearchEmpty;

        private static bool CaptureDiagnostics =>
            AWPerformanceSettings.EnablePerformanceDiagnostics ||
            Bench.bench_enabled || AWSimulationTickBenchmark.IsCapturing;

        internal AWCooperativeActorPostRunner()
        {
            _runEnemySearch = RunEnemySearch;
        }

        public bool WaitingForBackgroundWork =>
            _stage == PostStage.AwaitEnemySearch && _searchTicket.IsValid;

        public bool IsBackgroundWorkCompleted =>
            WaitingForBackgroundWork &&
            AWSimulationCoordinatorThread.Instance.IsCompleted(_searchTicket);

        public void Start(List<BatchActors> pActiveBatches, float pElapsed,
            ParallelOptions pParallelOptions)
        {
            _batches = pActiveBatches ??
                throw new ArgumentNullException(nameof(pActiveBatches));
            _parallelOptions = pParallelOptions ??
                throw new ArgumentNullException(nameof(pParallelOptions));
            _elapsed = pElapsed;
            _batchIndex = 0;
            _commitIndex = 0;
            _workGroupSize = Math.Max(1,
                AWPerformanceSettings.ForegroundParallelism * 4);
            _searchTicket = default;
            ResetWorkItems();

            if (_batches.Count == 0)
            {
                _enemySearchJobIndex = -1;
                _stage = PostStage.Finish;
                return;
            }

            _enemySearchJobIndex = FindEnemySearchJobIndex(
                _batches[0].jobs_post);
            if (_enemySearchJobIndex < 0)
                throw new InvalidOperationException(
                    "Actor post jobs do not contain b3_findEnemyTarget.");
            for (int i = 1; i < _batches.Count; i++)
                if (FindEnemySearchJobIndex(_batches[i].jobs_post) !=
                    _enemySearchJobIndex)
                    throw new InvalidOperationException(
                        "Actor post job order differs between active batches.");

            _stage = PostStage.PrepareBatches;
        }

        public string GetNextPhaseName(string pPhasePrefix)
        {
            switch (_stage)
            {
                case PostStage.PrepareBatches:
                    return pPhasePrefix + ".post.enemy.prepare";
                case PostStage.ScheduleEnemySearch:
                    return pPhasePrefix + ".post.enemy.schedule";
                case PostStage.AwaitEnemySearch:
                    return pPhasePrefix + ".post.enemy.await";
                case PostStage.CommitEnemySearch:
                    return pPhasePrefix + ".post.enemy.commit";
                case PostStage.AfterBatches:
                    return pPhasePrefix + ".post.after_enemy";
                case PostStage.Finish:
                    return pPhasePrefix + ".post.finish";
                default:
                    return pPhasePrefix + ".post.idle";
            }
        }

        public bool TryJoinBackgroundWork(double pMaximumMilliseconds)
        {
            return !WaitingForBackgroundWork ||
                   AWSimulationCoordinatorThread.Instance.TryWait(
                       _searchTicket, pMaximumMilliseconds);
        }

        public void WaitForBackgroundWork()
        {
            if (WaitingForBackgroundWork)
                AWSimulationCoordinatorThread.Instance.Wait(_searchTicket);
        }

        public bool Step()
        {
            while (true)
            {
                switch (_stage)
                {
                    case PostStage.Idle:
                        return true;
                    case PostStage.PrepareBatches:
                        if (_batchIndex < _batches.Count)
                        {
                            BatchActors pBatch = _batches[_batchIndex++];
                            RunPostRange(pBatch, 0,
                                _enemySearchJobIndex);
                            PrepareEnemySearchJob(pBatch);
                            return false;
                        }
                        ScheduleEnemySearch();
                        continue;
                    case PostStage.ScheduleEnemySearch:
                        ScheduleEnemySearch();
                        continue;
                    case PostStage.AwaitEnemySearch:
                        if (!AWSimulationCoordinatorThread.Instance
                                .IsCompleted(_searchTicket))
                            return false;
                        AWSimulationCoordinatorThread.Instance.Wait(
                            _searchTicket);
                        AWSimulationCoordinatorThread.WorkResult result =
                            AWSimulationCoordinatorThread.Instance.Complete(
                                _searchTicket);
                        _searchTicket = default;
                        if (CaptureDiagnostics)
                            Interlocked.Add(ref _workerTicks,
                                result.WallTicks);
                        _stage = PostStage.CommitEnemySearch;
                        continue;
                    case PostStage.CommitEnemySearch:
                        if (CommitEnemySearchGroup()) return false;
                        _batchIndex = 0;
                        _stage = PostStage.AfterBatches;
                        continue;
                    case PostStage.AfterBatches:
                        if (_batchIndex < _batches.Count)
                        {
                            BatchActors pBatch = _batches[_batchIndex++];
                            RunPostRange(pBatch,
                                _enemySearchJobIndex + 1,
                                pBatch.jobs_post.Count);
                            return false;
                        }
                        _stage = PostStage.Finish;
                        continue;
                    case PostStage.Finish:
                        ResetCycle();
                        return true;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public void Abort()
        {
            if (_searchTicket.IsValid)
                AWSimulationCoordinatorThread.Instance.WaitAndDiscard(
                    _searchTicket);
            ResetCycle();
        }

        internal static AWActorPostDiagnosticSnapshot TakeDiagnostics()
        {
            return new AWActorPostDiagnosticSnapshot(
                Interlocked.Exchange(ref _workerTicks, 0L),
                Interlocked.Exchange(ref _commitTicks, 0L),
                Interlocked.Exchange(ref _enemySearchCalls, 0L),
                Interlocked.Exchange(ref _enemySearchCandidates, 0L),
                Interlocked.Exchange(ref _enemySearchEmpty, 0L));
        }

        private void RunPostRange(BatchActors pBatch, int pStartJobIndex,
            int pEndJobIndex)
        {
            pBatch._elapsed = _elapsed;
            for (int i = pStartJobIndex; i < pEndJobIndex; i++)
            {
                Job<Actor> job = pBatch.jobs_post[i];
                pBatch._cur_container = job.container;
                if (job.current_skips > 0)
                {
                    job.current_skips--;
                    continue;
                }

                long startedAt = AWSimulationTickBenchmark.IsCapturing
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                job.job_updater();
                if (job.random_tick_skips > 0)
                    job.current_skips = Randy.randomInt(0,
                        job.random_tick_skips);
                if (startedAt != 0L)
                {
                    job.time_benchmark += ElapsedSeconds(startedAt);
                    job.counter += job.container?.Count ?? 0;
                }
            }
        }

        private void PrepareEnemySearchJob(BatchActors pBatch)
        {
            Job<Actor> job =
                pBatch.jobs_post[_enemySearchJobIndex];
            pBatch._elapsed = _elapsed;
            pBatch._cur_container = job.container;
            if (job.current_skips > 0)
            {
                job.current_skips--;
                return;
            }

            bool captureDiagnostics = CaptureDiagnostics;
            long startedAt = captureDiagnostics
                ? Stopwatch.GetTimestamp()
                : 0L;
            ObjectContainer<Actor> container = job.container;
            if (container == null)
                return;
            if (container.Count <= 0 && !container.isDirtyContainer())
            {
                AdvanceRandomSkip(job);
                return;
            }

            container.checkAddRemove();
            Actor[] actors = container.getFastSimpleArray() ??
                             Array.Empty<Actor>();
            int count = container.Count;
            pBatch._array = actors;
            pBatch._count = count;
            if (!World.world.isPaused())
                for (int i = 0; i < count; i++)
                    PrepareEnemySearch(actors[i]);

            AdvanceRandomSkip(job);
            if (AWSimulationTickBenchmark.IsCapturing)
            {
                job.time_benchmark += ElapsedSeconds(startedAt);
                job.counter += count;
            }
        }

        private void PrepareEnemySearch(Actor pActor)
        {
            if (pActor._update_done || pActor._beh_skip ||
                !pActor.isAllowedToLookForEnemies() ||
                pActor.isInWaterAndCantAttack() ||
                pActor._has_status_strange_urge)
                return;

            if (pActor.has_attack_target)
            {
                if (!pActor.hasTask() || !pActor.ai.task.in_combat)
                    pActor.setTask("fighting", pClean: true,
                        pCleanJob: true);
                return;
            }
            if (pActor._timeout_targets > 0f) return;

            bool applyBackoff = AWEnemySearchBackoffRules.ShouldApply(
                pActor.has_attack_target, pActor._timeout_targets,
                pActor.is_moving, pActor.isUsingPath());

            pActor._timeout_targets =
                0.1f + Randy.randomFloat(0f, 1f);
            EnemyFinderData data = EnemiesFinder.findEnemiesFrom(
                pActor.current_tile, pActor.kingdom);

            List<BaseSimObject> candidates = data.list;
            if (CaptureDiagnostics)
            {
                Interlocked.Increment(ref _enemySearchCalls);
                Interlocked.Add(ref _enemySearchCandidates,
                    candidates.Count);
                if (candidates.Count == 0)
                    Interlocked.Increment(ref _enemySearchEmpty);
            }

            bool findClosest = true;
            int randomOffset = 0;
            if (candidates.Count > 50)
            {
                findClosest = Randy.randomChance(0.6f);
                if (!findClosest)
                    randomOffset = Randy.randomInt(0, candidates.Count);
            }

            int aggressionCount = pActor._aggression_targets.Count;
            if (candidates.Count == 0 && aggressionCount == 0) return;
            SearchWorkItem item = RentSearchWorkItem();
            item.Configure(pActor, candidates, findClosest, randomOffset,
                aggressionCount, applyBackoff);
            _workItems.Add(item);
        }

        private void ScheduleEnemySearch()
        {
            if (_workItems.Count == 0)
            {
                _stage = PostStage.CommitEnemySearch;
                return;
            }
            _searchTicket = AWSimulationCoordinatorThread.Instance.Begin(
                "vanilla.actors.post.enemy.search", _runEnemySearch);
            _stage = PostStage.AwaitEnemySearch;
        }

        private void RunEnemySearch()
        {
            Parallel.For(0, _workItems.Count, _parallelOptions,
                SearchWorkItemAt);
        }

        private void SearchWorkItemAt(int pIndex)
        {
            _workItems[pIndex].Search();
        }

        private bool CommitEnemySearchGroup()
        {
            if (_commitIndex >= _workItems.Count) return false;
            int end = Math.Min(_workItems.Count,
                _commitIndex + _workGroupSize);
            bool captureDiagnostics = CaptureDiagnostics;
            long startedAt = captureDiagnostics
                ? Stopwatch.GetTimestamp()
                : 0L;
            for (; _commitIndex < end; _commitIndex++)
                _workItems[_commitIndex].Commit();
            if (captureDiagnostics)
                Interlocked.Add(ref _commitTicks,
                    Stopwatch.GetTimestamp() - startedAt);
            return true;
        }

        private static int FindEnemySearchJobIndex(
            List<Job<Actor>> pJobs)
        {
            for (int i = 0; i < pJobs.Count; i++)
                if (string.Equals(pJobs[i].id, EnemySearchJobId,
                        StringComparison.Ordinal))
                    return i;
            return -1;
        }

        private static void AdvanceRandomSkip(Job<Actor> pJob)
        {
            if (pJob.random_tick_skips > 0)
                pJob.current_skips = Randy.randomInt(0,
                    pJob.random_tick_skips);
        }

        private static double ElapsedSeconds(long pStartedAt)
        {
            return (Stopwatch.GetTimestamp() - pStartedAt) /
                   (double)Stopwatch.Frequency;
        }

        private void ResetWorkItems()
        {
            for (int i = 0; i < _workItems.Count; i++)
                _workItems[i].Reset();
            if (_workItemPool.Count < _workItems.Count)
                for (int i = _workItemPool.Count;
                     i < _workItems.Count; i++)
                    _workItemPool.Add(_workItems[i]);
            _workItems.Clear();
        }

        private SearchWorkItem RentSearchWorkItem()
        {
            int index = _workItems.Count;
            if (index < _workItemPool.Count)
                return _workItemPool[index];
            var item = new SearchWorkItem();
            _workItemPool.Add(item);
            return item;
        }

        private void ResetCycle()
        {
            ResetWorkItems();
            _batches = null;
            _parallelOptions = null;
            _elapsed = 0f;
            _enemySearchJobIndex = -1;
            _batchIndex = 0;
            _commitIndex = 0;
            _workGroupSize = 0;
            _searchTicket = default;
            _stage = PostStage.Idle;
        }

        private sealed class SearchWorkItem
        {
            private readonly CandidateView _candidateView =
                new CandidateView();
            private readonly List<BaseSimObject> _aggressionCandidates =
                new List<BaseSimObject>();
            private Actor _actor;
            private List<BaseSimObject> _primaryCandidates;
            private bool _findClosest;
            private int _randomOffset;
            private int _originalAggressionCount;
            private bool _clearAggressionTargets;
            private bool _applyBackoff;
            private BaseSimObject _result;

            internal void Configure(Actor pActor,
                List<BaseSimObject> pPrimaryCandidates, bool pFindClosest,
                int pRandomOffset, int pOriginalAggressionCount,
                bool pApplyBackoff)
            {
                _actor = pActor;
                _primaryCandidates = pPrimaryCandidates;
                _findClosest = pFindClosest;
                _randomOffset = pRandomOffset;
                _originalAggressionCount = pOriginalAggressionCount;
                _clearAggressionTargets = false;
                _applyBackoff = pApplyBackoff;
                _result = null;
            }

            internal void Search()
            {
                if (_primaryCandidates.Count > 0)
                {
                    IEnumerable<BaseSimObject> source = _primaryCandidates;
                    if (!_findClosest)
                    {
                        _candidateView.Configure(_primaryCandidates,
                            _randomOffset);
                        source = _candidateView;
                    }
                    _result = _actor.checkObjectList(source,
                        _actor.asset.can_attack_buildings, _findClosest,
                        pIgnoreStunned: false, int.MaxValue);
                }

                if (_result != null || _originalAggressionCount <= 0)
                    return;
                foreach (long targetId in _actor._aggression_targets)
                {
                    Actor target = World.world.units.get(targetId);
                    if (target != null && !target.isRekt())
                        _aggressionCandidates.Add(target);
                }
                if (_aggressionCandidates.Count == 0)
                {
                    _clearAggressionTargets = true;
                    return;
                }
                _result = _actor.checkObjectList(_aggressionCandidates,
                    _actor.asset.can_attack_buildings,
                    pFindClosest: true, pIgnoreStunned: true, 30);
            }

            internal void Commit()
            {
                Actor actor = _actor;
                if (actor == null || actor.isRekt() ||
                    actor.has_attack_target)
                    return;
                if (_result != null &&
                    (_result.isRekt() ||
                     !actor.canAttackTarget(_result,
                         pCheckForFactions: true,
                         pAttackBuildings:
                         actor.asset.can_attack_buildings)))
                    _result = null;
                if (_result == null)
                {
                    if (_clearAggressionTargets &&
                        actor._aggression_targets.Count ==
                        _originalAggressionCount)
                        actor._aggression_targets.Clear();
                    if (_applyBackoff)
                        actor._timeout_targets =
                            AWEnemySearchBackoffRules.ResolveTimeout(
                                actor._timeout_targets,
                                Config.time_scale_asset?.multiplier ?? 1f);
                    return;
                }
                actor.startFightingWith(_result);
                actor.stopMovement();
                actor.skipBehaviour();
            }

            internal void Reset()
            {
                _actor = null;
                _primaryCandidates = null;
                _aggressionCandidates.Clear();
                _candidateView.ResetSource();
                _applyBackoff = false;
                _result = null;
            }
        }

        private sealed class CandidateView : IEnumerable<BaseSimObject>,
            IEnumerator<BaseSimObject>
        {
            private List<BaseSimObject> _source;
            private int _offset;
            private int _index;

            public BaseSimObject Current =>
                _source[(_index + _offset) % _source.Count];
            object IEnumerator.Current => Current;

            internal void Configure(List<BaseSimObject> pSource,
                int pOffset)
            {
                _source = pSource;
                _offset = pOffset;
                _index = -1;
            }

            public IEnumerator<BaseSimObject> GetEnumerator()
            {
                _index = -1;
                return this;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public bool MoveNext() => ++_index < _source.Count;
            public void Reset() => _index = -1;
            public void Dispose() { }

            internal void ResetSource()
            {
                _source = null;
                _offset = 0;
                _index = -1;
            }
        }
    }

    internal readonly struct AWActorPostDiagnosticSnapshot
    {
        internal AWActorPostDiagnosticSnapshot(long pWorkerTicks,
            long pCommitTicks, long pCalls, long pCandidates,
            long pEmpty)
        {
            WorkerTicks = pWorkerTicks;
            CommitTicks = pCommitTicks;
            Calls = pCalls;
            Candidates = pCandidates;
            Empty = pEmpty;
        }

        internal long WorkerTicks { get; }
        internal long CommitTicks { get; }
        internal long Calls { get; }
        internal long Candidates { get; }
        internal long Empty { get; }
    }
}

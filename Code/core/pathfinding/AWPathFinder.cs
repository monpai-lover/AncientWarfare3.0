// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    public interface IAWPathGenerator
    {
        void Generate(AWPathRequest pRequest, CancellationToken pCancellation);
    }

    public interface IAWPathSegmentGenerator : IAWPathGenerator
    {
        AWPathGenerationResult GenerateSegment(AWPathRequest pRequest,
            CancellationToken pCancellation, int pMaximumSteps);
    }

    public enum AWPathSubmissionDisposition
    {
        Reused,
        Submitted,
        ReplacedPending,
        ReplacedRunning,
        Rejected
    }

    internal readonly struct AWPathQueueSnapshot
    {
        internal AWPathQueueSnapshot(int pOperationalQueued,
            int pEssentialQueued, int pAmbientQueued,
            int pOperationalActive, int pEssentialActive,
            int pAmbientActive)
        {
            OperationalQueued = pOperationalQueued;
            EssentialQueued = pEssentialQueued;
            AmbientQueued = pAmbientQueued;
            OperationalActive = pOperationalActive;
            EssentialActive = pEssentialActive;
            AmbientActive = pAmbientActive;
        }

        internal int OperationalQueued { get; }
        internal int EssentialQueued { get; }
        internal int AmbientQueued { get; }
        internal int OperationalActive { get; }
        internal int EssentialActive { get; }
        internal int AmbientActive { get; }
    }

    public sealed class AWPathFinder : IDisposable
    {
        private const int SegmentStepBudget = 16;
        private const int SegmentLowWatermark = 8;
        private readonly IAWPathGenerator _generator;
        private readonly AWPathDiagnostics _diagnostics;
        private readonly ConcurrentDictionary<long, PathSessionRecord> _sessions =
            new ConcurrentDictionary<long, PathSessionRecord>();
        private readonly object _lifecycleGate = new object();
        private readonly ConcurrentQueue<AWScheduledPathWork> _starvedQueue =
            new ConcurrentQueue<AWScheduledPathWork>();
        private readonly ConcurrentQueue<AWScheduledPathWork> _initialQueue =
            new ConcurrentQueue<AWScheduledPathWork>();
        private readonly ConcurrentQueue<AWScheduledPathWork> _continuationQueue =
            new ConcurrentQueue<AWScheduledPathWork>();
        private readonly SemaphoreSlim _queueSignal = new SemaphoreSlim(0);
        private readonly ManualResetEventSlim _drained =
            new ManualResetEventSlim(false);
        private Thread[] _workers = Array.Empty<Thread>();
        private int _started;
        private int _stopping;
        private int _disposed;
        private int _requestAdmissions;
        private int _queueDepth;
        private int _operationalQueued;
        private int _essentialQueued;
        private int _ambientQueued;
        private int _consecutiveStarvedWork;
        private long _staleWorkCount;

        public AWPathFinder(IAWPathGenerator pGenerator)
            : this(pGenerator, null)
        {
        }

        internal AWPathFinder(IAWPathGenerator pGenerator,
            AWPathDiagnostics pDiagnostics)
        {
            _generator = pGenerator ?? throw new ArgumentNullException(nameof(pGenerator));
            _diagnostics = pDiagnostics;
        }

        public int ActiveCount => _sessions.Count;
        public int QueueDepth => Math.Max(0, Volatile.Read(ref _queueDepth));
        public int WorkerCount => _workers.Length;
        public long StaleWorkCount => Interlocked.Read(ref _staleWorkCount);

        internal AWPathQueueSnapshot SnapshotQueues()
        {
            int operationalActive = 0;
            int essentialActive = 0;
            int ambientActive = 0;
            foreach (PathSessionRecord record in _sessions.Values)
            {
                lock (record.Gate)
                {
                    CountWork(record.Latest?.Request?.WorkClass, active: true,
                        ref operationalActive, ref essentialActive,
                        ref ambientActive);
                }
            }
            return new AWPathQueueSnapshot(
                Math.Max(0, Volatile.Read(ref _operationalQueued)),
                Math.Max(0, Volatile.Read(ref _essentialQueued)),
                Math.Max(0, Volatile.Read(ref _ambientQueued)),
                operationalActive, essentialActive, ambientActive);
        }

        public void Start(int pWorkers)
        {
            lock (_lifecycleGate)
            {
                if (Volatile.Read(ref _stopping) != 0 ||
                    Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                    return;
                int count = Math.Max(1, Math.Min(
                    AWPathfindingConfig.MaximumWorkerCount, pWorkers));
                _workers = new Thread[count];
                for (int i = 0; i < count; i++)
                {
                    var thread = new Thread(WorkerLoop)
                    {
                        IsBackground = true,
                        Name = "AW3 Path Worker " + (i + 1)
                    };
                    _workers[i] = thread;
                    thread.Start();
                }
            }
        }

        public bool Request(AWPathRequest pRequest, out bool pReused)
        {
            bool accepted = Request(pRequest,
                out AWPathSubmissionDisposition disposition);
            pReused = disposition == AWPathSubmissionDisposition.Reused;
            return accepted;
        }

        public bool Request(AWPathRequest pRequest,
            out AWPathSubmissionDisposition pDisposition)
        {
            pDisposition = AWPathSubmissionDisposition.Rejected;
            if (!TryEnterRequestAdmission())
            {
                Reject(pRequest, pDisposition);
                return false;
            }

            try
            {
                return RequestLocked(pRequest, out pDisposition);
            }
            finally
            {
                Interlocked.Decrement(ref _requestAdmissions);
            }
        }

        private bool TryEnterRequestAdmission()
        {
            if (Volatile.Read(ref _stopping) != 0) return false;
            Interlocked.Increment(ref _requestAdmissions);
            if (Volatile.Read(ref _stopping) == 0) return true;
            Interlocked.Decrement(ref _requestAdmissions);
            return false;
        }

        private bool RequestLocked(AWPathRequest pRequest,
            out AWPathSubmissionDisposition pDisposition)
        {
            pDisposition = AWPathSubmissionDisposition.Rejected;
            if (pRequest == null || Volatile.Read(ref _started) == 0 ||
                Volatile.Read(ref _stopping) != 0)
            {
                Reject(pRequest, pDisposition);
                return false;
            }

            while (true)
            {
                if (_sessions.TryGetValue(pRequest.ActorId,
                        out PathSessionRecord existing))
                {
                    lock (existing.Gate)
                    {
                        if (!_sessions.TryGetValue(pRequest.ActorId,
                                out PathSessionRecord current) ||
                            !ReferenceEquals(current, existing) ||
                            existing.Latest == null)
                            continue;
                        PathfindingTask latest = existing.Latest;
                        if (CanReuse(latest, pRequest.ReuseKey))
                        {
                            pRequest.Dispose();
                            pDisposition = AWPathSubmissionDisposition.Reused;
                            if (existing.Running != null)
                                _diagnostics?.OnReusedRunning();
                            _diagnostics?.OnSubmission(
                                latest.Request.WorkClass, pDisposition);
                            return true;
                        }

                        pDisposition = existing.Running != null
                            ? AWPathSubmissionDisposition.ReplacedRunning
                            : AWPathSubmissionDisposition.ReplacedPending;
                        ReplaceLocked(existing, pRequest);
                        _diagnostics?.OnSubmission(pRequest.WorkClass,
                            pDisposition);
                        return true;
                    }
                }

                var record = new PathSessionRecord(pRequest.ActorId);
                lock (record.Gate)
                {
                    if (!_sessions.TryAdd(pRequest.ActorId, record)) continue;
                    var task = new PathfindingTask(pRequest);
                    record.Latest = task;
                    record.Queued = task;
                    if (!ScheduleLocked(record,
                            PriorityFor(pRequest.WorkClass)))
                    {
                        ((ICollection<KeyValuePair<long, PathSessionRecord>>)_sessions)
                            .Remove(new KeyValuePair<long, PathSessionRecord>(
                                pRequest.ActorId, record));
                        task.Cancel(
                            AWPathFailureReason.CancelledByNewRequest);
                        task.ReleaseOwner();
                        task.ReleaseWorker();
                        Reject(null, pDisposition);
                        return false;
                    }
                }
                pDisposition = AWPathSubmissionDisposition.Submitted;
                _diagnostics?.OnSubmission(pRequest.WorkClass, pDisposition);
                return true;
            }
        }

        public bool TryReuse(AWPathReuseKey pReuseKey)
        {
            if (!_sessions.TryGetValue(pReuseKey.ActorId,
                    out PathSessionRecord record)) return false;
            lock (record.Gate)
            {
                if (!CanReuse(record.Latest, pReuseKey)) return false;
                if (record.Running != null) _diagnostics?.OnReusedRunning();
                _diagnostics?.OnSubmission(record.Latest.Request.WorkClass,
                    AWPathSubmissionDisposition.Reused);
                return true;
            }
        }

        public bool IsWaitingForWorker(long pActorId)
        {
            if (!_sessions.TryGetValue(pActorId,
                    out PathSessionRecord record)) return false;
            lock (record.Gate)
                return record.Queued != null &&
                       ReferenceEquals(record.Latest, record.Queued);
        }

        public bool IsWorkerRunning(long pActorId)
        {
            if (!_sessions.TryGetValue(pActorId,
                    out PathSessionRecord record)) return false;
            lock (record.Gate)
                return record.Running != null &&
                       ReferenceEquals(record.Latest, record.Running);
        }

        public AWPathPollResult Poll(long pActorId)
        {
            if (!_sessions.TryGetValue(pActorId, out PathSessionRecord record))
                return new AWPathPollResult(AWPathPollKind.NoRequest);
            PathfindingTask task;
            lock (record.Gate) task = record.Latest;
            return PollOwned(pActorId, task);
        }

        public AWPathPollResult OpenReadyCursor(long pActorId,
            out ReadyPathCursor pCursor)
        {
            pCursor = default;
            if (!_sessions.TryGetValue(pActorId, out PathSessionRecord record))
                return new AWPathPollResult(AWPathPollKind.NoRequest);
            PathfindingTask task;
            lock (record.Gate) task = record.Latest;
            AWPathPollResult result = PollOwned(pActorId, task);
            if (result.Kind == AWPathPollKind.StepReady)
                pCursor = new ReadyPathCursor(this, pActorId, task);
            return result;
        }

        public bool Consume(long pActorId)
        {
            return Consume(pActorId, null);
        }

        private bool Consume(long pActorId, PathfindingTask pExpectedTask)
        {
            if (!_sessions.TryGetValue(pActorId,
                    out PathSessionRecord record)) return false;
            bool shouldCleanup = false;
            bool consumed = false;
            PathfindingTask task;
            lock (record.Gate)
            {
                task = record.Latest;
                if (pExpectedTask != null &&
                    !ReferenceEquals(task, pExpectedTask)) return false;
                if (!task.Request.Stream.TryTake(out _)) return false;
                consumed = true;
                if (task.Request.Stream.Count <= SegmentLowWatermark)
                {
                    if (ScheduleLocked(record,
                            AWPathWorkPriority.Continuation))
                    {
                        // The session owns one continuation token, so a
                        // burst of consumers cannot enqueue duplicates.
                    }
                }
                shouldCleanup = task.Request.Stream.Count == 0 &&
                    IsTerminal(task.Request.Stream.State);
            }
            if (shouldCleanup) CleanupOwned(pActorId, task);
            return consumed;
        }

        public void Cancel(long pActorId, AWPathFailureReason pReason)
        {
            while (_sessions.TryGetValue(pActorId,
                       out PathSessionRecord record))
            {
                lock (record.Gate)
                {
                    if (!((ICollection<KeyValuePair<long,
                            PathSessionRecord>>)_sessions).Remove(
                            new KeyValuePair<long, PathSessionRecord>(
                                pActorId, record)))
                        continue;
                    CancelRecordLocked(record, pReason);
                    return;
                }
            }
        }

        public void Clear(AWPathFailureReason pReason)
        {
            lock (_lifecycleGate) ClearLocked(pReason);
        }

        private void ClearLocked(AWPathFailureReason pReason)
        {
            foreach (KeyValuePair<long, PathSessionRecord> pair in _sessions)
            {
                PathSessionRecord record = pair.Value;
                lock (record.Gate)
                {
                    if (!((ICollection<KeyValuePair<long,
                            PathSessionRecord>>)_sessions).Remove(
                            new KeyValuePair<long, PathSessionRecord>(
                                pair.Key, record)))
                        continue;
                    CancelRecordLocked(record, pReason);
                }
            }
        }

        public void StopAndDrain()
        {
            bool ownsDrain;
            lock (_lifecycleGate)
            {
                ownsDrain = Interlocked.CompareExchange(
                    ref _stopping, 1, 0) == 0;
            }
            if (!ownsDrain)
            {
                _drained.Wait();
                return;
            }

            WaitForRequestAdmissions();
            Thread[] workers;
            lock (_lifecycleGate)
            {
                ClearLocked(AWPathFailureReason.WorldCleared);
                workers = _workers;
                for (int i = 0; i < workers.Length; i++)
                    _queueSignal.Release();
            }
            foreach (Thread worker in workers)
            {
                if (worker == null || worker == Thread.CurrentThread) continue;
                worker.Join();
            }
            lock (_lifecycleGate)
            {
                _workers = Array.Empty<Thread>();
                Volatile.Write(ref _stopping, 2);
            }
            _drained.Set();
        }

        public void Dispose()
        {
            StopAndDrain();
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _queueSignal.Dispose();
        }

        private void WaitForRequestAdmissions()
        {
            SpinWait wait = new SpinWait();
            while (Volatile.Read(ref _requestAdmissions) != 0)
                wait.SpinOnce();
        }

        private void WorkerLoop()
        {
            try
            {
                while (true)
                {
                    _queueSignal.Wait();
                    if (Volatile.Read(ref _stopping) != 0) break;
                    if (!TryTakeWork(out AWScheduledPathWork work)) continue;
                    _diagnostics?.OnDequeued(work.Priority, work.EnqueuedAt);

                    PathfindingTask task = null;
                    PathSessionRecord record = null;
                    if (!_sessions.TryGetValue(work.OwnerId, out record))
                    {
                        Interlocked.Increment(ref _staleWorkCount);
                        continue;
                    }
                    lock (record.Gate)
                    {
                        if (!_sessions.TryGetValue(work.OwnerId,
                                out PathSessionRecord current) ||
                            !ReferenceEquals(current, record) ||
                            !record.Session.TryBeginWork(work.QueueVersion) ||
                            record.Queued == null)
                        {
                            Interlocked.Increment(ref _staleWorkCount);
                            continue;
                        }
                        task = record.Queued;
                        record.Queued = null;
                        record.Running = task;
                    }

                    IAWPathSegmentGenerator segmentGenerator = _generator as
                        IAWPathSegmentGenerator;
                    bool usedSegmentGenerator = segmentGenerator != null;
                    AWPathGenerationResult segmentResult = default;
                    try
                    {
                        if (!IsTerminal(task.Request.Stream.State))
                        {
                            if (usedSegmentGenerator)
                            {
                                segmentResult = segmentGenerator.GenerateSegment(
                                    task.Request,
                                    task.Request.Cancellation.Token,
                                    SegmentStepBudget);
                                ApplySegmentResult(task.Request, segmentResult);
                            }
                            else
                            {
                                _generator.Generate(task.Request,
                                    task.Request.Cancellation.Token);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        task.Request.Stream.Cancel(
                            AWPathFailureReason.CancelledByNewRequest);
                    }
                    catch (Exception error)
                    {
                        task.Request.Stream.Fail(
                            AWPathFailureReason.GeneratorException, error);
                    }
                    finally
                    {
                        if (!usedSegmentGenerator ||
                            !segmentResult.Succeeded ||
                            segmentResult.ReachedTarget)
                            task.Request.Stream.EnsureCompleted();
                        if (task.Request.Stream.State == AWPathRequestState.Succeeded)
                            _diagnostics?.OnCompleted();
                        else if (task.Request.Stream.State == AWPathRequestState.Failed)
                        {
                            _diagnostics?.OnFailed();
                            Exception error = task.Request.Stream.Error;
                            if (error != null)
                                _diagnostics?.Enqueue(new AWPathDiagnosticEvent(
                                    task.Request.ActorId,
                                    task.Request.Stream.FailureReason,
                                    error.GetType().Name + ": " + error.Message));
                        }
                        bool releaseWorker = true;
                        lock (record.Gate)
                        {
                            if (_sessions.TryGetValue(work.OwnerId,
                                    out PathSessionRecord current) &&
                                ReferenceEquals(current, record) &&
                                ReferenceEquals(record.Running, task))
                            {
                                record.Running = null;
                                bool hasMoreSegments = usedSegmentGenerator &&
                                    segmentResult.Succeeded &&
                                    !segmentResult.ReachedTarget;
                                bool scheduleWhenEmpty = hasMoreSegments &&
                                    task.Request.Stream.Count == 0;
                                bool scheduled = record.Session.CompleteWork(
                                        hasMoreSegments, scheduleWhenEmpty,
                                        out AWScheduledPathWork replacement);
                                if (scheduled)
                                {
                                    if (record.Queued == null)
                                        record.Queued = record.Latest;
                                    EnqueueLocked(record, replacement);
                                }
                                releaseWorker = !hasMoreSegments ||
                                    !ReferenceEquals(record.Latest, task);
                            }
                        }
                        if (releaseWorker) task.ReleaseWorker();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static void ApplySegmentResult(AWPathRequest pRequest,
            AWPathGenerationResult pResult)
        {
            if (pRequest == null) return;
            if (!pResult.Succeeded)
            {
                if (pResult.FailureReason ==
                    AWPathFailureReason.CancelledByNewRequest)
                    pRequest.Stream.Cancel(pResult.FailureReason);
                else
                    pRequest.Stream.Fail(pResult.FailureReason,
                        pResult.Error);
                return;
            }

            IReadOnlyList<AWPathStep> steps = pResult.Steps;
            for (int index = 0; index < (steps?.Count ?? 0); index++)
                if (!pRequest.Stream.AddStep(steps[index])) return;

            if (pResult.ReachedTarget)
                pRequest.Stream.Complete();
            else if (pResult.EndTileId >= 0)
                pRequest.AdvanceStartTile(pResult.EndTileId);
            else
                pRequest.Stream.Fail(AWPathFailureReason.Unreachable, null);
        }

        private void ReplaceLocked(PathSessionRecord pRecord,
            AWPathRequest pRequest)
        {
            PathfindingTask replaced = pRecord.Latest;
            pRecord.Session.Replace();
            bool replacedQueued = false;
            if (pRecord.Queued != null)
            {
                replacedQueued = ReferenceEquals(pRecord.Queued, replaced);
                pRecord.Queued.Cancel(AWPathFailureReason.CancelledByNewRequest);
                pRecord.Queued.ReleaseOwner();
                pRecord.Queued.ReleaseWorker();
                pRecord.Queued = null;
            }
            if (ReferenceEquals(replaced, pRecord.Running))
            {
                replaced.Cancel(AWPathFailureReason.CancelledByNewRequest);
                replaced.ReleaseOwner();
            }
            else if (!replacedQueued)
            {
                replaced.Cancel(AWPathFailureReason.CancelledByNewRequest);
                replaced.ReleaseOwner();
                replaced.ReleaseWorker();
            }

            var latest = new PathfindingTask(pRequest);
            pRecord.Latest = latest;
            pRecord.Queued = latest;
            if (pRecord.Running == null)
                ScheduleLocked(pRecord, PriorityFor(pRequest.WorkClass));
        }

        private bool ScheduleLocked(PathSessionRecord pRecord,
            AWPathWorkPriority pPriority)
        {
            if (!pRecord.Session.TrySchedule(pPriority,
                    out AWScheduledPathWork work)) return false;
            if (pRecord.Queued == null)
                pRecord.Queued = pRecord.Latest;
            EnqueueLocked(pRecord, work);
            return true;
        }

        private void EnqueueLocked(PathSessionRecord pRecord,
            AWScheduledPathWork pWork)
        {
            AWPathWorkClass workClass = pRecord.Latest?.Request?.WorkClass ??
                                        AWPathWorkClass.Ambient;
            pWork = pWork.WithWorkClass(workClass);
            QueueFor(pWork.Priority).Enqueue(pWork);
            Interlocked.Increment(ref _queueDepth);
            IncrementQueued(workClass);
            ObserveQueuedCounters();
            _queueSignal.Release();
        }

        private bool TryTakeWork(out AWScheduledPathWork pWork)
        {
            AWPathWorkPriority priority = AWPathQueueFairnessRules.Select(
                !_starvedQueue.IsEmpty, !_initialQueue.IsEmpty,
                !_continuationQueue.IsEmpty,
                Volatile.Read(ref _consecutiveStarvedWork));
            bool dequeued = TryDequeue(priority, out pWork);
            if (!dequeued)
            {
                // A producer may have changed a queue between the snapshot
                // and dequeue. Fall back to the remaining queues without
                // relaxing the bounded-streak counter.
                dequeued = _starvedQueue.TryDequeue(out pWork) ||
                    _initialQueue.TryDequeue(out pWork) ||
                    _continuationQueue.TryDequeue(out pWork);
            }
            if (!dequeued)
            {
                pWork = default;
                return false;
            }

            if (pWork.Priority == AWPathWorkPriority.Starved)
                Interlocked.Increment(ref _consecutiveStarvedWork);
            else
                Volatile.Write(ref _consecutiveStarvedWork, 0);
            Interlocked.Decrement(ref _queueDepth);
            DecrementQueued(pWork.WorkClass);
            return true;
        }

        private bool TryDequeue(AWPathWorkPriority pPriority,
            out AWScheduledPathWork pWork)
        {
            switch (pPriority)
            {
                case AWPathWorkPriority.Starved:
                    return _starvedQueue.TryDequeue(out pWork);
                case AWPathWorkPriority.Initial:
                    return _initialQueue.TryDequeue(out pWork);
                default:
                    return _continuationQueue.TryDequeue(out pWork);
            }
        }

        private ConcurrentQueue<AWScheduledPathWork> QueueFor(
            AWPathWorkPriority pPriority)
        {
            switch (pPriority)
            {
                case AWPathWorkPriority.Starved: return _starvedQueue;
                case AWPathWorkPriority.Continuation: return _continuationQueue;
                default: return _initialQueue;
            }
        }

        private static AWPathWorkPriority PriorityFor(AWPathWorkClass pWorkClass)
        {
            return pWorkClass == AWPathWorkClass.Operational
                ? AWPathWorkPriority.Starved
                : AWPathWorkPriority.Initial;
        }

        private void CancelRecordLocked(PathSessionRecord pRecord,
            AWPathFailureReason pReason)
        {
            pRecord.Session.Cancel();
            PathfindingTask latest = pRecord.Latest;
            latest.Cancel(pReason);
            latest.ReleaseOwner();
            if (!ReferenceEquals(pRecord.Running, latest))
                latest.ReleaseWorker();
            if (pRecord.Running != null && !ReferenceEquals(pRecord.Running,
                    latest))
                pRecord.Running.Cancel(pReason);
            _diagnostics?.OnCancelled();
        }

        private static bool CanReuse(PathfindingTask pTask,
            AWPathReuseKey pReuseKey)
        {
            if (pTask?.Request == null ||
                !AWPathRequestReuseRules.CanReuse(pTask.Request.ReuseKey,
                    pReuseKey, ageTicks: 0L, maximumAgeTicks: 0L)) return false;
            return !IsTerminal(pTask.Request.Stream.State) ||
                   pTask.Request.Stream.HasPendingSteps;
        }

        private AWPathPollResult PollOwned(long pActorId,
            PathfindingTask pTask)
        {
            AWPathPollResult result = pTask.Request.Stream.Poll();
            if (IsTerminal(result.Kind) && pTask.Request.Stream.Count == 0)
                CleanupOwned(pActorId, pTask);
            return result;
        }

        private void CleanupConsumedTerminal(long pActorId,
            PathfindingTask pTask)
        {
            if (pTask.Request.Stream.Count != 0 ||
                !IsTerminal(pTask.Request.Stream.State)) return;
            CleanupOwned(pActorId, pTask);
        }

        private void CleanupOwned(long pActorId, PathfindingTask pTask)
        {
            if (!_sessions.TryGetValue(pActorId,
                    out PathSessionRecord record)) return;
            lock (record.Gate)
            {
                if (!ReferenceEquals(record.Latest, pTask)) return;
                if (!((ICollection<KeyValuePair<long,
                        PathSessionRecord>>)_sessions).Remove(
                        new KeyValuePair<long, PathSessionRecord>(
                            pActorId, record))) return;
                record.Session.Cancel();
                pTask.ReleaseOwner();
            }
        }

        private static void CountWork(AWPathWorkClass? pWorkClass, bool active,
            ref int operational, ref int essential, ref int ambient)
        {
            switch (pWorkClass)
            {
                case AWPathWorkClass.Operational: operational++; break;
                case AWPathWorkClass.EssentialTravel: essential++; break;
                default: ambient++; break;
            }
        }

        private void IncrementQueued(AWPathWorkClass pWorkClass)
        {
            switch (pWorkClass)
            {
                case AWPathWorkClass.Operational:
                    Interlocked.Increment(ref _operationalQueued);
                    break;
                case AWPathWorkClass.EssentialTravel:
                    Interlocked.Increment(ref _essentialQueued);
                    break;
                default:
                    Interlocked.Increment(ref _ambientQueued);
                    break;
            }
        }

        private void DecrementQueued(AWPathWorkClass pWorkClass)
        {
            switch (pWorkClass)
            {
                case AWPathWorkClass.Operational:
                    Interlocked.Decrement(ref _operationalQueued);
                    break;
                case AWPathWorkClass.EssentialTravel:
                    Interlocked.Decrement(ref _essentialQueued);
                    break;
                default:
                    Interlocked.Decrement(ref _ambientQueued);
                    break;
            }
        }

        private void ObserveQueuedCounters()
        {
            _diagnostics?.ObserveQueue(new AWPathQueueSnapshot(
                Math.Max(0, Volatile.Read(ref _operationalQueued)),
                Math.Max(0, Volatile.Read(ref _essentialQueued)),
                Math.Max(0, Volatile.Read(ref _ambientQueued)), 0, 0, 0));
        }

        private void Reject(AWPathRequest pRequest,
            AWPathSubmissionDisposition pDisposition)
        {
            if (pRequest == null) return;
            _diagnostics?.OnSubmission(pRequest.WorkClass, pDisposition);
            pRequest.Dispose();
        }

        private static bool IsTerminal(AWPathPollKind pKind)
        {
            return pKind == AWPathPollKind.Completed ||
                   pKind == AWPathPollKind.Failed ||
                   pKind == AWPathPollKind.Cancelled;
        }

        private static bool IsTerminal(AWPathRequestState pState)
        {
            return pState == AWPathRequestState.Succeeded ||
                   pState == AWPathRequestState.Failed ||
                   pState == AWPathRequestState.Cancelled;
        }

        public readonly struct ReadyPathCursor
        {
            private readonly AWPathFinder _owner;
            private readonly long _actorId;
            private readonly PathfindingTask _task;

            internal ReadyPathCursor(AWPathFinder pOwner, long pActorId,
                PathfindingTask pTask)
            {
                _owner = pOwner;
                _actorId = pActorId;
                _task = pTask;
            }

            public bool IsValid => _owner != null && _task != null;
            public AWPathPollResult Poll() => IsValid
                ? _owner.PollOwned(_actorId, _task)
                : new AWPathPollResult(AWPathPollKind.NoRequest);

            public void Consume()
            {
                if (!IsValid) return;
                _owner.Consume(_actorId, _task);
            }
        }

        internal sealed class PathfindingTask
        {
            private int _references = 2;
            private int _workerReleased;

            internal PathfindingTask(AWPathRequest pRequest) { Request = pRequest; }
            internal AWPathRequest Request { get; }
            internal void ReleaseOwner() => Release();
            internal void ReleaseWorker()
            {
                if (Interlocked.Exchange(ref _workerReleased, 1) == 0) Release();
            }
            internal void Cancel(AWPathFailureReason pReason)
            {
                try { Request.Cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
                Request.Stream.Cancel(pReason);
            }
            private void Release()
            {
                if (Interlocked.Decrement(ref _references) == 0) Request.Dispose();
            }
        }

        private sealed class PathSessionRecord
        {
            internal PathSessionRecord(long pOwnerId)
            {
                Session = new AWPathSession(pOwnerId);
            }

            internal readonly object Gate = new object();
            internal readonly AWPathSession Session;
            internal PathfindingTask Latest;
            internal PathfindingTask Queued;
            internal PathfindingTask Running;
        }
    }
}

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
        private readonly IAWPathGenerator _generator;
        private readonly AWPathDiagnostics _diagnostics;
        private readonly ConcurrentDictionary<long, PathfindingTask> _active =
            new ConcurrentDictionary<long, PathfindingTask>();
        private readonly object _requestGate = new object();
        private readonly Dictionary<long, ActorWorkSlot> _workSlots =
            new Dictionary<long, ActorWorkSlot>();
        private readonly LinkedList<ActorWorkSlot> _operationalQueue =
            new LinkedList<ActorWorkSlot>();
        private readonly LinkedList<ActorWorkSlot> _essentialQueue =
            new LinkedList<ActorWorkSlot>();
        private readonly LinkedList<ActorWorkSlot> _ambientQueue =
            new LinkedList<ActorWorkSlot>();
        private readonly AutoResetEvent _queueSignal = new AutoResetEvent(false);
        private Thread[] _workers = Array.Empty<Thread>();
        private int _started;
        private int _stopping;
        private int _consecutiveOperationalRequests;
        private int _consecutiveNonAmbientRequests;

        public AWPathFinder(IAWPathGenerator pGenerator)
            : this(pGenerator, null)
        {
        }

        internal AWPathFinder(IAWPathGenerator pGenerator, AWPathDiagnostics pDiagnostics)
        {
            _generator = pGenerator ?? throw new ArgumentNullException(nameof(pGenerator));
            _diagnostics = pDiagnostics;
        }

        public int ActiveCount => _active.Count;
        public int QueueDepth
        {
            get
            {
                lock (_requestGate)
                    return _operationalQueue.Count + _essentialQueue.Count +
                           _ambientQueue.Count;
            }
        }

        internal AWPathQueueSnapshot SnapshotQueues()
        {
            lock (_requestGate) return SnapshotQueuesLocked();
        }

        public void Start(int pWorkers)
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
            int count = Math.Max(1, Math.Min(4, pWorkers));
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
            if (pRequest == null || Volatile.Read(ref _started) == 0 ||
                Volatile.Read(ref _stopping) != 0)
            {
                if (pRequest != null)
                    _diagnostics?.OnSubmission(pRequest.WorkClass,
                        pDisposition);
                pRequest?.Dispose();
                return false;
            }

            lock (_requestGate)
            {
                pDisposition = AWPathSubmissionDisposition.Submitted;
                if (_active.TryGetValue(pRequest.ActorId, out PathfindingTask existing))
                {
                    if (CanReuse(existing, pRequest.TargetTileId,
                            pRequest.Options))
                    {
                        pRequest.Dispose();
                        pDisposition = AWPathSubmissionDisposition.Reused;
                        _diagnostics?.OnSubmission(existing.Request.WorkClass,
                            pDisposition);
                        return true;
                    }
                    pDisposition = existing.WorkerStarted
                        ? AWPathSubmissionDisposition.ReplacedRunning
                        : AWPathSubmissionDisposition.ReplacedPending;
                    RemoveOwned(existing, AWPathFailureReason.CancelledByNewRequest);
                }

                var task = new PathfindingTask(pRequest);
                if (!_active.TryAdd(pRequest.ActorId, task))
                {
                    task.Cancel(AWPathFailureReason.CancelledByNewRequest);
                    task.ReleaseOwner();
                    task.ReleaseWorker();
                    pDisposition = AWPathSubmissionDisposition.Rejected;
                    _diagnostics?.OnSubmission(pRequest.WorkClass,
                        pDisposition);
                    return false;
                }
                try
                {
                    ScheduleLocked(task);
                    _diagnostics?.OnSubmission(pRequest.WorkClass,
                        pDisposition);
                    return true;
                }
                catch
                {
                    _active.TryRemove(pRequest.ActorId, out _);
                    task.Cancel(AWPathFailureReason.CancelledByNewRequest);
                    task.ReleaseOwner();
                    task.ReleaseWorkerIfNotStarted();
                    pDisposition = AWPathSubmissionDisposition.Rejected;
                    _diagnostics?.OnSubmission(pRequest.WorkClass,
                        pDisposition);
                    return false;
                }
            }
        }

        public bool TryReuse(long pActorId, int pTargetTileId,
            AWPathRequestOptions pOptions)
        {
            if (!_active.TryGetValue(pActorId, out PathfindingTask existing) ||
                !CanReuse(existing, pTargetTileId, pOptions)) return false;
            _diagnostics?.OnSubmission(existing.Request.WorkClass,
                AWPathSubmissionDisposition.Reused);
            return true;
        }

        public bool IsWaitingForWorker(long pActorId)
        {
            lock (_requestGate)
            {
                if (!_active.TryGetValue(pActorId,
                        out PathfindingTask task) || task.WorkerStarted)
                    return false;
                return _workSlots.TryGetValue(pActorId,
                           out ActorWorkSlot slot) &&
                       ReferenceEquals(slot.PendingTask, task);
            }
        }

        public bool IsWorkerRunning(long pActorId)
        {
            lock (_requestGate)
            {
                return _active.TryGetValue(pActorId,
                           out PathfindingTask task) &&
                       task.WorkerStarted &&
                       _workSlots.TryGetValue(pActorId,
                           out ActorWorkSlot slot) &&
                       ReferenceEquals(slot.RunningTask, task);
            }
        }

        private static bool CanReuse(PathfindingTask pTask,
            int pTargetTileId, AWPathRequestOptions pOptions)
        {
            if (pTask?.Request == null ||
                !pTask.Request.Matches(pTargetTileId, pOptions)) return false;
            return !IsTerminal(pTask.Request.Stream.State) ||
                   pTask.Request.Stream.HasPendingSteps;
        }

        public AWPathPollResult Poll(long pActorId)
        {
            if (!_active.TryGetValue(pActorId, out PathfindingTask task))
                return new AWPathPollResult(AWPathPollKind.NoRequest);
            return PollOwned(pActorId, task);
        }

        public AWPathPollResult OpenReadyCursor(long pActorId, out ReadyPathCursor pCursor)
        {
            pCursor = default;
            if (!_active.TryGetValue(pActorId, out PathfindingTask task))
                return new AWPathPollResult(AWPathPollKind.NoRequest);

            AWPathPollResult result = PollOwned(pActorId, task);
            if (result.Kind == AWPathPollKind.StepReady)
                pCursor = new ReadyPathCursor(this, pActorId, task);
            return result;
        }

        public bool Consume(long pActorId)
        {
            if (!_active.TryGetValue(pActorId, out PathfindingTask task) ||
                !task.Request.Stream.TryTake(out _)) return false;
            CleanupConsumedTerminal(pActorId, task);
            return true;
        }

        public void Cancel(long pActorId, AWPathFailureReason pReason)
        {
            lock (_requestGate)
            {
                if (_active.TryGetValue(pActorId, out PathfindingTask task))
                    RemoveOwned(task, pReason);
            }
        }

        public void Clear(AWPathFailureReason pReason)
        {
            lock (_requestGate)
            {
                var tasks = new List<PathfindingTask>(_active.Values);
                foreach (PathfindingTask task in tasks) RemoveOwned(task, pReason);
            }
        }

        public void StopAndDrain()
        {
            if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
            Clear(AWPathFailureReason.WorldCleared);
            for (int i = 0; i < _workers.Length; i++) _queueSignal.Set();
            foreach (Thread worker in _workers)
            {
                if (worker == null || worker == Thread.CurrentThread) continue;
                worker.Join(5000);
            }
            _workers = Array.Empty<Thread>();
        }

        public void Dispose()
        {
            StopAndDrain();
            _queueSignal.Dispose();
        }

        private void WorkerLoop()
        {
            try
            {
                while (true)
                {
                    PathfindingTask task = null;
                    lock (_requestGate)
                    {
                        ActorWorkSlot slot = DequeueLocked();
                        if (slot != null)
                        {
                            task = slot.PendingTask;
                            slot.PendingTask = null;
                            slot.RunningTask = task;
                            task?.MarkWorkerStarted();
                        }
                        else if (Volatile.Read(ref _stopping) != 0)
                        {
                            break;
                        }
                    }
                    if (task == null)
                    {
                        _queueSignal.WaitOne(50);
                        continue;
                    }
                    try
                    {
                        try
                        {
                            if (!IsTerminal(task.Request.Stream.State))
                                _generator.Generate(task.Request, task.Request.Cancellation.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            task.Request.Stream.Cancel(
                                AWPathFailureReason.CancelledByNewRequest);
                        }
                        catch (Exception error)
                        {
                            task.Request.Stream.Fail(AWPathFailureReason.GeneratorException,
                                error);
                        }
                        task.Request.Stream.EnsureCompleted();
                        if (task.Request.Stream.State == AWPathRequestState.Succeeded)
                            _diagnostics?.OnCompleted();
                        else if (task.Request.Stream.State == AWPathRequestState.Failed)
                        {
                            _diagnostics?.OnFailed();
                            Exception error = task.Request.Stream.Error;
                            if (error != null)
                                _diagnostics?.Enqueue(new AWPathDiagnosticEvent(
                                    task.Request.ActorId, task.Request.Stream.FailureReason,
                                    error.GetType().Name + ": " + error.Message));
                        }
                    }
                    finally
                    {
                        task.ReleaseWorker();
                        lock (_requestGate)
                        {
                            if (_workSlots.TryGetValue(task.Request.ActorId,
                                    out ActorWorkSlot slot) &&
                                ReferenceEquals(slot.RunningTask, task))
                            {
                                slot.RunningTask = null;
                                if (slot.PendingTask != null)
                                    EnqueueLocked(slot,
                                        slot.PendingTask.Request.WorkClass);
                                else
                                    RemoveEmptySlotLocked(slot);
                            }
                        }
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void ScheduleLocked(PathfindingTask pTask)
        {
            long actorId = pTask.Request.ActorId;
            if (!_workSlots.TryGetValue(actorId, out ActorWorkSlot slot))
            {
                slot = new ActorWorkSlot(actorId);
                _workSlots.Add(actorId, slot);
            }
            slot.PendingTask = pTask;
            if (slot.RunningTask == null)
                EnqueueLocked(slot, pTask.Request.WorkClass);
        }

        private void EnqueueLocked(ActorWorkSlot pSlot,
            AWPathWorkClass pWorkClass)
        {
            if (pSlot.QueueNode != null)
            {
                if (pWorkClass < pSlot.WorkClass)
                {
                    RemoveQueuedSlotLocked(pSlot);
                    pSlot.WorkClass = pWorkClass;
                    pSlot.QueueNode = QueueFor(pWorkClass).AddLast(pSlot);
                    ObserveQueuesLocked();
                    _queueSignal.Set();
                }
                return;
            }

            pSlot.WorkClass = pWorkClass;
            pSlot.QueueNode = QueueFor(pWorkClass).AddLast(pSlot);
            ObserveQueuesLocked();
            _queueSignal.Set();
        }

        private ActorWorkSlot DequeueLocked()
        {
            AWPathWorkClass workClass = AWPathWorkClassRules.Next(
                _operationalQueue.Count, _essentialQueue.Count,
                _ambientQueue.Count, _consecutiveOperationalRequests,
                _consecutiveNonAmbientRequests);
            LinkedListNode<ActorWorkSlot> node = QueueFor(workClass).First;
            if (node == null)
            {
                _consecutiveOperationalRequests = 0;
                _consecutiveNonAmbientRequests = 0;
                return null;
            }
            ActorWorkSlot slot = node.Value;
            QueueFor(workClass).Remove(node);
            slot.QueueNode = null;
            if (workClass == AWPathWorkClass.Operational)
            {
                _consecutiveOperationalRequests++;
                _consecutiveNonAmbientRequests++;
            }
            else if (workClass == AWPathWorkClass.EssentialTravel)
            {
                _consecutiveOperationalRequests = 0;
                _consecutiveNonAmbientRequests++;
            }
            else
            {
                _consecutiveOperationalRequests = 0;
                _consecutiveNonAmbientRequests = 0;
            }
            ObserveQueuesLocked();
            return slot;
        }

        private void RemoveOwned(PathfindingTask pTask, AWPathFailureReason pReason)
        {
            if (!_active.TryGetValue(pTask.Request.ActorId, out PathfindingTask current) ||
                !ReferenceEquals(current, pTask)) return;
            _active.TryRemove(pTask.Request.ActorId, out _);
            pTask.Cancel(pReason);
            _diagnostics?.OnCancelled();
            if (_workSlots.TryGetValue(pTask.Request.ActorId,
                    out ActorWorkSlot slot) &&
                ReferenceEquals(slot.PendingTask, pTask))
            {
                slot.PendingTask = null;
                RemoveQueuedSlotLocked(slot);
                pTask.ReleaseWorkerIfNotStarted();
                RemoveEmptySlotLocked(slot);
            }
            else if (slot == null || !ReferenceEquals(slot.RunningTask, pTask))
            {
                pTask.ReleaseWorkerIfNotStarted();
            }
            pTask.ReleaseOwner();
        }

        private void RemoveQueuedSlotLocked(ActorWorkSlot pSlot)
        {
            LinkedListNode<ActorWorkSlot> node = pSlot.QueueNode;
            if (node == null) return;
            QueueFor(pSlot.WorkClass).Remove(node);
            pSlot.QueueNode = null;
            ObserveQueuesLocked();
        }

        private LinkedList<ActorWorkSlot> QueueFor(AWPathWorkClass pWorkClass)
        {
            switch (pWorkClass)
            {
                case AWPathWorkClass.Operational:
                    return _operationalQueue;
                case AWPathWorkClass.EssentialTravel:
                    return _essentialQueue;
                default:
                    return _ambientQueue;
            }
        }

        private AWPathQueueSnapshot SnapshotQueuesLocked()
        {
            int operationalActive = 0;
            int essentialActive = 0;
            int ambientActive = 0;
            foreach (PathfindingTask task in _active.Values)
            {
                switch (task?.Request?.WorkClass)
                {
                    case AWPathWorkClass.Operational:
                        operationalActive++;
                        break;
                    case AWPathWorkClass.EssentialTravel:
                        essentialActive++;
                        break;
                    default:
                        ambientActive++;
                        break;
                }
            }
            return new AWPathQueueSnapshot(_operationalQueue.Count,
                _essentialQueue.Count, _ambientQueue.Count,
                operationalActive, essentialActive, ambientActive);
        }

        private void ObserveQueuesLocked()
        {
            _diagnostics?.ObserveQueue(SnapshotQueuesLocked());
        }

        private void RemoveEmptySlotLocked(ActorWorkSlot pSlot)
        {
            if (pSlot == null || pSlot.QueueNode != null ||
                pSlot.PendingTask != null || pSlot.RunningTask != null) return;
            _workSlots.Remove(pSlot.ActorId);
        }

        private AWPathPollResult PollOwned(long pActorId, PathfindingTask pTask)
        {
            AWPathPollResult result = pTask.Request.Stream.Poll();
            if (IsTerminal(result.Kind) && pTask.Request.Stream.Count == 0)
                CleanupOwned(pActorId, pTask);
            return result;
        }

        private void CleanupConsumedTerminal(long pActorId, PathfindingTask pTask)
        {
            if (pTask.Request.Stream.Count != 0 ||
                !IsTerminal(pTask.Request.Stream.State)) return;
            CleanupOwned(pActorId, pTask);
        }

        private void CleanupOwned(long pActorId, PathfindingTask pTask)
        {
            lock (_requestGate)
            {
                if (!_active.TryGetValue(pActorId, out PathfindingTask current) ||
                    !ReferenceEquals(current, pTask)) return;
                _active.TryRemove(pActorId, out _);
                pTask.ReleaseOwner();
            }
        }

        private static bool IsTerminal(AWPathPollKind pKind)
        {
            return pKind == AWPathPollKind.Completed || pKind == AWPathPollKind.Failed ||
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

            internal ReadyPathCursor(AWPathFinder pOwner, long pActorId, PathfindingTask pTask)
            {
                _owner = pOwner;
                _actorId = pActorId;
                _task = pTask;
            }

            public bool IsValid => _owner != null && _task != null;

            public AWPathPollResult Poll()
            {
                return IsValid
                    ? _owner.PollOwned(_actorId, _task)
                    : new AWPathPollResult(AWPathPollKind.NoRequest);
            }

            public void Consume()
            {
                if (!IsValid || !_task.Request.Stream.TryTake(out _)) return;
                _owner.CleanupConsumedTerminal(_actorId, _task);
            }
        }

        internal sealed class PathfindingTask
        {
            private int _references = 2;
            private int _workerStarted;
            private int _workerReleased;

            public PathfindingTask(AWPathRequest pRequest)
            {
                Request = pRequest;
            }

            public AWPathRequest Request { get; }
            public bool WorkerStarted =>
                Volatile.Read(ref _workerStarted) != 0;

            public void Cancel(AWPathFailureReason pReason)
            {
                try { Request.Cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
                Request.Stream.Cancel(pReason);
            }

            public void ReleaseOwner() => Release();

            public void MarkWorkerStarted()
            {
                Volatile.Write(ref _workerStarted, 1);
            }

            public void ReleaseWorker()
            {
                if (Interlocked.Exchange(ref _workerReleased, 1) == 0) Release();
            }

            public void ReleaseWorkerIfNotStarted()
            {
                if (Volatile.Read(ref _workerStarted) != 0) return;
                ReleaseWorker();
            }

            private void Release()
            {
                if (Interlocked.Decrement(ref _references) == 0) Request.Dispose();
            }
        }

        private sealed class ActorWorkSlot
        {
            public ActorWorkSlot(long pActorId)
            {
                ActorId = pActorId;
            }

            public readonly long ActorId;
            public PathfindingTask PendingTask;
            public PathfindingTask RunningTask;
            public LinkedListNode<ActorWorkSlot> QueueNode;
            public AWPathWorkClass WorkClass;
        }
    }
}

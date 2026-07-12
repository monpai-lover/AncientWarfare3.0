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

    public sealed class AWPathFinder : IDisposable
    {
        private readonly IAWPathGenerator _generator;
        private readonly AWPathDiagnostics _diagnostics;
        private readonly ConcurrentDictionary<long, PathfindingTask> _active =
            new ConcurrentDictionary<long, PathfindingTask>();
        private readonly object _requestGate = new object();
        private BlockingCollection<PathfindingTask> _queue;
        private Thread[] _workers = Array.Empty<Thread>();
        private int _started;
        private int _stopping;

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

        public void Start(int pWorkers)
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
            int count = Math.Max(1, Math.Min(4, pWorkers));
            _queue = new BlockingCollection<PathfindingTask>(new ConcurrentQueue<PathfindingTask>());
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
            pReused = false;
            if (pRequest == null || Volatile.Read(ref _started) == 0 ||
                Volatile.Read(ref _stopping) != 0)
            {
                pRequest?.Dispose();
                return false;
            }

            lock (_requestGate)
            {
                if (_active.TryGetValue(pRequest.ActorId, out PathfindingTask existing))
                {
                    if (existing.Request.Matches(pRequest.TargetTileId, pRequest.Options) &&
                        (!IsTerminal(existing.Request.Stream.State) ||
                         existing.Request.Stream.Count > 0))
                    {
                        pRequest.Dispose();
                        pReused = true;
                        _diagnostics?.OnReused();
                        return true;
                    }
                    RemoveOwned(existing, AWPathFailureReason.CancelledByNewRequest);
                }

                var task = new PathfindingTask(pRequest);
                if (!_active.TryAdd(pRequest.ActorId, task))
                {
                    task.Cancel(AWPathFailureReason.CancelledByNewRequest);
                    task.ReleaseOwner();
                    task.ReleaseWorker();
                    return false;
                }
                try
                {
                    _queue.Add(task);
                    _diagnostics?.OnGenerated();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    _active.TryRemove(pRequest.ActorId, out _);
                    task.Cancel(AWPathFailureReason.CancelledByNewRequest);
                    task.ReleaseOwner();
                    task.ReleaseWorker();
                    return false;
                }
            }
        }

        public AWPathPollResult Poll(long pActorId)
        {
            if (!_active.TryGetValue(pActorId, out PathfindingTask task))
                return new AWPathPollResult(AWPathPollKind.NoRequest);
            AWPathPollResult result = task.Request.Stream.Poll();
            if ((result.Kind == AWPathPollKind.Completed || result.Kind == AWPathPollKind.Failed ||
                 result.Kind == AWPathPollKind.Cancelled) && task.Request.Stream.Count == 0)
            {
                lock (_requestGate)
                {
                    if (_active.TryGetValue(pActorId, out PathfindingTask current) &&
                        ReferenceEquals(current, task))
                    {
                        _active.TryRemove(pActorId, out _);
                        task.ReleaseOwner();
                    }
                }
            }
            return result;
        }

        public bool Consume(long pActorId)
        {
            return _active.TryGetValue(pActorId, out PathfindingTask task) &&
                   task.Request.Stream.TryTake(out _);
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
            _queue?.CompleteAdding();
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
            _queue?.Dispose();
        }

        private void WorkerLoop()
        {
            try
            {
                foreach (PathfindingTask task in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        if (!IsTerminal(task.Request.Stream.State))
                            _generator.Generate(task.Request, task.Request.Cancellation.Token);
                        if (task.Request.Stream.State == AWPathRequestState.Completed)
                            _diagnostics?.OnCompleted();
                        else if (task.Request.Stream.State == AWPathRequestState.Failed)
                            _diagnostics?.OnFailed();
                    }
                    finally
                    {
                        task.ReleaseWorker();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void RemoveOwned(PathfindingTask pTask, AWPathFailureReason pReason)
        {
            if (!_active.TryGetValue(pTask.Request.ActorId, out PathfindingTask current) ||
                !ReferenceEquals(current, pTask)) return;
            _active.TryRemove(pTask.Request.ActorId, out _);
            pTask.Cancel(pReason);
            _diagnostics?.OnCancelled();
            pTask.ReleaseOwner();
        }

        private static bool IsTerminal(AWPathRequestState pState)
        {
            return pState == AWPathRequestState.Completed ||
                   pState == AWPathRequestState.Failed ||
                   pState == AWPathRequestState.Cancelled;
        }

        private sealed class PathfindingTask
        {
            private int _references = 2;

            public PathfindingTask(AWPathRequest pRequest)
            {
                Request = pRequest;
            }

            public AWPathRequest Request { get; }

            public void Cancel(AWPathFailureReason pReason)
            {
                try { Request.Cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
                Request.Stream.Cancel(pReason);
            }

            public void ReleaseOwner() => Release();
            public void ReleaseWorker() => Release();

            private void Release()
            {
                if (Interlocked.Decrement(ref _references) == 0) Request.Dispose();
            }
        }
    }
}

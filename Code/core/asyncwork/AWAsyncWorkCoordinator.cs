using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace AncientWarfare3.core.asyncwork
{
    internal sealed class AWAsyncWorkRequest
    {
        public AWAsyncWorkRequest(string pKey, AWAsyncLane pLane,
            AWAsyncStamp pStamp, Func<CancellationToken, object> pExecute,
            Action<object> pCommit, Action<Exception> pFault = null,
            Func<bool> pTryAdmit = null,
            AWAsyncCommitMode pCommitMode = AWAsyncCommitMode.MainThread)
        {
            Key = pKey;
            Lane = pLane;
            Stamp = pStamp;
            Execute = pExecute;
            Commit = pCommit;
            Fault = pFault;
            TryAdmit = pTryAdmit;
            CommitMode = pCommitMode;
        }

        public string Key { get; }
        public AWAsyncLane Lane { get; }
        public AWAsyncStamp Stamp { get; }
        public Func<CancellationToken, object> Execute { get; }
        public Action<object> Commit { get; }
        public Action<Exception> Fault { get; }
        public Func<bool> TryAdmit { get; }
        public AWAsyncCommitMode CommitMode { get; }
    }

    internal sealed class AWAsyncWorkCoordinator : IDisposable
    {
        private sealed class WorkerItem
        {
            public long Id;
            public string Key;
            public AWAsyncLane Lane;
            public AWAsyncStamp Stamp;
            public Func<CancellationToken, object> Execute;
            public CancellationToken Token;
            public bool CommitOnWorker;
        }

        private sealed class CommitItem
        {
            public long Id;
            public string Key;
            public AWAsyncLane Lane;
            public AWAsyncStamp Stamp;
            public Action<object> Commit;
            public Action<Exception> Fault;
            public bool CommitOnWorker;
        }

        private sealed class Completion
        {
            public long Id;
            public AWAsyncStamp Stamp;
            public object Result;
            public Exception Error;
        }

        private sealed class AdmissionReservation
        {
            public long Id;
            public string Key;
            public AWAsyncLane Lane;
        }

        private sealed class CancellationAttempt
        {
            private readonly object _startGate = new object();
            private bool _started;

            public CancellationAttempt(CancellationTokenSource pSource,
                string pThreadName)
            {
                Source = pSource;
                Thread = new Thread(Run)
                {
                    IsBackground = true,
                    Name = pThreadName,
                    Priority = ThreadPriority.BelowNormal
                };
            }

            public CancellationTokenSource Source { get; }
            public Thread Thread { get; }
            public Exception Error { get; private set; }

            public void Start()
            {
                lock (_startGate)
                {
                    if (_started) return;
                    Thread.Start();
                    _started = true;
                }
            }

            private void Run()
            {
                try { Source.Cancel(); }
                catch (Exception error) { Error = error; }
            }
        }

        private readonly object _gate = new object();
        private readonly bool _enabled;
        private readonly int _workerCapacity;
        private readonly SemaphoreSlim _workSignal = new SemaphoreSlim(0);
        private readonly AWBoundedLatestQueue<WorkerItem> _traversal;
        private readonly AWBoundedLatestQueue<WorkerItem> _ui;
        private readonly AWBoundedLatestQueue<WorkerItem> _ai;
        private readonly AWBoundedOrderedQueue<Completion> _completions =
            new AWBoundedOrderedQueue<Completion>(AWAsyncCapacity.CompletionBatches);
        private readonly Queue<AWAsyncFaultRecord> _faults =
            new Queue<AWAsyncFaultRecord>(AWAsyncCapacity.FaultRecords);
        private readonly Dictionary<long, CommitItem> _commits =
            new Dictionary<long, CommitItem>();
        private readonly Dictionary<string, long> _traversalReservations =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _uiReservations =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _aiReservations =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly List<CancellationTokenSource> _retiredCancellations =
            new List<CancellationTokenSource>();
        private readonly AWAsyncDiagnostics _diagnostics = new AWAsyncDiagnostics();

        private Thread[] _workers = Array.Empty<Thread>();
        private CancellationTokenSource _worldCancellation;
        private CancellationAttempt _cancellationAttempt;
        private AWAsyncLifecycleState _state = AWAsyncLifecycleState.Stopped;
        private long _worldGeneration;
        private long _nextWorkId;
        private long _nextAdmissionReservationId;
        private int _activeWorkerCount;
        private int _highPriorityStreak;
        private int _saveBarrierDrainThreadId;
        private AWAsyncLane _lastHighPriorityLane;
        private bool _shutdownRequested;
        private bool _saveBarrier;
        private bool _saveBarrierPending;
        private bool _disposed;

        public AWAsyncWorkCoordinator(bool pEnabled)
            : this(pEnabled, 0)
        {
        }

        internal AWAsyncWorkCoordinator(bool pEnabled, int pWorkerCount)
        {
            _enabled = pEnabled;
            _workerCapacity = pEnabled
                ? AWAsyncWorkerRules.NormalizeWorkerCount(pWorkerCount,
                    Environment.ProcessorCount)
                : 0;
            _traversal = new AWBoundedLatestQueue<WorkerItem>(1,
                pItem => pItem.Key);
            _ui = new AWBoundedLatestQueue<WorkerItem>(AWAsyncCapacity.Ui,
                pItem => pItem.Key);
            _ai = new AWBoundedLatestQueue<WorkerItem>(AWAsyncCapacity.Ai,
                pItem => pItem.Key);
        }

        public AWAsyncLifecycleState State
        {
            get { lock (_gate) return _state; }
        }

        public long WorldGeneration
        {
            get { lock (_gate) return _worldGeneration; }
        }

        public int PendingCompletionCount
        {
            get { lock (_gate) return _completions.Count; }
        }

        public int ActiveWorkerCount
        {
            get { lock (_gate) return _activeWorkerCount; }
        }

        public int RetiredCancellationCount
        {
            get { lock (_gate) return _retiredCancellations.Count; }
        }

        public bool WorkerAlive
        {
            get
            {
                lock (_gate)
                {
                    for (int i = 0; i < _workers.Length; i++)
                        if (_workers[i]?.IsAlive == true) return true;
                    return false;
                }
            }
        }

        public int WorkerCount
        {
            get { lock (_gate) return _workers.Length; }
        }

        public long StartWorld()
        {
            CancellationTokenSource cancellation;
            AWAsyncLifecycleState previousState;
            long generation;
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_state == AWAsyncLifecycleState.Draining ||
                    _state == AWAsyncLifecycleState.Starting ||
                    _state == AWAsyncLifecycleState.Faulted)
                    throw new InvalidOperationException(
                        "Async compute worker is not restartable in state " +
                        _state + ".");
                if (_cancellationAttempt != null)
                    throw new InvalidOperationException(
                        "Async compute cancellation is still active.");
                previousState = _state;
                _state = AWAsyncLifecycleState.Starting;
                cancellation = DetachCurrentWorldLocked();
                _worldGeneration++;
                generation = _worldGeneration;
            }

            Exception cancellationError = Cancel(cancellation);

            lock (_gate)
            {
                RetireCancellationLocked(cancellation);
                if (_state != AWAsyncLifecycleState.Starting)
                    throw new InvalidOperationException(
                        "Async compute world start was superseded by " +
                        _state + ".");
                if (cancellationError != null)
                {
                    _state = previousState;
                    throw new InvalidOperationException(
                        "Async compute world cancellation callback failed.",
                        cancellationError);
                }
                var newWorldCancellation = new CancellationTokenSource();
                bool previousShutdownRequested = _shutdownRequested;
                Thread[] workers = null;
                try
                {
                    if (previousState == AWAsyncLifecycleState.Stopped)
                    {
                        _shutdownRequested = false;
                        if (_enabled)
                        {
                            workers = new Thread[_workerCapacity];
                            for (int index = 0; index < workers.Length; index++)
                            {
                                Thread worker = new Thread(WorkerLoop)
                                {
                                    IsBackground = true,
                                    Name = "AW3 Async Compute " + (index + 1),
                                    Priority = ThreadPriority.BelowNormal
                                };
                                workers[index] = worker;
                                worker.Start();
                            }
                        }
                    }
                }
                catch
                {
                    newWorldCancellation.Dispose();
                    _shutdownRequested = previousShutdownRequested;
                    _state = previousState;
                    throw;
                }
                _worldCancellation = newWorldCancellation;
                if (workers != null) _workers = workers;
                _saveBarrier = false;
                _saveBarrierPending = false;
                _saveBarrierDrainThreadId = 0;
                _highPriorityStreak = 0;
                _lastHighPriorityLane = AWAsyncLane.None;
                _state = AWAsyncLifecycleState.Running;
                return generation;
            }
        }

        public bool TrySchedule(AWAsyncWorkRequest pRequest)
        {
            if (pRequest == null) return false;
            AdmissionReservation reservation;
            lock (_gate)
            {
                if (!CanScheduleLocked(pRequest))
                {
                    _diagnostics.RecordRejected();
                    return false;
                }
                if (pRequest.TryAdmit == null)
                    return TryEnqueueRequestLocked(pRequest);
                reservation = ReserveAdmissionLocked(pRequest.Key,
                    pRequest.Lane);
                if (reservation == null)
                {
                    _diagnostics.RecordRejected();
                    return false;
                }
            }

            bool admitted;
            try
            {
                admitted = pRequest.TryAdmit();
            }
            catch
            {
                lock (_gate) ReleaseAdmissionLocked(reservation);
                throw;
            }

            lock (_gate)
            {
                if (!OwnsAdmissionLocked(reservation))
                {
                    _diagnostics.RecordRejected();
                    return false;
                }
                ReleaseAdmissionLocked(reservation);
                if (!admitted || !CanFinalizeAdmissionLocked(pRequest))
                {
                    _diagnostics.RecordRejected();
                    return false;
                }
                return TryEnqueueRequestLocked(pRequest);
            }
        }

        private bool TryEnqueueRequestLocked(AWAsyncWorkRequest pRequest)
        {
            var item = new WorkerItem
            {
                Id = ++_nextWorkId,
                Key = pRequest.Key,
                Lane = pRequest.Lane,
                Stamp = pRequest.Stamp,
                Execute = pRequest.Execute,
                Token = _worldCancellation.Token,
                CommitOnWorker = pRequest.CommitMode ==
                    AWAsyncCommitMode.Background
            };
            if (!TryEnqueueLocked(item, out WorkerItem replaced))
            {
                _diagnostics.RecordRejected();
                return false;
            }

            if (replaced != null)
            {
                _commits.Remove(replaced.Id);
                _diagnostics.RecordMerged();
            }
            _commits[item.Id] = new CommitItem
            {
                Id = item.Id,
                Key = item.Key,
                Lane = item.Lane,
                Stamp = pRequest.Stamp,
                Commit = pRequest.Commit,
                Fault = pRequest.Fault,
                CommitOnWorker = pRequest.CommitMode ==
                    AWAsyncCommitMode.Background
            };
            _diagnostics.RecordScheduled();
            SignalWorkLocked();
            return true;
        }

        public bool CanSchedule(string pKey, AWAsyncLane pLane,
            AWAsyncStamp pStamp)
        {
            lock (_gate)
            {
                return _enabled &&
                       _state == AWAsyncLifecycleState.Running &&
                       !SaveBarrierBlocksCurrentThreadLocked() &&
                       _worldCancellation != null &&
                       pStamp.WorldGeneration == _worldGeneration &&
                       !string.IsNullOrEmpty(pKey) &&
                       CanEnqueueLocked(pKey, pLane);
            }
        }

        public void DrainMainThread(double milliseconds, int maxBatches)
        {
            if (maxBatches <= 0) return;
            long started = Stopwatch.GetTimestamp();
            long budget = Math.Max(1L, (long)(Stopwatch.Frequency *
                Math.Max(0.01, milliseconds) / 1000.0));
            int processed = 0;
            while (processed < maxBatches &&
                   Stopwatch.GetTimestamp() - started < budget)
            {
                Completion completion;
                CommitItem commit;
                lock (_gate)
                {
                    if (!_completions.TryDequeue(out completion)) break;
                    _commits.TryGetValue(completion.Id, out commit);
                    _commits.Remove(completion.Id);
                    Monitor.PulseAll(_gate);
                }
                processed++;
                if (commit == null) continue;
                if (completion.Stamp.WorldGeneration != WorldGeneration)
                {
                    _diagnostics.RecordStale();
                    continue;
                }
                if (completion.Error != null)
                {
                    _diagnostics.RecordFaulted();
                    NotifyFault(commit, AWAsyncFaultPhase.Execute,
                        completion.Error);
                    continue;
                }
                long commitStarted = Stopwatch.GetTimestamp();
                try
                {
                    commit.Commit(completion.Result);
                    _diagnostics.RecordCommitted();
                }
                catch (Exception error)
                {
                    _diagnostics.RecordFaulted();
                    NotifyFault(commit, AWAsyncFaultPhase.Commit, error);
                }
                finally
                {
                    _diagnostics.RecordMainThreadCommit(commit.Key,
                        commit.Lane,
                        Stopwatch.GetTimestamp() - commitStarted);
                }
            }
        }

        public AWAsyncCommitTimingSnapshot TakeMainThreadCommitTiming()
        {
            return _diagnostics.TakeMainThreadCommitTiming();
        }

        public AWAsyncDiagnosticsSnapshot SnapshotDiagnostics()
        {
            lock (_gate)
            {
                int queued = _traversal.Count + _ui.Count + _ai.Count;
                return _diagnostics.Snapshot().WithRuntime(queued,
                    _activeWorkerCount, _completions.Count,
                    _worldGeneration, _workers.Length);
            }
        }

        public AWAsyncFaultRecord[] SnapshotFaults()
        {
            lock (_gate) return _faults.ToArray();
        }

        public bool TryEnterSaveBarrier(TimeSpan pTimeout,
            out string pError)
        {
            return TryEnterSaveBarrier(pTimeout, null, out pError);
        }

        public bool TryEnterSaveBarrier(TimeSpan pTimeout,
            Action pPendingOwnerWork, out string pError)
        {
            long deadline = Deadline(pTimeout);
            lock (_gate)
            {
                if (_state == AWAsyncLifecycleState.Stopped)
                {
                    pError = string.Empty;
                    return true;
                }
                if (_state != AWAsyncLifecycleState.Running)
                {
                    pError = "async runtime is not running";
                    return false;
                }
                if (_saveBarrier)
                {
                    pError = "async save barrier is already active";
                    return false;
                }
                if (_saveBarrierPending)
                {
                    pError = "async save barrier is already pending";
                    return false;
                }
                _saveBarrierPending = true;
                _saveBarrierDrainThreadId =
                    Environment.CurrentManagedThreadId;
                SignalWorkersLocked();
                Monitor.PulseAll(_gate);
            }

            try
            {
                pPendingOwnerWork?.Invoke();
                if (Stopwatch.GetTimestamp() > deadline)
                {
                    lock (_gate) ClearSaveBarrierAttemptLocked();
                    pError = "async save barrier timed out";
                    return false;
                }
                return DrainToSaveBoundaryMainThread(deadline, out pError);
            }
            catch
            {
                lock (_gate) ClearSaveBarrierAttemptLocked();
                throw;
            }
        }

        public void ExitSaveBarrier()
        {
            lock (_gate)
            {
                _saveBarrier = false;
                _saveBarrierPending = false;
                _saveBarrierDrainThreadId = 0;
                Monitor.PulseAll(_gate);
            }
        }

        public void ClearWorld(TimeSpan pTimeout)
        {
            long deadline = Deadline(pTimeout);
            CancellationAttempt attempt;
            lock (_gate)
            {
                if (_state != AWAsyncLifecycleState.Running) return;
                _saveBarrier = false;
                _saveBarrierPending = false;
                _saveBarrierDrainThreadId = 0;
                attempt = _cancellationAttempt;
                if (attempt == null)
                {
                    CancellationTokenSource cancellation =
                        DetachCurrentWorldLocked();
                    _worldGeneration++;
                    if (cancellation != null)
                    {
                        attempt = new CancellationAttempt(cancellation,
                            "AW3 Async World Cancel");
                        _cancellationAttempt = attempt;
                    }
                }
            }

            attempt?.Start();
            if (!TryJoinUntil(attempt?.Thread, deadline))
                throw new TimeoutException(
                    "Async compute world cancellation timed out.");

            lock (_gate)
            {
                CompleteCancellationLocked(attempt);
                while (_activeWorkerCount > 0)
                {
                    long remaining = deadline - Stopwatch.GetTimestamp();
                    if (remaining <= 0L) break;
                    int waitMilliseconds = (int)Math.Min(50L,
                        Math.Max(1L, remaining * 1000L / Stopwatch.Frequency));
                    Monitor.Wait(_gate, waitMilliseconds);
                }
                if (_activeWorkerCount > 0)
                    throw new TimeoutException(
                        "Async compute world clear timed out.");
                if (attempt?.Error != null)
                    throw new InvalidOperationException(
                        "Async compute world cancellation callback failed.",
                        attempt.Error);
            }
        }

        public bool TryShutdown(TimeSpan pTimeout, out string pError)
        {
            long deadline = Deadline(pTimeout);
            Thread[] workers;
            CancellationAttempt attempt;
            lock (_gate)
            {
                if (_state == AWAsyncLifecycleState.Stopped)
                {
                    pError = string.Empty;
                    return true;
                }
                if (_state != AWAsyncLifecycleState.Draining)
                {
                    _state = AWAsyncLifecycleState.Draining;
                    _saveBarrier = false;
                    _saveBarrierPending = false;
                    _saveBarrierDrainThreadId = 0;
                    _shutdownRequested = true;
                }
                attempt = _cancellationAttempt;
                if (attempt == null)
                {
                    CancellationTokenSource cancellation =
                        DetachCurrentWorldLocked();
                    if (cancellation != null)
                    {
                        attempt = new CancellationAttempt(cancellation,
                            "AW3 Async Shutdown Cancel");
                        _cancellationAttempt = attempt;
                    }
                }
                workers = _workers;
                SignalWorkersLocked();
                Monitor.PulseAll(_gate);
            }

            attempt?.Start();
            if (!TryJoinUntil(attempt?.Thread, deadline))
            {
                pError = "async compute cancellation timed out";
                return false;
            }

            lock (_gate) CompleteCancellationLocked(attempt);

            if (!TryJoinUntil(workers, deadline))
            {
                pError = CancellationError("async compute",
                             attempt?.Error) +
                         ErrorSeparator(attempt?.Error) +
                         "async compute shutdown timed out";
                return false;
            }

            lock (_gate)
            {
                if (AnyWorkerAlive(workers))
                {
                    pError = CancellationError("async compute",
                                 attempt?.Error) +
                             ErrorSeparator(attempt?.Error) +
                             "async compute shutdown timed out";
                    return false;
                }
                while (_completions.TryDequeue(out Completion completion))
                    _commits.Remove(completion.Id);
                _commits.Clear();
                DisposeRetiredCancellationsLocked();
                _worldCancellation?.Dispose();
                _worldCancellation = null;
                _workers = Array.Empty<Thread>();
                _state = AWAsyncLifecycleState.Stopped;
                if (attempt?.Error != null)
                {
                    pError = CancellationError("async compute",
                        attempt.Error);
                    return false;
                }
                pError = string.Empty;
                return true;
            }
        }

        public void Shutdown(TimeSpan pTimeout)
        {
            if (TryShutdown(pTimeout, out string error)) return;
            throw new InvalidOperationException(error);
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (!TryShutdown(TimeSpan.FromSeconds(2), out string error))
                throw new InvalidOperationException(error);
            _workSignal.Dispose();
            _disposed = true;
        }

        private void WorkerLoop()
        {
            // Keep the raw thread entry point bounded. Any exception escaping
            // queue bookkeeping or a worker-side commit would otherwise
            // terminate the process without reaching the mod logger.
            while (true)
            {
                bool workerCounted = false;
                try
                {
                    if (!RunWorkerIteration(ref workerCounted)) return;
                }
                catch (Exception error)
                {
                    ReportWorkerLoopFault(error);
                    if (workerCounted) ReleaseFaultedWorkerSlot();
                }
            }
        }

        private void ReportWorkerLoopFault(Exception pError)
        {
            try
            {
                _diagnostics.RecordFaulted();
                AncientWarfare3.ModClass.LogWarning(
                    "[AW3 async worker] unhandled worker fault: " + pError);
            }
            catch
            {
            }
        }

        private void ReleaseFaultedWorkerSlot()
        {
            try
            {
                lock (_gate)
                {
                    if (_activeWorkerCount > 0) _activeWorkerCount--;
                    if (_activeWorkerCount == 0)
                        DisposeRetiredCancellationsLocked();
                    Monitor.PulseAll(_gate);
                }
            }
            catch
            {
            }
        }

        // Returns false when the worker should exit its loop.
        private bool RunWorkerIteration(ref bool pWorkerCounted)
        {
            WorkerItem item;
            lock (_gate)
            {
                if (_shutdownRequested) return false;
                if (!TryTakeNextLocked(out item))
                {
                    item = null;
                }
                else
                {
                    _activeWorkerCount++;
                    pWorkerCounted = true;
                }
            }
            if (item == null)
            {
                _workSignal.Wait(100);
                return true;
            }

            object result = null;
            Exception error = null;
            try
            {
                result = item.Execute(item.Token);
            }
            catch (Exception caught)
            {
                error = caught;
            }

            var completion = new Completion
            {
                Id = item.Id,
                Stamp = item.Stamp,
                Result = result,
                Error = error
            };
            if (item.CommitOnWorker)
            {
                RunWorkerCommit(item, result, error);
                lock (_gate)
                {
                    _activeWorkerCount--;
                    pWorkerCounted = false;
                    if (_activeWorkerCount == 0)
                        DisposeRetiredCancellationsLocked();
                    Monitor.PulseAll(_gate);
                }
                return true;
            }
            lock (_gate)
            {
                if (item.Stamp.WorldGeneration != _worldGeneration)
                {
                    _activeWorkerCount--;
                    pWorkerCounted = false;
                    if (_activeWorkerCount == 0)
                        DisposeRetiredCancellationsLocked();
                    Monitor.PulseAll(_gate);
                    return true;
                }
                while (!_shutdownRequested &&
                       item.Stamp.WorldGeneration == _worldGeneration &&
                       !_completions.TryEnqueue(completion))
                    Monitor.Wait(_gate, 50);
                _activeWorkerCount--;
                pWorkerCounted = false;
                if (_activeWorkerCount == 0)
                    DisposeRetiredCancellationsLocked();
                Monitor.PulseAll(_gate);
            }
            return true;
        }

        private void RunWorkerCommit(WorkerItem pItem, object pResult,
            Exception pExecuteError)
        {
            CommitItem commit;
            bool stale;
            lock (_gate)
            {
                _commits.TryGetValue(pItem.Id, out commit);
                _commits.Remove(pItem.Id);
                stale = pItem.Stamp.WorldGeneration != _worldGeneration;
                Monitor.PulseAll(_gate);
            }
            if (commit == null) return;
            if (stale)
            {
                _diagnostics.RecordStale();
                return;
            }
            if (pExecuteError != null)
            {
                _diagnostics.RecordFaulted();
                NotifyFault(commit, AWAsyncFaultPhase.Execute,
                    pExecuteError);
                return;
            }
            try
            {
                commit.Commit(pResult);
                _diagnostics.RecordCommitted();
                _diagnostics.RecordBackgroundCommitted();
            }
            catch (Exception error)
            {
                _diagnostics.RecordFaulted();
                NotifyFault(commit, AWAsyncFaultPhase.Commit, error);
            }
        }

        private void NotifyFault(CommitItem pCommit,
            AWAsyncFaultPhase pPhase, Exception pError)
        {
            RecordFault(pCommit, pPhase, pError);
            if (pCommit?.Fault == null) return;
            try
            {
                pCommit.Fault(pError);
            }
            catch (Exception callbackError)
            {
                _diagnostics.RecordFaulted();
                RecordFault(pCommit, AWAsyncFaultPhase.FaultCallback,
                    callbackError);
            }
        }

        private void RecordFault(CommitItem pCommit,
            AWAsyncFaultPhase pPhase, Exception pError)
        {
            if (pCommit == null) return;
            var record = new AWAsyncFaultRecord(pCommit.Id, pCommit.Key,
                pCommit.Lane, pCommit.Stamp, pPhase, pError);
            lock (_gate)
            {
                while (_faults.Count >= AWAsyncCapacity.FaultRecords)
                    _faults.Dequeue();
                _faults.Enqueue(record);
            }
        }

        private bool TryEnqueueLocked(WorkerItem pItem,
            out WorkerItem pReplaced)
        {
            switch (pItem.Lane)
            {
                case AWAsyncLane.Traversal:
                    return _traversal.TryEnqueue(pItem, out pReplaced);
                case AWAsyncLane.Ui:
                    return _ui.TryEnqueue(pItem, out pReplaced);
                case AWAsyncLane.Ai:
                    return _ai.TryEnqueue(pItem, out pReplaced);
                default:
                    pReplaced = null;
                    return false;
            }
        }

        private bool CanEnqueueLocked(string pKey, AWAsyncLane pLane)
        {
            switch (pLane)
            {
                case AWAsyncLane.Traversal:
                    return CanEnqueueLocked(_traversal,
                        _traversalReservations, pKey);
                case AWAsyncLane.Ui:
                    return CanEnqueueLocked(_ui, _uiReservations, pKey);
                case AWAsyncLane.Ai:
                    return CanEnqueueLocked(_ai, _aiReservations, pKey);
                default:
                    return false;
            }
        }

        private static bool CanEnqueueLocked(
            AWBoundedLatestQueue<WorkerItem> pQueue,
            Dictionary<string, long> pReservations, string pKey)
        {
            if (pReservations.ContainsKey(pKey)) return false;
            if (pQueue.ContainsKey(pKey)) return true;
            int occupied = pQueue.Count;
            foreach (string reservedKey in pReservations.Keys)
                if (!pQueue.ContainsKey(reservedKey)) occupied++;
            return occupied < pQueue.Capacity;
        }

        private AdmissionReservation ReserveAdmissionLocked(string pKey,
            AWAsyncLane pLane)
        {
            Dictionary<string, long> reservations =
                ReservationsForLaneLocked(pLane);
            if (reservations == null || reservations.ContainsKey(pKey))
                return null;
            var reservation = new AdmissionReservation
            {
                Id = ++_nextAdmissionReservationId,
                Key = pKey,
                Lane = pLane
            };
            reservations.Add(pKey, reservation.Id);
            return reservation;
        }

        private bool OwnsAdmissionLocked(AdmissionReservation pReservation)
        {
            Dictionary<string, long> reservations =
                ReservationsForLaneLocked(pReservation.Lane);
            return reservations != null &&
                   reservations.TryGetValue(pReservation.Key,
                       out long id) && id == pReservation.Id;
        }

        private void ReleaseAdmissionLocked(
            AdmissionReservation pReservation)
        {
            Dictionary<string, long> reservations =
                ReservationsForLaneLocked(pReservation.Lane);
            if (reservations != null &&
                reservations.TryGetValue(pReservation.Key, out long id) &&
                id == pReservation.Id)
                reservations.Remove(pReservation.Key);
            Monitor.PulseAll(_gate);
        }

        private Dictionary<string, long> ReservationsForLaneLocked(
            AWAsyncLane pLane)
        {
            switch (pLane)
            {
                case AWAsyncLane.Traversal:
                    return _traversalReservations;
                case AWAsyncLane.Ui:
                    return _uiReservations;
                case AWAsyncLane.Ai:
                    return _aiReservations;
                default:
                    return null;
            }
        }

        private bool CanScheduleLocked(AWAsyncWorkRequest pRequest)
        {
            return _enabled && _state == AWAsyncLifecycleState.Running &&
                   !SaveBarrierBlocksCurrentThreadLocked() &&
                   _worldCancellation != null &&
                   pRequest.Stamp.WorldGeneration == _worldGeneration &&
                   !string.IsNullOrEmpty(pRequest.Key) &&
                   pRequest.Execute != null && pRequest.Commit != null &&
                   CanEnqueueLocked(pRequest.Key, pRequest.Lane);
        }

        private bool CanFinalizeAdmissionLocked(
            AWAsyncWorkRequest pRequest)
        {
            return _enabled && _state == AWAsyncLifecycleState.Running &&
                   !_saveBarrier && _worldCancellation != null &&
                   pRequest.Stamp.WorldGeneration == _worldGeneration &&
                   !string.IsNullOrEmpty(pRequest.Key) &&
                   pRequest.Execute != null && pRequest.Commit != null &&
                   CanEnqueueLocked(pRequest.Key, pRequest.Lane);
        }

        private bool SaveBarrierBlocksCurrentThreadLocked()
        {
            return _saveBarrier || _saveBarrierPending &&
                   _saveBarrierDrainThreadId !=
                   Environment.CurrentManagedThreadId;
        }

        private bool DrainToSaveBoundaryMainThread(long pDeadline,
            out string pError)
        {
            while (true)
            {
                DrainMainThread(1.0, 16);

                lock (_gate)
                {
                    if (!_saveBarrierPending ||
                        _saveBarrierDrainThreadId !=
                        Environment.CurrentManagedThreadId ||
                        _state != AWAsyncLifecycleState.Running ||
                        _worldCancellation == null)
                    {
                        ClearSaveBarrierAttemptLocked();
                        pError = "async runtime changed during save barrier";
                        return false;
                    }
                    if (SaveBoundaryQuiescentLocked())
                    {
                        _saveBarrierPending = false;
                        _saveBarrierDrainThreadId = 0;
                        _saveBarrier = true;
                        pError = string.Empty;
                        return true;
                    }

                    long remaining = pDeadline - Stopwatch.GetTimestamp();
                    if (remaining <= 0L)
                    {
                        ClearSaveBarrierAttemptLocked();
                        pError = "async save barrier timed out";
                        return false;
                    }
                    SignalWorkersLocked();
                    int waitMilliseconds = (int)Math.Min(10L,
                        Math.Max(1L, remaining * 1000L /
                            Stopwatch.Frequency));
                    Monitor.Wait(_gate, waitMilliseconds);
                }
            }
        }

        private bool SaveBoundaryQuiescentLocked()
        {
            return _traversalReservations.Count == 0 &&
                   _uiReservations.Count == 0 &&
                   _aiReservations.Count == 0 &&
                   _traversal.Count == 0 && _ui.Count == 0 &&
                   _ai.Count == 0 && _activeWorkerCount == 0 &&
                   _completions.Count == 0 && _commits.Count == 0;
        }

        private void ClearSaveBarrierAttemptLocked()
        {
            _saveBarrierPending = false;
            _saveBarrier = false;
            _saveBarrierDrainThreadId = 0;
            Monitor.PulseAll(_gate);
        }

        private bool TryTakeNextLocked(out WorkerItem pItem)
        {
            AWAsyncLane lane = AWAsyncPriorityRules.SelectComputeLane(
                _traversal.Count > 0, _ui.Count > 0, _ai.Count > 0,
                _highPriorityStreak, _lastHighPriorityLane);
            bool found;
            switch (lane)
            {
                case AWAsyncLane.Traversal:
                    found = _traversal.TryDequeue(out pItem);
                    break;
                case AWAsyncLane.Ui:
                    found = _ui.TryDequeue(out pItem);
                    break;
                case AWAsyncLane.Ai:
                    found = _ai.TryDequeue(out pItem);
                    break;
                default:
                    pItem = null;
                    return false;
            }
            if (!found) return false;
            if (lane == AWAsyncLane.Ai) _highPriorityStreak = 0;
            else
            {
                _highPriorityStreak++;
                _lastHighPriorityLane = lane;
            }
            return true;
        }

        private CancellationTokenSource DetachCurrentWorldLocked()
        {
            CancellationTokenSource cancellation = _worldCancellation;
            _worldCancellation = null;
            _traversalReservations.Clear();
            _uiReservations.Clear();
            _aiReservations.Clear();
            _saveBarrierPending = false;
            _saveBarrierDrainThreadId = 0;
            CancelQueueLocked(_traversal);
            CancelQueueLocked(_ui);
            CancelQueueLocked(_ai);
            while (_completions.TryDequeue(out Completion completion))
                _commits.Remove(completion.Id);
            _commits.Clear();
            Monitor.PulseAll(_gate);
            return cancellation;
        }

        private static string CancellationError(string pOwner,
            Exception pError)
        {
            if (pError == null) return string.Empty;
            return pOwner + " cancellation callback failed: " +
                   pError.GetBaseException().Message;
        }

        private static Exception Cancel(CancellationTokenSource pCancellation)
        {
            if (pCancellation == null) return null;
            try
            {
                pCancellation.Cancel();
                return null;
            }
            catch (Exception error)
            {
                return error;
            }
        }

        private static string ErrorSeparator(Exception pError)
        {
            return pError == null ? string.Empty : "; ";
        }

        private void CompleteCancellationLocked(CancellationAttempt pAttempt)
        {
            if (pAttempt == null ||
                !ReferenceEquals(_cancellationAttempt, pAttempt)) return;
            _cancellationAttempt = null;
            RetireCancellationLocked(pAttempt.Source);
        }

        private void RetireCancellationLocked(
            CancellationTokenSource pCancellation)
        {
            if (pCancellation == null) return;
            _retiredCancellations.Add(pCancellation);
            if (_activeWorkerCount == 0)
                DisposeRetiredCancellationsLocked();
        }

        private static long Deadline(TimeSpan pTimeout)
        {
            return Stopwatch.GetTimestamp() + Math.Max(0L,
                (long)(Stopwatch.Frequency *
                    Math.Max(0d, pTimeout.TotalSeconds)));
        }

        private static bool TryJoinUntil(Thread pThread, long pDeadline)
        {
            if (pThread == null) return true;
            if (pThread == Thread.CurrentThread) return false;
            long remaining = pDeadline - Stopwatch.GetTimestamp();
            if (remaining <= 0L) return pThread.Join(0);
            int milliseconds = (int)Math.Min(int.MaxValue,
                Math.Max(1L, remaining * 1000L / Stopwatch.Frequency));
            return pThread.Join(milliseconds);
        }

        private static bool TryJoinUntil(Thread[] pThreads, long pDeadline)
        {
            if (pThreads == null || pThreads.Length == 0) return true;
            for (int index = 0; index < pThreads.Length; index++)
                if (!TryJoinUntil(pThreads[index], pDeadline)) return false;
            return true;
        }

        private static bool AnyWorkerAlive(Thread[] pThreads)
        {
            if (pThreads == null) return false;
            for (int index = 0; index < pThreads.Length; index++)
                if (pThreads[index]?.IsAlive == true) return true;
            return false;
        }

        private void CancelQueueLocked(AWBoundedLatestQueue<WorkerItem> pQueue)
        {
            while (pQueue.TryDequeue(out WorkerItem item))
            {
                _commits.Remove(item.Id);
                _diagnostics.RecordCancelled();
            }
        }

        private void DisposeRetiredCancellationsLocked()
        {
            foreach (CancellationTokenSource source in _retiredCancellations)
                source.Dispose();
            _retiredCancellations.Clear();
        }

        private void SignalWorkLocked()
        {
            if (!_enabled || _workers.Length == 0) return;
            int queuedWork = _traversal.Count + _ui.Count + _ai.Count;
            int releaseCount = AWAsyncWorkerRules.ResolveWorkSignalReleaseCount(
                queuedWork, _workers.Length, _activeWorkerCount,
                _workSignal.CurrentCount);
            if (releaseCount > 0) _workSignal.Release(releaseCount);
        }

        private void SignalWorkersLocked()
        {
            if (!_enabled || _workers.Length == 0) return;
            int releaseCount = AWAsyncWorkerRules.ResolveWorkerSignalReleaseCount(
                _workers.Length, _activeWorkerCount, _workSignal.CurrentCount);
            if (releaseCount > 0) _workSignal.Release(releaseCount);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(
                nameof(AWAsyncWorkCoordinator));
        }
    }
}

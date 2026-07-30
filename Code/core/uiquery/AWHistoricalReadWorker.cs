using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Threading;
using AncientWarfare3.core.asyncwork;

namespace AncientWarfare3.core.uiquery
{
    internal sealed class AWHistoricalReadRequest
    {
        public AWHistoricalReadRequest(string pKey, AWAsyncStamp pStamp,
            Func<SQLiteConnection, CancellationToken, object> pExecute,
            Action<object> pCommit, Action<Exception> pFault = null,
            long? pDatabaseEpoch = null)
        {
            Key = pKey;
            Stamp = pStamp;
            Execute = pExecute;
            Commit = pCommit;
            Fault = pFault;
            DatabaseEpoch = pDatabaseEpoch ?? pStamp.SourceRevision;
        }

        public string Key { get; }
        public AWAsyncStamp Stamp { get; }
        public Func<SQLiteConnection, CancellationToken, object> Execute
        {
            get;
        }
        public Action<object> Commit { get; }
        public Action<Exception> Fault { get; }
        public long DatabaseEpoch { get; }
    }

    internal sealed class AWHistoricalReadWorker : IDisposable
    {
        private sealed class WorkItem
        {
            public long Id;
            public string Key;
            public AWAsyncStamp Stamp;
            public long DatabaseEpoch;
            public Func<SQLiteConnection, CancellationToken, object> Execute;
            public CancellationToken Token;
        }

        private sealed class Completion
        {
            public long Id;
            public string Key;
            public AWAsyncStamp Stamp;
            public long DatabaseEpoch;
            public CompletionStatus Status;
            public object Result;
            public Exception Error;
        }

        private enum CompletionStatus
        {
            Succeeded,
            Faulted,
            Cancelled
        }

        private sealed class MainThreadCallbacks
        {
            public Action<object> Commit;
            public Action<Exception> Fault;
        }

        private sealed class MainThreadCallbackRegistry
        {
            private readonly Dictionary<long, MainThreadCallbacks> _entries =
                new Dictionary<long, MainThreadCallbacks>();
            private readonly int _ownerThreadId =
                Environment.CurrentManagedThreadId;
            private long _releasedCount;
            private int _foreignAccessCount;

            public int Count
            {
                get
                {
                    EnsureOwnerThread();
                    return _entries.Count;
                }
            }

            public long ReleasedCount
            {
                get
                {
                    EnsureOwnerThread();
                    return _releasedCount;
                }
            }

            public int ForeignAccessCount => _foreignAccessCount;

            public void Register(long pId, Action<object> pCommit,
                Action<Exception> pFault)
            {
                EnsureOwnerThread();
                _entries[pId] = new MainThreadCallbacks
                {
                    Commit = pCommit,
                    Fault = pFault
                };
            }

            public MainThreadCallbacks Take(long pId)
            {
                EnsureOwnerThread();
                if (!_entries.TryGetValue(pId,
                        out MainThreadCallbacks callbacks)) return null;
                _entries.Remove(pId);
                _releasedCount++;
                return callbacks;
            }

            public bool Release(long pId)
            {
                EnsureOwnerThread();
                if (!_entries.Remove(pId)) return false;
                _releasedCount++;
                return true;
            }

            public void Clear()
            {
                EnsureOwnerThread();
                _releasedCount += _entries.Count;
                _entries.Clear();
            }

            private void EnsureOwnerThread()
            {
                if (Environment.CurrentManagedThreadId == _ownerThreadId)
                    return;
                Interlocked.Increment(ref _foreignAccessCount);
                throw new InvalidOperationException(
                    "Historical read callback registry is main-thread-only.");
            }
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
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private readonly AWBoundedLatestQueue<WorkItem> _queue;
        private readonly Queue<Completion> _completions =
            new Queue<Completion>();
        private readonly Dictionary<string, long> _latestIds =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly MainThreadCallbackRegistry _callbacks =
            new MainThreadCallbackRegistry();
        private readonly AWAsyncDiagnostics _diagnostics =
            new AWAsyncDiagnostics();
        private readonly int _completionCapacity;
        private readonly Action<SQLiteConnection> _closeConnection;

        private Thread _thread;
        private SQLiteConnection _connection;
        private CancellationTokenSource _worldCancellation;
        private CancellationAttempt _cancellationAttempt;
        private string _databasePath = string.Empty;
        private long _databaseEpoch;
        private long _worldGeneration;
        private long _nextId;
        private int _activeCount;
        private bool _configured;
        private bool _accepting;
        private bool _saveBarrier;
        private bool _clearRequested;
        private bool _connectionOpen;
        private bool _shutdownRequested;
        private bool _disposed;

        public AWHistoricalReadWorker(int pCapacity = AWAsyncCapacity.Ui,
            int pCompletionCapacity = AWAsyncCapacity.CompletionBatches,
            Action<SQLiteConnection> pCloseConnection = null)
        {
            _queue = new AWBoundedLatestQueue<WorkItem>(
                Math.Max(1, pCapacity), pItem => pItem.Key);
            _completionCapacity = Math.Max(1, pCompletionCapacity);
            _closeConnection = pCloseConnection ?? CloseAndDisposeConnection;
        }

        public bool WorkerAlive
        {
            get { lock (_gate) return _thread?.IsAlive == true; }
        }

        public bool ConnectionOpen
        {
            get { lock (_gate) return _connectionOpen; }
        }

        public bool Accepting
        {
            get { lock (_gate) return _accepting; }
        }

        public int PendingCount
        {
            get { lock (_gate) return _queue.Count + _activeCount; }
        }

        public int PendingCompletionCount
        {
            get { lock (_gate) return _completions.Count; }
        }

        public long DatabaseEpoch
        {
            get { lock (_gate) return _databaseEpoch; }
        }

        public void StartWorld(string pDatabasePath, long pDatabaseEpoch,
            long pWorldGeneration)
        {
            if (string.IsNullOrWhiteSpace(pDatabasePath))
                throw new ArgumentException("Database path is required.",
                    nameof(pDatabasePath));
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_configured || _clearRequested || _activeCount != 0 ||
                    _connectionOpen || _shutdownRequested ||
                    _cancellationAttempt != null)
                    throw new InvalidOperationException(
                        "Historical read world must be cleared before restart.");
                _databasePath = pDatabasePath;
                _databaseEpoch = pDatabaseEpoch;
                _worldGeneration = pWorldGeneration;
                _worldCancellation = new CancellationTokenSource();
                _configured = true;
                _accepting = true;
                _saveBarrier = false;
                _clearRequested = false;
                _shutdownRequested = false;
                _latestIds.Clear();
                ClearQueueLocked();
                ClearCompletionsLocked();
                _callbacks.Clear();
                Monitor.PulseAll(_gate);
            }
        }

        public bool TrySchedule(AWHistoricalReadRequest pRequest)
        {
            return TrySchedule(pRequest, out _);
        }

        public bool TrySchedule(AWHistoricalReadRequest pRequest,
            out long pRequestId)
        {
            pRequestId = -1L;
            if (pRequest == null)
            {
                _diagnostics.RecordRejected();
                return false;
            }
            lock (_gate)
            {
                if (_disposed || !_configured || !_accepting ||
                    _saveBarrier || _shutdownRequested ||
                    pRequest.Stamp.WorldGeneration != _worldGeneration ||
                    pRequest.DatabaseEpoch != _databaseEpoch ||
                    string.IsNullOrEmpty(pRequest.Key) ||
                    pRequest.Execute == null || pRequest.Commit == null ||
                    _worldCancellation == null)
                {
                    _diagnostics.RecordRejected();
                    return false;
                }

                var item = new WorkItem
                {
                    Id = ++_nextId,
                    Key = pRequest.Key,
                    Stamp = pRequest.Stamp,
                    DatabaseEpoch = pRequest.DatabaseEpoch,
                    Execute = pRequest.Execute,
                    Token = _worldCancellation.Token
                };
                if (!_queue.TryEnqueue(item, out WorkItem replaced))
                {
                    _diagnostics.RecordRejected();
                    return false;
                }
                _callbacks.Register(item.Id, pRequest.Commit,
                    pRequest.Fault);
                if (replaced != null)
                {
                    _callbacks.Release(replaced.Id);
                    _diagnostics.RecordMerged();
                }
                _latestIds[pRequest.Key] = item.Id;
                pRequestId = item.Id;
                _diagnostics.RecordScheduled();
                EnsureThreadLocked();
                _signal.Set();
                return true;
            }
        }

        public bool Cancel(long pRequestId, string pKey)
        {
            if (pRequestId < 0L || string.IsNullOrEmpty(pKey)) return false;
            lock (_gate)
            {
                if (!_latestIds.TryGetValue(pKey, out long latestId) ||
                    latestId != pRequestId) return false;
                _latestIds.Remove(pKey);
                bool released = _callbacks.Release(pRequestId);
                if (released) _diagnostics.RecordCancelled();
                Monitor.PulseAll(_gate);
                return released;
            }
        }

        public void DrainMainThread(int pMaximumCompletions)
        {
            DrainMainThreadCore(pMaximumCompletions, 0L);
        }

        public void DrainMainThread(double pMilliseconds,
            int pMaximumCompletions)
        {
            long budget = Math.Max(1L, (long)(Stopwatch.Frequency *
                Math.Max(0.01, pMilliseconds) / 1000.0));
            DrainMainThreadCore(pMaximumCompletions,
                Stopwatch.GetTimestamp() + budget);
        }

        private void DrainMainThreadCore(int pMaximumCompletions,
            long pDeadline)
        {
            int processed = 0;
            while (processed < pMaximumCompletions &&
                   (processed == 0 || pDeadline == 0L ||
                    Stopwatch.GetTimestamp() < pDeadline))
            {
                Completion completion;
                MainThreadCallbacks callbacks;
                bool accepted;
                lock (_gate)
                {
                    if (_completions.Count == 0) break;
                    completion = _completions.Dequeue();
                    if (completion.Status == CompletionStatus.Cancelled)
                    {
                        _callbacks.Release(completion.Id);
                        if (_latestIds.TryGetValue(completion.Key,
                                out long cancelledLatestId) &&
                            cancelledLatestId == completion.Id)
                            _latestIds.Remove(completion.Key);
                        callbacks = null;
                        accepted = false;
                    }
                    else
                    {
                        accepted = _configured && !_saveBarrier &&
                        completion.Stamp.WorldGeneration == _worldGeneration &&
                        completion.DatabaseEpoch == _databaseEpoch &&
                        _latestIds.TryGetValue(completion.Key,
                            out long latestId) && latestId == completion.Id;
                        callbacks = _callbacks.Take(completion.Id);
                        accepted = accepted && callbacks != null;
                        if (accepted) _latestIds.Remove(completion.Key);
                    }
                    Monitor.PulseAll(_gate);
                }
                processed++;
                if (completion.Status == CompletionStatus.Cancelled)
                {
                    _diagnostics.RecordCancelled();
                    continue;
                }
                if (!accepted)
                {
                    _diagnostics.RecordStale();
                    continue;
                }
                if (completion.Error != null)
                {
                    _diagnostics.RecordFaulted();
                    try { callbacks.Fault?.Invoke(completion.Error); }
                    catch { }
                    continue;
                }
                try
                {
                    callbacks.Commit(completion.Result);
                    _diagnostics.RecordCommitted();
                }
                catch (Exception error)
                {
                    _diagnostics.RecordFaulted();
                    try { callbacks.Fault?.Invoke(error); }
                    catch { }
                }
            }
        }

        public AWAsyncDiagnosticsSnapshot SnapshotDiagnostics()
        {
            lock (_gate)
                return _diagnostics.Snapshot().WithRuntime(
                    _queue.Count, _activeCount, _completions.Count,
                    _worldGeneration);
        }

        public bool TryEnterSaveBarrier(TimeSpan pTimeout,
            out string pError)
        {
            lock (_gate)
            {
                if (!_configured)
                {
                    pError = string.Empty;
                    return true;
                }
                if (_saveBarrier)
                {
                    pError = "historical read save barrier is already active";
                    return false;
                }
                _saveBarrier = true;
                _accepting = false;
                ClearQueueLocked();
                ClearCompletionsLocked();
                _callbacks.Clear();
                _latestIds.Clear();
                _signal.Set();
                Monitor.PulseAll(_gate);

                long deadline = Deadline(pTimeout);
                while (_activeCount > 0 || _connectionOpen)
                {
                    if (!WaitUntilLocked(deadline))
                    {
                        pError = "historical read save barrier timed out";
                        return false;
                    }
                }
                pError = string.Empty;
                return true;
            }
        }

        public void ExitSaveBarrier()
        {
            lock (_gate)
            {
                if (!_configured) return;
                _saveBarrier = false;
                _accepting = !_clearRequested && !_shutdownRequested &&
                    _worldCancellation != null &&
                    !_worldCancellation.IsCancellationRequested;
                Monitor.PulseAll(_gate);
            }
        }

        public bool ClearWorld(TimeSpan pTimeout, out string pError)
        {
            long deadline = Deadline(pTimeout);
            CancellationAttempt attempt;
            lock (_gate)
            {
                _accepting = false;
                _saveBarrier = false;
                _clearRequested = true;
                attempt = GetOrCreateCancellationLocked(
                    "AW3 Historical Read World Cancel");
                ClearQueueLocked();
                ClearCompletionsLocked();
                _callbacks.Clear();
                _latestIds.Clear();
                _signal.Set();
                Monitor.PulseAll(_gate);
            }

            attempt?.Start();
            if (!TryJoinUntil(attempt?.Thread, deadline))
            {
                pError = "historical read cancellation timed out";
                return false;
            }

            lock (_gate)
            {
                while (_activeCount > 0 || _connectionOpen)
                {
                    if (WaitUntilLocked(deadline)) continue;
                    pError = CancellationError(attempt?.Error) +
                             ErrorSeparator(attempt?.Error) +
                             "historical read world clear timed out";
                    return false;
                }
                CompleteCancellationLocked(attempt);
                _configured = false;
                _clearRequested = false;
                _databasePath = string.Empty;
                _databaseEpoch = 0L;
                _worldGeneration = 0L;
                if (attempt?.Error != null)
                {
                    pError = CancellationError(attempt.Error);
                    return false;
                }
                pError = string.Empty;
                return true;
            }
        }

        public void ClearWorld(TimeSpan pTimeout)
        {
            if (ClearWorld(pTimeout, out string error)) return;
            throw new InvalidOperationException(error);
        }

        public bool TryShutdown(TimeSpan pTimeout, out string pError)
        {
            long deadline = Deadline(pTimeout);
            Thread thread;
            CancellationAttempt attempt;
            lock (_gate)
            {
                _accepting = false;
                _saveBarrier = false;
                _clearRequested = true;
                _shutdownRequested = true;
                attempt = GetOrCreateCancellationLocked(
                    "AW3 Historical Read Shutdown Cancel");
                ClearQueueLocked();
                ClearCompletionsLocked();
                _callbacks.Clear();
                _latestIds.Clear();
                thread = _thread;
                _signal.Set();
                Monitor.PulseAll(_gate);
            }

            attempt?.Start();
            if (!TryJoinUntil(attempt?.Thread, deadline))
            {
                pError = "historical read cancellation timed out";
                return false;
            }
            if (!TryJoinUntil(thread, deadline))
            {
                pError = CancellationError(attempt?.Error) +
                         ErrorSeparator(attempt?.Error) +
                         "historical read shutdown timed out";
                return false;
            }

            lock (_gate)
            {
                if (thread?.IsAlive == true)
                {
                    pError = CancellationError(attempt?.Error) +
                             ErrorSeparator(attempt?.Error) +
                             "historical read shutdown timed out";
                    return false;
                }
                _thread = null;
                CompleteCancellationLocked(attempt);
                _configured = false;
                _clearRequested = false;
                _shutdownRequested = false;
                _databasePath = string.Empty;
                _databaseEpoch = 0L;
                _worldGeneration = 0L;
                if (attempt?.Error != null)
                {
                    pError = CancellationError(attempt.Error);
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
            _signal.Dispose();
            _disposed = true;
        }

        private void WorkerLoop()
        {
            try
            {
                while (true)
                {
                    WorkItem item = null;
                    bool closeConnection;
                    bool stop;
                    lock (_gate)
                    {
                        stop = _shutdownRequested;
                        closeConnection = stop || _clearRequested ||
                            !_configured || _saveBarrier;
                        if (!stop && !closeConnection &&
                            _queue.TryDequeue(out item))
                            _activeCount++;
                    }

                    if (closeConnection) CloseConnectionOnWorker();
                    if (stop) return;
                    if (item == null)
                    {
                        _signal.WaitOne(100);
                        continue;
                    }

                    object result = null;
                    Exception error = null;
                    try
                    {
                        lock (_gate)
                        {
                            if (_shutdownRequested || _clearRequested ||
                                !_configured || _saveBarrier ||
                                item.Token.IsCancellationRequested ||
                                item.Stamp.WorldGeneration !=
                                _worldGeneration ||
                                item.DatabaseEpoch != _databaseEpoch ||
                                !_latestIds.TryGetValue(item.Key,
                                    out long currentId) ||
                                currentId != item.Id)
                                throw new OperationCanceledException(
                                    item.Token);
                        }
                        item.Token.ThrowIfCancellationRequested();
                        EnsureConnectionOnWorker();
                        item.Token.ThrowIfCancellationRequested();
                        result = item.Execute(_connection,
                            item.Token);
                    }
                    catch (Exception caught)
                    {
                        error = caught;
                    }

                    lock (_gate)
                    {
                        bool accepted = !_shutdownRequested &&
                            _configured && !_saveBarrier &&
                            !item.Token.IsCancellationRequested &&
                            item.Stamp.WorldGeneration ==
                            _worldGeneration &&
                            item.DatabaseEpoch == _databaseEpoch &&
                            _latestIds.TryGetValue(item.Key,
                                out long latestId) && latestId == item.Id;
                        while (accepted &&
                               _completions.Count >= _completionCapacity)
                        {
                            Monitor.Wait(_gate, 50);
                            accepted = !_shutdownRequested &&
                                _configured && !_saveBarrier &&
                                !item.Token.IsCancellationRequested &&
                                item.Stamp.WorldGeneration ==
                                _worldGeneration &&
                                item.DatabaseEpoch == _databaseEpoch &&
                                _latestIds.TryGetValue(item.Key,
                                    out latestId) && latestId == item.Id;
                        }
                        _completions.Enqueue(new Completion
                        {
                            Id = item.Id,
                            Key = item.Key,
                            Stamp = item.Stamp,
                            DatabaseEpoch = item.DatabaseEpoch,
                            Status = !accepted
                                ? CompletionStatus.Cancelled
                                : error == null
                                    ? CompletionStatus.Succeeded
                                    : CompletionStatus.Faulted,
                            Result = accepted ? result : null,
                            Error = accepted ? error : null
                        });
                        _activeCount--;
                        Monitor.PulseAll(_gate);
                    }
                }
            }
            finally
            {
                CloseConnectionOnWorker();
                lock (_gate)
                {
                    _activeCount = 0;
                    Monitor.PulseAll(_gate);
                }
            }
        }

        private void EnsureThreadLocked()
        {
            if (_thread?.IsAlive == true) return;
            var thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "AW3 Historical DB Reader",
                Priority = ThreadPriority.BelowNormal
            };
            thread.Start();
            _thread = thread;
        }

        private void EnsureConnectionOnWorker()
        {
            if (_connection != null) return;
            string path;
            lock (_gate) path = _databasePath;
            var connection = new SQLiteConnection(
                "Data Source=" + path +
                ";Version=3;Read Only=True;Pooling=False;");
            connection.Open();
            _connection = connection;
            lock (_gate)
            {
                _connectionOpen = true;
                Monitor.PulseAll(_gate);
            }
        }

        private void CloseConnectionOnWorker()
        {
            SQLiteConnection connection = _connection;
            _connection = null;
            try
            {
                if (connection != null) _closeConnection(connection);
            }
            catch
            {
                _diagnostics.RecordFaulted();
            }
            finally
            {
                lock (_gate)
                {
                    _connectionOpen = false;
                    Monitor.PulseAll(_gate);
                }
            }
        }

        private static void CloseAndDisposeConnection(
            SQLiteConnection pConnection)
        {
            try { pConnection.Close(); }
            finally { pConnection.Dispose(); }
        }

        private void ClearQueueLocked()
        {
            while (_queue.TryDequeue(out WorkItem item))
            {
                _callbacks.Release(item.Id);
                _diagnostics.RecordCancelled();
            }
        }

        private void ClearCompletionsLocked()
        {
            while (_completions.Count > 0)
            {
                Completion completion = _completions.Dequeue();
                _callbacks.Release(completion.Id);
                _diagnostics.RecordCancelled();
            }
        }

        private static long Deadline(TimeSpan pTimeout)
        {
            return Stopwatch.GetTimestamp() + Math.Max(0L,
                (long)(Stopwatch.Frequency *
                    Math.Max(0d, pTimeout.TotalSeconds)));
        }

        private static string CancellationError(Exception pError)
        {
            if (pError == null) return string.Empty;
            return "historical read cancellation callback failed: " +
                   pError.GetBaseException().Message;
        }

        private static string ErrorSeparator(Exception pError)
        {
            return pError == null ? string.Empty : "; ";
        }

        private CancellationAttempt GetOrCreateCancellationLocked(
            string pThreadName)
        {
            if (_cancellationAttempt != null) return _cancellationAttempt;
            CancellationTokenSource cancellation = _worldCancellation;
            _worldCancellation = null;
            if (cancellation == null) return null;
            _cancellationAttempt = new CancellationAttempt(cancellation,
                pThreadName);
            return _cancellationAttempt;
        }

        private void CompleteCancellationLocked(CancellationAttempt pAttempt)
        {
            if (pAttempt == null ||
                !ReferenceEquals(_cancellationAttempt, pAttempt)) return;
            _cancellationAttempt = null;
            pAttempt.Source.Dispose();
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

        private bool WaitUntilLocked(long pDeadline)
        {
            long remaining = pDeadline - Stopwatch.GetTimestamp();
            if (remaining <= 0L) return false;
            int milliseconds = (int)Math.Min(50L,
                Math.Max(1L, remaining * 1000L / Stopwatch.Frequency));
            Monitor.Wait(_gate, milliseconds);
            return true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(
                    nameof(AWHistoricalReadWorker));
        }
    }
}

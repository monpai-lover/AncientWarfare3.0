using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using AncientWarfare3.core.asyncwork;

namespace AncientWarfare3.core.db
{
    internal interface IHistoricalWriteBatchSink : IDisposable
    {
        void Open();
        HistoricalWriteBatchResult Execute(
            IReadOnlyList<HistoricalWriteEnvelope> pOperations);
    }

    internal static class HistoricalWriteFailureRules
    {
        private const int PrimaryResultMask = 0xff;
        private const int SqliteBusy = 5;
        private const int SqliteLocked = 6;

        public static bool IsRetryableSqliteErrorCode(int pErrorCode)
        {
            int primary = pErrorCode & PrimaryResultMask;
            return primary == SqliteBusy || primary == SqliteLocked;
        }

        public static bool IsRetryableSqliteMessage(string pMessage)
        {
            if (string.IsNullOrWhiteSpace(pMessage)) return false;
            return pMessage.IndexOf("database is locked",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   pMessage.IndexOf("database is busy",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   pMessage.IndexOf("SQLITE_BUSY",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   pMessage.IndexOf("SQLITE_LOCKED",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal sealed class HistoricalWriteBatchResult
    {
        private enum ResultKind
        {
            Committed,
            Retryable,
            Terminal
        }

        private HistoricalWriteBatchResult(ResultKind pKind, string pError,
            IReadOnlyList<object> pOutcomes)
        {
            Kind = pKind;
            Error = pError ?? string.Empty;
            Outcomes = pOutcomes ?? Array.Empty<object>();
        }

        private ResultKind Kind { get; }
        public bool IsCommitted => Kind == ResultKind.Committed;
        public bool IsRetryable => Kind == ResultKind.Retryable;
        public bool IsTerminal => Kind == ResultKind.Terminal;
        public string Error { get; }
        public IReadOnlyList<object> Outcomes { get; }

        public static HistoricalWriteBatchResult Committed(
            IReadOnlyList<object> pOutcomes = null)
        {
            return new HistoricalWriteBatchResult(ResultKind.Committed,
                string.Empty,
                pOutcomes);
        }

        public static HistoricalWriteBatchResult Retry(string pError)
        {
            return Retryable(pError);
        }

        public static HistoricalWriteBatchResult Retryable(string pError)
        {
            return new HistoricalWriteBatchResult(ResultKind.Retryable, pError,
                Array.Empty<object>());
        }

        public static HistoricalWriteBatchResult Terminal(string pError)
        {
            return new HistoricalWriteBatchResult(ResultKind.Terminal, pError,
                Array.Empty<object>());
        }
    }

    internal sealed class HistoricalWriteCompletion
    {
        public HistoricalWriteCompletion(IReadOnlyList<HistoricalWriteEnvelope>
            pOperations, IReadOnlyList<object> pOutcomes)
            : this(pOperations, pOutcomes, true, string.Empty)
        {
        }

        public HistoricalWriteCompletion(IReadOnlyList<HistoricalWriteEnvelope>
            pOperations, IReadOnlyList<object> pOutcomes, bool pIsCommitted,
            string pError)
        {
            HistoricalWriteEnvelope[] operations = pOperations?.ToArray() ??
                Array.Empty<HistoricalWriteEnvelope>();
            FirstSequence = operations.Length == 0 ? 0L : operations[0].Sequence;
            LastSequence = operations.Length == 0
                ? 0L
                : operations[operations.Length - 1].Sequence;
            Sequences = Array.AsReadOnly(operations
                .Select(pOperation => pOperation.Sequence).ToArray());
            OperationKeys = Array.AsReadOnly(operations
                .Select(pOperation => pOperation.OperationKey).ToArray());
            object[] outcomes = new object[operations.Length];
            if (pOutcomes != null)
                for (int index = 0; index < outcomes.Length &&
                     index < pOutcomes.Count; index++)
                    outcomes[index] = pOutcomes[index];
            Outcomes = Array.AsReadOnly(outcomes);
            IsCommitted = pIsCommitted;
            Error = pError ?? string.Empty;
        }

        public long FirstSequence { get; }
        public long LastSequence { get; }
        public IReadOnlyList<long> Sequences { get; }
        public IReadOnlyList<string> OperationKeys { get; }
        public IReadOnlyList<object> Outcomes { get; }
        public bool IsCommitted { get; }
        public string Error { get; }
    }

    internal sealed class HistoricalWriteWorker : IDisposable
    {
        private readonly object _gate = new object();
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private readonly IHistoricalWriteBatchSink _sink;
        private readonly HistoricalWriteQueue _queue;
        private readonly Queue<HistoricalWriteCompletion> _completions =
            new Queue<HistoricalWriteCompletion>();
        private readonly SortedSet<long> _uncommittedSequences =
            new SortedSet<long>();
        private readonly HistoricalWriteProgress _progress =
            new HistoricalWriteProgress();
        private readonly int _batchSize;
        private readonly int _retryDelayMilliseconds;
        private readonly int _maxRetryAttempts;

        private Thread _thread;
        private bool _started;
        private bool _stopRequested;
        private bool _openFailed;
        private bool _terminalFaulted;
        private bool _sinkOwnershipTransferred;
        private bool _disposed;
        private int _inFlightCount;
        private long _lastAcceptedSequence;
        private string _lastError = string.Empty;

        public HistoricalWriteWorker(IHistoricalWriteBatchSink pSink,
            int appendCapacity = AWAsyncCapacity.DatabaseAppend,
            int stateCapacity = AWAsyncCapacity.DatabaseState,
            int batchSize = 64, int retryDelayMilliseconds = 4,
            int maxRetryAttempts = 3)
        {
            _sink = pSink ?? throw new ArgumentNullException(nameof(pSink));
            _queue = new HistoricalWriteQueue(appendCapacity, stateCapacity);
            _batchSize = Math.Max(1, Math.Min(128, batchSize));
            _retryDelayMilliseconds = Math.Max(0, retryDelayMilliseconds);
            _maxRetryAttempts = Math.Max(1, maxRetryAttempts);
        }

        public long LastCommittedSequence => _progress.LastCommittedSequence;

        public int PendingCount
        {
            get { lock (_gate) return _queue.Count + _inFlightCount; }
        }

        public int PendingCompletionCount
        {
            get { lock (_gate) return _completions.Count; }
        }

        public bool WorkerAlive
        {
            get { lock (_gate) return _thread?.IsAlive == true; }
        }

        public bool TerminalFaulted
        {
            get { lock (_gate) return _terminalFaulted; }
        }

        public long EarliestUncommittedSequence
        {
            get
            {
                lock (_gate) return _uncommittedSequences.Count == 0
                    ? 0L
                    : _uncommittedSequences.Min;
            }
        }

        public bool TryEnqueue(HistoricalWriteEnvelope pEnvelope,
            out HistoricalWriteEnvelope pReplaced)
        {
            lock (_gate)
            {
                if (_stopRequested || _disposed || _terminalFaulted ||
                    pEnvelope == null ||
                    pEnvelope.Sequence <= _lastAcceptedSequence)
                {
                    pReplaced = null;
                    return false;
                }
                if (!_queue.TryEnqueue(pEnvelope, out pReplaced)) return false;
                if (pReplaced != null)
                    _uncommittedSequences.Remove(pReplaced.Sequence);
                _uncommittedSequences.Add(pEnvelope.Sequence);
                _lastAcceptedSequence = pEnvelope.Sequence;
                _signal.Set();
                return true;
            }
        }

        public long CaptureBarrierSequence()
        {
            lock (_gate) return _lastAcceptedSequence;
        }

        public void Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_started) return;
                var thread = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "AW3 Historical DB Writer",
                    Priority = ThreadPriority.BelowNormal
                };
                thread.Start();
                _thread = thread;
                _started = true;
                _sinkOwnershipTransferred = true;
            }
        }

        public bool Flush(TimeSpan pTimeout, out string pError)
        {
            return Flush(pTimeout, null, out pError);
        }

        public bool Flush(TimeSpan pTimeout, Action pCompletionPump,
            out string pError)
        {
            long target = CaptureBarrierSequence();
            long deadline = Stopwatch.GetTimestamp() +
                Math.Max(0L, (long)(Stopwatch.Frequency *
                    Math.Max(0d, pTimeout.TotalSeconds)));
            while (true)
            {
                lock (_gate)
                {
                    if (_progress.IsBarrierComplete(target)) break;
                    if (_openFailed || _terminalFaulted)
                    {
                        pError = CurrentFailureLocked(target);
                        return false;
                    }
                    long remaining = deadline - Stopwatch.GetTimestamp();
                    if (remaining <= 0L)
                    {
                        pError = "historical write flush timed out; " +
                                 UncommittedDetailLocked(target,
                                     _lastError);
                        return false;
                    }
                    int maximumWait = pCompletionPump == null ? 50 : 10;
                    int waitMilliseconds = (int)Math.Min(maximumWait,
                        Math.Max(1L, remaining * 1000L / Stopwatch.Frequency));
                    Monitor.Wait(_gate, waitMilliseconds);
                }
                if (!TryPumpCompletions(pCompletionPump, out pError))
                    return false;
            }
            if (!TryPumpCompletions(pCompletionPump, out pError))
                return false;
            pError = string.Empty;
            return true;
        }

        public bool TryDequeueCompletion(out HistoricalWriteCompletion pResult)
        {
            lock (_gate)
            {
                if (_completions.Count == 0)
                {
                    pResult = null;
                    return false;
                }
                pResult = _completions.Dequeue();
                Monitor.PulseAll(_gate);
                return true;
            }
        }

        public bool TryStop(TimeSpan pTimeout, out string pError)
        {
            Thread thread;
            lock (_gate)
            {
                if (!_started)
                {
                    if (_terminalFaulted ||
                        !_progress.IsBarrierComplete(_lastAcceptedSequence))
                    {
                        pError = CurrentFailureLocked(_lastAcceptedSequence);
                        return false;
                    }
                    pError = string.Empty;
                    return true;
                }
                _stopRequested = true;
                thread = _thread;
                _signal.Set();
                Monitor.PulseAll(_gate);
            }
            if (thread != null && thread != Thread.CurrentThread)
                thread.Join(pTimeout);
            lock (_gate)
            {
                if (thread?.IsAlive == true)
                {
                    pError = "historical write stop timed out; " +
                             UncommittedDetailLocked(_lastAcceptedSequence,
                                 _lastError);
                    return false;
                }
                _thread = null;
                _started = false;
                if (_terminalFaulted ||
                    !_progress.IsBarrierComplete(_lastAcceptedSequence))
                {
                    pError = CurrentFailureLocked(_lastAcceptedSequence);
                    return false;
                }
                pError = string.Empty;
                return true;
            }
        }

        public void Stop(TimeSpan pTimeout)
        {
            if (TryStop(pTimeout, out string error)) return;
            if (WorkerAlive) throw new TimeoutException(error);
            throw new InvalidOperationException(error);
        }

        public void Dispose()
        {
            bool disposeSinkOnCaller;
            lock (_gate)
            {
                if (_disposed) return;
                disposeSinkOnCaller = !_sinkOwnershipTransferred;
            }
            if (!disposeSinkOnCaller)
            {
                TryStop(TimeSpan.FromSeconds(2), out string error);
                if (WorkerAlive) throw new TimeoutException(error);
            }
            else
            {
                _sink.Dispose();
            }
            _signal.Dispose();
            lock (_gate) _disposed = true;
        }

        private void WorkerLoop()
        {
            List<HistoricalWriteEnvelope> batch = null;
            try
            {
                try
                {
                    _sink.Open();
                }
                catch (Exception error)
                {
                    lock (_gate)
                    {
                        _openFailed = true;
                        EnqueueFailureCompletionLocked(null, error.Message);
                        SetTerminalFaultLocked(error.Message);
                    }
                    return;
                }

                int attempts = 0;
                while (true)
                {
                    lock (_gate)
                    {
                        if (batch == null)
                        {
                            batch = TakeBatchLocked();
                            if (batch.Count == 0)
                            {
                                batch = null;
                                if (_stopRequested) return;
                            }
                            else
                            {
                                _inFlightCount = batch.Count;
                            }
                        }
                    }
                    if (batch == null)
                    {
                        _signal.WaitOne(100);
                        continue;
                    }

                    HistoricalWriteBatchResult result;
                    try
                    {
                        result = _sink.Execute(batch) ??
                            HistoricalWriteBatchResult.Terminal(
                                "historical write sink returned no result");
                    }
                    catch (Exception error)
                    {
                        result = HistoricalWriteBatchResult.Terminal(
                            error.Message);
                    }
                    attempts++;

                    if (!result.IsCommitted)
                    {
                        bool terminal = result.IsTerminal ||
                            attempts >= _maxRetryAttempts;
                        lock (_gate)
                        {
                            if (terminal)
                            {
                                string reason = result.IsTerminal
                                    ? result.Error
                                    : "retry limit " + _maxRetryAttempts +
                                      " reached: " + result.Error;
                                EnqueueFailureCompletionLocked(batch, reason);
                                SetTerminalFaultLocked(reason);
                                return;
                            }
                            _lastError = result.Error;
                            Monitor.PulseAll(_gate);
                            if (_stopRequested)
                            {
                                string reason =
                                    "stop requested during retry: " +
                                    result.Error;
                                EnqueueFailureCompletionLocked(batch, reason);
                                SetTerminalFaultLocked(reason);
                                return;
                            }
                        }
                        int shift = Math.Min(6, Math.Max(0, attempts - 1));
                        int delay = _retryDelayMilliseconds * (1 << shift);
                        if (delay > 0) _signal.WaitOne(delay);
                        continue;
                    }

                    HistoricalContentRevision.Advance();
                    var completion = new HistoricalWriteCompletion(batch,
                        result.Outcomes);
                    lock (_gate)
                    {
                        while (!_stopRequested && _completions.Count >=
                               AWAsyncCapacity.CompletionBatches)
                            Monitor.Wait(_gate, 50);
                        _progress.MarkCommitted(completion.LastSequence);
                        foreach (HistoricalWriteEnvelope operation in batch)
                            _uncommittedSequences.Remove(operation.Sequence);
                        _inFlightCount = 0;
                        _lastError = string.Empty;
                        _completions.Enqueue(completion);
                        batch = null;
                        attempts = 0;
                        Monitor.PulseAll(_gate);
                    }
                }
            }
            // This is a raw background thread entry point, so an escaping
            // exception terminates the process with no dialog and no log.
            // Only the sink open and batch execute were guarded; the queue
            // bookkeeping around them could kill the process and lose the
            // pending writes without a trace. Record a terminal fault so
            // Flush reports the failure instead of blocking to its timeout,
            // and let the existing finally dispose the sink.
            catch (Exception error)
            {
                try
                {
                    lock (_gate)
                    {
                        string reason =
                            "historical write worker faulted: " +
                            error.Message;
                        EnqueueFailureCompletionLocked(batch, reason);
                        SetTerminalFaultLocked(reason);
                    }
                    AncientWarfare3.ModClass.LogWarning(
                        "[AW3 historical writer] unhandled worker fault: " +
                        error);
                }
                catch
                {
                }
            }
            finally
            {
                try { _sink.Dispose(); }
                catch (Exception error)
                {
                    lock (_gate)
                    {
                        if (!_terminalFaulted &&
                            !_progress.IsBarrierComplete(
                                _lastAcceptedSequence))
                            SetTerminalFaultLocked(
                                "historical write sink dispose failed: " +
                                error.Message);
                        else if (!_terminalFaulted)
                            _lastError =
                                "historical write sink dispose failed: " +
                                error.Message;
                    }
                }
                lock (_gate)
                {
                    Monitor.PulseAll(_gate);
                }
            }
        }

        private List<HistoricalWriteEnvelope> TakeBatchLocked()
        {
            var result = new List<HistoricalWriteEnvelope>(_batchSize);
            while (result.Count < _batchSize &&
                   _queue.TryDequeue(out HistoricalWriteEnvelope envelope))
                result.Add(envelope);
            return result;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(
                nameof(HistoricalWriteWorker));
        }

        private void SetTerminalFaultLocked(string pError)
        {
            _terminalFaulted = true;
            _lastError = UncommittedDetailLocked(_lastAcceptedSequence,
                pError);
            Monitor.PulseAll(_gate);
        }

        private void EnqueueFailureCompletionLocked(
            IReadOnlyList<HistoricalWriteEnvelope> pBatch, string pError)
        {
            int batchCount = pBatch?.Count ?? 0;
            var failed = new List<HistoricalWriteEnvelope>(batchCount +
                _queue.Count);
            if (pBatch != null)
                for (int index = 0; index < pBatch.Count; index++)
                    failed.Add(pBatch[index]);
            while (_queue.TryDequeue(out HistoricalWriteEnvelope queued))
                failed.Add(queued);
            _inFlightCount = 0;
            if (failed.Count > 0)
            {
                // A terminal worker cannot wait for the completion consumer.
                // This final aggregate may exceed the bounded queue by one.
                _completions.Enqueue(new HistoricalWriteCompletion(failed,
                    Array.Empty<object>(), false, pError));
            }
            Monitor.PulseAll(_gate);
        }

        private string CurrentFailureLocked(long pTarget)
        {
            if (_terminalFaulted && !string.IsNullOrEmpty(_lastError))
                return _lastError;
            return UncommittedDetailLocked(pTarget, _lastError);
        }

        private string UncommittedDetailLocked(long pTarget, string pReason)
        {
            long earliest = _uncommittedSequences.Count == 0
                ? pTarget
                : _uncommittedSequences.Min;
            string detail = "earliest uncommitted sequence " + earliest +
                            ", target sequence " + pTarget;
            return string.IsNullOrEmpty(pReason)
                ? detail
                : detail + ": " + pReason;
        }

        private static bool TryPumpCompletions(Action pCompletionPump,
            out string pError)
        {
            if (pCompletionPump == null)
            {
                pError = string.Empty;
                return true;
            }
            try
            {
                pCompletionPump();
                pError = string.Empty;
                return true;
            }
            catch (Exception error)
            {
                pError = "historical completion pump failed: " + error.Message;
                return false;
            }
        }
    }
}

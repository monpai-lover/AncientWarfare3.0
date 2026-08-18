using System;
using System.Threading;

namespace AncientWarfare3.core.asyncwork
{
    internal enum AWAsyncFaultPhase
    {
        Execute,
        Commit,
        FaultCallback
    }

    internal readonly struct AWAsyncFaultRecord
    {
        public AWAsyncFaultRecord(long pWorkId, string pKey,
            AWAsyncLane pLane, AWAsyncStamp pStamp,
            AWAsyncFaultPhase pPhase, Exception pError)
        {
            WorkId = pWorkId;
            Key = pKey ?? string.Empty;
            Lane = pLane;
            Stamp = pStamp;
            Phase = pPhase;
            ExceptionType = pError?.GetType().FullName ?? string.Empty;
            Message = pError?.GetBaseException().Message ?? string.Empty;
        }

        public long WorkId { get; }
        public string Key { get; }
        public AWAsyncLane Lane { get; }
        public AWAsyncStamp Stamp { get; }
        public AWAsyncFaultPhase Phase { get; }
        public string ExceptionType { get; }
        public string Message { get; }
    }

    internal readonly struct AWAsyncDiagnosticsSnapshot
    {
        public AWAsyncDiagnosticsSnapshot(long pScheduled, long pMerged,
            long pStale, long pFaulted, long pCommitted, long pCancelled,
            long pRejected, int pQueued = 0, int pActive = 0,
            int pCompletions = 0, long pWorldGeneration = 0,
            long pBackgroundCommitted = 0, int pWorkerCount = 0)
        {
            Scheduled = pScheduled;
            Merged = pMerged;
            Stale = pStale;
            Faulted = pFaulted;
            Committed = pCommitted;
            Cancelled = pCancelled;
            Rejected = pRejected;
            BackgroundCommitted = pBackgroundCommitted;
            Queued = pQueued;
            Active = pActive;
            Completions = pCompletions;
            WorldGeneration = pWorldGeneration;
            WorkerCount = pWorkerCount;
        }

        public long Scheduled { get; }
        public long Merged { get; }
        public long Stale { get; }
        public long Faulted { get; }
        public long Committed { get; }
        public long Cancelled { get; }
        public long Rejected { get; }
        public long BackgroundCommitted { get; }
        public int Queued { get; }
        public int Active { get; }
        public int Completions { get; }
        public long WorldGeneration { get; }
        public int WorkerCount { get; }

        public AWAsyncDiagnosticsSnapshot WithRuntime(int pQueued,
            int pActive, int pCompletions, long pWorldGeneration,
            int pWorkerCount = 0)
        {
            return new AWAsyncDiagnosticsSnapshot(Scheduled, Merged, Stale,
                Faulted, Committed, Cancelled, Rejected, pQueued, pActive,
                pCompletions, pWorldGeneration, BackgroundCommitted,
                pWorkerCount);
        }
    }

    internal readonly struct AWAsyncCommitTimingSnapshot
    {
        internal AWAsyncCommitTimingSnapshot(string pSlowestKey,
            AWAsyncLane pSlowestLane, long pSlowestTicks,
            long pTotalTicks, int pCalls)
        {
            SlowestKey = string.IsNullOrEmpty(pSlowestKey) ? "none" : pSlowestKey;
            SlowestLane = pSlowestLane;
            SlowestTicks = Math.Max(0L, pSlowestTicks);
            TotalTicks = Math.Max(0L, pTotalTicks);
            Calls = Math.Max(0, pCalls);
        }

        internal string SlowestKey { get; }
        internal AWAsyncLane SlowestLane { get; }
        internal long SlowestTicks { get; }
        internal long TotalTicks { get; }
        internal int Calls { get; }
    }

    internal sealed class AWAsyncDiagnostics
    {
        private readonly object _commitTimingGate = new object();
        private long _scheduled;
        private long _merged;
        private long _stale;
        private long _faulted;
        private long _committed;
        private long _cancelled;
        private long _rejected;
        private long _backgroundCommitted;
        private string _slowestMainThreadCommitKey = "none";
        private AWAsyncLane _slowestMainThreadCommitLane;
        private long _slowestMainThreadCommitTicks;
        private long _mainThreadCommitTicks;
        private int _mainThreadCommitCalls;

        public void RecordScheduled() => Interlocked.Increment(ref _scheduled);
        public void RecordMerged() => Interlocked.Increment(ref _merged);
        public void RecordStale() => Interlocked.Increment(ref _stale);
        public void RecordFaulted() => Interlocked.Increment(ref _faulted);
        public void RecordCommitted() => Interlocked.Increment(ref _committed);
        public void RecordCancelled() => Interlocked.Increment(ref _cancelled);
        public void RecordRejected() => Interlocked.Increment(ref _rejected);
        public void RecordBackgroundCommitted() =>
            Interlocked.Increment(ref _backgroundCommitted);

        public void RecordMainThreadCommit(string pKey, AWAsyncLane pLane,
            long pElapsedTicks)
        {
            long elapsed = Math.Max(0L, pElapsedTicks);
            lock (_commitTimingGate)
            {
                _mainThreadCommitTicks += elapsed;
                _mainThreadCommitCalls++;
                if (elapsed <= _slowestMainThreadCommitTicks) return;
                _slowestMainThreadCommitTicks = elapsed;
                _slowestMainThreadCommitKey = string.IsNullOrEmpty(pKey)
                    ? "none" : pKey;
                _slowestMainThreadCommitLane = pLane;
            }
        }

        public AWAsyncCommitTimingSnapshot TakeMainThreadCommitTiming()
        {
            lock (_commitTimingGate)
            {
                var snapshot = new AWAsyncCommitTimingSnapshot(
                    _slowestMainThreadCommitKey,
                    _slowestMainThreadCommitLane,
                    _slowestMainThreadCommitTicks,
                    _mainThreadCommitTicks,
                    _mainThreadCommitCalls);
                _slowestMainThreadCommitKey = "none";
                _slowestMainThreadCommitLane = default;
                _slowestMainThreadCommitTicks = 0L;
                _mainThreadCommitTicks = 0L;
                _mainThreadCommitCalls = 0;
                return snapshot;
            }
        }

        public AWAsyncDiagnosticsSnapshot Snapshot()
        {
            return new AWAsyncDiagnosticsSnapshot(
                Interlocked.Read(ref _scheduled),
                Interlocked.Read(ref _merged),
                Interlocked.Read(ref _stale),
                Interlocked.Read(ref _faulted),
                Interlocked.Read(ref _committed),
                Interlocked.Read(ref _cancelled),
                Interlocked.Read(ref _rejected),
                pBackgroundCommitted: Interlocked.Read(
                    ref _backgroundCommitted));
        }
    }
}

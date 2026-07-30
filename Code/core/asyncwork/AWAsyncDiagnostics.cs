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
            int pCompletions = 0, long pWorldGeneration = 0)
        {
            Scheduled = pScheduled;
            Merged = pMerged;
            Stale = pStale;
            Faulted = pFaulted;
            Committed = pCommitted;
            Cancelled = pCancelled;
            Rejected = pRejected;
            Queued = pQueued;
            Active = pActive;
            Completions = pCompletions;
            WorldGeneration = pWorldGeneration;
        }

        public long Scheduled { get; }
        public long Merged { get; }
        public long Stale { get; }
        public long Faulted { get; }
        public long Committed { get; }
        public long Cancelled { get; }
        public long Rejected { get; }
        public int Queued { get; }
        public int Active { get; }
        public int Completions { get; }
        public long WorldGeneration { get; }

        public AWAsyncDiagnosticsSnapshot WithRuntime(int pQueued,
            int pActive, int pCompletions, long pWorldGeneration)
        {
            return new AWAsyncDiagnosticsSnapshot(Scheduled, Merged, Stale,
                Faulted, Committed, Cancelled, Rejected, pQueued, pActive,
                pCompletions, pWorldGeneration);
        }
    }

    internal sealed class AWAsyncDiagnostics
    {
        private long _scheduled;
        private long _merged;
        private long _stale;
        private long _faulted;
        private long _committed;
        private long _cancelled;
        private long _rejected;

        public void RecordScheduled() => Interlocked.Increment(ref _scheduled);
        public void RecordMerged() => Interlocked.Increment(ref _merged);
        public void RecordStale() => Interlocked.Increment(ref _stale);
        public void RecordFaulted() => Interlocked.Increment(ref _faulted);
        public void RecordCommitted() => Interlocked.Increment(ref _committed);
        public void RecordCancelled() => Interlocked.Increment(ref _cancelled);
        public void RecordRejected() => Interlocked.Increment(ref _rejected);

        public AWAsyncDiagnosticsSnapshot Snapshot()
        {
            return new AWAsyncDiagnosticsSnapshot(
                Interlocked.Read(ref _scheduled),
                Interlocked.Read(ref _merged),
                Interlocked.Read(ref _stale),
                Interlocked.Read(ref _faulted),
                Interlocked.Read(ref _committed),
                Interlocked.Read(ref _cancelled),
                Interlocked.Read(ref _rejected));
        }
    }
}

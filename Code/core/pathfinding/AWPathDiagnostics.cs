using System;
using System.Collections.Concurrent;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    internal readonly struct AWPathDiagnosticEvent
    {
        public AWPathDiagnosticEvent(long pActorId, AWPathFailureReason pReason, string pMessage)
        {
            ActorId = pActorId;
            Reason = pReason;
            Message = pMessage ?? "";
        }

        public long ActorId { get; }
        public AWPathFailureReason Reason { get; }
        public string Message { get; }
    }

    internal sealed class AWPathDiagnostics
    {
        private readonly ConcurrentQueue<AWPathDiagnosticEvent> _events =
            new ConcurrentQueue<AWPathDiagnosticEvent>();
        private long _generated;
        private long _reused;
        private long _cancelled;
        private long _completed;
        private long _failed;
        private long _expandedNodes;
        private long _fallbackSearches;
        private long _staleSteps;
        private bool _queuePressureReported;

        public long Generated => Interlocked.Read(ref _generated);
        public long Reused => Interlocked.Read(ref _reused);
        public long Cancelled => Interlocked.Read(ref _cancelled);
        public long Completed => Interlocked.Read(ref _completed);
        public long Failed => Interlocked.Read(ref _failed);
        public long ExpandedNodes => Interlocked.Read(ref _expandedNodes);
        public long FallbackSearches => Interlocked.Read(ref _fallbackSearches);
        public long StaleSteps => Interlocked.Read(ref _staleSteps);

        public void OnGenerated() => Interlocked.Increment(ref _generated);
        public void OnReused() => Interlocked.Increment(ref _reused);
        public void OnCancelled() => Interlocked.Increment(ref _cancelled);
        public void OnCompleted() => Interlocked.Increment(ref _completed);
        public void OnFailed() => Interlocked.Increment(ref _failed);
        public void AddExpandedNodes(int pCount) => Interlocked.Add(ref _expandedNodes, pCount);
        public void OnFallback() => Interlocked.Increment(ref _fallbackSearches);
        public void OnStaleStep() => Interlocked.Increment(ref _staleSteps);
        public void Enqueue(AWPathDiagnosticEvent pEvent) => _events.Enqueue(pEvent);
        public bool TryDequeue(out AWPathDiagnosticEvent pEvent) => _events.TryDequeue(out pEvent);

        public void DrainAndMaybeLog(int pBudget, int pQueueDepth, Action<string> pLogger)
        {
            int drained = 0;
            AWPathDiagnosticEvent latest = default;
            while (drained < Math.Max(0, pBudget) && _events.TryDequeue(out latest)) drained++;

            bool queuePressure = pQueueDepth > 2000;
            bool reportPressure = queuePressure && !_queuePressureReported;
            if (!queuePressure && pQueueDepth < 1000) _queuePressureReported = false;
            else if (queuePressure) _queuePressureReported = true;
            if (drained == 0 && !reportPressure) return;

            string detail = drained > 0
                ? ", latest actor=" + latest.ActorId + ", reason=" + latest.Reason +
                  ", error=" + latest.Message
                : "";
            pLogger?.Invoke("AW3 pathfinding diagnostics: queue=" + pQueueDepth +
                ", errors=" + drained + ", generated=" + Generated + ", completed=" +
                Completed + ", failed=" + Failed + detail);
        }
    }
}

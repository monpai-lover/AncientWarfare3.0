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
        private long _reusedRunning;
        private long _cancelled;
        private long _completed;
        private long _failed;
        private long _expandedNodes;
        private long _fallbackSearches;
        private long _staleSteps;
        private long _fastSteps;
        private long _vanillaSteps;
        private long _traversalChunksCaptured;
        private long _traversalBuildsPublished;
        private long _traversalBuildsStale;
        private long _traversalSyncFallbacks;
        private long _operationalRequests;
        private long _essentialTravelRequests;
        private long _ambientRequests;
        private long _replacedPending;
        private long _replacedRunning;
        private long _rejected;
        private int _operationalQueueHighWater;
        private int _essentialQueueHighWater;
        private int _ambientQueueHighWater;
        private bool _queuePressureReported;

        public long Generated => Interlocked.Read(ref _generated);
        public long Reused => Interlocked.Read(ref _reused);
        public long ReusedRunning => Interlocked.Read(ref _reusedRunning);
        public long Cancelled => Interlocked.Read(ref _cancelled);
        public long Completed => Interlocked.Read(ref _completed);
        public long Failed => Interlocked.Read(ref _failed);
        public long ExpandedNodes => Interlocked.Read(ref _expandedNodes);
        public long FallbackSearches => Interlocked.Read(ref _fallbackSearches);
        public long StaleSteps => Interlocked.Read(ref _staleSteps);
        public long FastSteps => Interlocked.Read(ref _fastSteps);
        public long VanillaSteps => Interlocked.Read(ref _vanillaSteps);
        public long TraversalChunksCaptured =>
            Interlocked.Read(ref _traversalChunksCaptured);
        public long TraversalBuildsPublished =>
            Interlocked.Read(ref _traversalBuildsPublished);
        public long TraversalBuildsStale =>
            Interlocked.Read(ref _traversalBuildsStale);
        public long TraversalSyncFallbacks =>
            Interlocked.Read(ref _traversalSyncFallbacks);
        public long OperationalRequests =>
            Interlocked.Read(ref _operationalRequests);
        public long EssentialTravelRequests =>
            Interlocked.Read(ref _essentialTravelRequests);
        public long AmbientRequests => Interlocked.Read(ref _ambientRequests);
        public long ReplacedPending => Interlocked.Read(ref _replacedPending);
        public long ReplacedRunning => Interlocked.Read(ref _replacedRunning);
        public long Rejected => Interlocked.Read(ref _rejected);
        public int OperationalQueueHighWater =>
            Volatile.Read(ref _operationalQueueHighWater);
        public int EssentialQueueHighWater =>
            Volatile.Read(ref _essentialQueueHighWater);
        public int AmbientQueueHighWater =>
            Volatile.Read(ref _ambientQueueHighWater);

        public void OnGenerated() => Interlocked.Increment(ref _generated);
        public void OnReused() => Interlocked.Increment(ref _reused);
        public void OnReusedRunning() =>
            Interlocked.Increment(ref _reusedRunning);
        public void OnSubmission(AWPathWorkClass pWorkClass,
            AWPathSubmissionDisposition pDisposition)
        {
            switch (pWorkClass)
            {
                case AWPathWorkClass.Operational:
                    Interlocked.Increment(ref _operationalRequests);
                    break;
                case AWPathWorkClass.EssentialTravel:
                    Interlocked.Increment(ref _essentialTravelRequests);
                    break;
                default:
                    Interlocked.Increment(ref _ambientRequests);
                    break;
            }

            switch (pDisposition)
            {
                case AWPathSubmissionDisposition.Reused:
                    OnReused();
                    break;
                case AWPathSubmissionDisposition.Submitted:
                    OnGenerated();
                    break;
                case AWPathSubmissionDisposition.ReplacedPending:
                    Interlocked.Increment(ref _replacedPending);
                    OnGenerated();
                    break;
                case AWPathSubmissionDisposition.ReplacedRunning:
                    Interlocked.Increment(ref _replacedRunning);
                    OnGenerated();
                    break;
                default:
                    Interlocked.Increment(ref _rejected);
                    break;
            }
        }
        public void ObserveQueue(AWPathQueueSnapshot pSnapshot)
        {
            ObserveHighWater(ref _operationalQueueHighWater,
                pSnapshot.OperationalQueued);
            ObserveHighWater(ref _essentialQueueHighWater,
                pSnapshot.EssentialQueued);
            ObserveHighWater(ref _ambientQueueHighWater,
                pSnapshot.AmbientQueued);
        }
        public void OnCancelled() => Interlocked.Increment(ref _cancelled);
        public void OnCompleted() => Interlocked.Increment(ref _completed);
        public void OnFailed() => Interlocked.Increment(ref _failed);
        public void AddExpandedNodes(int pCount) => Interlocked.Add(ref _expandedNodes, pCount);
        public void OnFallback() => Interlocked.Increment(ref _fallbackSearches);
        public void OnStaleStep() => Interlocked.Increment(ref _staleSteps);
        public void OnFastStep() => Interlocked.Increment(ref _fastSteps);
        public void OnVanillaStep() => Interlocked.Increment(ref _vanillaSteps);
        public void AddTraversalChunksCaptured(int pCount) =>
            Interlocked.Add(ref _traversalChunksCaptured, Math.Max(0, pCount));
        public void OnTraversalBuildPublished() =>
            Interlocked.Increment(ref _traversalBuildsPublished);
        public void OnTraversalBuildStale() =>
            Interlocked.Increment(ref _traversalBuildsStale);
        public void OnTraversalSyncFallback() =>
            Interlocked.Increment(ref _traversalSyncFallbacks);
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
                Completed + ", failed=" + Failed + ", traversal_captured=" +
                TraversalChunksCaptured + ", traversal_published=" +
                TraversalBuildsPublished + ", traversal_stale=" +
                TraversalBuildsStale + ", traversal_sync_fallback=" +
                TraversalSyncFallbacks + detail);
        }

        private static void ObserveHighWater(ref int pTarget, int pValue)
        {
            int value = Math.Max(0, pValue);
            int current = Volatile.Read(ref pTarget);
            while (value > current)
            {
                int observed = Interlocked.CompareExchange(ref pTarget,
                    value, current);
                if (observed == current) return;
                current = observed;
            }
        }
    }
}

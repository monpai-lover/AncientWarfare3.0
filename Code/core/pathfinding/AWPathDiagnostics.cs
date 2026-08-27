using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly long[] _failedByReason =
            new long[Enum.GetValues(typeof(AWPathFailureReason)).Length];
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
        private long _queueWaitTicks;
        private long _queueWaitSamples;
        private long _queueWaitMaxTicks;
        private long _firstStepTicks;
        private long _firstStepSamples;
        private long _firstStepMaxTicks;
        private long _dockRequests;
        private long _boatRetries;
        private long _rtsSharedRouteReuses;
        private long _straightSegments;
        private long _memberCorrections;
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
        public long QueueWaitTicks => Interlocked.Read(ref _queueWaitTicks);
        public long QueueWaitSamples => Interlocked.Read(ref _queueWaitSamples);
        public long QueueWaitMaxTicks => Interlocked.Read(ref _queueWaitMaxTicks);
        public long FirstStepTicks => Interlocked.Read(ref _firstStepTicks);
        public long FirstStepSamples => Interlocked.Read(ref _firstStepSamples);
        public long FirstStepMaxTicks => Interlocked.Read(ref _firstStepMaxTicks);
        public long DockRequests => Interlocked.Read(ref _dockRequests);
        public long BoatRetries => Interlocked.Read(ref _boatRetries);
        public long RtsSharedRouteReuses => Interlocked.Read(ref _rtsSharedRouteReuses);
        public long StraightSegments => Interlocked.Read(ref _straightSegments);
        public long MemberCorrections => Interlocked.Read(ref _memberCorrections);
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
        public void OnFailed() => OnFailed(AWPathFailureReason.None);
        public void OnFailed(AWPathFailureReason pReason)
        {
            Interlocked.Increment(ref _failed);
            int index = (int)pReason;
            if (index >= 0 && index < _failedByReason.Length)
                Interlocked.Increment(ref _failedByReason[index]);
        }
        public string FailedByReason()
        {
            var values = new List<string>();
            for (int index = 0; index < _failedByReason.Length; index++)
            {
                long count = Interlocked.Read(ref _failedByReason[index]);
                if (count == 0) continue;
                values.Add(((AWPathFailureReason)index) + "=" + count);
            }
            return values.Count == 0 ? "none" : string.Join(",", values);
        }
        public void AddExpandedNodes(int pCount) => Interlocked.Add(ref _expandedNodes, pCount);
        public void OnFallback() => Interlocked.Increment(ref _fallbackSearches);
        public void OnStaleStep() => Interlocked.Increment(ref _staleSteps);
        public void OnFastStep() => Interlocked.Increment(ref _fastSteps);
        public void OnVanillaStep() => Interlocked.Increment(ref _vanillaSteps);
        public void OnDockRequest() => Interlocked.Increment(ref _dockRequests);
        public void OnBoatRetry() => Interlocked.Increment(ref _boatRetries);
        public void OnRtsSharedRouteReuse() => Interlocked.Increment(ref _rtsSharedRouteReuses);
        public void OnStraightSegment() => Interlocked.Increment(ref _straightSegments);
        public void OnMemberCorrection() => Interlocked.Increment(ref _memberCorrections);
        public void OnDequeued(AWPathWorkPriority pPriority, long pEnqueuedAt)
        {
            long elapsed = Math.Max(0L, Stopwatch.GetTimestamp() - pEnqueuedAt);
            Interlocked.Add(ref _queueWaitTicks, elapsed);
            Interlocked.Increment(ref _queueWaitSamples);
            UpdateMaximum(ref _queueWaitMaxTicks, elapsed);
            if (pPriority == AWPathWorkPriority.Initial)
            {
                Interlocked.Add(ref _firstStepTicks, elapsed);
                Interlocked.Increment(ref _firstStepSamples);
                UpdateMaximum(ref _firstStepMaxTicks, elapsed);
            }
        }
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

        public void DrainAndMaybeLog(int pBudget, int pQueueDepth,
            int pActiveSessions = 0, int pWorkerCount = 0,
            long pStaleWorkCount = 0, Action<string> pLogger = null)
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
                ", active_sessions=" + pActiveSessions +
                ", workers=" + pWorkerCount +
                ", stale_work=" + pStaleWorkCount +
                ", errors=" + drained + ", generated=" + Generated + ", completed=" +
                Completed + ", failed=" + Failed + ", traversal_captured=" +
                TraversalChunksCaptured + ", traversal_published=" +
                TraversalBuildsPublished + ", traversal_stale=" +
                TraversalBuildsStale + ", traversal_sync_fallback=" +
                TraversalSyncFallbacks + ", queue_wait_avg_ms=" +
                AverageMilliseconds(QueueWaitTicks, QueueWaitSamples) +
                ", queue_wait_max_ms=" + Milliseconds(QueueWaitMaxTicks) +
                ", first_step_avg_ms=" +
                AverageMilliseconds(FirstStepTicks, FirstStepSamples) +
                ", first_step_max_ms=" + Milliseconds(FirstStepMaxTicks) +
                ", dock_requests=" + DockRequests +
                ", boat_retries=" + BoatRetries +
                ", rts_shared_route_reuse=" + RtsSharedRouteReuses +
                ", member_corrections=" + MemberCorrections + detail);
        }

        private static double Milliseconds(long pTicks)
        {
            return pTicks <= 0 ? 0d : pTicks * 1000d / Stopwatch.Frequency;
        }

        private static double AverageMilliseconds(long pTicks, long pSamples)
        {
            return pSamples <= 0 ? 0d : Milliseconds(pTicks / pSamples);
        }

        private static void UpdateMaximum(ref long pTarget, long pValue)
        {
            long current = Interlocked.Read(ref pTarget);
            while (pValue > current)
            {
                long observed = Interlocked.CompareExchange(ref pTarget,
                    pValue, current);
                if (observed == current) return;
                current = observed;
            }
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

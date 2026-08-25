using System;
using System.Diagnostics;

namespace AncientWarfare3.core.pathfinding
{
    internal enum AWPathWorkPriority
    {
        Starved,
        Initial,
        Continuation
    }

    internal static class AWPathQueueFairnessRules
    {
        internal const int MaximumConsecutiveStarved = 8;

        internal static AWPathWorkPriority Select(bool hasStarved,
            bool hasInitial, bool hasContinuation, int consecutiveStarved)
        {
            if (hasStarved &&
                (!hasInitial && !hasContinuation ||
                 consecutiveStarved < MaximumConsecutiveStarved))
                return AWPathWorkPriority.Starved;
            if (hasInitial) return AWPathWorkPriority.Initial;
            if (hasContinuation) return AWPathWorkPriority.Continuation;
            return AWPathWorkPriority.Starved;
        }
    }

    internal readonly struct AWScheduledPathWork
    {
        internal AWScheduledPathWork(AWPathAgentKey pOwnerKey,
            int pQueueVersion, AWPathWorkPriority pPriority)
        {
            OwnerKey = pOwnerKey;
            OwnerId = pOwnerKey.AgentId;
            QueueVersion = pQueueVersion;
            Priority = pPriority;
            EnqueuedAt = Stopwatch.GetTimestamp();
#if !AW3_RULES_TESTS
            ProfilerSession = null;
            ProfilerEnqueuedAt = 0L;
#endif
        }

        internal AWScheduledPathWork(long pOwnerId, int pQueueVersion,
            AWPathWorkPriority pPriority)
            : this(new AWPathAgentKey(AWPathWorldKey.MainWorld(0L),
                pOwnerId), pQueueVersion, pPriority)
        {
        }

        internal AWPathAgentKey OwnerKey { get; }
        internal long OwnerId { get; }
        internal int QueueVersion { get; }
        internal AWPathWorkPriority Priority { get; }
        internal long EnqueuedAt { get; }
#if !AW3_RULES_TESTS
        internal AWPathfindingProfiler.AWPathfindingProfilerSession ProfilerSession { get; }
        internal long ProfilerEnqueuedAt { get; }

        internal AWScheduledPathWork WithProfiler(
            AWPathfindingProfiler.AWPathfindingProfilerSession pSession,
            long pEnqueuedAt)
        {
            return new AWScheduledPathWork(OwnerKey, QueueVersion, Priority,
                EnqueuedAt, pSession, pEnqueuedAt);
        }

        private AWScheduledPathWork(AWPathAgentKey pOwnerKey,
            int pQueueVersion, AWPathWorkPriority pPriority,
            long pEnqueuedAt,
            AWPathfindingProfiler.AWPathfindingProfilerSession pSession,
            long pProfilerEnqueuedAt)
        {
            OwnerKey = pOwnerKey;
            OwnerId = pOwnerKey.AgentId;
            QueueVersion = pQueueVersion;
            Priority = pPriority;
            EnqueuedAt = pEnqueuedAt;
            ProfilerSession = pSession;
            ProfilerEnqueuedAt = pProfilerEnqueuedAt;
        }
#endif
    }

    internal sealed class AWPathSession
    {
        private readonly object _gate = new object();
        private int _queueVersion;
        private bool _queued;
        private bool _running;
        private bool _cancelled;
        private bool _replacementPending;
        private bool _hasMoreSegments = true;
        private bool _rescheduleRequested;
        private AWPathWorkPriority _requestedPriority =
            AWPathWorkPriority.Continuation;

        internal AWPathSession(AWPathAgentKey pAgentKey,
            bool pHasMoreSegments = true)
        {
            AgentKey = pAgentKey;
            OwnerId = pAgentKey.AgentId;
            _hasMoreSegments = pHasMoreSegments;
        }

        internal AWPathSession(long pOwnerId)
            : this(new AWPathAgentKey(AWPathWorldKey.MainWorld(0L),
                pOwnerId))
        {
        }

        internal AWPathAgentKey AgentKey { get; }
        internal long OwnerId { get; }

        internal bool TrySchedule(AWPathWorkPriority pPriority,
            out AWScheduledPathWork pWork)
        {
            lock (_gate)
            {
                pWork = default;
                if (_cancelled || !_hasMoreSegments) return false;
                if (_running)
                {
                    _rescheduleRequested = true;
                    if (pPriority < _requestedPriority)
                        _requestedPriority = pPriority;
                    return false;
                }
                if (_queued) return false;
                _queued = true;
                _queueVersion++;
                pWork = new AWScheduledPathWork(AgentKey, _queueVersion,
                    pPriority);
                return true;
            }
        }

        internal bool TryBeginWork(int pQueueVersion)
        {
            lock (_gate)
            {
                if (_cancelled || _running || !_queued ||
                    _queueVersion != pQueueVersion) return false;
                _queued = false;
                _running = true;
                _rescheduleRequested = false;
                _requestedPriority = AWPathWorkPriority.Continuation;
                return true;
            }
        }

        internal bool Replace()
        {
            lock (_gate)
            {
                if (_cancelled) return false;
                if (_running)
                {
                    _replacementPending = true;
                    return true;
                }
                _queued = false;
                _queueVersion++;
                // A queued token is now stale. The new request can be
                // scheduled immediately; only a running request retains a
                // deferred replacement.
                _replacementPending = false;
                _hasMoreSegments = true;
                _rescheduleRequested = false;
                _requestedPriority = AWPathWorkPriority.Continuation;
                return true;
            }
        }

        internal bool CompleteWork(bool pHasMoreSegments,
            bool pScheduleWhenEmpty, out AWScheduledPathWork pReplacement)
        {
            lock (_gate)
            {
                pReplacement = default;
                if (!_running) return false;
                _running = false;
                if (_cancelled) return false;
                _hasMoreSegments = pHasMoreSegments;
                bool replacementPending = _replacementPending;
                bool scheduleRequested = _rescheduleRequested;
                AWPathWorkPriority requestedPriority = _requestedPriority;
                _rescheduleRequested = false;
                _requestedPriority = AWPathWorkPriority.Continuation;
                if (!replacementPending && !_hasMoreSegments) return false;
                if (!replacementPending && !pScheduleWhenEmpty &&
                    !scheduleRequested) return false;
                _replacementPending = false;
                _queued = true;
                _queueVersion++;
                pReplacement = new AWScheduledPathWork(AgentKey,
                    _queueVersion, replacementPending
                        ? AWPathWorkPriority.Initial
                        : pScheduleWhenEmpty
                            ? AWPathWorkPriority.Continuation
                            : requestedPriority);
                return true;
            }
        }

        internal bool CompleteWork(out AWScheduledPathWork pReplacement)
        {
            return CompleteWork(false, false, out pReplacement);
        }

        internal void Cancel()
        {
            lock (_gate)
            {
                _cancelled = true;
                _queued = false;
                _replacementPending = false;
                _rescheduleRequested = false;
            }
        }

    internal bool IsCancelled
    {
        get
        {
            lock (_gate) return _cancelled;
        }
    }
}
}

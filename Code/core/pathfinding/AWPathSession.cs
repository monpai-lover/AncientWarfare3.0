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
        internal AWScheduledPathWork(long pOwnerId, int pQueueVersion,
            AWPathWorkPriority pPriority,
            AWPathWorkClass pWorkClass = AWPathWorkClass.Ambient)
        {
            OwnerId = pOwnerId;
            QueueVersion = pQueueVersion;
            Priority = pPriority;
            WorkClass = pWorkClass;
            EnqueuedAt = Stopwatch.GetTimestamp();
        }

        internal long OwnerId { get; }
        internal int QueueVersion { get; }
        internal AWPathWorkPriority Priority { get; }
        internal AWPathWorkClass WorkClass { get; }
        internal long EnqueuedAt { get; }

        internal AWScheduledPathWork WithWorkClass(
            AWPathWorkClass pWorkClass)
        {
            return new AWScheduledPathWork(OwnerId, QueueVersion, Priority,
                pWorkClass);
        }
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

        internal AWPathSession(long pOwnerId)
        {
            OwnerId = pOwnerId;
        }

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
                pWork = new AWScheduledPathWork(OwnerId, _queueVersion,
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
                pReplacement = new AWScheduledPathWork(OwnerId,
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
    }
}

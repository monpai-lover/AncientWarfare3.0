using System;

namespace AncientWarfare3.core.pathfinding
{
    internal enum AWPathWorkPriority
    {
        Starved,
        Initial,
        Continuation
    }

    internal readonly struct AWScheduledPathWork
    {
        internal AWScheduledPathWork(long pOwnerId, int pQueueVersion,
            AWPathWorkPriority pPriority)
        {
            OwnerId = pOwnerId;
            QueueVersion = pQueueVersion;
            Priority = pPriority;
        }

        internal long OwnerId { get; }
        internal int QueueVersion { get; }
        internal AWPathWorkPriority Priority { get; }
    }

    internal sealed class AWPathSession
    {
        private readonly object _gate = new object();
        private int _queueVersion;
        private bool _queued;
        private bool _running;
        private bool _cancelled;
        private bool _replacementPending;

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
                if (_cancelled || _running || _queued) return false;
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
                return true;
            }
        }

        internal bool CompleteWork(out AWScheduledPathWork pReplacement)
        {
            lock (_gate)
            {
                pReplacement = default;
                if (!_running) return false;
                _running = false;
                if (_cancelled || !_replacementPending) return false;
                _replacementPending = false;
                _queued = true;
                _queueVersion++;
                pReplacement = new AWScheduledPathWork(OwnerId,
                    _queueVersion, AWPathWorkPriority.Initial);
                return true;
            }
        }

        internal void Cancel()
        {
            lock (_gate)
            {
                _cancelled = true;
                _queued = false;
                _replacementPending = false;
            }
        }
    }
}

using System;
using System.Threading;

namespace AncientWarfare3.api.multiplayer
{
    public static class AW3MultiplayerReplicaScope
    {
        private static readonly object Gate = new object();
        private static object _sessionOwner;
        private static long _sessionRevision;

        [ThreadStatic]
        private static int _applyDepth;

        public static bool IsReplicaSession
        {
            get
            {
                lock (Gate) return _sessionOwner != null;
            }
        }

        public static bool IsApplying => _applyDepth > 0;

        public static long SessionRevision
        {
            get
            {
                lock (Gate) return _sessionRevision;
            }
        }

        public static bool Activate(object owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            lock (Gate)
            {
                if (_sessionOwner == null)
                {
                    long nextRevision = checked(_sessionRevision + 1);
                    _sessionOwner = owner;
                    _sessionRevision = nextRevision;
                    return true;
                }
                return ReferenceEquals(_sessionOwner, owner);
            }
        }

        public static bool Deactivate(object owner)
        {
            if (owner == null) return false;
            lock (Gate)
            {
                if (!ReferenceEquals(_sessionOwner, owner)) return false;
                long nextRevision = checked(_sessionRevision + 1);
                _sessionOwner = null;
                _sessionRevision = nextRevision;
                return true;
            }
        }

        public static IDisposable EnterApply()
        {
            _applyDepth = checked(_applyDepth + 1);
            return new ApplyLease(Thread.CurrentThread.ManagedThreadId);
        }

        private sealed class ApplyLease : IDisposable
        {
            private readonly int _threadId;
            private bool _disposed;

            internal ApplyLease(int threadId)
            {
                _threadId = threadId;
            }

            public void Dispose()
            {
                if (_disposed) return;
                if (Thread.CurrentThread.ManagedThreadId != _threadId)
                    throw new InvalidOperationException(
                        "Replica apply scope must close on its owner thread.");
                if (_applyDepth <= 0)
                    throw new InvalidOperationException(
                        "Replica apply scope depth is invalid.");
                _applyDepth--;
                _disposed = true;
            }
        }
    }
}

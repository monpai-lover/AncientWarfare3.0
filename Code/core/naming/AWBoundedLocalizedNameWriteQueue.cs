using System;
using System.Collections.Generic;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.naming
{
    internal static class AWLocalizedNameMigrationLimits
    {
        internal const int DefaultBatchSize = 24;
        internal const int PendingWriteCapacity = 4096;
    }

    internal sealed class AWLocalizedNamePendingWrite
    {
        internal AWLocalizedNamePendingWrite(string pMetaType, long pObjectId,
            AWLocalizedNameIdentitySnapshot pSnapshot)
        {
            MetaType = pMetaType;
            ObjectId = pObjectId;
            Snapshot = pSnapshot;
        }

        internal string MetaType { get; }
        internal long ObjectId { get; }
        internal AWLocalizedNameIdentitySnapshot Snapshot { get; }
    }

    internal sealed class AWBoundedLocalizedNameWriteQueue
    {
        private readonly int _capacity;
        private readonly LinkedList<AWLocalizedNamePendingWrite> _pending =
            new LinkedList<AWLocalizedNamePendingWrite>();
        private readonly Dictionary<Identity, LinkedListNode<
            AWLocalizedNamePendingWrite>> _byIdentity =
            new Dictionary<Identity, LinkedListNode<
                AWLocalizedNamePendingWrite>>();

        internal AWBoundedLocalizedNameWriteQueue(int pCapacity)
        {
            if (pCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(pCapacity));
            _capacity = pCapacity;
        }

        internal int Count => _pending.Count;
        internal bool FullRescanRequired { get; private set; }

        internal bool Enqueue(string pMetaType, long pObjectId,
            AWLocalizedNameIdentitySnapshot pSnapshot)
        {
            if (pSnapshot == null || pObjectId < 0 ||
                !LocalizedNameIdentitySchema.TryNormalizeMetaType(pMetaType,
                    out string metaType)) return false;

            var identity = new Identity(metaType, pObjectId);
            var pending = new AWLocalizedNamePendingWrite(metaType, pObjectId,
                pSnapshot);
            if (_byIdentity.TryGetValue(identity,
                    out LinkedListNode<AWLocalizedNamePendingWrite> existing))
            {
                existing.Value = pending;
                return true;
            }
            if (_pending.Count >= _capacity)
            {
                FullRescanRequired = true;
                return false;
            }

            LinkedListNode<AWLocalizedNamePendingWrite> node =
                _pending.AddLast(pending);
            _byIdentity[identity] = node;
            return true;
        }

        internal bool TryDequeue(out AWLocalizedNamePendingWrite pPending)
        {
            LinkedListNode<AWLocalizedNamePendingWrite> first = _pending.First;
            if (first == null)
            {
                pPending = null;
                return false;
            }

            pPending = first.Value;
            _pending.RemoveFirst();
            _byIdentity.Remove(new Identity(pPending.MetaType,
                pPending.ObjectId));
            return true;
        }

        internal int Flush(int pBudget,
            Func<AWLocalizedNamePendingWrite, bool> pWrite)
        {
            if (pBudget <= 0 || pWrite == null) return 0;
            int completed = 0;
            while (completed < pBudget && TryDequeue(
                       out AWLocalizedNamePendingWrite pending))
            {
                bool succeeded;
                try { succeeded = pWrite(pending); }
                catch
                {
                    RequeueFirst(pending);
                    throw;
                }

                if (!succeeded)
                {
                    RequeueFirst(pending);
                    break;
                }
                completed++;
            }
            return completed;
        }

        internal void Clear()
        {
            _pending.Clear();
            _byIdentity.Clear();
            FullRescanRequired = false;
        }

        internal void ClearFullRescanRequired()
        {
            FullRescanRequired = false;
        }

        private void RequeueFirst(AWLocalizedNamePendingWrite pPending)
        {
            var identity = new Identity(pPending.MetaType, pPending.ObjectId);
            if (_byIdentity.TryGetValue(identity,
                    out LinkedListNode<AWLocalizedNamePendingWrite> existing))
            {
                existing.Value = pPending;
                _pending.Remove(existing);
                _pending.AddFirst(existing);
                return;
            }
            LinkedListNode<AWLocalizedNamePendingWrite> node =
                _pending.AddFirst(pPending);
            _byIdentity[identity] = node;
        }

        private readonly struct Identity : IEquatable<Identity>
        {
            internal Identity(string pMetaType, long pObjectId)
            {
                MetaType = pMetaType;
                ObjectId = pObjectId;
            }

            private string MetaType { get; }
            private long ObjectId { get; }

            public bool Equals(Identity pOther)
            {
                return ObjectId == pOther.ObjectId && string.Equals(MetaType,
                    pOther.MetaType, StringComparison.Ordinal);
            }

            public override bool Equals(object pObject)
            {
                return pObject is Identity other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((MetaType != null
                        ? StringComparer.Ordinal.GetHashCode(MetaType)
                        : 0) * 397) ^ ObjectId.GetHashCode();
                }
            }
        }
    }
}

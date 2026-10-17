using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal sealed class NobleDeathSuccessionRetryQueue<T> where T : class
    {
        private sealed class PendingEntry
        {
            internal readonly T Value;
            internal readonly LinkedListNode<long> Node;

            internal PendingEntry(T pValue, LinkedListNode<long> pNode)
            {
                Value = pValue;
                Node = pNode;
            }
        }

        private readonly Dictionary<long, PendingEntry> _pending =
            new Dictionary<long, PendingEntry>();
        private readonly LinkedList<long> _order = new LinkedList<long>();

        public int Count => _pending.Count;

        public bool TryUpsert(long pActorId, T pValue)
        {
            if (pActorId < 0L || pValue == null) return false;
            LinkedListNode<long> node = _pending.TryGetValue(pActorId, out PendingEntry current)
                ? current.Node
                : _order.AddLast(pActorId);
            _pending[pActorId] = new PendingEntry(pValue, node);
            return true;
        }

        public bool TryProcess(long pActorId, Func<T, bool> pProcess)
        {
            if (!_pending.TryGetValue(pActorId, out PendingEntry entry)) return true;
            if (!TryInvoke(pProcess, entry.Value)) return false;
            // A callback may enqueue newer state for this actor, including after a world clear.
            if (_pending.TryGetValue(pActorId, out PendingEntry current) &&
                ReferenceEquals(entry, current))
            {
                _pending.Remove(pActorId);
                _order.Remove(entry.Node);
            }
            return true;
        }

        public bool TryProcessOne(Func<T, bool> pProcess)
        {
            LinkedListNode<long> node = _order.First;
            if (node == null) return true;
            // Rotate before invoking so one persistent failure cannot starve other deaths.
            _order.RemoveFirst();
            _order.AddLast(node);
            return TryProcess(node.Value, pProcess);
        }

        public bool TryFlushAll(Func<T, bool> pProcess)
        {
            var actorIds = new List<long>(_order);
            for (int i = 0; i < actorIds.Count; i++)
                TryProcess(actorIds[i], pProcess);
            return _pending.Count == 0;
        }

        public void Clear()
        {
            _pending.Clear();
            _order.Clear();
        }

        private static bool TryInvoke(Func<T, bool> pProcess, T pValue)
        {
            if (pProcess == null) return false;
            try { return pProcess(pValue); }
            catch { return false; }
        }
    }
}

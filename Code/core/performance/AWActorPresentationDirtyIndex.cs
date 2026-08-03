using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    public sealed class AWActorPresentationDirtyIndex
    {
        private readonly HashSet<long> _dirty = new HashSet<long>();
        private readonly HashSet<long> _removed = new HashSet<long>();
        private readonly Queue<long> _dirtyOrder = new Queue<long>();
        private readonly Queue<long> _removedOrder = new Queue<long>();

        public int WorldGeneration { get; private set; }
        public int PendingCount => _dirty.Count + _removed.Count;

        public bool MarkDirty(long pActorId)
        {
            if (pActorId < 0L || _removed.Contains(pActorId) ||
                !_dirty.Add(pActorId)) return false;
            _dirtyOrder.Enqueue(pActorId);
            return true;
        }

        public bool MarkRemoved(long pActorId)
        {
            if (pActorId < 0L || !_removed.Add(pActorId)) return false;
            _dirty.Remove(pActorId);
            _removedOrder.Enqueue(pActorId);
            return true;
        }

        public void Take(List<long> pDirty, List<long> pRemoved,
            int maximumItems)
        {
            if (pDirty == null) throw new ArgumentNullException(nameof(pDirty));
            if (pRemoved == null)
                throw new ArgumentNullException(nameof(pRemoved));
            int remaining = Math.Max(0, maximumItems);
            while (remaining > 0 && _removedOrder.Count > 0)
            {
                long actorId = _removedOrder.Dequeue();
                if (!_removed.Remove(actorId)) continue;
                pRemoved.Add(actorId);
                remaining--;
            }
            while (remaining > 0 && _dirtyOrder.Count > 0)
            {
                long actorId = _dirtyOrder.Dequeue();
                if (!_dirty.Remove(actorId)) continue;
                pDirty.Add(actorId);
                remaining--;
            }
        }

        public void Reset(int pWorldGeneration)
        {
            _dirty.Clear();
            _removed.Clear();
            _dirtyOrder.Clear();
            _removedOrder.Clear();
            WorldGeneration = pWorldGeneration;
        }
    }
}

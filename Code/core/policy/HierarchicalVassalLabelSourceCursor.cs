using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    internal sealed class HierarchicalVassalLabelSourceCursor<T>
    {
        private readonly IReadOnlyList<T> _items;
        private int _index;

        internal HierarchicalVassalLabelSourceCursor(IReadOnlyList<T> pItems)
        {
            _items = pItems ?? Array.Empty<T>();
        }

        internal bool IsComplete => _index >= _items.Count;

        internal int Remaining => Math.Max(0, _items.Count - _index);

        internal IReadOnlyList<T> Take(int pMaximum)
        {
            int count = Math.Max(0, Math.Min(pMaximum, Remaining));
            var batch = new List<T>(count);
            for (int offset = 0; offset < count; offset++)
                batch.Add(_items[_index++]);
            return batch;
        }
    }
}

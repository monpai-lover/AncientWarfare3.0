using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.db
{
    internal sealed class HistoricalPendingStateStore<TKey, TValue>
    {
        private sealed class Entry
        {
            public long Sequence;
            public TValue Value;
        }

        private readonly object _gate = new object();
        private readonly Func<TValue, TValue> _clone;
        private readonly Dictionary<TKey, Entry> _entries =
            new Dictionary<TKey, Entry>();

        public HistoricalPendingStateStore(Func<TValue, TValue> pClone)
        {
            _clone = pClone ?? throw new ArgumentNullException(nameof(pClone));
        }

        public int Count
        {
            get { lock (_gate) return _entries.Count; }
        }

        public void Publish(TKey pKey, long sequence, TValue pValue)
        {
            lock (_gate)
            {
                _entries[pKey] = new Entry
                {
                    Sequence = sequence,
                    Value = _clone(pValue)
                };
            }
        }

        public bool TryRead(TKey pKey, out TValue pValue)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(pKey, out Entry entry))
                {
                    pValue = default;
                    return false;
                }
                pValue = _clone(entry.Value);
                return true;
            }
        }

        public bool Complete(TKey pKey, long sequence)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(pKey, out Entry entry) ||
                    entry.Sequence != sequence) return false;
                _entries.Remove(pKey);
                return true;
            }
        }

        public void Clear()
        {
            lock (_gate) _entries.Clear();
        }
    }
}

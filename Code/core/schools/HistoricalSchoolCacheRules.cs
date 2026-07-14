using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public readonly struct HistoricalSchoolCityCacheStamp
    {
        public HistoricalSchoolCityCacheStamp(
            int pIdentityToken,
            long pKingdomId,
            int pZoneCount,
            int pCenterX,
            int pCenterY)
        {
            IdentityToken = pIdentityToken;
            KingdomId = pKingdomId;
            ZoneCount = pZoneCount;
            CenterX = pCenterX;
            CenterY = pCenterY;
        }

        public int IdentityToken { get; }
        public long KingdomId { get; }
        public int ZoneCount { get; }
        public int CenterX { get; }
        public int CenterY { get; }

        public bool Matches(
            int pIdentityToken,
            long pKingdomId,
            int pZoneCount,
            int pCenterX,
            int pCenterY)
        {
            return IdentityToken == pIdentityToken &&
                   KingdomId == pKingdomId &&
                   ZoneCount == pZoneCount &&
                   CenterX == pCenterX &&
                   CenterY == pCenterY;
        }
    }

    public sealed class HistoricalSchoolFixedLru<TKey, TValue>
    {
        private sealed class Entry
        {
            public TValue Value;
            public LinkedListNode<TKey> Node;
        }

        private readonly int _capacity;
        private readonly Dictionary<TKey, Entry> _entries;
        private readonly LinkedList<TKey> _recency = new LinkedList<TKey>();

        public HistoricalSchoolFixedLru(int pCapacity)
        {
            if (pCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(pCapacity));
            _capacity = pCapacity;
            _entries = new Dictionary<TKey, Entry>(pCapacity);
        }

        public int Count => _entries.Count;

        public bool ContainsKey(TKey pKey) => _entries.ContainsKey(pKey);

        public bool TryGet(TKey pKey, out TValue pValue)
        {
            if (!_entries.TryGetValue(pKey, out Entry entry))
            {
                pValue = default;
                return false;
            }
            Touch(entry);
            pValue = entry.Value;
            return true;
        }

        public void Set(TKey pKey, TValue pValue)
        {
            if (_entries.TryGetValue(pKey, out Entry existing))
            {
                existing.Value = pValue;
                Touch(existing);
                return;
            }

            if (_entries.Count >= _capacity)
            {
                LinkedListNode<TKey> oldest = _recency.First;
                if (oldest != null)
                {
                    _recency.RemoveFirst();
                    _entries.Remove(oldest.Value);
                }
            }
            var node = _recency.AddLast(pKey);
            _entries.Add(pKey, new Entry { Value = pValue, Node = node });
        }

        public bool Remove(TKey pKey)
        {
            if (!_entries.TryGetValue(pKey, out Entry entry)) return false;
            _entries.Remove(pKey);
            _recency.Remove(entry.Node);
            return true;
        }

        public void Clear()
        {
            _entries.Clear();
            _recency.Clear();
        }

        private void Touch(Entry pEntry)
        {
            if (pEntry.Node == _recency.Last) return;
            _recency.Remove(pEntry.Node);
            _recency.AddLast(pEntry.Node);
        }
    }
}

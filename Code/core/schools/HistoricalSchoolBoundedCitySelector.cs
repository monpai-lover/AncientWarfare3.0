using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public sealed class HistoricalSchoolCircularCitySelector<TCity>
    {
        private readonly int _capacity;
        private readonly long _cursor;
        private readonly Func<TCity, long> _cityId;
        private readonly Func<TCity, bool> _eligible;
        private readonly SortedSet<Entry> _afterCursor =
            new SortedSet<Entry>(EntryComparer.Instance);
        private readonly SortedSet<Entry> _wrapped =
            new SortedSet<Entry>(EntryComparer.Instance);
        private readonly HashSet<long> _retainedCityIds = new HashSet<long>();

        public HistoricalSchoolCircularCitySelector(int pCapacity, long pCursor,
            Func<TCity, long> pCityId, Func<TCity, bool> pEligible)
        {
            if (pCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(pCapacity));
            _capacity = pCapacity;
            _cursor = pCursor;
            _cityId = pCityId ?? throw new ArgumentNullException(nameof(pCityId));
            _eligible = pEligible ?? throw new ArgumentNullException(nameof(pEligible));
        }

        public int Count => Math.Min(_capacity, _afterCursor.Count + _wrapped.Count);

        public void Consider(TCity pCity)
        {
            if (!_eligible(pCity)) return;
            long cityId = _cityId(pCity);
            if (!_retainedCityIds.Add(cityId)) return;
            SortedSet<Entry> retained = cityId > _cursor ? _afterCursor : _wrapped;
            retained.Add(new Entry(cityId, pCity));
            if (retained.Count <= _capacity) return;
            Entry removed = retained.Max;
            retained.Remove(removed);
            _retainedCityIds.Remove(removed.CityId);
        }

        public IEnumerable<TCity> AscendingFromCursor()
        {
            int emitted = 0;
            foreach (Entry entry in _afterCursor)
            {
                if (emitted++ >= _capacity) yield break;
                yield return entry.City;
            }
            foreach (Entry entry in _wrapped)
            {
                if (emitted++ >= _capacity) yield break;
                yield return entry.City;
            }
        }

        private sealed class Entry
        {
            public Entry(long pCityId, TCity pCity)
            {
                CityId = pCityId;
                City = pCity;
            }

            public long CityId { get; }
            public TCity City { get; }
        }

        private sealed class EntryComparer : IComparer<Entry>
        {
            public static readonly EntryComparer Instance = new EntryComparer();

            public int Compare(Entry pFirst, Entry pSecond)
            {
                if (ReferenceEquals(pFirst, pSecond)) return 0;
                if (pFirst == null) return -1;
                if (pSecond == null) return 1;
                return pFirst.CityId.CompareTo(pSecond.CityId);
            }
        }
    }

    public sealed class HistoricalSchoolBoundedCitySelector<TCity>
    {
        private readonly int _capacity;
        private readonly Func<TCity, long> _cityId;
        private readonly Func<TCity, bool> _eligible;
        private readonly SortedSet<Entry> _retained =
            new SortedSet<Entry>(EntryComparer.Instance);
        private long _sequence;

        public HistoricalSchoolBoundedCitySelector(int pCapacity,
            Func<TCity, long> pCityId, Func<TCity, bool> pEligible)
        {
            if (pCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(pCapacity));
            _capacity = pCapacity;
            _cityId = pCityId ?? throw new ArgumentNullException(nameof(pCityId));
            _eligible = pEligible ?? throw new ArgumentNullException(nameof(pEligible));
        }

        public int Count => _retained.Count;

        public void Consider(TCity pCity)
        {
            if (!_eligible(pCity)) return;
            var entry = new Entry(_cityId(pCity), _sequence++, pCity);
            _retained.Add(entry);
            if (_retained.Count > _capacity) _retained.Remove(_retained.Max);
        }

        public IEnumerable<TCity> Ascending()
        {
            foreach (Entry entry in _retained) yield return entry.City;
        }

        private sealed class Entry
        {
            public Entry(long pCityId, long pSequence, TCity pCity)
            {
                CityId = pCityId;
                Sequence = pSequence;
                City = pCity;
            }

            public long CityId { get; }
            public long Sequence { get; }
            public TCity City { get; }
        }

        private sealed class EntryComparer : IComparer<Entry>
        {
            public static readonly EntryComparer Instance = new EntryComparer();

            public int Compare(Entry pFirst, Entry pSecond)
            {
                if (ReferenceEquals(pFirst, pSecond)) return 0;
                if (pFirst == null) return -1;
                if (pSecond == null) return 1;
                int idOrder = pFirst.CityId.CompareTo(pSecond.CityId);
                return idOrder != 0 ? idOrder : pFirst.Sequence.CompareTo(pSecond.Sequence);
            }
        }
    }
}

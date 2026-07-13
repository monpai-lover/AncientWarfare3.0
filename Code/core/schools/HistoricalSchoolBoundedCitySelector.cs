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
        private readonly HistoricalSchoolBoundedCitySelector<TCity> _afterCursor;
        private readonly HistoricalSchoolBoundedCitySelector<TCity> _wrapped;

        public HistoricalSchoolCircularCitySelector(int pCapacity, long pCursor,
            Func<TCity, long> pCityId, Func<TCity, bool> pEligible)
        {
            if (pCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(pCapacity));
            _capacity = pCapacity;
            _cursor = pCursor;
            _cityId = pCityId ?? throw new ArgumentNullException(nameof(pCityId));
            _eligible = pEligible ?? throw new ArgumentNullException(nameof(pEligible));
            _afterCursor = new HistoricalSchoolBoundedCitySelector<TCity>(pCapacity,
                pCityId, _ => true);
            _wrapped = new HistoricalSchoolBoundedCitySelector<TCity>(pCapacity,
                pCityId, _ => true);
        }

        public int Count => _afterCursor.Count + _wrapped.Count;

        public void Consider(TCity pCity)
        {
            if (!_eligible(pCity)) return;
            if (_cityId(pCity) > _cursor) _afterCursor.Consider(pCity);
            else _wrapped.Consider(pCity);
        }

        public IEnumerable<TCity> AscendingFromCursor()
        {
            int emitted = 0;
            foreach (TCity city in _afterCursor.Ascending())
            {
                if (emitted++ >= _capacity) yield break;
                yield return city;
            }
            foreach (TCity city in _wrapped.Ascending())
            {
                if (emitted++ >= _capacity) yield break;
                yield return city;
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

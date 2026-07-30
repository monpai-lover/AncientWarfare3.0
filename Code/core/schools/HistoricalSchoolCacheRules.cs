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

    public readonly struct HistoricalSchoolYearCityKey :
        IEquatable<HistoricalSchoolYearCityKey>
    {
        public HistoricalSchoolYearCityKey(long pCityId, int pYear)
        {
            CityId = pCityId;
            Year = pYear;
        }

        public long CityId { get; }
        public int Year { get; }

        public bool Equals(HistoricalSchoolYearCityKey pOther)
        {
            return CityId == pOther.CityId && Year == pOther.Year;
        }

        public override bool Equals(object pObject)
        {
            return pObject is HistoricalSchoolYearCityKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (CityId.GetHashCode() * 397) ^ Year;
            }
        }
    }

    public sealed class HistoricalSchoolYearCityCache<TValue>
    {
        private readonly HistoricalSchoolFixedLru<HistoricalSchoolYearCityKey, TValue>
            _entries;

        public HistoricalSchoolYearCityCache(int pCapacity)
        {
            _entries = new HistoricalSchoolFixedLru<
                HistoricalSchoolYearCityKey, TValue>(pCapacity);
        }

        public int Count => _entries.Count;

        public bool TryGet(long pCityId, int pYear, out TValue pValue)
        {
            return _entries.TryGet(new HistoricalSchoolYearCityKey(pCityId, pYear),
                out pValue);
        }

        public void Set(long pCityId, int pYear, TValue pValue)
        {
            if (pCityId < 0 || pYear < 0) return;
            _entries.Set(new HistoricalSchoolYearCityKey(pCityId, pYear), pValue);
        }

        public bool Remove(long pCityId, int pYear)
        {
            return _entries.Remove(new HistoricalSchoolYearCityKey(pCityId, pYear));
        }

        public long[] CollectMisses(IReadOnlyList<long> pCityIds, int pYear,
            IDictionary<long, TValue> pHits)
        {
            if (pCityIds == null || pCityIds.Count == 0)
                return Array.Empty<long>();
            var missing = new List<long>(pCityIds.Count);
            var seen = new HashSet<long>();
            for (int index = 0; index < pCityIds.Count; index++)
            {
                long cityId = pCityIds[index];
                if (cityId < 0 || !seen.Add(cityId)) continue;
                if (TryGet(cityId, pYear, out TValue cached))
                {
                    if (pHits != null) pHits[cityId] = cached;
                    continue;
                }
                missing.Add(cityId);
            }
            return missing.Count == 0 ? Array.Empty<long>() : missing.ToArray();
        }

        public void Clear()
        {
            _entries.Clear();
        }
    }

    public sealed class HistoricalSchoolActiveReservationBook<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, TValue> _values;

        public HistoricalSchoolActiveReservationBook(int pCapacity)
        {
            if (pCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(pCapacity));
            _capacity = pCapacity;
            _values = new Dictionary<TKey, TValue>(pCapacity);
        }

        public int Count => _values.Count;

        public bool TryGet(TKey pKey, out TValue pValue)
        {
            return _values.TryGetValue(pKey, out pValue);
        }

        public bool TryAdd(TKey pKey, TValue pValue)
        {
            if (_values.ContainsKey(pKey) || _values.Count >= _capacity) return false;
            _values.Add(pKey, pValue);
            return true;
        }

        public bool TryRemove(TKey pKey, out TValue pValue)
        {
            if (!_values.TryGetValue(pKey, out pValue)) return false;
            _values.Remove(pKey);
            return true;
        }

        public void Clear()
        {
            _values.Clear();
        }
    }

    public static class HistoricalSchoolTravelReservationRestoreRules
    {
        public static bool ShouldUseExamTravelerReservation(
            bool activeTravel, bool qualifiedTeacher)
        {
            return activeTravel && !qualifiedTeacher;
        }
    }

    public sealed class HistoricalSchoolTravelReservationBook
    {
        private readonly int _capacityPerSchool;
        private readonly Dictionary<string, HashSet<long>> _actorsBySchool =
            new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        private readonly Dictionary<long, string> _schoolByActor =
            new Dictionary<long, string>();

        public HistoricalSchoolTravelReservationBook(int pCapacityPerSchool)
        {
            if (pCapacityPerSchool <= 0)
                throw new ArgumentOutOfRangeException(nameof(pCapacityPerSchool));
            _capacityPerSchool = pCapacityPerSchool;
        }

        public int Count => _schoolByActor.Count;

        public int CountForSchool(string pSchoolId)
        {
            return !string.IsNullOrEmpty(pSchoolId) &&
                   _actorsBySchool.TryGetValue(pSchoolId, out HashSet<long> actors)
                ? actors.Count
                : 0;
        }

        public bool TryReserve(string pSchoolId, long pActorId)
        {
            if (string.IsNullOrEmpty(pSchoolId) || pActorId < 0) return false;
            if (_schoolByActor.TryGetValue(pActorId, out string existingSchool))
                return string.Equals(existingSchool, pSchoolId, StringComparison.Ordinal);
            if (!_actorsBySchool.TryGetValue(pSchoolId, out HashSet<long> actors))
            {
                actors = new HashSet<long>();
                _actorsBySchool.Add(pSchoolId, actors);
            }
            if (actors.Count >= _capacityPerSchool) return false;
            actors.Add(pActorId);
            _schoolByActor.Add(pActorId, pSchoolId);
            return true;
        }

        public bool Release(long pActorId)
        {
            if (!_schoolByActor.TryGetValue(pActorId, out string schoolId)) return false;
            _schoolByActor.Remove(pActorId);
            if (_actorsBySchool.TryGetValue(schoolId, out HashSet<long> actors))
            {
                actors.Remove(pActorId);
                if (actors.Count == 0) _actorsBySchool.Remove(schoolId);
            }
            return true;
        }

        public void Clear()
        {
            _actorsBySchool.Clear();
            _schoolByActor.Clear();
        }
    }

    public sealed class HistoricalSchoolTransientIdGate
    {
        private readonly HashSet<long> _ids = new HashSet<long>();

        public int Count => _ids.Count;

        public bool TryBegin(long pId)
        {
            return pId >= 0 && _ids.Add(pId);
        }

        public bool Complete(long pId)
        {
            return _ids.Remove(pId);
        }

        public void Clear()
        {
            _ids.Clear();
        }
    }
}

using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    internal readonly struct CourtVacancyEntry
    {
        internal CourtVacancyEntry(CourtVacancyKey pKey,
            int pMissingSeats)
        {
            Key = pKey;
            MissingSeats = pMissingSeats;
        }

        internal CourtVacancyKey Key { get; }
        internal int MissingSeats { get; }
    }

    internal sealed class CourtVacancyRegistryState
    {
        private readonly Dictionary<long,
            Dictionary<CourtVacancyKey, CourtVacancyEntry>> _byKingdom =
            new Dictionary<long,
                Dictionary<CourtVacancyKey, CourtVacancyEntry>>();

        internal void Upsert(CourtVacancyKey pKey, int pMissingSeats)
        {
            if (pMissingSeats <= 0)
            {
                Remove(pKey);
                return;
            }

            if (!_byKingdom.TryGetValue(pKey.KingdomId, out var entries))
            {
                entries = new Dictionary<CourtVacancyKey,
                    CourtVacancyEntry>();
                _byKingdom[pKey.KingdomId] = entries;
            }

            entries[pKey] = new CourtVacancyEntry(pKey, pMissingSeats);
        }

        internal IReadOnlyList<CourtVacancyEntry> ForKingdom(
            long pKingdomId)
        {
            if (!_byKingdom.TryGetValue(pKingdomId, out var entries) ||
                entries.Count == 0)
                return Array.Empty<CourtVacancyEntry>();

            var snapshot = new List<CourtVacancyEntry>(entries.Values);
            snapshot.Sort(CompareEntries);
            return snapshot.ToArray();
        }

        internal bool Contains(CourtVacancyKey pKey)
        {
            return _byKingdom.TryGetValue(pKey.KingdomId,
                       out var entries) &&
                   entries.ContainsKey(pKey);
        }

        internal void Remove(CourtVacancyKey pKey)
        {
            if (!_byKingdom.TryGetValue(pKey.KingdomId, out var entries))
                return;
            entries.Remove(pKey);
            if (entries.Count == 0) _byKingdom.Remove(pKey.KingdomId);
        }

        internal void RemoveCity(long pKingdomId, long pCityId)
        {
            if (!_byKingdom.TryGetValue(pKingdomId, out var entries))
                return;

            var remove = new List<CourtVacancyKey>();
            foreach (CourtVacancyKey key in entries.Keys)
                if (key.CityId == pCityId) remove.Add(key);
            foreach (CourtVacancyKey key in remove) entries.Remove(key);
            if (entries.Count == 0) _byKingdom.Remove(pKingdomId);
        }

        internal void RemoveKingdom(long pKingdomId)
        {
            _byKingdom.Remove(pKingdomId);
        }

        internal void Clear()
        {
            _byKingdom.Clear();
        }

        private static int CompareEntries(CourtVacancyEntry pLeft,
            CourtVacancyEntry pRight)
        {
            int result = CourtVacancyRules.Priority(pLeft.Key).CompareTo(
                CourtVacancyRules.Priority(pRight.Key));
            if (result != 0) return result;
            result = pLeft.Key.CityId.CompareTo(pRight.Key.CityId);
            if (result != 0) return result;
            result = pLeft.Key.CountyId.CompareTo(pRight.Key.CountyId);
            if (result != 0) return result;
            result = string.CompareOrdinal(pLeft.Key.Layer,
                pRight.Key.Layer);
            if (result != 0) return result;
            return string.CompareOrdinal(pLeft.Key.OfficeId,
                pRight.Key.OfficeId);
        }
    }

    internal static class CourtVacancyRegistry
    {
        private static readonly CourtVacancyRegistryState State =
            new CourtVacancyRegistryState();

        internal static void Register(CourtVacancyKey pKey,
            int pMissingSeats = 1)
        {
            State.Upsert(pKey, pMissingSeats);
        }

        internal static IReadOnlyList<CourtVacancyEntry> Snapshot(
            long pKingdomId)
        {
            return State.ForKingdom(pKingdomId);
        }

        internal static bool Contains(CourtVacancyKey pKey)
        {
            return State.Contains(pKey);
        }

        internal static void Remove(CourtVacancyKey pKey)
        {
            State.Remove(pKey);
        }

        internal static void RemoveCity(long pKingdomId, long pCityId)
        {
            State.RemoveCity(pKingdomId, pCityId);
        }

        internal static void RemoveKingdom(long pKingdomId)
        {
            State.RemoveKingdom(pKingdomId);
        }

        internal static void ClearRuntime()
        {
            State.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct CityMilitaryThreatDiagnostics
    {
        internal CityMilitaryThreatDiagnostics(long pRequests,
            long pPhysicalScans, long pHits, long pInvalidations,
            long pRevision)
        {
            Requests = pRequests;
            PhysicalScans = pPhysicalScans;
            Hits = pHits;
            Invalidations = pInvalidations;
            Revision = pRevision;
        }

        internal long Requests { get; }
        internal long PhysicalScans { get; }
        internal long Hits { get; }
        internal long Invalidations { get; }
        internal long Revision { get; }
    }

    internal static class CityMilitaryThreatFacts
    {
        private readonly struct PresenceKey : IEquatable<PresenceKey>
        {
            internal PresenceKey(long pWarId, long pCityId)
            {
                WarId = pWarId;
                CityId = pCityId;
            }

            internal long WarId { get; }
            internal long CityId { get; }

            public bool Equals(PresenceKey pOther)
            {
                return WarId == pOther.WarId && CityId == pOther.CityId;
            }

            public override bool Equals(object pObject)
            {
                return pObject is PresenceKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked { return ((int)WarId * 397) ^ (int)CityId; }
            }
        }

        private readonly struct FactEntry
        {
            internal FactEntry(bool pHostile, long pCacheEpoch,
                double pCachedAt)
            {
                Hostile = pHostile;
                CacheEpoch = pCacheEpoch;
                CachedAt = pCachedAt;
            }

            internal bool Hostile { get; }
            internal long CacheEpoch { get; }
            internal double CachedAt { get; }
        }

        private readonly struct PresenceEntry
        {
            internal PresenceEntry(Kingdom[] pKingdoms, long pCacheEpoch,
                double pCachedAt)
            {
                Kingdoms = pKingdoms ?? Array.Empty<Kingdom>();
                CacheEpoch = pCacheEpoch;
                CachedAt = pCachedAt;
            }

            internal Kingdom[] Kingdoms { get; }
            internal long CacheEpoch { get; }
            internal double CachedAt { get; }
        }

        private static readonly Dictionary<CityMilitaryThreatKey, FactEntry>
            Facts = new Dictionary<CityMilitaryThreatKey, FactEntry>();
        private static readonly Dictionary<PresenceKey, PresenceEntry>
            PresenceFacts = new Dictionary<PresenceKey, PresenceEntry>();
        private static bool _cycleActive;
        private static long _requests;
        private static long _physicalScans;
        private static long _hits;
        private static long _invalidations;
        private static long _revision;
        private static long _cacheEpoch = 1L;

        internal static long Revision => _revision;

        internal static void BeginAuthorityCycle()
        {
            _cycleActive = true;
        }

        internal static void EndAuthorityCycle()
        {
            _cycleActive = false;
        }

        internal static bool TryGet(War pWar, City pCity,
            Kingdom pKingdom, out bool pHostile)
        {
            _requests++;
            pHostile = false;
            if (!TryCreateKey(pWar, pCity, pKingdom, out var key))
                return false;
            if (!Facts.TryGetValue(key, out FactEntry entry) ||
                !CityMilitaryThreatFactsRules.ShouldReuse(
                    entry.CacheEpoch, _cacheEpoch, RealtimeSeconds(),
                    entry.CachedAt))
                return false;
            pHostile = entry.Hostile;
            _hits++;
            return true;
        }

        internal static void Store(War pWar, City pCity, Kingdom pKingdom,
            bool pHostile)
        {
            if (!TryCreateKey(pWar, pCity, pKingdom, out var key)) return;
            Facts[key] = new FactEntry(pHostile, _cacheEpoch,
                RealtimeSeconds());
        }

        internal static bool TryGetPresence(War pWar, City pCity,
            out Kingdom[] pKingdoms)
        {
            pKingdoms = null;
            if (!TryCreatePresenceKey(pWar, pCity, out PresenceKey key) ||
                !PresenceFacts.TryGetValue(key, out PresenceEntry entry) ||
                !CityMilitaryThreatFactsRules.ShouldReusePresence(
                    entry.CacheEpoch, _cacheEpoch, RealtimeSeconds(),
                    entry.CachedAt)) return false;
            pKingdoms = entry.Kingdoms;
            return true;
        }

        internal static void StorePresence(War pWar, City pCity,
            Kingdom[] pKingdoms)
        {
            if (!TryCreatePresenceKey(pWar, pCity, out PresenceKey key))
                return;
            PresenceFacts[key] = new PresenceEntry(pKingdoms, _cacheEpoch,
                RealtimeSeconds());
        }

        internal static void RecordPhysicalScan()
        {
            _physicalScans++;
        }

        internal static void InvalidateCity(City pCity)
        {
            long cityId = pCity?.data == null ? -1L : pCity.id;
            if (!CityMilitaryThreatFactsRules.
                    ShouldAdvanceRevisionForInvalidation(cityId)) return;
            AdvanceRevision();
            RemovePresenceByCity(cityId);
            if (Facts.Count == 0) return;
            var removed = new List<CityMilitaryThreatKey>();
            foreach (CityMilitaryThreatKey key in Facts.Keys)
                if (CityMilitaryThreatFactsRules.ShouldInvalidate(key.CityId,
                        cityId))
                    removed.Add(key);
            if (removed.Count == 0) return;
            for (int index = 0; index < removed.Count; index++)
                Facts.Remove(removed[index]);
            _invalidations += removed.Count;
        }

        internal static void InvalidateWar(War pWar)
        {
            long warId = pWar?.data == null ? -1L : pWar.data.id;
            if (!CityMilitaryThreatFactsRules.
                    ShouldAdvanceRevisionForInvalidation(warId)) return;
            AdvanceRevision();
            RemovePresenceByWar(warId);
            if (Facts.Count == 0) return;
            var removed = new List<CityMilitaryThreatKey>();
            foreach (CityMilitaryThreatKey key in Facts.Keys)
                if (key.WarId == warId) removed.Add(key);
            if (removed.Count == 0) return;
            for (int index = 0; index < removed.Count; index++)
                Facts.Remove(removed[index]);
            _invalidations += removed.Count;
        }

        internal static CityMilitaryThreatDiagnostics SnapshotDiagnostics()
        {
            return new CityMilitaryThreatDiagnostics(_requests,
                _physicalScans, _hits, _invalidations, _revision);
        }

        internal static void Reset()
        {
            Facts.Clear();
            PresenceFacts.Clear();
            _cycleActive = false;
            _requests = 0L;
            _physicalScans = 0L;
            _hits = 0L;
            _invalidations = 0L;
            AdvanceRevision();
            AdvanceCacheEpoch();
        }

        private static bool TryCreateKey(War pWar, City pCity,
            Kingdom pKingdom, out CityMilitaryThreatKey pKey)
        {
            pKey = default;
            long warId;
            long cityId;
            long kingdomId;
            try
            {
                warId = pWar?.data?.id ?? -1L;
                cityId = pCity?.data == null ? -1L : pCity.id;
                kingdomId = pKingdom?.data == null ? -1L : pKingdom.id;
            }
            catch
            {
                return false;
            }
            if (!CityMilitaryThreatFactsRules.CanCache(warId, cityId,
                    kingdomId))
                return false;
            pKey = new CityMilitaryThreatKey(warId, cityId, kingdomId);
            return true;
        }

        private static bool TryCreatePresenceKey(War pWar, City pCity,
            out PresenceKey pKey)
        {
            pKey = default;
            long warId;
            long cityId;
            try
            {
                warId = pWar?.data?.id ?? -1L;
                cityId = pCity?.data == null ? -1L : pCity.id;
            }
            catch { return false; }
            if (!CityMilitaryThreatFactsRules.CanCachePresence(warId,
                    cityId)) return false;
            pKey = new PresenceKey(warId, cityId);
            return true;
        }

        private static void RemovePresenceByCity(long pCityId)
        {
            if (PresenceFacts.Count == 0) return;
            var removed = new List<PresenceKey>();
            foreach (PresenceKey key in PresenceFacts.Keys)
                if (key.CityId == pCityId) removed.Add(key);
            for (int index = 0; index < removed.Count; index++)
                PresenceFacts.Remove(removed[index]);
        }

        private static void RemovePresenceByWar(long pWarId)
        {
            if (PresenceFacts.Count == 0) return;
            var removed = new List<PresenceKey>();
            foreach (PresenceKey key in PresenceFacts.Keys)
                if (key.WarId == pWarId) removed.Add(key);
            for (int index = 0; index < removed.Count; index++)
                PresenceFacts.Remove(removed[index]);
        }

        private static void AdvanceRevision()
        {
            _revision = _revision == long.MaxValue ? 1L : _revision + 1L;
        }

        private static void AdvanceCacheEpoch()
        {
            _cacheEpoch = _cacheEpoch == long.MaxValue
                ? 1L
                : _cacheEpoch + 1L;
        }

        private static double RealtimeSeconds()
        {
            try { return UnityEngine.Time.realtimeSinceStartupAsDouble; }
            catch
            {
                return (double)Stopwatch.GetTimestamp() /
                       Stopwatch.Frequency;
            }
        }
    }
}

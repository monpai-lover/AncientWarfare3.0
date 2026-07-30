using System;
using System.Collections.Generic;

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
        private static readonly Dictionary<CityMilitaryThreatKey, bool> Facts =
            new Dictionary<CityMilitaryThreatKey, bool>();
        private static bool _cycleActive;
        private static long _requests;
        private static long _physicalScans;
        private static long _hits;
        private static long _invalidations;
        private static long _revision;

        internal static long Revision => _revision;

        internal static void BeginAuthorityCycle()
        {
            Facts.Clear();
            _cycleActive = true;
        }

        internal static void EndAuthorityCycle()
        {
            Facts.Clear();
            _cycleActive = false;
        }

        internal static bool TryGet(War pWar, City pCity,
            Kingdom pKingdom, out bool pHostile)
        {
            _requests++;
            pHostile = false;
            if (!TryCreateKey(pWar, pCity, pKingdom, out var key))
                return false;
            if (!Facts.TryGetValue(key, out pHostile)) return false;
            _hits++;
            return true;
        }

        internal static void Store(War pWar, City pCity, Kingdom pKingdom,
            bool pHostile)
        {
            if (!TryCreateKey(pWar, pCity, pKingdom, out var key)) return;
            Facts[key] = pHostile;
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
            _cycleActive = false;
            _requests = 0L;
            _physicalScans = 0L;
            _hits = 0L;
            _invalidations = 0L;
            AdvanceRevision();
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
            if (!CityMilitaryThreatFactsRules.CanCache(_cycleActive, warId,
                    cityId, kingdomId))
                return false;
            pKey = new CityMilitaryThreatKey(warId, cityId, kingdomId);
            return true;
        }

        private static void AdvanceRevision()
        {
            _revision = _revision == long.MaxValue ? 1L : _revision + 1L;
        }
    }
}

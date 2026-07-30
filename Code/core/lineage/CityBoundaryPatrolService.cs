using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class CityBoundaryPatrolService
    {
        private sealed class BoundarySnapshot
        {
            public City City;
            public int CityZoneCount;
            public int BorderZoneCount;
            public TileZone[] Zones = Array.Empty<TileZone>();
            public int Count;
        }

        private static readonly Dictionary<long, BoundarySnapshot> ByCity =
            new Dictionary<long, BoundarySnapshot>();

        public static TileZone GetBoundaryZone(City pCity, long pActorId,
            int pVisit)
        {
            if (pCity?.data == null || pCity.isRekt() ||
                pCity.border_zones.Count == 0) return null;

            if (!ByCity.TryGetValue(pCity.id, out BoundarySnapshot snapshot) ||
                !IsSnapshotCurrent(snapshot, pCity))
                snapshot = Rebuild(pCity);
            if (snapshot?.Count <= 0) return null;

            int index = WartimeGarrisonRules.PatrolStartIndex(pActorId,
                pVisit, snapshot.Count);
            TileZone zone = snapshot.Zones[index];
            if (IsCurrentBoundary(pCity, zone)) return zone;

            snapshot = Rebuild(pCity);
            if (snapshot?.Count <= 0) return null;
            index = WartimeGarrisonRules.PatrolStartIndex(pActorId,
                pVisit, snapshot.Count);
            return snapshot.Zones[index];
        }

        public static void Invalidate(City pCity)
        {
            if (pCity?.data != null) ByCity.Remove(pCity.id);
        }

        public static void ClearRuntime()
        {
            ByCity.Clear();
        }

        private static bool IsSnapshotCurrent(BoundarySnapshot pSnapshot,
            City pCity)
        {
            return pSnapshot != null && ReferenceEquals(pSnapshot.City, pCity) &&
                   pSnapshot.CityZoneCount == pCity.zones.Count &&
                   pSnapshot.BorderZoneCount == pCity.border_zones.Count &&
                   pSnapshot.Count > 0;
        }

        private static bool IsCurrentBoundary(City pCity, TileZone pZone)
        {
            return pZone != null && pZone.city == pCity &&
                   pCity.border_zones.Contains(pZone);
        }

        private static BoundarySnapshot Rebuild(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return null;
            int capacity = pCity.border_zones.Count;
            var snapshot = new BoundarySnapshot
            {
                City = pCity,
                CityZoneCount = pCity.zones.Count,
                BorderZoneCount = capacity,
                Zones = capacity > 0
                    ? new TileZone[capacity]
                    : Array.Empty<TileZone>()
            };
            foreach (TileZone zone in pCity.border_zones)
            {
                if (zone == null || zone.city != pCity) continue;
                snapshot.Zones[snapshot.Count++] = zone;
            }
            ByCity[pCity.id] = snapshot;
            return snapshot;
        }
    }
}

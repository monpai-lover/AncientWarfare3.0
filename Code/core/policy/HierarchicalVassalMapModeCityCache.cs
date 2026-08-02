using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal sealed class HierarchicalVassalMapModeCityCacheEntry
    {
        internal readonly List<TileZone> VisibleZones =
            new List<TileZone>();
        internal readonly List<Vector2Int> LandTiles =
            new List<Vector2Int>();
        internal HierarchicalVassalMapModeGeometryMetrics Metrics;
    }

    internal static class HierarchicalVassalMapModeCityCache
    {
        private static readonly Dictionary<long,
            HierarchicalVassalMapModeCityCacheEntry> Entries =
            new Dictionary<long, HierarchicalVassalMapModeCityCacheEntry>();
        private static readonly HashSet<long> Dirty = new HashSet<long>();

        internal static HierarchicalVassalMapModeCityCacheEntry Get(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return null;
            if (!Entries.TryGetValue(pCity.id, out var entry) ||
                Dirty.Contains(pCity.id))
            {
                entry = Rebuild(pCity);
                if (entry == null) return null;
                Entries[pCity.id] = entry;
                Dirty.Remove(pCity.id);
            }
            return entry;
        }

        internal static void MarkDirty(long pCityId)
        {
            if (pCityId >= 0L) Dirty.Add(pCityId);
        }

        internal static void Remove(long pCityId)
        {
            if (pCityId < 0L) return;
            Entries.Remove(pCityId);
            Dirty.Remove(pCityId);
        }

        internal static void Clear()
        {
            Entries.Clear();
            Dirty.Clear();
        }

        private static HierarchicalVassalMapModeCityCacheEntry Rebuild(
            City pCity)
        {
            var entry = new HierarchicalVassalMapModeCityCacheEntry();
            try
            {
                if (pCity.zones == null) return entry;
                var seen = new HashSet<Vector2Int>();
                for (int zoneIndex = 0; zoneIndex < pCity.zones.Count;
                     zoneIndex++)
                {
                    TileZone zone = pCity.zones[zoneIndex];
                    if (zone == null || zone.city != pCity ||
                        zone.tiles == null) continue;
                    bool hasLand = false;
                    for (int tileIndex = 0; tileIndex < zone.tiles.Length;
                         tileIndex++)
                    {
                        WorldTile tile = zone.tiles[tileIndex];
                        if (!HierarchicalVassalMapModeService.
                                IsVisibleLand(tile)) continue;
                        hasLand = true;
                        var position = new Vector2Int(tile.x, tile.y);
                        if (seen.Add(position)) entry.LandTiles.Add(position);
                    }
                    if (hasLand) entry.VisibleZones.Add(zone);
                }
            }
            catch
            {
                return entry;
            }
            entry.Metrics = HierarchicalVassalMapModeGeometry.CalculateMetrics(
                entry.LandTiles);
            return entry;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AncientWarfare3.core.lineage
{
    internal static class RoyalAsylumVenueService
    {
        private const int MaxCachedCities = 128;
        private const int MaxCandidatesPerCity = 48;
        private static readonly Dictionary<long, CacheEntry> Cache =
            new Dictionary<long, CacheEntry>();

        public static bool TryPick(City pCity, long pActorId, int pYear,
            out WorldTile pTile)
        {
            pTile = null;
            if (pCity?.data == null || pCity.isRekt()) return false;
            List<WorldTile> candidates = Candidates(pCity);
            if (candidates.Count == 0) return false;
            int start = PositiveModulo(pActorId * 31L + pYear, candidates.Count);
            for (int offset = 0; offset < candidates.Count; offset++)
            {
                WorldTile candidate = candidates[(start + offset) % candidates.Count];
                if (!IsCandidate(candidate, pCity)) continue;
                pTile = candidate;
                return true;
            }
            Cache.Remove(pCity.id);
            candidates = Candidates(pCity);
            if (candidates.Count == 0) return false;
            pTile = candidates[PositiveModulo(pActorId * 31L + pYear, candidates.Count)];
            return true;
        }

        public static void Clear()
        {
            Cache.Clear();
        }

        private static List<WorldTile> Candidates(City pCity)
        {
            CacheStamp stamp = Stamp(pCity);
            if (Cache.TryGetValue(pCity.id, out CacheEntry cached) &&
                ReferenceEquals(cached.City, pCity) && cached.Stamp.Equals(stamp))
                return cached.Tiles;

            var result = new List<WorldTile>(MaxCandidatesPerCity);
            try
            {
                foreach (TileZone zone in pCity.zones)
                {
                    if (zone == null) continue;
                    foreach (WorldTile tile in zone.tiles)
                    {
                        if (!IsCandidate(tile, pCity)) continue;
                        result.Add(tile);
                        if (result.Count >= MaxCandidatesPerCity) break;
                    }
                    if (result.Count >= MaxCandidatesPerCity) break;
                }
            }
            catch { }
            if (Cache.Count >= MaxCachedCities) Cache.Clear();
            Cache[pCity.id] = new CacheEntry { City = pCity, Stamp = stamp, Tiles = result };
            return result;
        }

        private static bool IsCandidate(WorldTile pTile, City pCity)
        {
            if (pTile?.Type == null || pTile.zone?.city != pCity ||
                pTile == pCity?.getTile()) return false;
            if (!pTile.Type.ground || pTile.Type.liquid || pTile.Type.lava ||
                pTile.Type.block) return false;
            try
            {
                return World.world?.GetTile(pTile.x - 1, pTile.y)?.zone?.city == pCity &&
                       World.world?.GetTile(pTile.x + 1, pTile.y)?.zone?.city == pCity &&
                       World.world?.GetTile(pTile.x, pTile.y - 1)?.zone?.city == pCity &&
                       World.world?.GetTile(pTile.x, pTile.y + 1)?.zone?.city == pCity;
            }
            catch { return false; }
        }

        private static CacheStamp Stamp(City pCity)
        {
            WorldTile center = pCity?.getTile();
            return new CacheStamp(RuntimeHelpers.GetHashCode(pCity),
                pCity?.kingdom?.data?.id ?? -1L, pCity?.zones?.Count ?? 0,
                center?.x ?? int.MinValue, center?.y ?? int.MinValue);
        }

        private static int PositiveModulo(long pValue, int pCount)
        {
            long result = pValue % pCount;
            return (int)(result < 0 ? result + pCount : result);
        }

        private sealed class CacheEntry
        {
            public City City;
            public CacheStamp Stamp;
            public List<WorldTile> Tiles;
        }

        private readonly struct CacheStamp : IEquatable<CacheStamp>
        {
            private readonly int _identity;
            private readonly long _ownerId;
            private readonly int _zoneCount;
            private readonly int _centerX;
            private readonly int _centerY;

            public CacheStamp(int pIdentity, long pOwnerId, int pZoneCount,
                int pCenterX, int pCenterY)
            {
                _identity = pIdentity;
                _ownerId = pOwnerId;
                _zoneCount = pZoneCount;
                _centerX = pCenterX;
                _centerY = pCenterY;
            }

            public bool Equals(CacheStamp other)
            {
                return _identity == other._identity && _ownerId == other._ownerId &&
                       _zoneCount == other._zoneCount && _centerX == other._centerX &&
                       _centerY == other._centerY;
            }
        }
    }
}

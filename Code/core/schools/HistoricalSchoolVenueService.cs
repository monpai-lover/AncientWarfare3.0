using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.schools
{
    internal sealed class HistoricalSchoolVenueClaim
    {
        public HistoricalSchoolVenueClaim(string pOperationKey, long pCityId,
            WorldTile pPrimary, WorldTile pSecondary)
        {
            OperationKey = pOperationKey;
            CityId = pCityId;
            Primary = pPrimary;
            Secondary = pSecondary;
        }

        public string OperationKey { get; }
        public long CityId { get; }
        public WorldTile Primary { get; }
        public WorldTile Secondary { get; }
    }

    internal static class HistoricalSchoolVenueService
    {
        private const int MaxCandidates = 48;
        private static readonly Dictionary<string, HistoricalSchoolVenueClaim> ByOperation =
            new Dictionary<string, HistoricalSchoolVenueClaim>(StringComparer.Ordinal);
        private static readonly Dictionary<long, HashSet<long>> OccupiedByCity =
            new Dictionary<long, HashSet<long>>();
        private static readonly Dictionary<long, List<WorldTile>> CandidateTilesByCity =
            new Dictionary<long, List<WorldTile>>();

        public static bool TryClaimLecture(City pCity, string pOperationKey,
            out HistoricalSchoolVenueClaim pClaim)
        {
            return TryClaim(pCity, pOperationKey, pNeedsSecondary: false, out pClaim);
        }

        public static bool TryClaimDebate(City pCity, string pOperationKey,
            out HistoricalSchoolVenueClaim pClaim)
        {
            return TryClaim(pCity, pOperationKey, pNeedsSecondary: true, out pClaim);
        }

        public static void Release(string pOperationKey)
        {
            if (string.IsNullOrEmpty(pOperationKey) ||
                !ByOperation.TryGetValue(pOperationKey, out HistoricalSchoolVenueClaim claim))
                return;
            ByOperation.Remove(pOperationKey);
            if (!OccupiedByCity.TryGetValue(claim.CityId, out HashSet<long> occupied)) return;
            occupied.Remove(TileKey(claim.Primary));
            if (claim.Secondary != null) occupied.Remove(TileKey(claim.Secondary));
            if (occupied.Count == 0) OccupiedByCity.Remove(claim.CityId);
        }

        public static void Clear()
        {
            ByOperation.Clear();
            OccupiedByCity.Clear();
            CandidateTilesByCity.Clear();
        }

        private static bool TryClaim(City pCity, string pOperationKey,
            bool pNeedsSecondary, out HistoricalSchoolVenueClaim pClaim)
        {
            pClaim = null;
            if (pCity?.data == null || pCity.isRekt() ||
                string.IsNullOrEmpty(pOperationKey)) return false;
            if (ByOperation.TryGetValue(pOperationKey, out pClaim)) return true;
            List<WorldTile> candidates = CandidatesForCity(pCity,
                pNeedsSecondary ? 2 : 1);
            if (candidates.Count == 0) return false;
            if (!OccupiedByCity.TryGetValue(pCity.data.id, out HashSet<long> occupiedKeys))
            {
                occupiedKeys = new HashSet<long>();
                OccupiedByCity[pCity.data.id] = occupiedKeys;
            }
            var occupiedIndices = new HashSet<int>();
            for (int index = 0; index < candidates.Count; index++)
                if (occupiedKeys.Contains(TileKey(candidates[index]))) occupiedIndices.Add(index);
            if (!HistoricalSchoolVenueRules.TrySelect(StableHash(pOperationKey),
                    candidates.Count, occupiedIndices, out int primaryIndex)) return false;
            WorldTile primary = candidates[primaryIndex];
            WorldTile secondary = null;
            if (pNeedsSecondary)
            {
                secondary = candidates
                    .Where(tile => tile != primary && !occupiedKeys.Contains(TileKey(tile)))
                    .OrderBy(tile => DistanceSquared(primary, tile))
                    .ThenBy(tile => tile.x)
                    .ThenBy(tile => tile.y)
                    .FirstOrDefault();
                if (secondary == null) return false;
            }
            occupiedKeys.Add(TileKey(primary));
            if (secondary != null) occupiedKeys.Add(TileKey(secondary));
            pClaim = new HistoricalSchoolVenueClaim(pOperationKey, pCity.data.id,
                primary, secondary);
            ByOperation[pOperationKey] = pClaim;
            return true;
        }

        private static List<WorldTile> CandidatesForCity(City pCity, int pMinimumCount)
        {
            WorldTile center = pCity.getTile();
            if (CandidateTilesByCity.TryGetValue(pCity.data.id,
                    out List<WorldTile> cached))
            {
                List<WorldTile> valid = cached.Where(tile => tile != center &&
                        IsUsable(tile, pCity))
                    .Take(MaxCandidates).ToList();
                if (valid.Count >= pMinimumCount) return valid;
            }
            List<WorldTile> rebuilt = BuildCandidates(pCity);
            CandidateTilesByCity[pCity.data.id] = rebuilt;
            return rebuilt;
        }

        private static List<WorldTile> BuildCandidates(City pCity)
        {
            var result = new List<WorldTile>(MaxCandidates);
            WorldTile center = pCity.getTile();
            try
            {
                foreach (TileZone zone in pCity.zones.OrderBy(zone => zone.id))
                {
                    foreach (WorldTile tile in zone.tiles.OrderBy(tile => tile.x)
                                 .ThenBy(tile => tile.y))
                    {
                        if (tile == center || !IsUsable(tile, pCity)) continue;
                        result.Add(tile);
                        if (result.Count >= MaxCandidates) return result;
                    }
                }
            }
            catch { }
            return result;
        }

        private static bool IsUsable(WorldTile pTile, City pCity)
        {
            return pTile?.Type != null && pTile.zone?.city == pCity &&
                   pTile.Type.ground && !pTile.Type.liquid && !pTile.Type.lava &&
                   !pTile.Type.block;
        }

        private static long TileKey(WorldTile pTile)
        {
            return pTile == null ? long.MinValue : ((long)pTile.x << 32) ^ (uint)pTile.y;
        }

        private static long StableHash(string pValue)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                foreach (char character in pValue ?? "")
                    hash = (hash ^ character) * 1099511628211L;
                return hash;
            }
        }

        private static int DistanceSquared(WorldTile pFirst, WorldTile pSecond)
        {
            int dx = pFirst.x - pSecond.x;
            int dy = pFirst.y - pSecond.y;
            return dx * dx + dy * dy;
        }
    }
}

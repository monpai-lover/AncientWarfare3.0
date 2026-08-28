using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyStrategicEndpointService
    {
        public static WorldTile Resolve(Army pArmy, City pTargetCity,
            int pExcludedTileId = -1)
        {
            Actor captain = SafeCaptain(pArmy);
            WorldTile origin = captain?.current_tile;
            WorldTile targetCenter = SafeCityCenter(pTargetCity);
            if (origin?.data == null || targetCenter?.data == null ||
                pTargetCity?.data == null) return null;

            try { pTargetCity.recalculateNeighbourZones(); }
            catch { }
            var seen = new HashSet<int>();
            int examined = 0;
            WorldTile best = null;
            int bestTier = int.MaxValue;
            long bestDistance = long.MaxValue;
            ScanZones(pTargetCity.border_zones, tier: 0, pTargetCity,
                targetCenter, origin, pExcludedTileId, seen, ref examined,
                ref best, ref bestTier, ref bestDistance);
            ScanZones(pTargetCity.zones, tier: 1, pTargetCity,
                targetCenter, origin, pExcludedTileId, seen, ref examined,
                ref best, ref bestTier, ref bestDistance);
            if (best == null && examined <
                ArmyStrategicEndpointRules.MaximumCandidateTiles)
                ScanAdjacent(pTargetCity, targetCenter, origin,
                    pExcludedTileId, seen, ref examined, ref best,
                    ref bestTier, ref bestDistance);
            // 边界带、内圈、相邻格全部落空时,退回城市中心而不是返回 null。
            // 返回 null 会让 ResolveMovementTarget 得不到任何目标,军队进入
            // March 后既无路线也无目标,任务又不会被回收,导致永久停在原地。
            // 城市中心至少是个方向正确的目标,后续 tick 仍会重新尝试精确端点。
            if (best == null && targetCenter.data.tile_id != pExcludedTileId)
                best = targetCenter;
            return best;
        }

        private static void ScanZones(IEnumerable<TileZone> pZones,
            int tier, City pTargetCity, WorldTile pTargetCenter,
            WorldTile pOrigin, int pExcludedTileId, HashSet<int> pSeen,
            ref int pExamined, ref WorldTile pBest, ref int pBestTier,
            ref long pBestDistance)
        {
            if (pZones == null || pExamined >=
                ArmyStrategicEndpointRules.MaximumCandidateTiles) return;
            try
            {
                foreach (TileZone zone in pZones)
                {
                    if (zone?.tiles == null || zone.city != pTargetCity)
                        continue;
                    WorldTile[] tiles = zone.tiles;
                    for (int i = 0; i < tiles.Length; i++)
                    {
                        Consider(tiles[i], tier, pTargetCity,
                            pTargetCenter, pOrigin, pExcludedTileId, pSeen,
                            ref pExamined, ref pBest, ref pBestTier,
                            ref pBestDistance);
                        if (pExamined >= ArmyStrategicEndpointRules.
                                MaximumCandidateTiles) return;
                    }
                }
            }
            catch { }
        }

        private static void ScanAdjacent(City pTargetCity,
            WorldTile pTargetCenter, WorldTile pOrigin,
            int pExcludedTileId, HashSet<int> pSeen, ref int pExamined,
            ref WorldTile pBest, ref int pBestTier,
            ref long pBestDistance)
        {
            try
            {
                foreach (TileZone zone in pTargetCity.zones)
                {
                    if (zone?.tiles == null || zone.city != pTargetCity)
                        continue;
                    WorldTile[] tiles = zone.tiles;
                    for (int i = 0; i < tiles.Length; i++)
                    {
                        WorldTile[] neighbours = tiles[i]?.neighboursAll;
                        int count = Math.Min(8, neighbours?.Length ?? 0);
                        for (int j = 0; j < count; j++)
                        {
                            Consider(neighbours[j], tier: 2, pTargetCity,
                                pTargetCenter, pOrigin, pExcludedTileId,
                                pSeen, ref pExamined, ref pBest,
                                ref pBestTier, ref pBestDistance);
                            if (pExamined >= ArmyStrategicEndpointRules.
                                    MaximumCandidateTiles) return;
                        }
                    }
                }
            }
            catch { }
        }

        private static void Consider(WorldTile candidate, int tier,
            City pTargetCity, WorldTile pTargetCenter, WorldTile pOrigin,
            int pExcludedTileId, HashSet<int> pSeen, ref int pExamined,
            ref WorldTile pBest, ref int pBestTier,
            ref long pBestDistance)
        {
            int tileId = candidate?.data?.tile_id ?? -1;
            if (tileId < 0 || tileId == pExcludedTileId ||
                !pSeen.Add(tileId)) return;
            pExamined++;
            TileTypeBase type = candidate.Type;
            bool walled = true;
            try { walled = candidate.hasWallsAround(); }
            catch { }
            City candidateCity = null;
            try { candidateCity = candidate.zone?.city; }
            catch { }
            bool belongsToTargetCity = candidateCity == pTargetCity;
            bool belongsToOtherCity = candidateCity?.data != null &&
                                      candidateCity != pTargetCity;
            bool adjacentToTargetCity = !belongsToTargetCity &&
                                        TouchesTargetCity(candidate,
                                            pTargetCity);
            bool onTargetIsland = false;
            try { onTargetIsland = candidate.isSameIsland(pTargetCenter); }
            catch { }
            if (!ArmyStrategicEndpointRules.CanUseCandidate(
                    tileValid: candidate.data != null,
                    ground: type?.ground == true,
                    liquid: type?.liquid == true,
                    ocean: type?.ocean == true,
                    lava: type?.lava == true,
                    blocked: type?.block == true,
                    walled: walled,
                    cityCenter: candidate == pTargetCenter,
                    belongsToTargetCity: belongsToTargetCity,
                    adjacentToTargetCity: adjacentToTargetCity,
                    belongsToOtherCity: belongsToOtherCity,
                    onTargetIsland: onTargetIsland)) return;
            long distance = DistanceSquared(pOrigin, candidate);
            if (!ArmyStrategicEndpointRules.IsBetterCandidate(tier,
                    distance, tileId, pBestTier, pBestDistance,
                    pBest?.data?.tile_id ?? -1)) return;
            pBest = candidate;
            pBestTier = tier;
            pBestDistance = distance;
        }

        private static bool TouchesTargetCity(WorldTile pTile,
            City pTargetCity)
        {
            WorldTile[] neighbours = pTile?.neighboursAll;
            int count = Math.Min(8, neighbours?.Length ?? 0);
            for (int i = 0; i < count; i++)
                try
                {
                    if (neighbours[i]?.zone?.city == pTargetCity)
                        return true;
                }
                catch { }
            return false;
        }

        private static long DistanceSquared(WorldTile pFirst,
            WorldTile pSecond)
        {
            if (pFirst == null || pSecond == null) return long.MaxValue;
            long x = (long)pFirst.x - pSecond.x;
            long y = (long)pFirst.y - pSecond.y;
            return x * x + y * y;
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try { return pArmy?.getCaptain(); }
            catch { return null; }
        }

        private static WorldTile SafeCityCenter(City pCity)
        {
            try { return pCity?.getTile(); }
            catch { return null; }
        }
    }
}

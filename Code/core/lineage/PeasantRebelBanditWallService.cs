using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using Newtonsoft.Json;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditWallService
    {
        private const int REPAIR_BUDGET_PER_YEAR = 12;

        private sealed class WallPoint
        {
            public int x;
            public int y;
        }

        internal static void CaptureAndBuild(Kingdom pKingdom)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return;
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                TopTileLibrary.wall_wild == null) return;

            var points = new List<WallPoint>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom) continue;
                    city.recalculateNeighbourZones();
                    if (city.border_zones == null) continue;
                    foreach (TileZone zone in city.border_zones)
                    {
                        if (zone?.tiles == null) continue;
                        foreach (WorldTile tile in zone.tiles)
                        {
                            if (!IsInsideKingdom(tile, pKingdom) ||
                                !TouchesOutsideKingdom(tile, pKingdom) ||
                                !IsTerrainEligible(tile)) continue;
                            string key = tile.x + ":" + tile.y;
                            if (seen.Add(key))
                                points.Add(new WallPoint
                                    { x = tile.x, y = tile.y });
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Bandit wall boundary capture failed: " +
                                    e.Message);
            }

            PersistAndBuild(pKingdom, points);
        }

        private static void PersistAndBuild(Kingdom pKingdom,
            List<WallPoint> pPoints)
        {
            pPoints.Sort((left, right) =>
            {
                int x = left.x.CompareTo(right.x);
                return x != 0 ? x : left.y.CompareTo(right.y);
            });
            pKingdom.data.set(LineageKeys.MANDATE_REBEL_BANDIT_WALLS,
                Serialize(pPoints));
            pKingdom.data.set(LineageKeys.MANDATE_REBEL_BANDIT_WALL_CURSOR,
                0);

            for (int i = 0; i < pPoints.Count; i++)
            {
                WallPoint point = pPoints[i];
                WorldTile tile = World.world?.GetTile(point.x, point.y);
                if (!IsInsideKingdom(tile, pKingdom) ||
                    !IsTerrainEligible(tile)) continue;
                try
                {
                    tile.setTopTileType(TopTileLibrary.wall_wild);
                }
                catch (Exception e)
                {
                    ModClass.LogWarning("Bandit wooden wall failed: " +
                                        e.Message);
                }
            }
        }

        internal static void RepairYear(Kingdom pKingdom,
            bool pSuppressionActive)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return;
            if (!PeasantRebelRouteRules.ShouldRepairWalls(
                    PeasantRebelRouteService.IsBandit(pKingdom),
                    pSuppressionActive) || TopTileLibrary.wall_wild == null)
                return;

            string json = ReadString(pKingdom,
                LineageKeys.MANDATE_REBEL_BANDIT_WALLS);
            List<WallPoint> points = Deserialize(json);
            if (points == null || points.Count == 0) return;
            int cursor = ReadInt(pKingdom,
                LineageKeys.MANDATE_REBEL_BANDIT_WALL_CURSOR, 0);
            cursor = ((cursor % points.Count) + points.Count) % points.Count;
            int repaired = 0;
            int inspected = 0;
            int budget = PeasantRebelRouteRules.RepairCount(points.Count,
                REPAIR_BUDGET_PER_YEAR);
            while (inspected < points.Count && repaired < budget)
            {
                WallPoint point = points[(cursor + inspected) % points.Count];
                WorldTile tile = World.world?.GetTile(point.x, point.y);
                inspected++;
                if (tile?.top_type == TopTileLibrary.wall_wild) continue;
                if (!CanRestoreAtRecordedPosition(tile)) continue;
                try
                {
                    tile.setTopTileType(TopTileLibrary.wall_wild);
                    repaired++;
                }
                catch { }
            }
            pKingdom.data.set(LineageKeys.MANDATE_REBEL_BANDIT_WALL_CURSOR,
                (cursor + inspected) % points.Count);
        }

        private static bool CanRestoreAtRecordedPosition(WorldTile pTile)
        {
            return IsTerrainEligible(pTile);
        }

        private static bool IsTerrainEligible(WorldTile pTile)
        {
            if (pTile?.Type == null) return false;
            return MandateBorderWallRules.IsWallBuildTileTerrainValid(
                pInsideCity: true,
                pGround: pTile.Type.ground,
                pLiquid: pTile.Type.liquid,
                pLava: pTile.Type.lava,
                pBlock: pTile.Type.block,
                pWall: pTile.Type.wall,
                pRoad: pTile.Type.road,
                pHasTopTile: pTile.top_type != null,
                pHasBuilding: pTile.hasBuilding());
        }

        private static bool IsInsideKingdom(WorldTile pTile,
            Kingdom pKingdom)
        {
            if (pTile == null || pKingdom?.data == null) return false;
            try
            {
                City city = pTile.zone_city ?? pTile.zone?.city;
                return city?.kingdom == pKingdom;
            }
            catch { return false; }
        }

        private static bool TouchesOutsideKingdom(WorldTile pTile,
            Kingdom pKingdom)
        {
            try
            {
                foreach (WorldTile neighbour in pTile.neighboursAll)
                    if (!IsInsideKingdom(neighbour, pKingdom)) return true;
            }
            catch { }
            return false;
        }

        private static string Serialize(IReadOnlyList<WallPoint> pPoints)
        {
            return JsonConvert.SerializeObject(pPoints);
        }

        private static List<WallPoint> Deserialize(string pJson)
        {
            if (string.IsNullOrWhiteSpace(pJson)) return null;
            try
            {
                return JsonConvert.DeserializeObject<List<WallPoint>>(pJson);
            }
            catch { return null; }
        }

        private static string ReadString(Kingdom pKingdom, string pKey)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(pKey, out string value, "");
            return value;
        }

        private static int ReadInt(Kingdom pKingdom, string pKey,
            int pFallback)
        {
            if (pKingdom?.data == null) return pFallback;
            pKingdom.data.get(pKey, out int value, pFallback);
            return value;
        }
    }
}

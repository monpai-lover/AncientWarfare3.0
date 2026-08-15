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

        internal static bool CanCaptureAndBuild(Kingdom pKingdom)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying ||
                pKingdom?.data == null || pKingdom.isRekt() ||
                TopTileLibrary.wall_wild == null) return false;

            bool foundCity = false;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom) continue;
                    foundCity = true;
                    if (!CultiwayStyleCityWallService.TryPlan(city, 1, true,
                            out IReadOnlyList<CultiwayWallPoint> points) ||
                        points.Count == 0) return false;
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Bandit wall preflight failed: " +
                                    e.Message);
                return false;
            }
            return foundCity;
        }

        internal static bool CaptureAndBuild(Kingdom pKingdom)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return false;
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                TopTileLibrary.wall_wild == null) return false;

            var points = new Dictionary<string, WallPoint>(
                StringComparer.Ordinal);
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom) continue;
                    CultiwayStyleCityWallResult result =
                        CultiwayStyleCityWallService.Build(city,
                            TopTileLibrary.wall_wild, 1, true);
                    if (result.Points.Count == 0) return false;
                    foreach (CultiwayWallPoint point in result.Points)
                    {
                        points[point.X + ":" + point.Y] = new WallPoint
                            { x = point.X, y = point.Y };
                    }
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Bandit wall boundary capture failed: " +
                                    e.Message);
                return false;
            }

            if (points.Count == 0) return false;
            PersistAndBuild(pKingdom, new List<WallPoint>(points.Values));
            return true;
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
                if (!CanRestoreAtRecordedPosition(tile)) continue;
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
            if ((points == null || points.Count == 0) &&
                PeasantRebelBanditStateStore.TryResolveActive(pKingdom,
                    out PeasantRebelBanditStrongholdState state))
            {
                points = new List<WallPoint>(state.WallPoints.Count);
                foreach (BanditStrongholdPoint point in state.WallPoints)
                    points.Add(new WallPoint { x = point.X, y = point.Y });
            }
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
            return pTile?.Type != null && !pTile.Type.liquid &&
                !pTile.Type.ocean && !pTile.Type.mountains &&
                !pTile.Type.summit;
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

using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    internal sealed class CultiwayStyleCityWallResult
    {
        internal CultiwayStyleCityWallResult(
            List<CultiwayWallPoint> points, int changed)
        {
            Points = points ?? new List<CultiwayWallPoint>();
            Changed = changed;
        }

        internal IReadOnlyList<CultiwayWallPoint> Points { get; }
        internal int Changed { get; }
    }

    internal static class CultiwayStyleCityWallService
    {
        private const int RadiusMin = 3;
        private const int RadiusMax = 60;
        private const int RemoteUtilityDistance = 16;
        private const int WallMargin = 3;
        private const int TerrainCollectionPadding = 1;

        internal static bool TryPlan(City pCity, int pWidth,
            bool pCarvePassages,
            out IReadOnlyList<CultiwayWallPoint> pPoints)
        {
            pPoints = Array.Empty<CultiwayWallPoint>();
            if (pCity?.data == null || pCity.isRekt() || pWidth <= 0 ||
                World.world == null || MapBox.width <= 0 ||
                MapBox.height <= 0) return false;

            try
            {
                if (!TryGetBuildingBounds(pCity,
                        out CultiwayWallBounds buildingBounds)) return false;
                var wallBounds = new CultiwayWallBounds(
                    buildingBounds.CenterX, buildingBounds.CenterY,
                    buildingBounds.HalfWidth + WallMargin,
                    buildingBounds.HalfHeight + WallMargin);
                WorldTile centerTile = pCity.getTile();
                if (centerTile == null) return false;

                var cityLand = new HashSet<CultiwayWallPoint>();
                var roads = new HashSet<CultiwayWallPoint>();
                CollectCityLandAndRoads(pCity, cityLand, roads);
                if (cityLand.Count == 0) return false;

                HashSet<CultiwayWallPoint> passable = CollectPassableLand(
                    wallBounds);
                HashSet<CultiwayWallPoint> docks = CollectDockTiles(pCity);
                var input = new CultiwayWallGeometryInput(
                    MapBox.width, MapBox.height,
                    new CultiwayWallPoint(centerTile.x, centerTile.y),
                    wallBounds, cityLand, passable, roads, docks,
                    pWidth, pCarvePassages);
                IReadOnlyList<CultiwayWallPoint> computed =
                    CultiwayStyleWallGeometryRules.Compute(input);

                pPoints = computed.Where(point =>
                        CanPlaceAt(pCity,
                            World.world.GetTile(point.X, point.Y)))
                    .OrderBy(point => point.X)
                    .ThenBy(point => point.Y).ToArray();
                return pPoints.Count > 0;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Cultiway-style city wall plan failed: " +
                                    e.Message);
                pPoints = Array.Empty<CultiwayWallPoint>();
                return false;
            }
        }

        internal static CultiwayStyleCityWallResult Build(City pCity,
            TopTileType pWallType, int pWidth, bool pCarvePassages)
        {
            if (pWallType == null || !TryPlan(pCity, pWidth,
                    pCarvePassages,
                    out IReadOnlyList<CultiwayWallPoint> planned))
                return new CultiwayStyleCityWallResult(
                    new List<CultiwayWallPoint>(), 0);

            var placed = new List<CultiwayWallPoint>(planned.Count);
            int changed = 0;
            foreach (CultiwayWallPoint point in planned)
            {
                WorldTile tile = World.world?.GetTile(point.X, point.Y);
                if (!CanPlaceAt(pCity, tile)) continue;
                placed.Add(point);
                if (tile.top_type == pWallType) continue;
                try
                {
                    tile.setTopTileType(pWallType);
                    changed++;
                }
                catch (Exception e)
                {
                    ModClass.LogWarning(
                        "Cultiway-style city wall placement failed: " +
                        e.Message);
                }
            }
            return new CultiwayStyleCityWallResult(placed, changed);
        }

        private static bool TryGetBuildingBounds(City pCity,
            out CultiwayWallBounds pBounds)
        {
            pBounds = default;
            if (pCity?.buildings == null || pCity.buildings.Count == 0)
                return false;

            WorldTile reference = pCity.getBuildingOfType("type_hall")
                                      ?.current_tile ??
                                  pCity.getBuildingOfType("type_bonfire")
                                      ?.current_tile ??
                                  pCity.getTile();
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minY = int.MaxValue;
            int maxY = int.MinValue;
            int count = 0;
            foreach (Building building in pCity.buildings)
            {
                WorldTile tile = building?.current_tile;
                if (tile == null || IsRemoteUtility(building, reference))
                    continue;
                minX = Math.Min(minX, tile.x);
                maxX = Math.Max(maxX, tile.x);
                minY = Math.Min(minY, tile.y);
                maxY = Math.Max(maxY, tile.y);
                count++;
            }
            if (count == 0) return false;

            pBounds = new CultiwayWallBounds(
                (minX + maxX) / 2,
                (minY + maxY) / 2,
                Clamp((maxX - minX) / 2, RadiusMin, RadiusMax),
                Clamp((maxY - minY) / 2, RadiusMin, RadiusMax));
            return true;
        }

        private static bool IsRemoteUtility(Building building,
            WorldTile pReference)
        {
            if (pReference == null || building?.asset == null ||
                building.current_tile == null) return false;
            string type = building.asset.type;
            if (type != "type_windmill" && type != "type_mine" &&
                type != "type_crops") return false;
            return Math.Max(
                Math.Abs(building.current_tile.x - pReference.x),
                Math.Abs(building.current_tile.y - pReference.y)) >
                RemoteUtilityDistance;
        }

        private static void CollectCityLandAndRoads(City pCity,
            HashSet<CultiwayWallPoint> pCityLand,
            HashSet<CultiwayWallPoint> pRoads)
        {
            foreach (TileZone zone in pCity.zones)
            {
                if (zone == null || zone.city != pCity || zone.tiles == null)
                    continue;
                foreach (WorldTile tile in zone.tiles)
                {
                    if (IsWater(tile)) continue;
                    var point = new CultiwayWallPoint(tile.x, tile.y);
                    pCityLand.Add(point);
                    if (tile.Type?.road == true) pRoads.Add(point);
                }
            }
        }

        private static HashSet<CultiwayWallPoint> CollectPassableLand(
            CultiwayWallBounds pBounds)
        {
            int minX = Math.Max(0, pBounds.CenterX - pBounds.HalfWidth -
                TerrainCollectionPadding);
            int maxX = Math.Min(MapBox.width - 1,
                pBounds.CenterX + pBounds.HalfWidth +
                TerrainCollectionPadding);
            int minY = Math.Max(0, pBounds.CenterY - pBounds.HalfHeight -
                TerrainCollectionPadding);
            int maxY = Math.Min(MapBox.height - 1,
                pBounds.CenterY + pBounds.HalfHeight +
                TerrainCollectionPadding);
            var result = new HashSet<CultiwayWallPoint>();
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    WorldTile tile = World.world.GetTile(x, y);
                    if (!IsPassable(tile)) continue;
                    result.Add(new CultiwayWallPoint(x, y));
                }
            }
            return result;
        }

        private static HashSet<CultiwayWallPoint> CollectDockTiles(City pCity)
        {
            var result = new HashSet<CultiwayWallPoint>();
            foreach (Building building in pCity.buildings)
            {
                if (building?.asset == null || !building.asset.docks)
                    continue;
                bool addedBuildingTile = false;
                if (building.tiles != null)
                {
                    foreach (WorldTile tile in building.tiles)
                    {
                        if (tile == null) continue;
                        result.Add(new CultiwayWallPoint(tile.x, tile.y));
                        addedBuildingTile = true;
                    }
                }
                if (!addedBuildingTile && building.current_tile != null)
                {
                    result.Add(new CultiwayWallPoint(
                        building.current_tile.x, building.current_tile.y));
                }
            }
            return result;
        }

        private static bool CanPlaceAt(City pCity, WorldTile tile)
        {
            return tile?.zone?.city == pCity && IsPassable(tile);
        }

        private static bool IsPassable(WorldTile pTile)
        {
            return pTile?.Type != null && !IsWater(pTile) &&
                !pTile.Type.mountains && !pTile.Type.summit;
        }

        private static bool IsWater(WorldTile pTile)
        {
            return pTile?.Type == null || pTile.Type.liquid ||
                pTile.Type.ocean;
        }

        private static int Clamp(int pValue, int pMin, int pMax)
        {
            return Math.Max(pMin, Math.Min(pMax, pValue));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditZoneWallService
    {
        private const int TerrainCollectionPadding = 1;

        internal static bool TryPlan(City pMother,
            IReadOnlyCollection<TileZone> pSelectedZones,
            WorldTile pCenter, out BanditZoneWallPlan pPlan)
        {
            pPlan = null;
            if (pMother?.data == null || pMother.isRekt() ||
                pSelectedZones == null || pSelectedZones.Count == 0 ||
                pCenter == null || World.world == null ||
                MapBox.width <= 0 || MapBox.height <= 0) return false;

            try
            {
                var selected = new HashSet<TileZone>(pSelectedZones.Where(
                    zone => zone != null && zone.city == pMother));
                if (selected.Count != pSelectedZones.Count) return false;

                var territory = new HashSet<CultiwayWallPoint>();
                var roads = new HashSet<CultiwayWallPoint>();
                foreach (TileZone zone in selected)
                {
                    if (zone.tiles == null) return false;
                    foreach (WorldTile tile in zone.tiles)
                    {
                        if (tile == null) continue;
                        var point = new CultiwayWallPoint(tile.x, tile.y);
                        territory.Add(point);
                        if (tile.Type?.road == true) roads.Add(point);
                    }
                }
                if (territory.Count == 0) return false;

                HashSet<CultiwayWallPoint> passable =
                    CollectPassableLand(territory);
                BanditZoneWallPlan computed =
                    PeasantRebelBanditZoneWallRules.Build(
                        MapBox.width, MapBox.height,
                        new CultiwayWallPoint(pCenter.x, pCenter.y),
                        territory, passable, roads);
                CultiwayWallPoint[] closed = computed.ClosedWallPoints
                    .Where(point => CanPlaceAt(pMother, selected,
                        World.world.GetTile(point.X, point.Y)))
                    .OrderBy(point => point.X).ThenBy(point => point.Y)
                    .ToArray();
                CultiwayWallPoint[] opened = computed.WallPoints
                    .Where(point => CanPlaceAt(pMother, selected,
                        World.world.GetTile(point.X, point.Y)))
                    .OrderBy(point => point.X).ThenBy(point => point.Y)
                    .ToArray();
                if (closed.Length == 0 || opened.Length == 0) return false;
                pPlan = new BanditZoneWallPlan(closed, opened);
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Bandit zone-aligned wall plan failed: " + e.Message);
                pPlan = null;
                return false;
            }
        }

        private static HashSet<CultiwayWallPoint> CollectPassableLand(
            HashSet<CultiwayWallPoint> pTerritory)
        {
            int minX = Math.Max(0, pTerritory.Min(point => point.X) -
                TerrainCollectionPadding);
            int maxX = Math.Min(MapBox.width - 1,
                pTerritory.Max(point => point.X) +
                TerrainCollectionPadding);
            int minY = Math.Max(0, pTerritory.Min(point => point.Y) -
                TerrainCollectionPadding);
            int maxY = Math.Min(MapBox.height - 1,
                pTerritory.Max(point => point.Y) +
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

        private static bool CanPlaceAt(City pMother,
            HashSet<TileZone> pSelected, WorldTile pTile)
        {
            return pTile?.zone?.city == pMother &&
                   pSelected.Contains(pTile.zone) && IsPassable(pTile);
        }

        private static bool IsPassable(WorldTile pTile)
        {
            return pTile?.Type != null && !pTile.Type.liquid &&
                   !pTile.Type.ocean && !pTile.Type.mountains &&
                   !pTile.Type.summit;
        }
    }
}

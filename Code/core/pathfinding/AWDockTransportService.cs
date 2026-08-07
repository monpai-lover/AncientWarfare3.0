using System;

namespace AncientWarfare3.core.pathfinding
{
    internal static class AWDockTransportService
    {
        private static readonly AWDockRouteRegistry Registry =
            new AWDockRouteRegistry();

        internal static void Register(Docks pDocks)
        {
            Building building = pDocks?.building;
            WorldTile dockTile = building?.current_tile;
            if (building?.data == null || dockTile?.data == null) return;
            try
            {
                if (pDocks.tiles_ocean == null || pDocks.tiles_ocean.Count == 0)
                    pDocks.recalculateOceanTiles();
                for (int i = 0; i < pDocks.tiles_ocean.Count; i++)
                {
                    int component = pDocks.tiles_ocean[i]?.region?.island?.id ?? -1;
                    if (component < 0) continue;
                    Registry.Register(new AWDockEndpoint(building.data.id,
                        dockTile.data.tile_id, component));
                    return;
                }
            }
            catch { }
        }

        internal static void Remove(Docks pDocks)
        {
            try { Registry.Remove(pDocks?.building?.data?.id ?? -1L); }
            catch { }
        }

        internal static void Clear() => Registry.Clear();

        internal static bool TryResolveRoute(WorldTile pStart, WorldTile pTarget,
            out AWDockRouteCandidate pCandidate)
        {
            pCandidate = default;
            if (pStart == null || pTarget == null) return false;
            AWDockEndpoint[] endpoints = Registry.Snapshot();
            if (endpoints.Length < 2)
            {
                RefreshFromWorld();
                endpoints = Registry.Snapshot();
            }
            for (int first = 0; first < endpoints.Length; first++)
            {
                AWDockEndpoint entry = endpoints[first];
                WorldTile entryTile = World.world?.GetTile(entry.TileId);
                if (!entry.IsValid || entryTile == null ||
                    !pStart.isSameIsland(entryTile)) continue;
                for (int second = 0; second < endpoints.Length; second++)
                {
                    AWDockEndpoint exit = endpoints[second];
                    if (!exit.IsValid || entry.Id == exit.Id ||
                        entry.WaterComponent != exit.WaterComponent) continue;
                    WorldTile exitTile = World.world?.GetTile(exit.TileId);
                    if (exitTile == null || !pTarget.isSameIsland(exitTile)) continue;
                    pCandidate = new AWDockRouteCandidate(entry, exit);
                    return pCandidate.IsValid;
                }
            }
            return false;
        }

        private static void RefreshFromWorld()
        {
            foreach (City city in World.world?.cities)
            {
                if (city?.buildings == null) continue;
                for (int i = 0; i < city.buildings.Count; i++)
                {
                    Building building = city.buildings[i];
                    if (building?.component_docks != null) Register(building.component_docks);
                }
            }
        }
    }
}

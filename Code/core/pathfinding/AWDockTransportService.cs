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
                if (!TryGetLiveEndpoint(entry, out WorldTile entryTile) ||
                    !pStart.isSameIsland(entryTile)) continue;
                for (int second = 0; second < endpoints.Length; second++)
                {
                    AWDockEndpoint exit = endpoints[second];
                    if (!exit.IsValid || entry.Id == exit.Id ||
                        entry.WaterComponent != exit.WaterComponent) continue;
                    if (!TryGetLiveEndpoint(exit, out WorldTile exitTile) ||
                        !pTarget.isSameIsland(exitTile)) continue;
                    pCandidate = new AWDockRouteCandidate(entry, exit);
                    return pCandidate.IsValid;
                }
            }
            return false;
        }

        internal static bool IsEndpointLive(long pDockId)
        {
            AWDockEndpoint[] endpoints = Registry.Snapshot();
            for (int i = 0; i < endpoints.Length; i++)
                if (endpoints[i].Id == pDockId)
                    return TryGetLiveEndpoint(endpoints[i], out _);
            return false;
        }

        private static bool TryGetLiveEndpoint(AWDockEndpoint pEndpoint,
            out WorldTile pTile)
        {
            pTile = ResolveTile(pEndpoint.TileId);
            Building building = pTile?.building;
            Docks docks = building?.component_docks;
            if (!pEndpoint.IsValid || docks == null || building?.data?.id != pEndpoint.Id)
            {
                Registry.Remove(pEndpoint.Id);
                return false;
            }
            try
            {
                for (int i = 0; i < docks.tiles_ocean.Count; i++)
                {
                    WorldTile ocean = docks.tiles_ocean[i];
                    if (ocean != null && ocean.isGoodForBoat() &&
                        ocean.region?.island?.id == pEndpoint.WaterComponent)
                        return true;
                }
            }
            catch { }
            Registry.Remove(pEndpoint.Id);
            return false;
        }

        private static WorldTile ResolveTile(int pTileId)
        {
            WorldTile[] tiles = World.world?.tiles_list;
            return tiles != null && pTileId >= 0 && pTileId < tiles.Length
                ? tiles[pTileId]
                : null;
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

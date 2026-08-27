using System;
using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.pathfinding
{
    internal static class AWDockTransportService
    {
        private static readonly AWDockRouteRegistry Registry =
            new AWDockRouteRegistry();
        private static readonly Dictionary<MapRegion, int> WaterComponents =
            new Dictionary<MapRegion, int>();
        private static readonly Dictionary<long, Building> DockBuildings =
            new Dictionary<long, Building>();
        private static readonly List<AWDockEndpoint> ShoreEndpoints =
            new List<AWDockEndpoint>();
        private static bool _topologyDirty = true;
        private static int _topologyRevision;
        private static long _traversalTopologySourceRevision = -1L;
        private static int _lastTopologyRebuildFrame = -1;

        internal static int TopologyRevision => _topologyRevision;

        internal static void Register(Docks pDocks)
        {
            if (pDocks?.building == null) return;
            MarkTopologyDirty();
        }

        internal static void Remove(Docks pDocks)
        {
            if (pDocks?.building == null) return;
            MarkTopologyDirty();
        }

        internal static void MarkTopologyDirty()
        {
            _topologyDirty = true;
        }

        internal static void Clear()
        {
            Registry.Clear();
            WaterComponents.Clear();
            DockBuildings.Clear();
            ShoreEndpoints.Clear();
            _topologyDirty = true;
            _topologyRevision = 0;
            _traversalTopologySourceRevision = -1L;
            _lastTopologyRebuildFrame = -1;
        }

        internal static bool TryResolveRoute(WorldTile pStart, WorldTile pTarget,
            out AWDockRouteCandidate pCandidate)
        {
            pCandidate = default;
            if (pStart?.data == null || pTarget?.data == null ||
                !AWDockTransportRules.ShouldAttemptDockLookup(
                    pStart.isSameIsland(pTarget))) return false;

            EnsureTopology();
            if (TryResolveDockRoute(pStart, pTarget, out pCandidate))
                return true;
            return TryResolveShoreFallback(pStart, pTarget, out pCandidate);
        }

        // Compatibility entry point for temporary-boat provisioning. It uses
        // the registered shoreline fallback and never performs a new map scan.
        internal static bool TryResolveEmergencyShoreRoute(WorldTile pStart,
            WorldTile pTarget, out AWDockRouteCandidate pCandidate,
            out AWDockRouteFailureReason pReason)
        {
            pCandidate = default;
            pReason = AWDockRouteFailureReason.None;
            if (pStart?.data == null || pTarget?.data == null)
            {
                pReason = AWDockRouteFailureReason.InvalidEndpoints;
                return false;
            }
            if (pStart.isSameIsland(pTarget))
            {
                pReason = AWDockRouteFailureReason.SameIsland;
                return false;
            }
            EnsureTopology();
            if (TryResolveShoreFallback(pStart, pTarget, out pCandidate))
                return true;
            pReason = ShoreEndpoints.Count == 0
                ? AWDockRouteFailureReason.NoStableShore
                : AWDockRouteFailureReason.NoDockOrShorePair;
            return false;
        }

        internal static bool TryResolveRouteTiles(
            AWDockRouteCandidate pRoute, out WorldTile pEntryLand,
            out WorldTile pPickupSea, out WorldTile pDestinationSea,
            out WorldTile pLandingLand)
        {
            pEntryLand = ResolveTile(pRoute.Entry.LandTileId);
            pPickupSea = ResolveTile(pRoute.Entry.OceanTileId);
            pDestinationSea = ResolveTile(pRoute.Exit.OceanTileId);
            pLandingLand = ResolveTile(pRoute.Exit.LandTileId);
            if (!pRoute.IsValid || !IsStableLand(pEntryLand) ||
                !IsStableLand(pLandingLand) ||
                !IsBoatSafe(pPickupSea) || !IsBoatSafe(pDestinationSea))
                return false;

            EnsureTopology();
            return TryResolveWaterComponent(pPickupSea, out int pickup) &&
                   TryResolveWaterComponent(pDestinationSea,
                       out int destination) &&
                   pickup == destination &&
                   pickup == pRoute.Entry.WaterComponent;
        }

        internal static bool IsRouteLive(AWDockRouteCandidate pRoute)
        {
            if (!TryResolveRouteTiles(pRoute, out _, out _, out _, out _))
                return false;
            return (pRoute.Entry.Id <= 0L ||
                    IsEndpointLive(pRoute.Entry.Id)) &&
                   (pRoute.Exit.Id <= 0L ||
                    IsEndpointLive(pRoute.Exit.Id));
        }

        internal static bool TryResolveDestinationTiles(
            AWDockRouteCandidate pRoute, out WorldTile pDestinationSea,
            out WorldTile pLandingLand)
        {
            pDestinationSea = ResolveTile(pRoute.Exit.OceanTileId);
            pLandingLand = ResolveTile(pRoute.Exit.LandTileId);
            if (!pRoute.IsValid || !IsBoatSafe(pDestinationSea) ||
                !IsStableLand(pLandingLand)) return false;
            EnsureTopology();
            if (!TryResolveWaterComponent(pDestinationSea,
                    out int component) ||
                component != pRoute.Entry.WaterComponent) return false;
            return pRoute.Exit.Id <= 0L ||
                   TryGetLiveDockEndpoint(pRoute.Exit,
                       out _, out _);
        }

        internal static bool TryResolveDestination(
            AWDockRouteCandidate pRoute, WorldTile pTarget,
            out AWDockRouteCandidate pCandidate)
        {
            pCandidate = default;
            if (!pRoute.IsValid || pTarget?.data == null) return false;
            EnsureTopology();
            WorldTile entryOcean = ResolveTile(
                pRoute.Entry.OceanTileId);
            if (!IsBoatSafe(entryOcean) ||
                !TryResolveWaterComponent(entryOcean,
                    out int entryComponent) ||
                entryComponent != pRoute.Entry.WaterComponent)
                return false;

            float bestCost = float.PositiveInfinity;
            AWDockEndpoint[] dockEndpoints = Registry.Snapshot();
            for (int i = 0; i < dockEndpoints.Length; i++)
            {
                AWDockEndpoint exit = dockEndpoints[i];
                if (exit.Id == pRoute.Entry.Id ||
                    exit.WaterComponent != entryComponent ||
                    !TryGetLiveDockEndpoint(exit,
                        out WorldTile exitLand,
                        out WorldTile exitOcean) ||
                    !pTarget.isSameIsland(exitLand)) continue;
                ConsiderDestination(pRoute.Entry, exit, entryOcean,
                    exitOcean, exitLand, pTarget,
                    ref pCandidate, ref bestCost);
            }
            for (int i = 0; i < ShoreEndpoints.Count; i++)
            {
                AWDockEndpoint exit = ShoreEndpoints[i];
                WorldTile exitLand = ResolveTile(exit.LandTileId);
                WorldTile exitOcean = ResolveTile(exit.OceanTileId);
                if (exit.WaterComponent != entryComponent ||
                    !IsStableLand(exitLand) || !IsBoatSafe(exitOcean) ||
                    !pTarget.isSameIsland(exitLand)) continue;
                ConsiderDestination(pRoute.Entry, exit, entryOcean,
                    exitOcean, exitLand, pTarget,
                    ref pCandidate, ref bestCost);
            }
            return pCandidate.IsValid;
        }

        private static void ConsiderDestination(AWDockEndpoint pEntry,
            AWDockEndpoint pExit, WorldTile pEntryOcean,
            WorldTile pExitOcean, WorldTile pExitLand, WorldTile pTarget,
            ref AWDockRouteCandidate pCandidate, ref float pBestCost)
        {
            float cost = Distance(pEntryOcean, pExitOcean) +
                         Distance(pExitLand, pTarget);
            if (cost >= pBestCost) return;
            AWTransportRouteSource source = pEntry.Id > 0L &&
                                            pExit.Id > 0L
                ? AWTransportRouteSource.DockPortal
                : AWTransportRouteSource.ShoreFallback;
            var candidate = new AWDockRouteCandidate(source,
                pEntry, pExit, cost);
            if (!candidate.IsValid) return;
            pCandidate = candidate;
            pBestCost = cost;
        }

        internal static bool IsEndpointLive(long pDockId)
        {
            if (pDockId <= 0) return false;
            EnsureTopology();
            AWDockEndpoint[] endpoints = Registry.Snapshot();
            for (int i = 0; i < endpoints.Length; i++)
            {
                if (endpoints[i].Id == pDockId &&
                    TryGetLiveDockEndpoint(endpoints[i], out _, out _))
                    return true;
            }
            return false;
        }

        internal static bool TryGetWaterComponent(WorldTile pOcean,
            out int pComponent)
        {
            pComponent = -1;
            if (pOcean?.data == null || !IsBoatSafe(pOcean)) return false;
            EnsureTopology();
            return TryResolveWaterComponent(pOcean, out pComponent);
        }

        private static bool TryResolveDockRoute(WorldTile pStart,
            WorldTile pTarget, out AWDockRouteCandidate pCandidate)
        {
            pCandidate = default;
            float bestRouteTiles = float.PositiveInfinity;
            AWDockEndpoint[] endpoints = Registry.Snapshot();
            for (int first = 0; first < endpoints.Length; first++)
            {
                AWDockEndpoint entry = endpoints[first];
                if (!TryGetLiveDockEndpoint(entry, out WorldTile entryLand,
                        out WorldTile entryOcean) ||
                    !pStart.isSameIsland(entryLand)) continue;
                for (int second = 0; second < endpoints.Length; second++)
                {
                    AWDockEndpoint exit = endpoints[second];
                    if (entry.Id == exit.Id ||
                        entry.WaterComponent != exit.WaterComponent ||
                        !TryGetLiveDockEndpoint(exit,
                            out WorldTile exitLand,
                            out WorldTile exitOcean) ||
                        !pTarget.isSameIsland(exitLand)) continue;

                    float routeTiles = AWDockTransportRules.
                        EstimateRouteTiles(
                            pStart.x, pStart.y, entryLand.x, entryLand.y,
                            entryOcean.x, entryOcean.y,
                            exitOcean.x, exitOcean.y,
                            exitLand.x, exitLand.y,
                            pTarget.x, pTarget.y);
                    if (routeTiles >= bestRouteTiles) continue;
                    var candidate = new AWDockRouteCandidate(
                        AWTransportRouteSource.DockPortal, entry, exit,
                        routeTiles);
                    if (!candidate.IsValid) continue;
                    pCandidate = candidate;
                    bestRouteTiles = routeTiles;
                }
            }
            return pCandidate.IsValid;
        }

        private static bool TryResolveShoreFallback(WorldTile pStart,
            WorldTile pTarget, out AWDockRouteCandidate pCandidate)
        {
            pCandidate = default;
            var sourceByComponent =
                new Dictionary<int, AWDockEndpoint>();
            var sourceCostByComponent =
                new Dictionary<int, float>();
            var targetByComponent =
                new Dictionary<int, AWDockEndpoint>();
            var targetCostByComponent =
                new Dictionary<int, float>();

            for (int i = 0; i < ShoreEndpoints.Count; i++)
            {
                AWDockEndpoint endpoint = ShoreEndpoints[i];
                WorldTile land = ResolveTile(endpoint.LandTileId);
                if (!IsStableLand(land)) continue;
                int component = endpoint.WaterComponent;
                if (pStart.isSameIsland(land))
                {
                    float cost = Distance(pStart, land);
                    if (!sourceCostByComponent.TryGetValue(component,
                            out float old) || cost < old)
                    {
                        sourceCostByComponent[component] = cost;
                        sourceByComponent[component] = endpoint;
                    }
                }
                if (pTarget.isSameIsland(land))
                {
                    float cost = Distance(pTarget, land);
                    if (!targetCostByComponent.TryGetValue(component,
                            out float old) || cost < old)
                    {
                        targetCostByComponent[component] = cost;
                        targetByComponent[component] = endpoint;
                    }
                }
            }

            float best = float.PositiveInfinity;
            foreach (KeyValuePair<int, AWDockEndpoint> pair in
                     sourceByComponent)
            {
                if (!targetByComponent.TryGetValue(pair.Key,
                        out AWDockEndpoint exit)) continue;
                AWDockEndpoint entry = pair.Value;
                if (entry.LandTileId == exit.LandTileId) continue;
                WorldTile entryOcean = ResolveTile(entry.OceanTileId);
                WorldTile exitOcean = ResolveTile(exit.OceanTileId);
                if (!IsBoatSafe(entryOcean) || !IsBoatSafe(exitOcean))
                    continue;
                float routeTiles = sourceCostByComponent[pair.Key] +
                    Distance(entryOcean, exitOcean) +
                    targetCostByComponent[pair.Key];
                if (routeTiles >= best) continue;
                var candidate = new AWDockRouteCandidate(
                    AWTransportRouteSource.ShoreFallback, entry, exit,
                    routeTiles);
                if (!candidate.IsValid) continue;
                pCandidate = candidate;
                best = routeTiles;
            }
            return pCandidate.IsValid;
        }

        private static void EnsureTopology()
        {
            AWTraversalCache cache = AWPathfindingBootstrap.Cache;
            long sourceRevision = cache.TopologySourceRevision;
            int currentFrame = Time.frameCount;
            if (!AWDockTransportRules.ShouldRebuildTopology(
                    _topologyDirty, _traversalTopologySourceRevision,
                    sourceRevision, cache.DirtyTileCount,
                    _lastTopologyRebuildFrame, currentFrame)) return;
            RebuildTopology();
            _lastTopologyRebuildFrame = currentFrame;
        }

        private static void RebuildTopology()
        {
            Registry.Clear();
            WaterComponents.Clear();
            DockBuildings.Clear();
            ShoreEndpoints.Clear();

            WorldTile[] tiles = World.world?.tiles_list;
            if (tiles == null)
            {
                _topologyDirty = false;
                _traversalTopologySourceRevision =
                    AWPathfindingBootstrap.Cache.TopologySourceRevision;
                return;
            }

            int nextComponent = 0;
            var visitedRegions = new HashSet<MapRegion>();
            for (int i = 0; i < tiles.Length; i++)
            {
                MapRegion region = tiles[i]?.region;
                if (region == null || visitedRegions.Contains(region) ||
                    !IsOceanRegion(region)) continue;
                int component = nextComponent++;
                FloodOceanComponent(region, component, visitedRegions);
            }

            RegisterLiveDocks();
            BuildShoreEndpoints(tiles);
            _topologyDirty = false;
            _traversalTopologySourceRevision =
                AWPathfindingBootstrap.Cache.TopologySourceRevision;
            if (_topologyRevision < int.MaxValue) _topologyRevision++;
        }

        private static void FloodOceanComponent(MapRegion pStart,
            int pComponent, HashSet<MapRegion> pVisited)
        {
            var queue = new Queue<MapRegion>();
            pVisited.Add(pStart);
            queue.Enqueue(pStart);
            while (queue.Count > 0)
            {
                MapRegion current = queue.Dequeue();
                WaterComponents[current] = pComponent;
                List<MapRegion> neighbours = current.neighbours;
                int count = neighbours?.Count ?? 0;
                for (int i = 0; i < count; i++)
                {
                    MapRegion next = neighbours[i];
                    if (next == null || pVisited.Contains(next) ||
                        !IsOceanRegion(next)) continue;
                    pVisited.Add(next);
                    queue.Enqueue(next);
                }
            }
        }

        private static void RegisterLiveDocks()
        {
            foreach (City city in World.world?.cities)
            {
                if (city?.buildings == null) continue;
                for (int i = 0; i < city.buildings.Count; i++)
                {
                    Building building = city.buildings[i];
                    if (!IsUsableDock(building)) continue;
                    Docks docks = building.component_docks;
                    try
                    {
                        if (docks.tiles_ocean == null ||
                            docks.tiles_ocean.Count == 0)
                            docks.recalculateOceanTiles();
                    }
                    catch { continue; }

                    WorldTile land = building.current_tile;
                    if (land?.data == null) continue;
                    var registeredComponents = new HashSet<int>();
                    int oceanCount = docks.tiles_ocean?.Count ?? 0;
                    for (int oceanIndex = 0;
                         oceanIndex < oceanCount; oceanIndex++)
                    {
                        WorldTile ocean = docks.tiles_ocean[oceanIndex];
                        int component;
                        if (!IsBoatSafe(ocean) ||
                            !TryResolveWaterComponent(ocean,
                                out int capturedComponent) ||
                            (component = AWDockEndpointRules.ResolveWaterComponent(
                                    capturedComponent,
                                    ocean.region?.island?.id ?? -1)) < 0 ||
                            !registeredComponents.Add(component)) continue;
                        Registry.Register(new AWDockEndpoint(
                            building.data.id, land.data.tile_id,
                            ocean.data.tile_id, component));
                        DockBuildings[building.data.id] = building;
                    }
                }
            }
        }

        private static void BuildShoreEndpoints(WorldTile[] pTiles)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < pTiles.Length; i++)
            {
                WorldTile land = pTiles[i];
                if (!IsStableLand(land)) continue;
                WorldTile[] neighbours = land.neighboursAll;
                int count = neighbours?.Length ?? 0;
                for (int n = 0; n < count; n++)
                {
                    WorldTile ocean = neighbours[n];
                    if (!IsBoatSafe(ocean) ||
                        !TryResolveWaterComponent(ocean,
                            out int component)) continue;
                    string key = land.data.tile_id + ":" + component;
                    if (!seen.Add(key)) continue;
                    ShoreEndpoints.Add(new AWDockEndpoint(0L,
                        land.data.tile_id, ocean.data.tile_id, component));
                }
            }
        }

        private static bool TryGetLiveDockEndpoint(AWDockEndpoint pEndpoint,
            out WorldTile pLand, out WorldTile pOcean)
        {
            pLand = ResolveTile(pEndpoint.LandTileId);
            pOcean = ResolveTile(pEndpoint.OceanTileId);
            if (!pEndpoint.IsDockPortal || !IsStableLand(pLand) ||
                !IsBoatSafe(pOcean)) return false;
            Building building = FindDockBuilding(pEndpoint.Id);
            if (!IsUsableDock(building) ||
                building.current_tile?.data?.tile_id !=
                    pEndpoint.LandTileId) return false;
            return TryResolveWaterComponent(pOcean, out int component) &&
                   component == pEndpoint.WaterComponent;
        }

        private static Building FindDockBuilding(long pDockId)
        {
            if (pDockId <= 0) return null;
            if (!DockBuildings.TryGetValue(pDockId,
                    out Building building)) return null;
            if (IsUsableDock(building) && building.data.id == pDockId)
                return building;
            DockBuildings.Remove(pDockId);
            return null;
        }

        private static bool TryResolveWaterComponent(WorldTile pOcean,
            out int pComponent)
        {
            pComponent = -1;
            return pOcean?.region != null &&
                   WaterComponents.TryGetValue(pOcean.region,
                       out pComponent);
        }

        private static bool IsOceanRegion(MapRegion pRegion)
        {
            List<WorldTile> tiles = pRegion?.tiles;
            int count = tiles?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                WorldTile tile = tiles[i];
                if (tile?.Type?.ocean == true) return true;
            }
            return false;
        }

        private static bool IsUsableDock(Building pBuilding)
        {
            try
            {
                return pBuilding?.data != null &&
                       pBuilding.component_docks != null &&
                       !pBuilding.isUnderConstruction() &&
                       pBuilding.isUsable();
            }
            catch { return false; }
        }

        private static bool IsStableLand(WorldTile pTile)
        {
            TileTypeBase type = pTile?.Type;
            return pTile?.data != null && type != null &&
                   !type.ocean && !type.liquid && !type.lava &&
                   !type.block;
        }

        private static bool IsBoatSafe(WorldTile pTile)
        {
            try
            {
                return pTile?.data != null && pTile.isGoodForBoat();
            }
            catch { return false; }
        }

        private static WorldTile ResolveTile(int pTileId)
        {
            WorldTile[] tiles = World.world?.tiles_list;
            return tiles != null && pTileId >= 0 && pTileId < tiles.Length
                ? tiles[pTileId]
                : null;
        }

        private static float Distance(WorldTile pLeft, WorldTile pRight)
        {
            if (pLeft == null || pRight == null)
                return float.PositiveInfinity;
            float dx = pLeft.x - pRight.x;
            float dy = pLeft.y - pRight.y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}

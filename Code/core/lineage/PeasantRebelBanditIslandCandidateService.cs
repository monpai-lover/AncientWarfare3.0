using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditIslandCandidateService
    {
        private sealed class IslandGeometry
        {
            internal TileIsland Island;
            internal readonly List<WorldTile> Buildable =
                new List<WorldTile>();
            internal readonly List<WorldTile> Coastal =
                new List<WorldTile>();
        }

        private static readonly Dictionary<int, IslandGeometry> Geometry =
            new Dictionary<int, IslandGeometry>();
        private static int _generation = int.MinValue;
        private static int _topologyRevision = int.MinValue;

        internal static bool TrySelect(City pOldStronghold,
            Kingdom pSuppressor,
            out PeasantRebelBanditIslandCandidate pCandidate)
        {
            pCandidate = null;
            WorldTile origin = pOldStronghold?.getTile();
            if (origin?.data == null) return false;
            EnsureGeometry();

            var candidates = new Dictionary<long,
                PeasantRebelBanditIslandCandidate>();
            var facts = new List<BanditIslandCandidateFact>();
            foreach (IslandGeometry geometry in Geometry.Values)
            {
                TileIsland island = geometry.Island;
                if (island == null || island == origin.region?.island ||
                    geometry.Buildable.Count == 0 ||
                    geometry.Coastal.Count == 0) continue;
                bool hasCity = HasLiveCity(geometry);
                bool hasStronghold = HasStronghold(geometry);
                bool hostileOccupied = HasHostilePresence(geometry,
                    pSuppressor);
                bool eligible = PeasantRebelBanditIslandRules.
                    IsEligibleIsland(hasCity, hasStronghold,
                        geometry.Buildable.Count,
                        geometry.Coastal.Count > 0, hostileOccupied);
                if (!eligible) continue;

                WorldTile founding = null;
                AWDockRouteCandidate route = default;
                for (int i = 0; i < geometry.Coastal.Count; i++)
                {
                    WorldTile tile = geometry.Coastal[i];
                    if (!IsBuildable(tile) ||
                        !AWDockTransportService.TryResolveRoute(origin,
                            tile, out route)) continue;
                    founding = tile;
                    break;
                }
                if (founding?.data == null || !route.IsValid) continue;
                int safety = ResolveSafetyScore(geometry, pSuppressor);
                int routeCost = (int)Math.Min(int.MaxValue,
                    Math.Max(0f, route.EstimatedRouteTiles));
                var candidate = new PeasantRebelBanditIslandCandidate
                {
                    Island = island,
                    LandingTile = founding,
                    FoundingTile = founding,
                    BuildableArea = geometry.Buildable.Count,
                    SafetyScore = safety,
                    RouteCost = routeCost
                };
                candidates[island.id] = candidate;
                facts.Add(new BanditIslandCandidateFact(island.id, true,
                    safety, routeCost, geometry.Buildable.Count));
            }

            BanditIslandCandidateFact selected =
                PeasantRebelBanditIslandRules.RankIslands(facts).
                    FirstOrDefault();
            return selected != null &&
                candidates.TryGetValue(selected.IslandId, out pCandidate);
        }

        internal static void Clear()
        {
            Geometry.Clear();
            _generation = int.MinValue;
            _topologyRevision = int.MinValue;
        }

        private static void EnsureGeometry()
        {
            int generation = AWSimulationTime.Generation;
            int topology = AWDockTransportService.TopologyRevision;
            if (_generation == generation &&
                _topologyRevision == topology) return;
            Geometry.Clear();
            _generation = generation;
            _topologyRevision = topology;
            ListPool<TileIsland> islands =
                World.world?.islands_calculator?.islands;
            if (islands == null) return;
            for (int i = 0; i < islands.Count; i++)
            {
                TileIsland island = islands[i];
                if (island == null || island.type != TileLayerType.Ground)
                    continue;
                var geometry = new IslandGeometry { Island = island };
                List<MapRegion> regions = island.regions.getSimpleList();
                for (int r = 0; r < regions.Count; r++)
                {
                    List<WorldTile> tiles = regions[r]?.tiles;
                    int count = tiles?.Count ?? 0;
                    for (int t = 0; t < count; t++)
                    {
                        WorldTile tile = tiles[t];
                        if (!IsBuildable(tile)) continue;
                        geometry.Buildable.Add(tile);
                        if (HasWaterNeighbour(tile))
                            geometry.Coastal.Add(tile);
                    }
                }
                Geometry[island.id] = geometry;
            }
        }

        private static bool IsBuildable(WorldTile pTile)
        {
            return pTile?.data != null && pTile.Type.ground &&
                !pTile.hasBuilding() && pTile.zone != null &&
                pTile.zone.city == null;
        }

        private static bool HasWaterNeighbour(WorldTile pTile)
        {
            WorldTile[] neighbours = pTile?.neighboursAll;
            int count = neighbours?.Length ?? 0;
            for (int i = 0; i < count; i++)
            {
                WorldTile neighbour = neighbours[i];
                if (neighbour?.data != null && !neighbour.Type.ground &&
                    neighbour.region?.island?.type == TileLayerType.Ocean)
                    return true;
            }
            return false;
        }

        private static bool HasLiveCity(IslandGeometry pGeometry)
        {
            for (int i = 0; i < pGeometry.Buildable.Count; i++)
                if (pGeometry.Buildable[i]?.zone?.city?.data != null)
                    return true;
            List<MapRegion> regions = pGeometry.Island.regions.
                getSimpleList();
            for (int r = 0; r < regions.Count; r++)
            foreach (WorldTile tile in regions[r].tiles)
                if (tile?.zone?.city?.data != null &&
                    !tile.zone.city.isRekt()) return true;
            return false;
        }

        private static bool HasStronghold(IslandGeometry pGeometry)
        {
            List<MapRegion> regions = pGeometry.Island.regions.
                getSimpleList();
            for (int r = 0; r < regions.Count; r++)
            foreach (WorldTile tile in regions[r].tiles)
            {
                City city = tile?.zone?.city;
                if (city?.data != null &&
                    PeasantRebelBanditStrongholdService.IsStronghold(city))
                    return true;
            }
            return false;
        }

        private static bool HasHostilePresence(IslandGeometry pGeometry,
            Kingdom pSuppressor)
        {
            if (pSuppressor?.data == null) return false;
            List<Actor> actors = pGeometry.Island.actors;
            for (int i = 0; i < actors.Count; i++)
                if (actors[i]?.kingdom == pSuppressor) return true;
            return false;
        }

        private static int ResolveSafetyScore(IslandGeometry pGeometry,
            Kingdom pSuppressor)
        {
            if (pSuppressor?.data == null) return 100;
            int hostileActors = 0;
            List<Actor> actors = pGeometry.Island.actors;
            for (int i = 0; i < actors.Count; i++)
                if (actors[i]?.kingdom == pSuppressor) hostileActors++;
            return Math.Max(0, 100 - hostileActors * 20);
        }
    }
}

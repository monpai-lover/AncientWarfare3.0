using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public static class HierarchicalVassalBoundaryTopologyRules
    {
        public static BoundaryTier Classify(
            BoundaryCellFacts pLeft,
            BoundaryCellFacts pRight,
            BoundaryDisplayLayer pLayer)
        {
            bool leftVisible = HasDisplayedOwner(pLeft);
            bool rightVisible = HasDisplayedOwner(pRight);
            if (!leftVisible && !rightVisible)
                return BoundaryTier.None;
            if (!leftVisible || !rightVisible)
                return BoundaryTier.SuzerainSystem;
            if (pLeft.SystemId != pRight.SystemId)
                return BoundaryTier.SuzerainSystem;
            if (pLeft.RealmId != pRight.RealmId)
                return BoundaryTier.VassalRealm;
            if (pLayer == BoundaryDisplayLayer.Cities &&
                pLeft.CityId != pRight.CityId)
            {
                return BoundaryTier.City;
            }
            return BoundaryTier.None;
        }

        public static BoundaryTopologyDraft Extract(
            BoundaryCellRaster pRaster,
            BoundaryDisplayLayer pLayer)
        {
            if (pRaster == null)
                throw new ArgumentNullException(nameof(pRaster));

            var edges = new List<BoundaryRawEdge>();
            EmitVerticalEdges(pRaster, pLayer, edges);
            EmitHorizontalEdges(pRaster, pLayer, edges);
            return BuildChains(edges);
        }

        public static bool OwnsSeamEdge(
            BoundaryChunkKey pChunk,
            BoundaryRawEdge pEdge)
        {
            BoundaryChunkKey owner;
            if (pEdge.Start.X == pEdge.End.X)
            {
                int edgeX = pEdge.Start.X;
                int cellX = edgeX > 0 &&
                            edgeX % HierarchicalVassalBoundaryChunkRules.ChunkSize == 0
                    ? edgeX - 1
                    : edgeX;
                int cellY = Math.Min(pEdge.Start.Y, pEdge.End.Y);
                owner = HierarchicalVassalBoundaryChunkRules.ForTile(cellX, cellY);
            }
            else
            {
                int edgeY = pEdge.Start.Y;
                int cellY = edgeY > 0 &&
                            edgeY % HierarchicalVassalBoundaryChunkRules.ChunkSize == 0
                    ? edgeY - 1
                    : edgeY;
                int cellX = Math.Min(pEdge.Start.X, pEdge.End.X);
                owner = HierarchicalVassalBoundaryChunkRules.ForTile(cellX, cellY);
            }
            return owner.Equals(pChunk);
        }

        private static void EmitVerticalEdges(
            BoundaryCellRaster pRaster,
            BoundaryDisplayLayer pLayer,
            List<BoundaryRawEdge> pEdges)
        {
            for (int x = pRaster.OriginX; x <= pRaster.MaxXExclusive; x++)
            {
                for (int y = pRaster.OriginY; y < pRaster.MaxYExclusive; y++)
                {
                    BoundaryCellFacts west = pRaster.GetOrInvalid(x - 1, y);
                    BoundaryCellFacts east = pRaster.GetOrInvalid(x, y);
                    BoundaryTier tier = Classify(west, east, pLayer);
                    if (tier == BoundaryTier.None)
                        continue;
                    pEdges.Add(new BoundaryRawEdge(
                        new BoundaryGridPoint(x, y),
                        new BoundaryGridPoint(x, y + 1),
                        tier,
                        OwnerId(west, tier),
                        OwnerId(east, tier)));
                }
            }
        }

        private static void EmitHorizontalEdges(
            BoundaryCellRaster pRaster,
            BoundaryDisplayLayer pLayer,
            List<BoundaryRawEdge> pEdges)
        {
            for (int y = pRaster.OriginY; y <= pRaster.MaxYExclusive; y++)
            {
                for (int x = pRaster.OriginX; x < pRaster.MaxXExclusive; x++)
                {
                    BoundaryCellFacts south = pRaster.GetOrInvalid(x, y - 1);
                    BoundaryCellFacts north = pRaster.GetOrInvalid(x, y);
                    BoundaryTier tier = Classify(north, south, pLayer);
                    if (tier == BoundaryTier.None)
                        continue;
                    pEdges.Add(new BoundaryRawEdge(
                        new BoundaryGridPoint(x, y),
                        new BoundaryGridPoint(x + 1, y),
                        tier,
                        OwnerId(north, tier),
                        OwnerId(south, tier)));
                }
            }
        }

        private static BoundaryTopologyDraft BuildChains(
            IReadOnlyList<BoundaryRawEdge> pEdges)
        {
            var adjacency = new Dictionary<BoundaryGridPoint, List<int>>();
            for (int i = 0; i < pEdges.Count; i++)
            {
                AddIncident(adjacency, pEdges[i].Start, i);
                AddIncident(adjacency, pEdges[i].End, i);
            }

            var protectedVertices = new HashSet<BoundaryGridPoint>();
            foreach (KeyValuePair<BoundaryGridPoint, List<int>> pair in adjacency)
            {
                pair.Value.Sort();
                if (IsProtected(pair.Key, pair.Value, pEdges))
                    protectedVertices.Add(pair.Key);
            }

            var visited = new bool[pEdges.Count];
            var openChains = new List<BoundaryChain>();
            var protectedOrder = new List<BoundaryGridPoint>(protectedVertices);
            protectedOrder.Sort();
            foreach (BoundaryGridPoint start in protectedOrder)
            {
                List<int> incident = adjacency[start];
                for (int i = 0; i < incident.Count; i++)
                {
                    if (!visited[incident[i]])
                    {
                        openChains.Add(TraceOpen(
                            start, incident[i], pEdges, adjacency,
                            protectedVertices, visited));
                    }
                }
            }

            var closedChains = new List<BoundaryChain>();
            for (int i = 0; i < pEdges.Count; i++)
            {
                if (!visited[i])
                    closedChains.Add(TraceClosed(i, pEdges, adjacency, visited));
            }

            return new BoundaryTopologyDraft(
                pEdges, openChains, closedChains, protectedVertices);
        }

        private static BoundaryChain TraceOpen(
            BoundaryGridPoint pStart,
            int pFirstEdge,
            IReadOnlyList<BoundaryRawEdge> pEdges,
            IReadOnlyDictionary<BoundaryGridPoint, List<int>> pAdjacency,
            HashSet<BoundaryGridPoint> pProtected,
            bool[] pVisited)
        {
            var points = new List<BoundaryGridPoint> { pStart };
            var edges = new List<BoundaryRawEdge>();
            BoundaryGridPoint current = pStart;
            int edgeIndex = pFirstEdge;
            while (edgeIndex >= 0 && !pVisited[edgeIndex])
            {
                pVisited[edgeIndex] = true;
                BoundaryRawEdge edge = pEdges[edgeIndex];
                edges.Add(edge);
                current = edge.Other(current);
                points.Add(current);
                if (pProtected.Contains(current))
                    break;
                edgeIndex = NextUnvisited(pAdjacency[current], pVisited);
            }
            return new BoundaryChain(points, edges, pClosed: false);
        }

        private static BoundaryChain TraceClosed(
            int pFirstEdge,
            IReadOnlyList<BoundaryRawEdge> pEdges,
            IReadOnlyDictionary<BoundaryGridPoint, List<int>> pAdjacency,
            bool[] pVisited)
        {
            BoundaryGridPoint start = pEdges[pFirstEdge].Start;
            BoundaryGridPoint current = start;
            int edgeIndex = pFirstEdge;
            var points = new List<BoundaryGridPoint> { start };
            var edges = new List<BoundaryRawEdge>();
            while (edgeIndex >= 0 && !pVisited[edgeIndex])
            {
                pVisited[edgeIndex] = true;
                BoundaryRawEdge edge = pEdges[edgeIndex];
                edges.Add(edge);
                current = edge.Other(current);
                points.Add(current);
                if (current.Equals(start))
                    break;
                edgeIndex = NextUnvisited(pAdjacency[current], pVisited);
            }
            return new BoundaryChain(points, edges, pClosed: true);
        }

        private static int NextUnvisited(
            IReadOnlyList<int> pIncident,
            bool[] pVisited)
        {
            for (int i = 0; i < pIncident.Count; i++)
            {
                if (!pVisited[pIncident[i]])
                    return pIncident[i];
            }
            return -1;
        }

        private static bool IsProtected(
            BoundaryGridPoint pPoint,
            IReadOnlyList<int> pIncident,
            IReadOnlyList<BoundaryRawEdge> pEdges)
        {
            if (pIncident.Count != 2)
                return true;
            BoundaryRawEdge first = pEdges[pIncident[0]];
            BoundaryRawEdge second = pEdges[pIncident[1]];
            if (first.Tier != second.Tier || !SameOwnerPair(first, second))
                return true;
            int chunkSize = HierarchicalVassalBoundaryChunkRules.ChunkSize;
            return (pPoint.X > 0 && pPoint.X % chunkSize == 0) ||
                   (pPoint.Y > 0 && pPoint.Y % chunkSize == 0);
        }

        private static bool SameOwnerPair(
            BoundaryRawEdge pFirst,
            BoundaryRawEdge pSecond)
        {
            return pFirst.LeftOwnerId == pSecond.LeftOwnerId &&
                   pFirst.RightOwnerId == pSecond.RightOwnerId ||
                   pFirst.LeftOwnerId == pSecond.RightOwnerId &&
                   pFirst.RightOwnerId == pSecond.LeftOwnerId;
        }

        private static void AddIncident(
            IDictionary<BoundaryGridPoint, List<int>> pAdjacency,
            BoundaryGridPoint pPoint,
            int pEdgeIndex)
        {
            if (!pAdjacency.TryGetValue(pPoint, out List<int> incident))
            {
                incident = new List<int>(4);
                pAdjacency.Add(pPoint, incident);
            }
            incident.Add(pEdgeIndex);
        }

        private static bool HasDisplayedOwner(BoundaryCellFacts pCell)
        {
            return pCell.IsLand && pCell.SystemId >= 0;
        }

        private static long OwnerId(BoundaryCellFacts pCell, BoundaryTier pTier)
        {
            if (!HasDisplayedOwner(pCell))
                return -1;
            switch (pTier)
            {
                case BoundaryTier.City:
                    return pCell.CityId;
                case BoundaryTier.VassalRealm:
                    return pCell.RealmId;
                default:
                    return pCell.SystemId;
            }
        }
    }
}

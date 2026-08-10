using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.pathfinding
{
    // Immutable region adjacency captured from an AWTraversalGeneration.
    // It contains no WorldBox or Unity references and is safe to share with workers.
    internal sealed class AWRegionTopologySnapshot
    {
        private readonly Dictionary<int, AWRegionNode> _regions;

        private AWRegionTopologySnapshot(int pRevision,
            Dictionary<int, AWRegionNode> pRegions)
        {
            Revision = Math.Max(1, pRevision);
            _regions = pRegions ?? new Dictionary<int, AWRegionNode>();
        }

        internal int Revision { get; }

        internal bool TryGetRegion(int pRegionId, out AWRegionNode pRegion)
        {
            return _regions.TryGetValue(pRegionId, out pRegion);
        }

        internal static AWRegionTopologySnapshot Build(
            AWTileTraversalSnapshot[][] pChunks, int pWidth, int pHeight,
            int pChunkSize)
        {
            var builders = new Dictionary<int, RegionBuilder>();
            if (pChunks == null || pWidth <= 0 || pHeight <= 0)
                return new AWRegionTopologySnapshot(1, null);

            int chunksWide = Math.Max(1, (pWidth + pChunkSize - 1) / pChunkSize);
            int tileCount = pWidth * pHeight;
            for (int tileId = 0; tileId < tileCount; tileId++)
            {
                if (!TryGetTile(pChunks, pWidth, pHeight, pChunkSize, chunksWide,
                        tileId, out AWTileTraversalSnapshot tile) ||
                    tile.RegionId < 0)
                    continue;

                if (!builders.TryGetValue(tile.RegionId, out RegionBuilder builder))
                {
                    builder = new RegionBuilder();
                    builders.Add(tile.RegionId, builder);
                }
                if (builder.CenterTileId < 0) builder.CenterTileId = tile.Id;
            }

            for (int tileId = 0; tileId < tileCount; tileId++)
            {
                if (!TryGetTile(pChunks, pWidth, pHeight, pChunkSize, chunksWide,
                        tileId, out AWTileTraversalSnapshot tile) ||
                    tile.RegionId < 0 || !builders.TryGetValue(tile.RegionId,
                        out RegionBuilder source))
                    continue;

                for (int i = 0; i < tile.NeighborCount; i++)
                {
                    int neighbourId = tile.GetNeighbor(i);
                    if (!TryGetTile(pChunks, pWidth, pHeight, pChunkSize, chunksWide,
                            neighbourId, out AWTileTraversalSnapshot neighbour) ||
                        neighbour.RegionId < 0 || neighbour.RegionId == tile.RegionId)
                        continue;

                    source.Neighbours.Add(neighbour.RegionId);
                    if (builders.TryGetValue(neighbour.RegionId,
                            out RegionBuilder target))
                        target.Neighbours.Add(tile.RegionId);
                }
            }

            var result = new Dictionary<int, AWRegionNode>(builders.Count);
            foreach (KeyValuePair<int, RegionBuilder> pair in builders)
            {
                int[] neighbours = new int[pair.Value.Neighbours.Count];
                pair.Value.Neighbours.CopyTo(neighbours);
                Array.Sort(neighbours);
                result.Add(pair.Key, new AWRegionNode(pair.Key,
                    pair.Value.CenterTileId, neighbours));
            }
            return new AWRegionTopologySnapshot(1, result);
        }

        internal static AWRegionTopologySnapshot Build(
            AWTraversalGeneration pGeneration)
        {
            var builders = new Dictionary<int, RegionBuilder>();
            if (pGeneration == null || pGeneration.Width <= 0 ||
                pGeneration.Height <= 0)
                return new AWRegionTopologySnapshot(1, null);

            int tileCount = pGeneration.TileCount;
            for (int tileId = 0; tileId < tileCount; tileId++)
            {
                if (!pGeneration.TryGet(tileId,
                        out AWTileTraversalSnapshot tile) ||
                    tile.RegionId < 0) continue;
                if (!builders.TryGetValue(tile.RegionId,
                        out RegionBuilder builder))
                {
                    builder = new RegionBuilder();
                    builders.Add(tile.RegionId, builder);
                }
                if (builder.CenterTileId < 0) builder.CenterTileId = tile.Id;
            }

            for (int tileId = 0; tileId < tileCount; tileId++)
            {
                if (!pGeneration.TryGet(tileId,
                        out AWTileTraversalSnapshot tile) ||
                    tile.RegionId < 0 || !builders.TryGetValue(tile.RegionId,
                        out RegionBuilder source)) continue;
                for (int index = 0; index < tile.NeighborCount; index++)
                {
                    int neighbourId = tile.GetNeighbor(index);
                    if (!pGeneration.TryGet(neighbourId,
                            out AWTileTraversalSnapshot neighbour) ||
                        neighbour.RegionId < 0 ||
                        neighbour.RegionId == tile.RegionId) continue;
                    source.Neighbours.Add(neighbour.RegionId);
                    if (builders.TryGetValue(neighbour.RegionId,
                            out RegionBuilder target))
                        target.Neighbours.Add(tile.RegionId);
                }
            }

            var result = new Dictionary<int, AWRegionNode>(builders.Count);
            foreach (KeyValuePair<int, RegionBuilder> pair in builders)
            {
                int[] neighbours = new int[pair.Value.Neighbours.Count];
                pair.Value.Neighbours.CopyTo(neighbours);
                Array.Sort(neighbours);
                result.Add(pair.Key, new AWRegionNode(pair.Key,
                    pair.Value.CenterTileId, neighbours));
            }
            return new AWRegionTopologySnapshot(1, result);
        }

        internal AWRegionTopologySnapshot WithRevision(int pRevision)
        {
            return new AWRegionTopologySnapshot(pRevision, _regions);
        }

        private static bool TryGetTile(AWTileTraversalSnapshot[][] pChunks,
            int pWidth, int pHeight, int pChunkSize, int pChunksWide, int pTileId,
            out AWTileTraversalSnapshot pTile)
        {
            pTile = default;
            if (pTileId < 0 || pTileId >= pWidth * pHeight)
                return false;
            int x = pTileId % pWidth;
            int y = pTileId / pWidth;
            int chunkId = x / pChunkSize + y / pChunkSize * pChunksWide;
            if (chunkId < 0 || chunkId >= pChunks.Length) return false;
            AWTileTraversalSnapshot[] chunk = pChunks[chunkId];
            if (chunk == null) return false;
            int local = x % pChunkSize + y % pChunkSize * pChunkSize;
            if (local < 0 || local >= chunk.Length) return false;
            pTile = chunk[local];
            return pTile.Exists && pTile.Id == pTileId;
        }

        private sealed class RegionBuilder
        {
            internal int CenterTileId = -1;
            internal readonly HashSet<int> Neighbours = new HashSet<int>();
        }
    }

    internal readonly struct AWRegionNode
    {
        internal AWRegionNode(int pId, int pCenterTileId, int[] pNeighbours)
        {
            Id = pId;
            CenterTileId = pCenterTileId;
            Neighbours = pNeighbours ?? Array.Empty<int>();
        }

        internal int Id { get; }
        internal int CenterTileId { get; }
        internal int[] Neighbours { get; }
    }

    public sealed class AWRegionRouteCache
    {
        private readonly int _capacity;
        private readonly object _gate = new object();
        private readonly Dictionary<RouteKey, LinkedListNode<RouteEntry>> _entries =
            new Dictionary<RouteKey, LinkedListNode<RouteEntry>>();
        private readonly LinkedList<RouteEntry> _lru = new LinkedList<RouteEntry>();

        public AWRegionRouteCache(int pCapacity)
        {
            _capacity = Math.Max(1, pCapacity);
        }

        public int Capacity => _capacity;

        public int[] GetOrBuild(AWTraversalGeneration pGeneration,
            int pStartTileId, int pTargetTileId, int pTraversalClass)
        {
            if (pGeneration == null ||
                !pGeneration.TryGet(pStartTileId, out AWTileTraversalSnapshot start) ||
                !pGeneration.TryGet(pTargetTileId, out AWTileTraversalSnapshot target) ||
                start.RegionId < 0 || target.RegionId < 0)
                return null;

            AWRegionTopologySnapshot topology = pGeneration.RegionTopology;
            var key = new RouteKey(pGeneration, topology, start.RegionId,
                target.RegionId, pTraversalClass);
            lock (_gate)
            {
                if (_entries.TryGetValue(key,
                        out LinkedListNode<RouteEntry> cached))
                {
                    _lru.Remove(cached);
                    _lru.AddFirst(cached);
                    return cached.Value.Route;
                }
            }

            int[] route = BuildRoute(topology, start.RegionId, target.RegionId);
            lock (_gate)
            {
                if (_entries.TryGetValue(key,
                        out LinkedListNode<RouteEntry> existing))
                {
                    _lru.Remove(existing);
                    _lru.AddFirst(existing);
                    return existing.Value.Route;
                }

                var node = new LinkedListNode<RouteEntry>(
                    new RouteEntry(key, route));
                _entries.Add(key, node);
                _lru.AddFirst(node);
                while (_entries.Count > _capacity)
                {
                    LinkedListNode<RouteEntry> last = _lru.Last;
                    if (last == null) break;
                    _lru.RemoveLast();
                    _entries.Remove(last.Value.Key);
                }
            }
            return route;
        }

        public void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
                _lru.Clear();
            }
        }

        private static int[] BuildRoute(AWRegionTopologySnapshot pTopology,
            int pStartRegion, int pTargetRegion)
        {
            if (pTopology == null) return null;
            if (pStartRegion == pTargetRegion) return new[] { pStartRegion };
            if (!pTopology.TryGetRegion(pStartRegion, out _) ||
                !pTopology.TryGetRegion(pTargetRegion, out _)) return null;

            var parents = new Dictionary<int, int>();
            var queue = new Queue<int>();
            parents[pStartRegion] = int.MinValue;
            queue.Enqueue(pStartRegion);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!pTopology.TryGetRegion(current,
                        out AWRegionNode region)) continue;
                for (int i = 0; i < region.Neighbours.Length; i++)
                {
                    int neighbour = region.Neighbours[i];
                    if (parents.ContainsKey(neighbour)) continue;
                    parents.Add(neighbour, current);
                    if (neighbour == pTargetRegion)
                        return Reconstruct(parents, pStartRegion,
                            pTargetRegion);
                    queue.Enqueue(neighbour);
                }
            }
            return null;
        }

        private static int[] Reconstruct(Dictionary<int, int> pParents,
            int pStartRegion, int pTargetRegion)
        {
            var reversed = new List<int>();
            int current = pTargetRegion;
            while (current != int.MinValue)
            {
                reversed.Add(current);
                if (current == pStartRegion) break;
                if (!pParents.TryGetValue(current, out current)) return null;
            }
            reversed.Reverse();
            return reversed.ToArray();
        }

        private readonly struct RouteKey : IEquatable<RouteKey>
        {
            internal RouteKey(AWTraversalGeneration pGeneration,
                AWRegionTopologySnapshot pTopology, int pStartRegion,
                int pTargetRegion, int pTraversalClass)
            {
                GenerationIdentity = pGeneration?.Identity ?? 0L;
                TopologyRevision = pTopology?.Revision ?? 0;
                StartRegion = pStartRegion;
                TargetRegion = pTargetRegion;
                TraversalClass = pTraversalClass;
            }

            internal long GenerationIdentity { get; }
            internal int TopologyRevision { get; }
            internal int StartRegion { get; }
            internal int TargetRegion { get; }
            internal int TraversalClass { get; }

            public bool Equals(RouteKey pOther)
            {
                return GenerationIdentity == pOther.GenerationIdentity &&
                       TopologyRevision == pOther.TopologyRevision &&
                       StartRegion == pOther.StartRegion &&
                       TargetRegion == pOther.TargetRegion &&
                       TraversalClass == pOther.TraversalClass;
            }

            public override bool Equals(object pObject)
            {
                return pObject is RouteKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = GenerationIdentity.GetHashCode();
                    hash = hash * 397 ^ TopologyRevision;
                    hash = hash * 397 ^ StartRegion;
                    hash = hash * 397 ^ TargetRegion;
                    hash = hash * 397 ^ TraversalClass;
                    return hash;
                }
            }
        }

        private readonly struct RouteEntry
        {
            internal RouteEntry(RouteKey pKey, int[] pRoute)
            {
                Key = pKey;
                Route = pRoute;
            }

            internal RouteKey Key { get; }
            internal int[] Route { get; }
        }
    }
}

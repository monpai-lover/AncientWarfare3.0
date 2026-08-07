using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.pathfinding
{
    internal static class AWOceanConnectivityRules
    {
        internal static int[] Compute(int pWidth, int pHeight,
            IReadOnlyList<AWTileTraversalSnapshot> pTiles)
        {
            int count = pWidth <= 0 || pHeight <= 0 ? 0 : pWidth * pHeight;
            var components = new int[count];
            for (int i = 0; i < components.Length; i++) components[i] = -1;
            if (pTiles == null) return components;

            int component = 0;
            var queue = new Queue<int>();
            for (int id = 0; id < count && id < pTiles.Count; id++)
            {
                AWTileTraversalSnapshot tile = pTiles[id];
                if (!tile.Exists || (!tile.Liquid && !tile.Ocean) ||
                    components[id] >= 0) continue;
                components[id] = component;
                queue.Enqueue(id);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    AWTileTraversalSnapshot source = pTiles[current];
                    for (int n = 0; n < source.NeighborCount; n++)
                    {
                        int neighbor = source.GetNeighbor(n);
                        if (neighbor < 0 || neighbor >= count ||
                            neighbor >= pTiles.Count || components[neighbor] >= 0)
                            continue;
                        AWTileTraversalSnapshot candidate = pTiles[neighbor];
                        if (!candidate.Exists ||
                            (!candidate.Liquid && !candidate.Ocean)) continue;
                        components[neighbor] = component;
                        queue.Enqueue(neighbor);
                    }
                }
                component++;
            }
            return components;
        }

        internal static bool SameWaterBody(int pFirstTileId, int pSecondTileId,
            int[] pComponents)
        {
            return pComponents != null && pFirstTileId >= 0 &&
                   pSecondTileId >= 0 && pFirstTileId < pComponents.Length &&
                   pSecondTileId < pComponents.Length &&
                   pComponents[pFirstTileId] >= 0 &&
                   pComponents[pFirstTileId] == pComponents[pSecondTileId];
        }

        internal static AWTileTraversalSnapshot[][] Apply(int pWidth,
            int pHeight, int pChunkSize,
            AWTileTraversalSnapshot[][] pChunks)
        {
            int count = pWidth <= 0 || pHeight <= 0 ? 0 : pWidth * pHeight;
            var flat = new AWTileTraversalSnapshot[count];
            for (int id = 0; id < count; id++)
            {
                int x = id % pWidth;
                int y = id / pWidth;
                int chunkWide = Math.Max(1, (pWidth + pChunkSize - 1) / pChunkSize);
                int chunk = x / pChunkSize + y / pChunkSize * chunkWide;
                int local = x % pChunkSize + y % pChunkSize * pChunkSize;
                if (pChunks != null && chunk >= 0 && chunk < pChunks.Length &&
                    pChunks[chunk] != null && local < pChunks[chunk].Length)
                    flat[id] = pChunks[chunk][local];
            }
            int[] components = Compute(pWidth, pHeight, flat);
            var result = new AWTileTraversalSnapshot[pChunks?.Length ?? 0][];
            for (int i = 0; i < result.Length; i++)
                result[i] = pChunks[i] == null ? null :
                    (AWTileTraversalSnapshot[])pChunks[i].Clone();
            for (int id = 0; id < count; id++)
            {
                AWTileTraversalSnapshot tile = flat[id];
                if (!tile.Exists) continue;
                int x = id % pWidth;
                int y = id / pWidth;
                int chunkWide = Math.Max(1, (pWidth + pChunkSize - 1) / pChunkSize);
                int chunk = x / pChunkSize + y / pChunkSize * chunkWide;
                int local = x % pChunkSize + y % pChunkSize * pChunkSize;
                result[chunk][local] = tile.WithOceanComponent(components[id]);
            }
            return result;
        }
    }
}

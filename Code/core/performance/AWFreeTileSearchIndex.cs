using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.performance;

internal static class AWFreeTileSearchIndex
{
    private static readonly Dictionary<MapChunk, Dictionary<TileIsland, List<WorldTile>>> TilesByChunk = new();
    private static readonly Stack<Dictionary<TileIsland, List<WorldTile>>> IslandMapPool = new();
    private static readonly Stack<List<WorldTile>> TileListPool = new();
    private static readonly object CacheLock = new();

    [ThreadStatic]
    private static MapChunk[] queryChunkBuffer;

    private static int indexedGeneration = -1;

    internal static bool TryFind(WorldTile origin, out WorldTile result)
    {
        result = null;
        if (origin?.chunk == null || origin.region?.island == null)
        {
            return false;
        }

        EnsureCurrentWorld();
        MapChunk[] chunks = queryChunkBuffer ??= new MapChunk[9];
        chunks[0] = origin.chunk;
        int chunkCount = 1;
        MapChunk[] neighbours = origin.chunk.neighbours_all;
        for (int i = 0; i < neighbours.Length && chunkCount < chunks.Length; i++)
        {
            if (neighbours[i] != null)
            {
                chunks[chunkCount++] = neighbours[i];
            }
        }

        int chunkOffset = Randy.randomInt(0, chunkCount);
        TileIsland island = origin.region.island;
        for (int i = 0; i < chunkCount; i++)
        {
            List<WorldTile> candidates = GetCandidates(chunks[(i + chunkOffset) % chunkCount], island);
            if (candidates.Count == 0)
            {
                continue;
            }

            int tileOffset = Randy.randomInt(0, candidates.Count);
            for (int j = 0; j < candidates.Count; j++)
            {
                WorldTile tile = candidates[(j + tileOffset) % candidates.Count];
                if (!IsFreeFor(tile, origin))
                {
                    continue;
                }

                result = tile;
                return true;
            }
        }

        return false;
    }

    internal static void Reset()
    {
        lock (CacheLock)
        {
            RecycleCache();
            indexedGeneration = -1;
        }
    }

    private static void EnsureCurrentWorld()
    {
        int generation = AWSimulationTime.Generation;
        if (indexedGeneration == generation)
        {
            return;
        }

        lock (CacheLock)
        {
            if (indexedGeneration == generation)
            {
                return;
            }

            RecycleCache();
            indexedGeneration = generation;
        }
    }

    private static List<WorldTile> GetCandidates(MapChunk chunk, TileIsland island)
    {
        lock (CacheLock)
        {
            if (!TilesByChunk.TryGetValue(chunk, out Dictionary<TileIsland, List<WorldTile>> byIsland))
            {
                byIsland = IslandMapPool.Count == 0
                    ? new Dictionary<TileIsland, List<WorldTile>>()
                    : IslandMapPool.Pop();
                TilesByChunk.Add(chunk, byIsland);
            }

            if (byIsland.TryGetValue(island, out List<WorldTile> candidates))
            {
                return candidates;
            }

            candidates = TileListPool.Count == 0 ? new List<WorldTile>(128) : TileListPool.Pop();
            WorldTile[] tiles = chunk.tiles;
            for (int i = 0; i < tiles.Length; i++)
            {
                WorldTile tile = tiles[i];
                if (tile?.region?.island == island && tile.Type.ground && !tile.hasBuilding())
                {
                    candidates.Add(tile);
                }
            }

            byIsland.Add(island, candidates);
            return candidates;
        }
    }

    private static bool IsFreeFor(WorldTile tile, WorldTile origin)
    {
        return tile != null && tile.Type.ground && !tile.hasBuilding() && tile.isSameIsland(origin);
    }

    private static void RecycleCache()
    {
        foreach (Dictionary<TileIsland, List<WorldTile>> byIsland in TilesByChunk.Values)
        {
            foreach (List<WorldTile> candidates in byIsland.Values)
            {
                candidates.Clear();
                TileListPool.Push(candidates);
            }

            byIsland.Clear();
            IslandMapPool.Push(byIsland);
        }

        TilesByChunk.Clear();
    }
}
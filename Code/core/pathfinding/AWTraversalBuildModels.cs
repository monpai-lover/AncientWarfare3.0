using System;

namespace AncientWarfare3.core.pathfinding
{
    internal sealed class AWTraversalChunkCapture
    {
        public AWTraversalChunkCapture(int chunkId, long sourceRevision,
            AWTileTraversalSnapshot[] tiles)
        {
            ChunkId = chunkId;
            SourceRevision = sourceRevision;
            Tiles = tiles == null
                ? Array.Empty<AWTileTraversalSnapshot>()
                : (AWTileTraversalSnapshot[])tiles.Clone();
        }

        public int ChunkId { get; }
        public long SourceRevision { get; }
        public AWTileTraversalSnapshot[] Tiles { get; }
    }

    internal sealed class AWTraversalBuildInput
    {
        public AWTraversalBuildInput(long worldGeneration,
            int baseGenerationId, long sourceRevision, int width,
            int height, int chunkSize,
            AWTileTraversalSnapshot[][] baseChunks,
            AWTraversalChunkCapture[] captures,
            bool rebuildWaterConnectivity = false,
            int resultGenerationId = 0,
            long gridIdentity = 0L)
        {
            WorldGeneration = worldGeneration;
            BaseGenerationId = baseGenerationId;
            SourceRevision = sourceRevision;
            Width = Math.Max(0, width);
            Height = Math.Max(0, height);
            ChunkSize = Math.Max(1, chunkSize);
            BaseChunks = baseChunks == null
                ? Array.Empty<AWTileTraversalSnapshot[]>()
                : (AWTileTraversalSnapshot[][])baseChunks.Clone();
            Captures = captures == null
                ? Array.Empty<AWTraversalChunkCapture>()
                : (AWTraversalChunkCapture[])captures.Clone();
            RebuildWaterConnectivity = rebuildWaterConnectivity;
            ResultGenerationId = Math.Max(0, resultGenerationId);
            GridIdentity = gridIdentity;
        }

        public long WorldGeneration { get; }
        public int BaseGenerationId { get; }
        public long SourceRevision { get; }
        public int Width { get; }
        public int Height { get; }
        public int ChunkSize { get; }
        public AWTileTraversalSnapshot[][] BaseChunks { get; }
        public AWTraversalChunkCapture[] Captures { get; }
        public bool RebuildWaterConnectivity { get; }
        public int ResultGenerationId { get; }
        public long GridIdentity { get; }
    }

    internal sealed class AWTraversalBuildResult
    {
        public AWTraversalBuildResult(long pWorldGeneration,
            int pBaseGenerationId, long pSourceRevision, int pWidth,
            int pHeight, int pChunkSize,
            AWTileTraversalSnapshot[][] pChunks,
            AWRegionTopologySnapshot pRegionTopology = null,
            AWTraversalGeneration pPreparedGeneration = null,
            long pGridIdentity = 0L)
        {
            WorldGeneration = pWorldGeneration;
            BaseGenerationId = pBaseGenerationId;
            SourceRevision = pSourceRevision;
            Width = pWidth;
            Height = pHeight;
            ChunkSize = pChunkSize;
            Chunks = pChunks ?? Array.Empty<AWTileTraversalSnapshot[]>();
            RegionTopology = pRegionTopology;
            PreparedGeneration = pPreparedGeneration;
            GridIdentity = pGridIdentity != 0L
                ? pGridIdentity
                : pPreparedGeneration?.Identity ?? 0L;
        }

        public long WorldGeneration { get; }
        public int BaseGenerationId { get; }
        public long SourceRevision { get; }
        public int Width { get; }
        public int Height { get; }
        public int ChunkSize { get; }
        public AWTileTraversalSnapshot[][] Chunks { get; }
        // Built with the immutable snapshot on the worker, so publication only
        // swaps references on the main thread.
        internal AWRegionTopologySnapshot RegionTopology { get; }
        internal AWTraversalGeneration PreparedGeneration { get; }
        internal long GridIdentity { get; }
    }

    internal sealed class AWTraversalOverlayEntry
    {
        public AWTraversalOverlayEntry(int pChunkId, long pSourceRevision,
            AWTileTraversalSnapshot[] pTiles)
        {
            ChunkId = pChunkId;
            SourceRevision = pSourceRevision;
            Tiles = pTiles == null
                ? Array.Empty<AWTileTraversalSnapshot>()
                : (AWTileTraversalSnapshot[])pTiles.Clone();
        }

        public int ChunkId { get; }
        public long SourceRevision { get; }
        public AWTileTraversalSnapshot[] Tiles { get; }
    }
}

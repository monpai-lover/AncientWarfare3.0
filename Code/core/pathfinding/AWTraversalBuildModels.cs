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
            AWTraversalChunkCapture[] captures)
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
        }

        public long WorldGeneration { get; }
        public int BaseGenerationId { get; }
        public long SourceRevision { get; }
        public int Width { get; }
        public int Height { get; }
        public int ChunkSize { get; }
        public AWTileTraversalSnapshot[][] BaseChunks { get; }
        public AWTraversalChunkCapture[] Captures { get; }
    }

    internal sealed class AWTraversalBuildResult
    {
        public AWTraversalBuildResult(long pWorldGeneration,
            int pBaseGenerationId, long pSourceRevision, int pWidth,
            int pHeight, int pChunkSize,
            AWTileTraversalSnapshot[][] pChunks)
        {
            WorldGeneration = pWorldGeneration;
            BaseGenerationId = pBaseGenerationId;
            SourceRevision = pSourceRevision;
            Width = pWidth;
            Height = pHeight;
            ChunkSize = pChunkSize;
            Chunks = pChunks ?? Array.Empty<AWTileTraversalSnapshot[]>();
        }

        public long WorldGeneration { get; }
        public int BaseGenerationId { get; }
        public long SourceRevision { get; }
        public int Width { get; }
        public int Height { get; }
        public int ChunkSize { get; }
        public AWTileTraversalSnapshot[][] Chunks { get; }
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

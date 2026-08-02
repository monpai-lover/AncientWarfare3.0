using System;

namespace AncientWarfare3.core.policy
{
    public sealed class HierarchicalVassalBoundaryChunkSnapshot
    {
        public HierarchicalVassalBoundaryChunkSnapshot(
            long pWorldGeneration,
            BoundaryChunkKey pChunkKey,
            long pRevision,
            BoundaryDisplayLayer pLayer,
            BoundaryCellRaster pCells,
            HierarchyColorAssignment pColorAssignment,
            ulong pFingerprint)
        {
            if (pWorldGeneration < 0L)
                throw new ArgumentOutOfRangeException(nameof(pWorldGeneration));
            if (pRevision <= 0L)
                throw new ArgumentOutOfRangeException(nameof(pRevision));
            WorldGeneration = pWorldGeneration;
            ChunkKey = pChunkKey;
            Revision = pRevision;
            Layer = pLayer;
            Cells = pCells ?? throw new ArgumentNullException(nameof(pCells));
            ColorAssignment = pColorAssignment ??
                              throw new ArgumentNullException(
                                  nameof(pColorAssignment));
            Fingerprint = pFingerprint;
        }

        public long WorldGeneration { get; }
        public BoundaryChunkKey ChunkKey { get; }
        public long Revision { get; }
        public BoundaryDisplayLayer Layer { get; }
        public BoundaryCellRaster Cells { get; }
        public HierarchyColorAssignment ColorAssignment { get; }
        public ulong Fingerprint { get; }
    }
}

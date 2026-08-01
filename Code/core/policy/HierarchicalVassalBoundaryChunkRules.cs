using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public static class HierarchicalVassalBoundaryChunkRules
    {
        public const int ChunkSize = 32;

        public const int Halo = 2;

        public const int CaptureBudgetPerFrame = 2;

        public const int UploadBudgetPerFrame = 2;

        public static BoundaryChunkKey ForTile(int pX, int pY)
        {
            return new BoundaryChunkKey(FloorDivide(pX), FloorDivide(pY));
        }

        public static BoundaryChunkBounds CaptureBounds(
            BoundaryChunkKey pKey,
            int pWorldWidth,
            int pWorldHeight)
        {
            if (pWorldWidth < 0)
                throw new ArgumentOutOfRangeException(nameof(pWorldWidth));
            if (pWorldHeight < 0)
                throw new ArgumentOutOfRangeException(nameof(pWorldHeight));

            int interiorMinX = Clamp(pKey.X * ChunkSize, 0, pWorldWidth);
            int interiorMinY = Clamp(pKey.Y * ChunkSize, 0, pWorldHeight);
            int interiorMaxX = Clamp(interiorMinX + ChunkSize, 0, pWorldWidth);
            int interiorMaxY = Clamp(interiorMinY + ChunkSize, 0, pWorldHeight);
            int captureMinX = Math.Max(0, interiorMinX - Halo);
            int captureMinY = Math.Max(0, interiorMinY - Halo);
            int captureMaxX = Math.Min(pWorldWidth, interiorMaxX + Halo);
            int captureMaxY = Math.Min(pWorldHeight, interiorMaxY + Halo);

            return new BoundaryChunkBounds(
                captureMinX,
                captureMinY,
                captureMaxX,
                captureMaxY,
                interiorMinX,
                interiorMinY,
                interiorMaxX,
                interiorMaxY);
        }

        public static IReadOnlyList<BoundaryChunkKey> DirtyNeighborhood(
            BoundaryChunkKey key,
            int chunkCountX,
            int chunkCountY)
        {
            var result = new List<BoundaryChunkKey>(9);
            if (chunkCountX <= 0 || chunkCountY <= 0)
                return result;

            for (int x = key.X - 1; x <= key.X + 1; x++)
            {
                if (x < 0 || x >= chunkCountX)
                    continue;
                for (int y = key.Y - 1; y <= key.Y + 1; y++)
                {
                    if (y < 0 || y >= chunkCountY)
                        continue;
                    result.Add(new BoundaryChunkKey(x, y));
                }
            }
            return result;
        }

        public static bool AcceptResult(
            long resultWorldGeneration,
            long currentWorldGeneration,
            long resultRevision,
            long currentRevision,
            BoundaryDisplayLayer resultLayer,
            BoundaryDisplayLayer currentLayer)
        {
            return resultWorldGeneration == currentWorldGeneration &&
                   resultRevision == currentRevision &&
                   resultLayer == currentLayer;
        }

        private static int FloorDivide(int pValue)
        {
            int quotient = pValue / ChunkSize;
            if (pValue < 0 && pValue % ChunkSize != 0)
                quotient--;
            return quotient;
        }

        private static int Clamp(int pValue, int pMinimum, int pMaximum)
        {
            if (pValue < pMinimum)
                return pMinimum;
            return pValue > pMaximum ? pMaximum : pValue;
        }
    }
}

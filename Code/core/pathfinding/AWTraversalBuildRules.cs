using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    internal readonly struct AWTraversalCaptureBatch
    {
        public AWTraversalCaptureBatch(int pStart, int pCount,
            int pNextCursor, bool pCompleted)
        {
            Start = pStart;
            Count = pCount;
            NextCursor = pNextCursor;
            Completed = pCompleted;
        }

        public int Start { get; }
        public int Count { get; }
        public int NextCursor { get; }
        public bool Completed { get; }
    }

    internal readonly struct AWTraversalChunkPlacement
    {
        public AWTraversalChunkPlacement(int pChunkId, int pLocalIndex,
            bool pValid)
        {
            ChunkId = pChunkId;
            LocalIndex = pLocalIndex;
            Valid = pValid;
        }

        public int ChunkId { get; }
        public int LocalIndex { get; }
        public bool Valid { get; }
    }

    internal static class AWTraversalCaptureRules
    {
        public const int InitialCaptureTileBudget = 512;
        public const double InitialCaptureBudgetMilliseconds = 0.75d;

        public static bool MatchesMapSize(int width, int height,
            int tileCount)
        {
            return width > 0 && height > 0 && tileCount >= 0 &&
                   (long)width * height == tileCount;
        }

        public static int ChunkCount(int width, int height, int chunkSize)
        {
            if (width <= 0 || height <= 0 || chunkSize <= 0) return 0;
            long wide = ((long)width + chunkSize - 1L) / chunkSize;
            long high = ((long)height + chunkSize - 1L) / chunkSize;
            long count = wide * high;
            return count > int.MaxValue ? 0 : (int)count;
        }

        public static AWTraversalCaptureBatch NextBatch(int totalCount,
            int cursor, int itemBudget)
        {
            int total = Math.Max(0, totalCount);
            int start = Math.Max(0, Math.Min(total, cursor));
            int count = Math.Min(total - start, Math.Max(0, itemBudget));
            int next = start + count;
            return new AWTraversalCaptureBatch(start, count, next,
                next >= total);
        }

        public static AWTraversalChunkPlacement PlacementForIndex(
            int tileIndex, int width, int height, int chunkSize)
        {
            if (!MatchesMapSize(width, height,
                    Math.Max(0, width) * Math.Max(0, height)) ||
                tileIndex < 0 || (long)tileIndex >= (long)width * height)
                return default;
            return PlacementForCoordinates(tileIndex % width,
                tileIndex / width, width, height, chunkSize);
        }

        public static AWTraversalChunkPlacement PlacementForCoordinates(
            int x, int y, int width, int height, int chunkSize)
        {
            if (x < 0 || y < 0 || x >= width || y >= height ||
                width <= 0 || height <= 0 || chunkSize <= 0)
                return default;
            int chunksWide = (int)(((long)width + chunkSize - 1L) /
                                   chunkSize);
            int chunkId = x / chunkSize + y / chunkSize * chunksWide;
            int local = x % chunkSize + y % chunkSize * chunkSize;
            return new AWTraversalChunkPlacement(chunkId, local, true);
        }

        public static bool ReadyToIntercept(bool traversalReady,
            bool workerRunning, bool finderAvailable)
        {
            return traversalReady && workerRunning && finderAvailable;
        }

        public static int InitialDirtyChunkBudget(int capturedTiles,
            int tileBudget, int chunkSize)
        {
            if (chunkSize <= 0) return 0;
            long chunkTiles = (long)chunkSize * chunkSize;
            long remaining = Math.Max(0L,
                (long)Math.Max(0, tileBudget) - Math.Max(0, capturedTiles));
            return (int)Math.Min(int.MaxValue, remaining / chunkTiles);
        }
    }

    internal static class AWTraversalBuildRules
    {
        public static AWTraversalBuildResult Build(
            AWTraversalBuildInput pInput)
        {
            if (pInput == null)
                throw new ArgumentNullException(nameof(pInput));
            var chunks = (AWTileTraversalSnapshot[][])
                pInput.BaseChunks.Clone();
            foreach (AWTraversalChunkCapture capture in pInput.Captures)
            {
                if (capture == null || capture.ChunkId < 0 ||
                    capture.ChunkId >= chunks.Length ||
                    capture.SourceRevision > pInput.SourceRevision) continue;
                chunks[capture.ChunkId] = capture.Tiles;
            }
            chunks = AWOceanConnectivityRules.Apply(pInput.Width,
                pInput.Height, pInput.ChunkSize, chunks);
            return new AWTraversalBuildResult(pInput.WorldGeneration,
                pInput.BaseGenerationId, pInput.SourceRevision,
                pInput.Width, pInput.Height, pInput.ChunkSize, chunks);
        }

        public static bool CanPublish(AWTraversalBuildResult pResult,
            long currentWorldGeneration, int currentBaseGenerationId,
            long currentSourceRevision)
        {
            if (pResult == null) return false;
            return CanPublish(pResult, currentWorldGeneration,
                currentBaseGenerationId, currentSourceRevision,
                pResult.Width, pResult.Height, pResult.ChunkSize,
                pResult.Chunks.Length);
        }

        public static bool CanPublish(AWTraversalBuildResult pResult,
            long currentWorldGeneration, int currentBaseGenerationId,
            long currentSourceRevision, int currentWidth,
            int currentHeight, int currentChunkSize,
            int currentChunkCount)
        {
            return pResult != null &&
                   pResult.WorldGeneration == currentWorldGeneration &&
                   pResult.BaseGenerationId == currentBaseGenerationId &&
                   pResult.SourceRevision <= currentSourceRevision &&
                   pResult.Width == currentWidth &&
                   pResult.Height == currentHeight &&
                   pResult.ChunkSize == currentChunkSize &&
                   pResult.Chunks.Length == currentChunkCount &&
                   currentChunkCount == AWTraversalCaptureRules.ChunkCount(
                       currentWidth, currentHeight, currentChunkSize);
        }

        public static IReadOnlyList<AWTraversalOverlayEntry>
            RemoveCommittedOverlay(
                IEnumerable<AWTraversalOverlayEntry> pEntries,
                long committedRevision)
        {
            var result = new List<AWTraversalOverlayEntry>();
            if (pEntries == null) return result;
            foreach (AWTraversalOverlayEntry entry in pEntries)
                if (entry != null && entry.SourceRevision > committedRevision)
                    result.Add(entry);
            return result;
        }
    }

    internal static class AWTraversalShadowRules
    {
        public static string SummarizeChunks(AWTraversalBuildResult pResult,
            IEnumerable<int> pChunkIds)
        {
            if (pResult == null) return "missing";
            var chunkIds = new SortedSet<int>();
            if (pChunkIds != null)
                foreach (int chunkId in pChunkIds)
                    if (chunkId >= 0 && chunkId < pResult.Chunks.Length)
                        chunkIds.Add(chunkId);
            if (chunkIds.Count == 0) return "none";
            var summary = new StringBuilder();
            foreach (int chunkId in chunkIds)
            {
                if (summary.Length > 0) summary.Append(';');
                summary.Append("chunk=").Append(chunkId)
                    .Append(",hash=").Append(HashChunk(
                        pResult.Chunks[chunkId]).ToString("X16",
                        CultureInfo.InvariantCulture));
            }
            return summary.ToString();
        }

        private static ulong HashChunk(AWTileTraversalSnapshot[] pTiles)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                Mix(ref hash, pTiles?.Length ?? 0);
                if (pTiles == null) return hash;
                for (int index = 0; index < pTiles.Length; index++)
                {
                    AWTileTraversalSnapshot tile = pTiles[index];
                    Mix(ref hash, tile.Exists ? 1 : 0);
                    Mix(ref hash, tile.Id);
                    Mix(ref hash, tile.X);
                    Mix(ref hash, tile.Y);
                    Mix(ref hash, tile.Ground ? 1 : 0);
                    Mix(ref hash, tile.Block ? 1 : 0);
                    Mix(ref hash, tile.Liquid ? 1 : 0);
                    Mix(ref hash, tile.Ocean ? 1 : 0);
                    Mix(ref hash, tile.Lava ? 1 : 0);
                    Mix(ref hash, tile.Fire ? 1 : 0);
                    Mix(ref hash, tile.DamageUnits ? 1 : 0);
                    Mix(ref hash, tile.TerrainDamage.GetHashCode());
                    Mix(ref hash, tile.WalkMultiplier.GetHashCode());
                    Mix(ref hash, tile.GoodForBoat ? 1 : 0);
                    Mix(ref hash, tile.OceanComponent);
                    Mix(ref hash, tile.RegionId);
                    Mix(ref hash, tile.IslandId);
                    Mix(ref hash, tile.NeighborCount);
                    for (int neighbor = 0; neighbor < 8; neighbor++)
                        Mix(ref hash, tile.GetNeighbor(neighbor));
                }
                return hash;
            }
        }

        private static void Mix(ref ulong pHash, int pValue)
        {
            unchecked
            {
                pHash ^= (uint)pValue;
                pHash *= 1099511628211UL;
            }
        }
    }

    internal sealed class AWTraversalLatestBuildSlot<T>
    {
        private bool _running;
        private bool _hasPending;
        private T _pending;

        public bool TryStart(T pRequest)
        {
            if (!_running)
            {
                _running = true;
                return true;
            }
            _pending = pRequest;
            _hasPending = true;
            return false;
        }

        public bool TryComplete(out T pNext)
        {
            if (_hasPending)
            {
                pNext = _pending;
                _pending = default;
                _hasPending = false;
                return true;
            }
            _running = false;
            pNext = default;
            return false;
        }

        public void Clear()
        {
            _running = false;
            _hasPending = false;
            _pending = default;
        }
    }

    internal sealed class AWTraversalBuildExecution
    {
        private readonly AWTraversalBuildInput _input;

        public AWTraversalBuildExecution(AWTraversalBuildInput pInput)
        {
            _input = pInput ?? throw new ArgumentNullException(nameof(pInput));
        }

        public object Execute(CancellationToken pToken)
        {
            pToken.ThrowIfCancellationRequested();
            return AWTraversalBuildRules.Build(_input);
        }
    }
}

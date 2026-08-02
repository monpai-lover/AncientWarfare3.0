using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

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

    internal sealed class HierarchicalVassalBoundarySnapshotCapture
    {
        private readonly HierarchicalVassalBoundaryDirtyTracker _dirtyTracker;
        private readonly Dictionary<BoundaryChunkKey, ulong> _auditFingerprints =
            new Dictionary<BoundaryChunkKey, ulong>();
        private int _mainThreadId;
        private int _auditCursor;
        private long _worldGeneration = -1L;

        public HierarchicalVassalBoundarySnapshotCapture(
            HierarchicalVassalBoundaryDirtyTracker pDirtyTracker)
        {
            _dirtyTracker = pDirtyTracker ??
                            throw new ArgumentNullException(nameof(pDirtyTracker));
        }

        public void ResetWorld(long pWorldGeneration,
            int pWorldWidth, int pWorldHeight)
        {
            AssertMainThread(pCaptureIfUnset: true);
            if (pWorldGeneration < 0L)
                throw new ArgumentOutOfRangeException(nameof(pWorldGeneration));
            _dirtyTracker.ResetWorld(pWorldWidth, pWorldHeight);
            _auditFingerprints.Clear();
            _auditCursor = 0;
            _worldGeneration = pWorldGeneration;
        }

        public int ProcessFrame(long pWorldGeneration,
            BoundaryDisplayLayer pLayer,
            HierarchicalVassalMapModeSnapshot pDisplaySnapshot,
            Action<HierarchicalVassalBoundaryChunkSnapshot> pSubmit)
        {
            AssertMainThread();
            if (pWorldGeneration != _worldGeneration ||
                pDisplaySnapshot == null || pSubmit == null ||
                !TryValidateWorld(out WorldTile[] tiles,
                    out int width, out int height)) return 0;
            if (width != _dirtyTracker.WorldWidth ||
                height != _dirtyTracker.WorldHeight) return 0;

            int captured = 0;
            while (captured <
                   HierarchicalVassalBoundaryChunkRules.CaptureBudgetPerFrame &&
                   _dirtyTracker.TryTake(out BoundaryChunkKey key,
                       out long revision))
            {
                try
                {
                    HierarchicalVassalBoundaryChunkSnapshot snapshot =
                        CaptureChunk(pWorldGeneration, key, revision, pLayer,
                            pDisplaySnapshot, tiles, width, height);
                    pSubmit(snapshot);
                    _auditFingerprints[key] = snapshot.Fingerprint;
                    captured++;
                }
                catch (Exception)
                {
                    _dirtyTracker.Requeue(key);
                    break;
                }
            }
            return captured;
        }

        public bool AuditOneChunkPerSimulationCycle(
            BoundaryDisplayLayer pLayer,
            HierarchicalVassalMapModeSnapshot pDisplaySnapshot)
        {
            AssertMainThread();
            if (pDisplaySnapshot == null ||
                !TryValidateWorld(out WorldTile[] tiles,
                    out int width, out int height) ||
                width != _dirtyTracker.WorldWidth ||
                height != _dirtyTracker.WorldHeight) return false;

            int chunkCount = checked(_dirtyTracker.ChunkCountX *
                                     _dirtyTracker.ChunkCountY);
            if (chunkCount <= 0)
            {
                _auditCursor = 0;
                return false;
            }
            if (_auditCursor < 0 || _auditCursor >= chunkCount)
                _auditCursor = 0;
            BoundaryChunkKey key = new BoundaryChunkKey(
                _auditCursor % _dirtyTracker.ChunkCountX,
                _auditCursor / _dirtyTracker.ChunkCountX);
            _auditCursor = (_auditCursor + 1) % chunkCount;

            BoundaryCellRaster raster = CaptureRaster(key, pLayer,
                pDisplaySnapshot, tiles, width, height);
            ulong fingerprint =
                HierarchicalVassalBoundaryChunkRules.Fingerprint(raster);
            bool hasPrevious = _auditFingerprints.TryGetValue(
                key, out ulong previous);
            if (!HierarchicalVassalBoundaryChunkRules.HasAuditChange(
                    hasPrevious, previous, fingerprint))
            {
                _auditFingerprints[key] = fingerprint;
                return false;
            }
            _auditFingerprints[key] = fingerprint;
            _dirtyTracker.MarkChunk(key);
            return true;
        }

        private static HierarchicalVassalBoundaryChunkSnapshot CaptureChunk(
            long pWorldGeneration,
            BoundaryChunkKey pKey,
            long pRevision,
            BoundaryDisplayLayer pLayer,
            HierarchicalVassalMapModeSnapshot pDisplaySnapshot,
            WorldTile[] pTiles,
            int pWorldWidth,
            int pWorldHeight)
        {
            BoundaryCellRaster raster = CaptureRaster(pKey, pLayer,
                pDisplaySnapshot, pTiles, pWorldWidth, pWorldHeight);
            ulong fingerprint =
                HierarchicalVassalBoundaryChunkRules.Fingerprint(raster);
            return new HierarchicalVassalBoundaryChunkSnapshot(
                pWorldGeneration, pKey, pRevision, pLayer, raster,
                pDisplaySnapshot.BoundaryColorAssignment, fingerprint);
        }

        private static BoundaryCellRaster CaptureRaster(
            BoundaryChunkKey pKey,
            BoundaryDisplayLayer pLayer,
            HierarchicalVassalMapModeSnapshot pDisplaySnapshot,
            WorldTile[] pTiles,
            int pWorldWidth,
            int pWorldHeight)
        {
            int size = HierarchicalVassalBoundaryChunkRules.ChunkSize;
            int halo = HierarchicalVassalBoundaryChunkRules.Halo;
            int dimension = checked(size + halo * 2);
            long originX = checked((long)pKey.X * size - halo);
            long originY = checked((long)pKey.Y * size - halo);
            if (originX < int.MinValue || originX > int.MaxValue ||
                originY < int.MinValue || originY > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(pKey));

            var copiedCells = new BoundaryCellFacts[
                checked(dimension * dimension)];
            for (int localY = 0; localY < dimension; localY++)
            {
                int worldY = checked((int)originY + localY);
                for (int localX = 0; localX < dimension; localX++)
                {
                    int worldX = checked((int)originX + localX);
                    BoundaryCellFacts cell = CaptureCell(worldX, worldY,
                        pLayer, pDisplaySnapshot, pTiles,
                        pWorldWidth, pWorldHeight);
                    copiedCells[localY * dimension + localX] = cell;
                }
            }
            return new BoundaryCellRaster((int)originX, (int)originY,
                dimension, dimension, copiedCells);
        }

        private static BoundaryCellFacts CaptureCell(
            int pX,
            int pY,
            BoundaryDisplayLayer pLayer,
            HierarchicalVassalMapModeSnapshot pDisplaySnapshot,
            WorldTile[] pTiles,
            int pWorldWidth,
            int pWorldHeight)
        {
            if (pX < 0 || pY < 0 ||
                pX >= pWorldWidth || pY >= pWorldHeight)
                return InvalidCell(pX, pY);
            long index = (long)pY * pWorldWidth + pX;
            if (index < 0L || index >= pTiles.Length)
                return InvalidCell(pX, pY);
            WorldTile tile = pTiles[(int)index];
            if (tile == null) return InvalidCell(pX, pY);

            TileTypeBase type = tile.Type;
            BoundaryWaterKind water = BoundaryWaterKind.Land;
            if (type?.lava == true) water = BoundaryWaterKind.Lava;
            else if (type?.ocean == true) water = BoundaryWaterKind.Ocean;
            else if (type?.liquid == true) water = BoundaryWaterKind.InlandWater;
            byte height = (byte)Mathf.Clamp(tile.Height, 0, 255);

            int zoneId = tile.zone?.id ?? -1;
            pDisplaySnapshot.TryGetBoundaryCellFacts(zoneId, pLayer,
                out long systemId, out long realmId,
                out long cityId, out uint rgba);
            return new BoundaryCellFacts(pX, pY, true, water, height,
                systemId, realmId, cityId, rgba);
        }

        private static BoundaryCellFacts InvalidCell(int pX, int pY)
        {
            return new BoundaryCellFacts(pX, pY, false,
                BoundaryWaterKind.Land, 0, -1L, -1L, -1L, 0u);
        }

        private static bool TryValidateWorld(
            out WorldTile[] pTiles, out int pWidth, out int pHeight)
        {
            pTiles = World.world?.tiles_list;
            pWidth = MapBox.width;
            pHeight = MapBox.height;
            if (pWidth < 0 || pHeight < 0 || pTiles == null) return false;
            long expected = (long)pWidth * pHeight;
            return expected >= 0L && expected <= int.MaxValue &&
                   pTiles.Length == (int)expected;
        }

        private void AssertMainThread(bool pCaptureIfUnset = false)
        {
            int current = Thread.CurrentThread.ManagedThreadId;
            if (_mainThreadId == 0 && pCaptureIfUnset) _mainThreadId = current;
            if (_mainThreadId == 0)
                throw new InvalidOperationException(
                    "Boundary capture has not been initialized.");
            if (_mainThreadId != current)
                throw new InvalidOperationException(
                    "Boundary snapshots must be captured on the main thread.");
        }
    }
}

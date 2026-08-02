using System;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.policy
{
    internal sealed class HierarchicalVassalBoundaryDirtyTracker
    {
        private readonly Dictionary<BoundaryChunkKey, long> _revisions =
            new Dictionary<BoundaryChunkKey, long>();
        private readonly Queue<BoundaryChunkKey> _queue =
            new Queue<BoundaryChunkKey>();
        private readonly HashSet<BoundaryChunkKey> _queued =
            new HashSet<BoundaryChunkKey>();
        private int _mainThreadId;
        private int _worldWidth;
        private int _worldHeight;
        private int _chunkCountX;
        private int _chunkCountY;
        private long _nextRevision;

        public int PendingCount
        {
            get { return _queue.Count; }
        }

        public int WorldWidth
        {
            get { return _worldWidth; }
        }

        public int WorldHeight
        {
            get { return _worldHeight; }
        }

        public int ChunkCountX
        {
            get { return _chunkCountX; }
        }

        public int ChunkCountY
        {
            get { return _chunkCountY; }
        }

        public void ResetWorld(int pWorldWidth, int pWorldHeight)
        {
            AssertMainThread(pCaptureIfUnset: true);
            if (pWorldWidth < 0)
                throw new ArgumentOutOfRangeException(nameof(pWorldWidth));
            if (pWorldHeight < 0)
                throw new ArgumentOutOfRangeException(nameof(pWorldHeight));

            _worldWidth = pWorldWidth;
            _worldHeight = pWorldHeight;
            _chunkCountX = ChunkCount(pWorldWidth);
            _chunkCountY = ChunkCount(pWorldHeight);
            _revisions.Clear();
            _queue.Clear();
            _queued.Clear();
            _nextRevision = 0L;
        }

        public void MarkAll()
        {
            AssertMainThread();
            for (int y = 0; y < _chunkCountY; y++)
                for (int x = 0; x < _chunkCountX; x++)
                    MarkExactChunk(new BoundaryChunkKey(x, y));
        }

        public void MarkTile(WorldTile pTile)
        {
            AssertMainThread();
            if (pTile == null) return;
            MarkTile(pTile.x, pTile.y);
        }

        public void MarkTile(int pX, int pY)
        {
            AssertMainThread();
            if (pX < 0 || pY < 0 ||
                pX >= _worldWidth || pY >= _worldHeight) return;
            MarkChunk(HierarchicalVassalBoundaryChunkRules.ForTile(pX, pY));
        }

        public void MarkZone(TileZone pZone)
        {
            AssertMainThread();
            WorldTile[] tiles = pZone?.tiles;
            if (tiles == null) return;
            var touched = new HashSet<BoundaryChunkKey>();
            for (int i = 0; i < tiles.Length; i++)
            {
                WorldTile tile = tiles[i];
                if (tile == null || tile.x < 0 || tile.y < 0 ||
                    tile.x >= _worldWidth || tile.y >= _worldHeight) continue;
                touched.Add(HierarchicalVassalBoundaryChunkRules.ForTile(
                    tile.x, tile.y));
            }
            foreach (BoundaryChunkKey key in touched) MarkChunk(key);
        }

        public void MarkKingdom(Kingdom pKingdom)
        {
            AssertMainThread();
            if (pKingdom == null) return;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.zones == null) continue;
                    for (int i = 0; i < city.zones.Count; i++)
                        MarkZone(city.zones[i]);
                }
            }
            catch
            {
                // A kingdom may be destroyed while its event is being routed.
            }
        }

        public void MarkChunk(BoundaryChunkKey pKey)
        {
            AssertMainThread();
            IReadOnlyList<BoundaryChunkKey> neighborhood =
                HierarchicalVassalBoundaryChunkRules.DirtyNeighborhood(
                    pKey, _chunkCountX, _chunkCountY);
            for (int i = 0; i < neighborhood.Count; i++)
                MarkExactChunk(neighborhood[i]);
        }

        public bool TryTake(out BoundaryChunkKey pKey, out long pRevision)
        {
            AssertMainThread();
            while (_queue.Count > 0)
            {
                pKey = _queue.Dequeue();
                _queued.Remove(pKey);
                if (_revisions.TryGetValue(pKey, out pRevision)) return true;
            }
            pKey = default(BoundaryChunkKey);
            pRevision = 0L;
            return false;
        }

        public bool TryGetRevision(BoundaryChunkKey pKey, out long pRevision)
        {
            AssertMainThread();
            return _revisions.TryGetValue(pKey, out pRevision);
        }

        public void Requeue(BoundaryChunkKey pKey)
        {
            AssertMainThread();
            if (!_revisions.ContainsKey(pKey) || !_queued.Add(pKey)) return;
            _queue.Enqueue(pKey);
        }

        private void MarkExactChunk(BoundaryChunkKey pKey)
        {
            if (pKey.X < 0 || pKey.Y < 0 ||
                pKey.X >= _chunkCountX || pKey.Y >= _chunkCountY) return;
            long revision = checked(_nextRevision + 1L);
            _nextRevision = revision;
            _revisions[pKey] = revision;
            if (!_queued.Add(pKey)) return;
            _queue.Enqueue(pKey);
        }

        private static int ChunkCount(int pDimension)
        {
            if (pDimension == 0) return 0;
            long count = ((long)pDimension +
                          HierarchicalVassalBoundaryChunkRules.ChunkSize - 1L) /
                         HierarchicalVassalBoundaryChunkRules.ChunkSize;
            if (count > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(pDimension));
            return (int)count;
        }

        private void AssertMainThread(bool pCaptureIfUnset = false)
        {
            int current = Thread.CurrentThread.ManagedThreadId;
            if (_mainThreadId == 0 && pCaptureIfUnset) _mainThreadId = current;
            if (_mainThreadId == 0)
                throw new InvalidOperationException(
                    "Boundary dirty tracker has not been initialized.");
            if (_mainThreadId != current)
                throw new InvalidOperationException(
                    "Boundary dirty routing must run on the main thread.");
        }
    }
}

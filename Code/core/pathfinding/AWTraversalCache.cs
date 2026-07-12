using System;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    internal sealed class AWTraversalCache
    {
        private readonly Queue<int> _dirtyChunks = new Queue<int>();
        private readonly HashSet<int> _queuedChunks = new HashSet<int>();
        private AWTraversalGeneration _current;
        private int _generationId;
        private int _mainThreadId;
        private int _sweepCursor;
        private int _width;
        private int _height;
        private int _chunksWide;

        public int GenerationId => _current?.Id ?? -1;
        public int DirtyChunkCount => _dirtyChunks.Count;

        public void Initialize()
        {
            AssertMainThread(pCaptureIfUnset: true);
            Clear();
            WorldTile[] tiles = World.world?.tiles_list;
            _width = MapBox.width;
            _height = MapBox.height;
            if (tiles == null || tiles.Length == 0 || _width <= 0 || _height <= 0) return;

            _chunksWide = Math.Max(1,
                (_width + AWTraversalGeneration.DefaultChunkSize - 1) /
                AWTraversalGeneration.DefaultChunkSize);
            var snapshots = new AWTileTraversalSnapshot[tiles.Length];
            var neighbors = new int[8];
            for (int i = 0; i < tiles.Length; i++)
                snapshots[i] = Capture(tiles[i], neighbors);
            _current = AWTraversalGeneration.FromTiles(++_generationId, _width, _height, snapshots);
            _sweepCursor = 0;
        }

        public AWTraversalGeneration Pin()
        {
            AssertMainThread();
            return _current?.Retain();
        }

        public void MarkDirty(WorldTile pTile)
        {
            AssertMainThread();
            if (pTile?.data == null || _current == null || _width <= 0) return;
            int chunkId = ChunkId(pTile.x, pTile.y);
            if (chunkId < 0 || !_queuedChunks.Add(chunkId)) return;
            _dirtyChunks.Enqueue(chunkId);
        }

        public int ProcessDirty(int pChunkBudget)
        {
            AssertMainThread();
            if (_current == null || pChunkBudget <= 0 || _dirtyChunks.Count == 0) return 0;
            WorldTile[] worldTiles = World.world?.tiles_list;
            if (worldTiles == null || worldTiles.Length != _current.TileCount) return 0;

            AWTileTraversalSnapshot[][] chunks = _current.CopyChunkReferences();
            int processed = 0;
            var neighbors = new int[8];
            while (processed < pChunkBudget && _dirtyChunks.Count > 0)
            {
                int chunkId = _dirtyChunks.Dequeue();
                _queuedChunks.Remove(chunkId);
                if (chunkId < 0 || chunkId >= chunks.Length) continue;
                chunks[chunkId] = CaptureChunk(chunkId, worldTiles, neighbors);
                processed++;
            }

            if (processed <= 0) return 0;
            AWTraversalGeneration previous = _current;
            _current = new AWTraversalGeneration(++_generationId, _width, _height,
                AWTraversalGeneration.DefaultChunkSize, chunks);
            previous.Dispose();
            return processed;
        }

        public int ConsistencySweep(int pTileBudget)
        {
            AssertMainThread();
            if (_current == null || pTileBudget <= 0) return 0;
            WorldTile[] tiles = World.world?.tiles_list;
            if (tiles == null || tiles.Length != _current.TileCount) return 0;

            int checkedTiles = 0;
            var neighbors = new int[8];
            while (checkedTiles < pTileBudget && tiles.Length > 0)
            {
                if (_sweepCursor >= tiles.Length) _sweepCursor = 0;
                WorldTile live = tiles[_sweepCursor++];
                AWTileTraversalSnapshot captured = Capture(live, neighbors);
                if (!_current.TryGet(captured.Id, out AWTileTraversalSnapshot cached) ||
                    !Equivalent(cached, captured))
                    MarkDirty(live);
                checkedTiles++;
            }
            return checkedTiles;
        }

        public void Clear()
        {
            AssertMainThread(pCaptureIfUnset: true);
            _dirtyChunks.Clear();
            _queuedChunks.Clear();
            _current?.Dispose();
            _current = null;
            _generationId = 0;
            _sweepCursor = 0;
            _width = 0;
            _height = 0;
            _chunksWide = 0;
        }

        private AWTileTraversalSnapshot[] CaptureChunk(int pChunkId, WorldTile[] pTiles,
            int[] pNeighbors)
        {
            int size = AWTraversalGeneration.DefaultChunkSize;
            var result = new AWTileTraversalSnapshot[size * size];
            int chunkX = pChunkId % _chunksWide;
            int chunkY = pChunkId / _chunksWide;
            int startX = chunkX * size;
            int startY = chunkY * size;
            int endX = Math.Min(_width, startX + size);
            int endY = Math.Min(_height, startY + size);
            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    int tileId = x + y * _width;
                    int local = x - startX + (y - startY) * size;
                    result[local] = Capture(pTiles[tileId], pNeighbors);
                }
            }
            return result;
        }

        private static AWTileTraversalSnapshot Capture(WorldTile pTile, int[] pNeighbors)
        {
            if (pTile?.data == null) return default;
            for (int i = 0; i < pNeighbors.Length; i++) pNeighbors[i] = -1;
            WorldTile[] liveNeighbors = pTile.neighboursAll;
            int neighborCount = Math.Min(pNeighbors.Length, liveNeighbors?.Length ?? 0);
            for (int i = 0; i < neighborCount; i++)
                pNeighbors[i] = liveNeighbors[i]?.data?.tile_id ?? -1;

            TileTypeBase type = pTile.Type;
            bool fire;
            bool goodForBoat;
            try { fire = pTile.isOnFire(); }
            catch { fire = false; }
            try { goodForBoat = pTile.isGoodForBoat(); }
            catch { goodForBoat = false; }
            return new AWTileTraversalSnapshot(pTile.data.tile_id, pTile.x, pTile.y,
                ground: type?.ground ?? false,
                block: type?.block ?? false,
                liquid: type?.liquid ?? false,
                ocean: type?.ocean ?? false,
                lava: type?.lava ?? false,
                fire: fire,
                damageUnits: type?.damage_units ?? false,
                terrainDamage: type?.damage ?? 0f,
                walkMultiplier: type?.walk_multiplier ?? 1f,
                goodForBoat: goodForBoat,
                oceanComponent: -1,
                regionId: pTile.region?.id ?? -1,
                pNeighbors: pNeighbors);
        }

        private int ChunkId(int pX, int pY)
        {
            if (pX < 0 || pY < 0 || pX >= _width || pY >= _height || _chunksWide <= 0) return -1;
            int size = AWTraversalGeneration.DefaultChunkSize;
            return pX / size + pY / size * _chunksWide;
        }

        private static bool Equivalent(AWTileTraversalSnapshot pLeft,
            AWTileTraversalSnapshot pRight)
        {
            if (pLeft.Id != pRight.Id || pLeft.Ground != pRight.Ground ||
                pLeft.Block != pRight.Block || pLeft.Liquid != pRight.Liquid ||
                pLeft.Ocean != pRight.Ocean || pLeft.Lava != pRight.Lava ||
                pLeft.Fire != pRight.Fire || pLeft.DamageUnits != pRight.DamageUnits ||
                pLeft.TerrainDamage != pRight.TerrainDamage ||
                pLeft.WalkMultiplier != pRight.WalkMultiplier ||
                pLeft.GoodForBoat != pRight.GoodForBoat ||
                pLeft.RegionId != pRight.RegionId ||
                pLeft.NeighborCount != pRight.NeighborCount) return false;
            for (int i = 0; i < pLeft.NeighborCount; i++)
                if (pLeft.GetNeighbor(i) != pRight.GetNeighbor(i)) return false;
            return true;
        }

        private void AssertMainThread(bool pCaptureIfUnset = false)
        {
            int current = Thread.CurrentThread.ManagedThreadId;
            if (_mainThreadId == 0 && pCaptureIfUnset) _mainThreadId = current;
            if (_mainThreadId != 0 && _mainThreadId != current)
                throw new InvalidOperationException("AW traversal cache must run on the main thread");
        }
    }
}

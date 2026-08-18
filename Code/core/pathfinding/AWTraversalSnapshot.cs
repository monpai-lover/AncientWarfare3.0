// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    public static class AWTraversalCacheBudgetRules
    {
        public const int DirtyPublishIntervalFrames = 4;
        public const int DirtyTileBudget = 128;
        public const int ConsistencySweepIntervalFrames = 16;
        public const int ConsistencyTileBudget = 16;
        public const int MaximumDirtyTilesPerFrame = DirtyTileBudget;

        public static bool ShouldProcessDirty(long frame, int dirtyTileCount)
        {
            return dirtyTileCount > 0 &&
                   frame > 0 &&
                   frame % DirtyPublishIntervalFrames == 0;
        }

        public static int DirtyTileBudgetForFrame(int dirtyTileCount)
        {
            return Math.Min(DirtyTileBudget, Math.Max(0, dirtyTileCount));
        }

        public static bool ShouldRunConsistencySweep(long frame)
        {
            return frame > 0 &&
                   frame % ConsistencySweepIntervalFrames == 0;
        }
    }

    public readonly struct AWTileTraversalSnapshot
    {
        private readonly int _neighbor0;
        private readonly int _neighbor1;
        private readonly int _neighbor2;
        private readonly int _neighbor3;
        private readonly int _neighbor4;
        private readonly int _neighbor5;
        private readonly int _neighbor6;
        private readonly int _neighbor7;

        public AWTileTraversalSnapshot(int pId, int pX, int pY, bool ground = false,
            bool block = false, bool liquid = false, bool ocean = false, bool lava = false,
            bool fire = false, bool damageUnits = false, float terrainDamage = 0f,
            float walkMultiplier = 1f, bool goodForBoat = false, int oceanComponent = -1,
            int regionId = -1, int islandId = -1, int[] pNeighbors = null,
            bool hasType = true)
            : this(pId, pX, pY, ground, block, liquid, ocean, lava, fire,
                damageUnits, terrainDamage, walkMultiplier, goodForBoat,
                oceanComponent, regionId, islandId,
                Math.Min(8, pNeighbors?.Length ?? 0),
                Neighbor(pNeighbors, 0), Neighbor(pNeighbors, 1),
                Neighbor(pNeighbors, 2), Neighbor(pNeighbors, 3),
                Neighbor(pNeighbors, 4), Neighbor(pNeighbors, 5),
                Neighbor(pNeighbors, 6), Neighbor(pNeighbors, 7), hasType)
        {
        }

        internal AWTileTraversalSnapshot(int pId, int pX, int pY,
            bool ground, bool block, bool liquid, bool ocean, bool lava,
            bool fire, bool damageUnits, float terrainDamage,
            float walkMultiplier, bool goodForBoat, int oceanComponent,
            int regionId, int islandId, int neighborCount,
            int pNeighbor0, int pNeighbor1, int pNeighbor2, int pNeighbor3,
            int pNeighbor4, int pNeighbor5, int pNeighbor6, int pNeighbor7,
            bool hasType)
        {
            Exists = pId >= 0;
            HasType = Exists && hasType;
            Id = pId;
            X = pX;
            Y = pY;
            Ground = ground;
            Block = block;
            Liquid = liquid;
            Ocean = ocean;
            Lava = lava;
            Fire = fire;
            DamageUnits = damageUnits;
            TerrainDamage = Math.Max(0f, terrainDamage);
            WalkMultiplier = walkMultiplier > 0f ? walkMultiplier : 1f;
            GoodForBoat = goodForBoat;
            OceanComponent = oceanComponent;
            RegionId = regionId;
            IslandId = islandId;
            NeighborCount = Math.Max(0, Math.Min(8, neighborCount));
            _neighbor0 = pNeighbor0;
            _neighbor1 = pNeighbor1;
            _neighbor2 = pNeighbor2;
            _neighbor3 = pNeighbor3;
            _neighbor4 = pNeighbor4;
            _neighbor5 = pNeighbor5;
            _neighbor6 = pNeighbor6;
            _neighbor7 = pNeighbor7;
        }

        public bool Exists { get; }
        public bool HasType { get; }
        public int Id { get; }
        public int X { get; }
        public int Y { get; }
        public bool Ground { get; }
        public bool Block { get; }
        public bool Liquid { get; }
        public bool Ocean { get; }
        public bool Lava { get; }
        public bool Fire { get; }
        public bool DamageUnits { get; }
        public float TerrainDamage { get; }
        public float WalkMultiplier { get; }
        public bool GoodForBoat { get; }
        public int OceanComponent { get; }
        public int RegionId { get; }
        public int IslandId { get; }
        public int NeighborCount { get; }

        public int GetNeighbor(int pIndex)
        {
            switch (pIndex)
            {
                case 0: return _neighbor0;
                case 1: return _neighbor1;
                case 2: return _neighbor2;
                case 3: return _neighbor3;
                case 4: return _neighbor4;
                case 5: return _neighbor5;
                case 6: return _neighbor6;
                case 7: return _neighbor7;
                default: return -1;
            }
        }

        internal AWTileTraversalSnapshot WithOceanComponent(int pComponent)
        {
            var neighbors = new int[NeighborCount];
            for (int i = 0; i < NeighborCount; i++) neighbors[i] = GetNeighbor(i);
            return new AWTileTraversalSnapshot(Id, X, Y, Ground, Block,
                Liquid, Ocean, Lava, Fire, DamageUnits, TerrainDamage,
                WalkMultiplier, GoodForBoat, pComponent, RegionId, IslandId,
                neighbors, HasType);
        }

        private static int Neighbor(int[] pNeighbors, int pIndex)
        {
            return pNeighbors != null && pIndex >= 0 && pIndex < pNeighbors.Length
                ? pNeighbors[pIndex]
                : -1;
        }
    }

    public readonly struct AWActorTraversalProfile
    {
        public AWActorTraversalProfile(bool pCanFly, bool pIsBoat, bool pIsWaterCreature,
            bool pForceLandCreature, bool pImmuneToFire, bool pDamagedByOcean,
            bool pDiesInLava, bool pBurning, bool pStartsInLiquid, bool pStartsInWater,
            float pHealth, float pMaxHealth, float pStamina, float pMaxStamina,
            float pMovementSpeed, float pWaterDamage, float pStaminaRegeneration,
            bool pIsMilitary = false, bool pHasFastSwimming = false)
        {
            CanFly = pCanFly;
            IsBoat = pIsBoat;
            IsWaterCreature = pIsWaterCreature;
            ForceLandCreature = pForceLandCreature;
            ImmuneToFire = pImmuneToFire;
            DamagedByOcean = pDamagedByOcean;
            DiesInLava = pDiesInLava;
            Burning = pBurning;
            StartsInLiquid = pStartsInLiquid;
            StartsInWater = pStartsInWater;
            Health = Math.Max(0f, pHealth);
            MaxHealth = Math.Max(1f, pMaxHealth);
            Stamina = Math.Max(0f, pStamina);
            MaxStamina = Math.Max(1f, pMaxStamina);
            MovementSpeed = Math.Max(0.01f, pMovementSpeed);
            WaterDamage = Math.Max(0f, pWaterDamage);
            StaminaRegeneration = Math.Max(0f, pStaminaRegeneration);
            IsMilitary = pIsMilitary;
            HasFastSwimming = pHasFastSwimming;
        }

        public bool CanFly { get; }
        public bool IsBoat { get; }
        public bool IsWaterCreature { get; }
        public bool ForceLandCreature { get; }
        public bool ImmuneToFire { get; }
        public bool DamagedByOcean { get; }
        public bool DiesInLava { get; }
        public bool Burning { get; }
        public bool StartsInLiquid { get; }
        public bool StartsInWater { get; }
        public float Health { get; }
        public float MaxHealth { get; }
        public float Stamina { get; }
        public float MaxStamina { get; }
        public float MovementSpeed { get; }
        public float WaterDamage { get; }
        public float StaminaRegeneration { get; }
        public bool IsMilitary { get; }
        public bool HasFastSwimming { get; }

        public static AWActorTraversalProfile CreateWalker(float health, float stamina, float speed)
        {
            return new AWActorTraversalProfile(false, false, false, false, false, true,
                true, false, false, false, health, health, stamina, stamina, speed, 2f, 0f);
        }
    }

    public sealed class AWTraversalGeneration : IDisposable
    {
        public const int DefaultChunkSize = 8;
        private const int ExistsFlag = 1 << 0;
        private const int HasTypeFlag = 1 << 1;
        private const int GroundFlag = 1 << 2;
        private const int BlockFlag = 1 << 3;
        private const int LiquidFlag = 1 << 4;
        private const int OceanFlag = 1 << 5;
        private const int LavaFlag = 1 << 6;
        private const int FireFlag = 1 << 7;
        private const int DamageUnitsFlag = 1 << 8;
        private const int GoodForBoatFlag = 1 << 9;
        private static long _nextIdentity;

        private readonly int[] _tileFlags;
        private readonly float[] _terrainDamage;
        private readonly float[] _walkMultipliers;
        private readonly int[] _oceanComponents;
        private readonly int[] _regionIds;
        private readonly int[] _islandIds;
        private readonly int[] _neighborCounts;
        private readonly int[] _neighbors;
        private readonly IReadOnlyDictionary<int,
            AWTileTraversalSnapshot[]> _overlayChunks;
        private AWRegionTopologySnapshot _regionTopology;
        private int _topologyRevision;
        private long _revision = 1L;
        private readonly long _identity;
        private AWTraversalGeneration _baseGeneration;
        private int _references = 1;

        internal AWTraversalGeneration(int pId, int pWidth, int pHeight, int pChunkSize,
            AWTileTraversalSnapshot[][] pChunks,
            AWRegionTopologySnapshot pRegionTopology = null)
        {
            Id = pId;
            Width = Math.Max(0, pWidth);
            Height = Math.Max(0, pHeight);
            ChunkSize = Math.Max(1, pChunkSize);
            ChunksWide = Math.Max(1, (Width + ChunkSize - 1) / ChunkSize);
            int tileCount = TileCount;
            _tileFlags = new int[tileCount];
            _terrainDamage = new float[tileCount];
            _walkMultipliers = new float[tileCount];
            _oceanComponents = new int[tileCount];
            _regionIds = new int[tileCount];
            _islandIds = new int[tileCount];
            _neighborCounts = new int[tileCount];
            _neighbors = new int[tileCount * 8];
            for (int tileId = 0; tileId < tileCount; tileId++)
            {
                _oceanComponents[tileId] = -1;
                _regionIds[tileId] = -1;
                _islandIds[tileId] = -1;
            }
            for (int index = 0; index < _neighbors.Length; index++)
                _neighbors[index] = -1;
            _overlayChunks = null;
            _identity = Interlocked.Increment(ref _nextIdentity);
            _regionTopology = pRegionTopology ?? AWRegionTopologySnapshot.Build(
                pChunks, Width, Height, ChunkSize);
            _topologyRevision = _regionTopology.Revision;
            LoadChunks(pChunks);
        }

        private AWTraversalGeneration(int pId, AWTraversalGeneration pBase,
            IReadOnlyDictionary<int, AWTileTraversalSnapshot[]> pOverlays)
        {
            if (pBase == null) throw new ArgumentNullException(nameof(pBase));
            Id = pId;
            Width = pBase.Width;
            Height = pBase.Height;
            ChunkSize = pBase.ChunkSize;
            ChunksWide = pBase.ChunksWide;
            _tileFlags = null;
            _terrainDamage = null;
            _walkMultipliers = null;
            _oceanComponents = null;
            _regionIds = null;
            _islandIds = null;
            _neighborCounts = null;
            _neighbors = null;
            _identity = Interlocked.Increment(ref _nextIdentity);
            _regionTopology = pBase.RegionTopology;
            _topologyRevision = _regionTopology?.Revision ?? 1;
            if (pOverlays == null || pOverlays.Count == 0)
                _overlayChunks = null;
            else
            {
                var overlays = new Dictionary<int,
                    AWTileTraversalSnapshot[]>(pOverlays.Count);
                foreach (KeyValuePair<int, AWTileTraversalSnapshot[]> entry in
                         pOverlays)
                    overlays[entry.Key] = entry.Value;
                _overlayChunks = overlays;
            }
            _baseGeneration = pBase.Retain();
        }

        public int Id { get; }
        public int Width { get; }
        public int Height { get; }
        public int ChunkSize { get; }
        public int ChunksWide { get; }
        public int TileCount => Width * Height;
        public int ReferenceCount => Math.Max(0, Volatile.Read(ref _references));
        internal long Identity => _identity;
        internal long Revision => Volatile.Read(ref _revision);
        internal AWRegionTopologySnapshot RegionTopology =>
            Volatile.Read(ref _regionTopology);

        public AWTraversalGeneration Retain()
        {
            while (true)
            {
                int current = Volatile.Read(ref _references);
                if (current <= 0 || current == int.MaxValue)
                    throw new ObjectDisposedException(nameof(AWTraversalGeneration));
                if (Interlocked.CompareExchange(ref _references, current + 1,
                        current) == current)
                    return this;
            }
        }

        public bool TryGet(int pTileId, out AWTileTraversalSnapshot pTile)
        {
            pTile = default;
            if (pTileId < 0 || pTileId >= TileCount || Width <= 0) return false;
            int x = pTileId % Width;
            int y = pTileId / Width;
            int chunkX = x / ChunkSize;
            int chunkY = y / ChunkSize;
            int chunkId = chunkX + chunkY * ChunksWide;
            AWTileTraversalSnapshot[] chunk;
            if (_overlayChunks != null &&
                _overlayChunks.TryGetValue(chunkId, out chunk)) { }
            else if (_baseGeneration != null)
                return _baseGeneration.TryGet(pTileId, out pTile);
            else
            {
                int flags = Volatile.Read(ref _tileFlags[pTileId]);
                if ((flags & ExistsFlag) == 0) return false;
                int neighborOffset = pTileId * 8;
                pTile = new AWTileTraversalSnapshot(pTileId, x, y,
                    HasFlag(flags, GroundFlag), HasFlag(flags, BlockFlag),
                    HasFlag(flags, LiquidFlag), HasFlag(flags, OceanFlag),
                    HasFlag(flags, LavaFlag), HasFlag(flags, FireFlag),
                    HasFlag(flags, DamageUnitsFlag),
                    Volatile.Read(ref _terrainDamage[pTileId]),
                    Volatile.Read(ref _walkMultipliers[pTileId]),
                    HasFlag(flags, GoodForBoatFlag),
                    Volatile.Read(ref _oceanComponents[pTileId]),
                    Volatile.Read(ref _regionIds[pTileId]),
                    Volatile.Read(ref _islandIds[pTileId]),
                    Volatile.Read(ref _neighborCounts[pTileId]),
                    Volatile.Read(ref _neighbors[neighborOffset]),
                    Volatile.Read(ref _neighbors[neighborOffset + 1]),
                    Volatile.Read(ref _neighbors[neighborOffset + 2]),
                    Volatile.Read(ref _neighbors[neighborOffset + 3]),
                    Volatile.Read(ref _neighbors[neighborOffset + 4]),
                    Volatile.Read(ref _neighbors[neighborOffset + 5]),
                    Volatile.Read(ref _neighbors[neighborOffset + 6]),
                    Volatile.Read(ref _neighbors[neighborOffset + 7]),
                    HasFlag(flags, HasTypeFlag));
                return true;
            }
            if (chunk == null) return false;
            int local = x % ChunkSize + (y % ChunkSize) * ChunkSize;
            if (local < 0 || local >= chunk.Length) return false;
            pTile = chunk[local];
            return pTile.Exists && pTile.Id == pTileId;
        }

        internal AWTileTraversalSnapshot[][] CopyChunkReferences()
        {
            if (_baseGeneration == null)
            {
                int chunksHigh = Math.Max(1,
                    (Height + ChunkSize - 1) / ChunkSize);
                var stableChunks = new AWTileTraversalSnapshot[
                    ChunksWide * chunksHigh][];
                for (int chunkId = 0; chunkId < stableChunks.Length; chunkId++)
                    stableChunks[chunkId] = CopyChunkSnapshot(chunkId);
                return stableChunks;
            }
            AWTileTraversalSnapshot[][] chunks =
                _baseGeneration.CopyChunkReferences();
            if (_overlayChunks != null)
                foreach (KeyValuePair<int, AWTileTraversalSnapshot[]> entry in
                         _overlayChunks)
                    if (entry.Key >= 0 && entry.Key < chunks.Length)
                        chunks[entry.Key] = entry.Value;
            return chunks;
        }

        internal AWTileTraversalSnapshot[] CopyChunkSnapshot(int pChunkId)
        {
            if (pChunkId < 0) return Array.Empty<AWTileTraversalSnapshot>();
            if (_baseGeneration != null)
            {
                if (_overlayChunks != null && _overlayChunks.TryGetValue(
                        pChunkId, out AWTileTraversalSnapshot[] overlay))
                    return (AWTileTraversalSnapshot[])overlay.Clone();
                return _baseGeneration.CopyChunkSnapshot(pChunkId);
            }
            int chunksHigh = Math.Max(1,
                (Height + ChunkSize - 1) / ChunkSize);
            if (pChunkId >= ChunksWide * chunksHigh)
                return Array.Empty<AWTileTraversalSnapshot>();
            var result = new AWTileTraversalSnapshot[ChunkSize * ChunkSize];
            int chunkX = pChunkId % ChunksWide;
            int chunkY = pChunkId / ChunksWide;
            for (int localY = 0; localY < ChunkSize; localY++)
            for (int localX = 0; localX < ChunkSize; localX++)
            {
                int x = chunkX * ChunkSize + localX;
                int y = chunkY * ChunkSize + localY;
                if (x >= Width || y >= Height) continue;
                int tileId = x + y * Width;
                if (TryGet(tileId, out AWTileTraversalSnapshot tile))
                    result[localX + localY * ChunkSize] = tile;
            }
            return result;
        }

        internal void ApplyTileSnapshots(
            IReadOnlyList<AWTileTraversalSnapshot> pTiles)
        {
            if (_baseGeneration != null || pTiles == null ||
                pTiles.Count == 0) return;
            for (int index = 0; index < pTiles.Count; index++)
            {
                AWTileTraversalSnapshot tile = pTiles[index];
                if (!tile.Exists || tile.Id < 0 || tile.Id >= TileCount)
                    continue;
                WriteTileSnapshot(tile);
            }
            Interlocked.Increment(ref _revision);
        }

        private void LoadChunks(AWTileTraversalSnapshot[][] pChunks)
        {
            if (pChunks == null) return;
            for (int chunkId = 0; chunkId < pChunks.Length; chunkId++)
            {
                AWTileTraversalSnapshot[] chunk = pChunks[chunkId];
                if (chunk == null) continue;
                for (int local = 0; local < chunk.Length; local++)
                {
                    AWTileTraversalSnapshot tile = chunk[local];
                    if (tile.Exists && tile.Id >= 0 && tile.Id < TileCount)
                        WriteTileSnapshot(tile);
                }
            }
        }

        private void WriteTileSnapshot(AWTileTraversalSnapshot pTile)
        {
            int tileId = pTile.Id;
            if (_tileFlags == null || tileId < 0 || tileId >= TileCount)
                return;
            Volatile.Write(ref _terrainDamage[tileId], pTile.TerrainDamage);
            Volatile.Write(ref _walkMultipliers[tileId], pTile.WalkMultiplier);
            Volatile.Write(ref _oceanComponents[tileId], pTile.OceanComponent);
            Volatile.Write(ref _regionIds[tileId], pTile.RegionId);
            Volatile.Write(ref _islandIds[tileId], pTile.IslandId);
            Volatile.Write(ref _neighborCounts[tileId], pTile.NeighborCount);
            int neighborOffset = tileId * 8;
            for (int index = 0; index < 8; index++)
                Volatile.Write(ref _neighbors[neighborOffset + index],
                    pTile.GetNeighbor(index));
            Volatile.Write(ref _tileFlags[tileId], FlagsOf(pTile));
        }

        private static int FlagsOf(AWTileTraversalSnapshot pTile)
        {
            int flags = pTile.Exists ? ExistsFlag : 0;
            if (pTile.HasType) flags |= HasTypeFlag;
            if (pTile.Ground) flags |= GroundFlag;
            if (pTile.Block) flags |= BlockFlag;
            if (pTile.Liquid) flags |= LiquidFlag;
            if (pTile.Ocean) flags |= OceanFlag;
            if (pTile.Lava) flags |= LavaFlag;
            if (pTile.Fire) flags |= FireFlag;
            if (pTile.DamageUnits) flags |= DamageUnitsFlag;
            if (pTile.GoodForBoat) flags |= GoodForBoatFlag;
            return flags;
        }

        private static bool HasFlag(int pFlags, int pFlag)
        {
            return (pFlags & pFlag) != 0;
        }

        internal void ReplaceTopologySnapshot(
            AWRegionTopologySnapshot pTopology)
        {
            if (_baseGeneration != null || pTopology == null) return;
            int revision = Interlocked.Increment(ref _topologyRevision);
            AWRegionTopologySnapshot topology = pTopology.WithRevision(revision);
            Volatile.Write(ref _regionTopology, topology);
            Interlocked.Increment(ref _revision);
        }

        internal static AWTraversalGeneration FromOverlay(int pId,
            AWTraversalGeneration pBase,
            IReadOnlyDictionary<int, AWTileTraversalSnapshot[]> pOverlays)
        {
            return pBase == null ? null : new AWTraversalGeneration(pId,
                pBase, pOverlays);
        }

        public static AWTraversalGeneration FromTiles(int pId, int width, int height,
            AWTileTraversalSnapshot[] pTiles, int pChunkSize = DefaultChunkSize)
        {
            int chunkSize = Math.Max(1, pChunkSize);
            int chunksWide = Math.Max(1, (width + chunkSize - 1) / chunkSize);
            int chunksHigh = Math.Max(1, (height + chunkSize - 1) / chunkSize);
            var chunks = new AWTileTraversalSnapshot[chunksWide * chunksHigh][];
            for (int i = 0; i < chunks.Length; i++)
                chunks[i] = new AWTileTraversalSnapshot[chunkSize * chunkSize];

            foreach (AWTileTraversalSnapshot tile in pTiles ?? Array.Empty<AWTileTraversalSnapshot>())
            {
                if (!tile.Exists || tile.Id < 0 || tile.Id >= width * height) continue;
                int chunkX = tile.X / chunkSize;
                int chunkY = tile.Y / chunkSize;
                int chunkId = chunkX + chunkY * chunksWide;
                int local = tile.X % chunkSize + tile.Y % chunkSize * chunkSize;
                chunks[chunkId][local] = tile;
            }
            return new AWTraversalGeneration(pId, width, height, chunkSize, chunks);
        }

        public void Dispose()
        {
            int value = Interlocked.Decrement(ref _references);
            if (value == 0)
            {
                AWTraversalGeneration baseGeneration = Interlocked.Exchange(
                    ref _baseGeneration, null);
                baseGeneration?.Dispose();
            }
            else if (value < 0) Interlocked.Exchange(ref _references, 0);
        }
    }
}

// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    public static class AWTraversalCacheBudgetRules
    {
        public const int DirtyPublishIntervalFrames = 4;
        public const int DirtyChunkBudget = 8;
        public const int AverageDirtyChunkBudgetPerFrame =
            DirtyChunkBudget / DirtyPublishIntervalFrames;
        public const int ConsistencySweepIntervalFrames = 16;
        public const int ConsistencyTileBudget = 16;
        public const int MaximumDirtyTilesPerFrame = AverageDirtyChunkBudgetPerFrame *
                                                    AWTraversalGeneration.DefaultChunkSize *
                                                    AWTraversalGeneration.DefaultChunkSize;

        public static bool ShouldProcessDirty(long frame, int dirtyChunkCount)
        {
            return dirtyChunkCount > 0 &&
                   frame > 0 &&
                   frame % DirtyPublishIntervalFrames == 0;
        }

        public static int DirtyChunkBudgetForFrame(int dirtyChunkCount)
        {
            return Math.Min(DirtyChunkBudget, Math.Max(0, dirtyChunkCount));
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
            int regionId = -1, int islandId = -1, int[] pNeighbors = null)
        {
            Exists = pId >= 0;
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
            NeighborCount = Math.Min(8, pNeighbors?.Length ?? 0);
            _neighbor0 = Neighbor(pNeighbors, 0);
            _neighbor1 = Neighbor(pNeighbors, 1);
            _neighbor2 = Neighbor(pNeighbors, 2);
            _neighbor3 = Neighbor(pNeighbors, 3);
            _neighbor4 = Neighbor(pNeighbors, 4);
            _neighbor5 = Neighbor(pNeighbors, 5);
            _neighbor6 = Neighbor(pNeighbors, 6);
            _neighbor7 = Neighbor(pNeighbors, 7);
        }

        public bool Exists { get; }
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
                neighbors);
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
        private static long _nextIdentity;

        private readonly AWTileTraversalSnapshot[][] _chunks;
        private readonly IReadOnlyDictionary<int,
            AWTileTraversalSnapshot[]> _overlayChunks;
        private readonly AWRegionTopologySnapshot _regionTopology;
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
            _chunks = pChunks ?? Array.Empty<AWTileTraversalSnapshot[]>();
            _overlayChunks = null;
            _identity = Interlocked.Increment(ref _nextIdentity);
            _regionTopology = pRegionTopology ?? AWRegionTopologySnapshot.Build(
                _chunks, Width, Height, ChunkSize);
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
            _chunks = Array.Empty<AWTileTraversalSnapshot[]>();
            _identity = Interlocked.Increment(ref _nextIdentity);
            _regionTopology = pBase.RegionTopology;
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
        internal AWRegionTopologySnapshot RegionTopology => _regionTopology;

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
                if (chunkId < 0 || chunkId >= _chunks.Length) return false;
                chunk = _chunks[chunkId];
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
                return (AWTileTraversalSnapshot[][])_chunks.Clone();
            AWTileTraversalSnapshot[][] chunks =
                _baseGeneration.CopyChunkReferences();
            if (_overlayChunks != null)
                foreach (KeyValuePair<int, AWTileTraversalSnapshot[]> entry in
                         _overlayChunks)
                    if (entry.Key >= 0 && entry.Key < chunks.Length)
                        chunks[entry.Key] = entry.Value;
            return chunks;
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

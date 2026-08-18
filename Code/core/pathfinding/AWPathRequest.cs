// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    public readonly struct AWPathRequestOptions : IEquatable<AWPathRequestOptions>
    {
        public AWPathRequestOptions(bool pPathOnWater, bool pWalkOnBlocks, bool pWalkOnLava,
            int pLimitPathfindingRegions)
            : this(pPathOnWater, pWalkOnBlocks, pWalkOnLava,
                pLimitPathfindingRegions, pBoundedMilitaryWater: false,
                pMaximumConsecutiveWaterTiles: 0)
        {
        }

        private AWPathRequestOptions(bool pPathOnWater, bool pWalkOnBlocks,
            bool pWalkOnLava, int pLimitPathfindingRegions,
            bool pBoundedMilitaryWater, int pMaximumConsecutiveWaterTiles)
        {
            PathOnWater = pPathOnWater;
            WalkOnBlocks = pWalkOnBlocks;
            WalkOnLava = pWalkOnLava;
            LimitPathfindingRegions = Math.Max(0, pLimitPathfindingRegions);
            BoundedMilitaryWater = pBoundedMilitaryWater;
            MaximumConsecutiveWaterTiles = pBoundedMilitaryWater
                ? Math.Max(1, pMaximumConsecutiveWaterTiles)
                : 0;
        }

        public bool PathOnWater { get; }
        public bool WalkOnBlocks { get; }
        public bool WalkOnLava { get; }
        public int LimitPathfindingRegions { get; }
        public bool BoundedMilitaryWater { get; }
        public int MaximumConsecutiveWaterTiles { get; }

        public static AWPathRequestOptions Default => new AWPathRequestOptions(false, false, false, 0);

        public AWPathRequestOptions WithBoundedMilitaryWater(int pMaximumTiles)
        {
            return new AWPathRequestOptions(true, WalkOnBlocks, WalkOnLava,
                LimitPathfindingRegions, pBoundedMilitaryWater: true,
                pMaximumConsecutiveWaterTiles: pMaximumTiles);
        }

        public bool Equals(AWPathRequestOptions pOther)
        {
            return PathOnWater == pOther.PathOnWater &&
                   WalkOnBlocks == pOther.WalkOnBlocks &&
                   WalkOnLava == pOther.WalkOnLava &&
                   LimitPathfindingRegions == pOther.LimitPathfindingRegions &&
                   BoundedMilitaryWater == pOther.BoundedMilitaryWater &&
                   MaximumConsecutiveWaterTiles ==
                   pOther.MaximumConsecutiveWaterTiles;
        }

        public override bool Equals(object pObject)
        {
            return pObject is AWPathRequestOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = PathOnWater ? 1 : 0;
                hash = hash * 397 ^ (WalkOnBlocks ? 1 : 0);
                hash = hash * 397 ^ (WalkOnLava ? 1 : 0);
                hash = hash * 397 ^ LimitPathfindingRegions;
                hash = hash * 397 ^ (BoundedMilitaryWater ? 1 : 0);
                hash = hash * 397 ^ MaximumConsecutiveWaterTiles;
                return hash;
            }
        }
    }

    public sealed class AWPathRequest : IDisposable
    {
        private int _disposed;
        private AWPathStep[] _cachedRoute;
        private int _cachedRouteOffset;
        private bool _cachedRouteReachesTarget;
        private long _cachedRouteRevision;

        public AWPathRequest(long pActorId, int pStartTileId, int pTargetTileId,
            AWPathRequestOptions pOptions, AWActorTraversalProfile pProfile,
            AWTraversalGeneration pGeneration, double pCreatedTime,
            bool pHighPriority, long pTerrainRevision = 0L,
            long pWorldGeneration = 0L, bool pInsideBoat = false,
            bool pPhysicalTransportAvailable = false,
            float pPhysicalTransportRouteTiles = float.PositiveInfinity,
            long pEntryPortalId = -1L, long pExitPortalId = -1L,
            int pEntryLandTileId = -1, int pPickupSeaTileId = -1,
            int pDestinationSeaTileId = -1,
            int pLandingLandTileId = -1)
            : this(pActorId, pStartTileId, pTargetTileId, pOptions, pProfile,
                pGeneration, pCreatedTime, pHighPriority
                    ? AWPathWorkClass.Operational
                    : AWPathWorkClass.Ambient, pTerrainRevision,
                pWorldGeneration, pInsideBoat, pPhysicalTransportAvailable,
                pPhysicalTransportRouteTiles, pEntryPortalId,
                pExitPortalId, pEntryLandTileId, pPickupSeaTileId,
                pDestinationSeaTileId, pLandingLandTileId)
        {
        }

        public AWPathRequest(long pActorId, int pStartTileId, int pTargetTileId,
            AWPathRequestOptions pOptions, AWActorTraversalProfile pProfile,
            AWTraversalGeneration pGeneration, double pCreatedTime,
            AWPathWorkClass pWorkClass = AWPathWorkClass.Ambient,
            long pTerrainRevision = 0L, long pWorldGeneration = 0L,
            bool pInsideBoat = false, bool pPhysicalTransportAvailable = false,
            float pPhysicalTransportRouteTiles = float.PositiveInfinity,
            long pEntryPortalId = -1L, long pExitPortalId = -1L,
            int pEntryLandTileId = -1, int pPickupSeaTileId = -1,
            int pDestinationSeaTileId = -1,
            int pLandingLandTileId = -1)
        {
            ActorId = pActorId;
            _startTileId = pStartTileId;
            TargetTileId = pTargetTileId;
            Options = pOptions;
            Key = new AWPathRequestKey(pTargetTileId, pOptions.PathOnWater,
                pOptions.WalkOnBlocks, pOptions.WalkOnLava,
                pOptions.LimitPathfindingRegions,
                pOptions.BoundedMilitaryWater,
                pOptions.MaximumConsecutiveWaterTiles);
            ReuseKey = new AWPathReuseKey(pActorId,
                StartRegion(pStartTileId, pGeneration), Key,
                pTerrainRevision, pWorldGeneration, pInsideBoat);
            Profile = pProfile;
            Generation = pGeneration?.Retain() ?? throw new ArgumentNullException(nameof(pGeneration));
            CreatedTime = pCreatedTime;
            WorkClass = pWorkClass;
            PhysicalTransportAvailable = pPhysicalTransportAvailable;
            PhysicalTransportRouteTiles = pPhysicalTransportRouteTiles;
            EntryPortalId = pEntryPortalId;
            ExitPortalId = pExitPortalId;
            EntryLandTileId = pEntryLandTileId;
            PickupSeaTileId = pPickupSeaTileId;
            DestinationSeaTileId = pDestinationSeaTileId;
            LandingLandTileId = pLandingLandTileId;
            Cancellation = new CancellationTokenSource();
            Stream = new AWPathStream();
        }

        public long ActorId { get; }
        private int _startTileId;

        public int StartTileId => Volatile.Read(ref _startTileId);
        public int TargetTileId { get; }
        public AWPathRequestOptions Options { get; }
        public AWPathRequestKey Key { get; }
        public AWPathReuseKey ReuseKey { get; }
        public AWActorTraversalProfile Profile { get; }
        public AWTraversalGeneration Generation { get; }
        public int WorldGeneration => Generation.Id;
        public double CreatedTime { get; }
        public AWPathWorkClass WorkClass { get; }
        public bool HighPriority => WorkClass == AWPathWorkClass.Operational;
        public bool PhysicalTransportAvailable { get; }
        public float PhysicalTransportRouteTiles { get; }
        public long EntryPortalId { get; }
        public long ExitPortalId { get; }
        public int EntryLandTileId { get; }
        public int PickupSeaTileId { get; }
        public int DestinationSeaTileId { get; }
        public int LandingLandTileId { get; }
        public CancellationTokenSource Cancellation { get; }
        public AWPathStream Stream { get; }

        internal void AdvanceStartTile(int pTileId)
        {
            if (pTileId >= 0) Volatile.Write(ref _startTileId, pTileId);
        }

        internal bool TryTakeCachedSegment(int pMaximumSteps,
            long pTraversalRevision,
            out AWPathStep[] pSteps, out bool pReachedTarget)
        {
            AWPathStep[] route = _cachedRoute;
            int offset = _cachedRouteOffset;
            if (route != null && _cachedRouteRevision !=
                    pTraversalRevision)
            {
                _cachedRoute = null;
                _cachedRouteOffset = 0;
                _cachedRouteReachesTarget = false;
                route = null;
                offset = 0;
            }
            if (route == null || offset >= route.Length)
            {
                pSteps = Array.Empty<AWPathStep>();
                pReachedTarget = false;
                return false;
            }
            int count = Math.Min(Math.Max(1, pMaximumSteps), route.Length - offset);
            pSteps = new AWPathStep[count];
            Array.Copy(route, offset, pSteps, 0, count);
            _cachedRouteOffset = offset + count;
            bool exhausted = _cachedRouteOffset >= route.Length;
            pReachedTarget = exhausted && _cachedRouteReachesTarget;
            if (exhausted)
            {
                _cachedRoute = null;
                _cachedRouteReachesTarget = false;
            }
            return true;
        }

        internal void CacheRoute(AWPathStep[] pRoute, int pConsumed,
            bool pReachesTarget, long pTraversalRevision)
        {
            _cachedRoute = pRoute;
            _cachedRouteOffset = Math.Max(0, Math.Min(pConsumed,
                pRoute?.Length ?? 0));
            _cachedRouteReachesTarget = pReachesTarget;
            _cachedRouteRevision = pTraversalRevision;
            if (_cachedRouteOffset >= (pRoute?.Length ?? 0))
            {
                _cachedRoute = null;
                _cachedRouteReachesTarget = false;
            }
        }

        private static int StartRegion(int pTileId,
            AWTraversalGeneration pGeneration)
        {
            if (pGeneration == null || pGeneration.Width <= 0 ||
                pTileId < 0 || pTileId >= pGeneration.TileCount)
                return -1;
            int x = pTileId % pGeneration.Width;
            int y = pTileId / pGeneration.Width;
            return x / pGeneration.ChunkSize +
                   y / pGeneration.ChunkSize * pGeneration.ChunksWide;
        }

        public bool Matches(int pTargetTileId, AWPathRequestOptions pOptions)
        {
            return Key.Matches(pTargetTileId, pOptions.PathOnWater, pOptions.WalkOnBlocks,
                       pOptions.WalkOnLava, pOptions.LimitPathfindingRegions,
                       pOptions.BoundedMilitaryWater,
                       pOptions.MaximumConsecutiveWaterTiles) &&
                   Volatile.Read(ref _disposed) == 0;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Cancellation.Dispose();
            Generation.Dispose();
        }
    }
}

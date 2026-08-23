// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections;
using System.Collections.Generic;
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
        private bool _cachedRouteCompletesRequest;
        private long _cachedRouteTerrainRevision;
        private long _cachedRouteWorldGeneration;
        private bool _cachedRouteHasRevision;
        private int _cachedRouteExpectedStartTile = -1;
        private bool _cachedRouteInsideBoat;
        private CachedSegmentView _cachedSegmentView;
        private long _recoveryTerrainRevision;
        private long _recoveryWorldGeneration;
        private int _recoveryTargetId = -1;
        private bool _recoveryAttempted;

        public AWPathRequest(long pActorId, int pStartTileId, int pTargetTileId,
            AWPathRequestOptions pOptions, AWActorTraversalProfile pProfile,
            AWTraversalGeneration pGeneration, double pCreatedTime,
            bool pHighPriority, long pTerrainRevision = 0L,
            long pWorldGeneration = 0L, bool pInsideBoat = false,
            bool pPhysicalTransportAvailable = false)
            : this(pActorId, pStartTileId, pTargetTileId, pOptions, pProfile,
                pGeneration, pCreatedTime, pHighPriority
                    ? AWPathWorkClass.Operational
                    : AWPathWorkClass.Ambient, pTerrainRevision,
                pWorldGeneration, pInsideBoat, pPhysicalTransportAvailable)
        {
        }

        public AWPathRequest(long pActorId, int pStartTileId, int pTargetTileId,
            AWPathRequestOptions pOptions, AWActorTraversalProfile pProfile,
            AWTraversalGeneration pGeneration, double pCreatedTime,
            AWPathWorkClass pWorkClass = AWPathWorkClass.Ambient,
            long pTerrainRevision = 0L, long pWorldGeneration = 0L,
            bool pInsideBoat = false, bool pPhysicalTransportAvailable = false)
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
        public long TerrainRevision => ReuseKey.TerrainRevision;
        public bool InsideBoat => ReuseKey.InsideBoat;
        public CancellationTokenSource Cancellation { get; }
        public AWPathStream Stream { get; }

        internal void AdvanceStartTile(int pTileId)
        {
            if (pTileId >= 0) Volatile.Write(ref _startTileId, pTileId);
        }

        internal bool TryTakeCachedSegment(int pMaximumSteps,
            out IReadOnlyList<AWPathStep> pSteps, out bool pReachedTarget)
        {
            return TryTakeCachedSegment(pMaximumSteps, TerrainRevision,
                ReuseKey.WorldGeneration, InsideBoat, out pSteps,
                out pReachedTarget);
        }

        internal bool TryTakeCachedSegment(int pMaximumSteps,
            long pTerrainRevision, long pWorldGeneration, bool pInsideBoat,
            out IReadOnlyList<AWPathStep> pSteps, out bool pReachedTarget)
        {
            if (!IsCachedRouteCurrent(pTerrainRevision, pWorldGeneration,
                    pInsideBoat) ||
                (_cachedRouteOffset > 0 &&
                  _cachedRouteExpectedStartTile != StartTileId))
            {
                ClearCachedRoute();
                pSteps = Array.Empty<AWPathStep>();
                pReachedTarget = false;
                return false;
            }
            AWPathStep[] route = _cachedRoute;
            int offset = _cachedRouteOffset;
            if (route == null || offset >= route.Length)
            {
                pSteps = Array.Empty<AWPathStep>();
                pReachedTarget = false;
                return false;
            }
            int count = Math.Min(Math.Max(1, pMaximumSteps), route.Length - offset);
            _cachedSegmentView ??= new CachedSegmentView();
            _cachedSegmentView.SetCapacity(count);
            Array.Copy(route, offset, _cachedSegmentView.Buffer, 0, count);
            _cachedSegmentView.SetCount(count);
            pSteps = _cachedSegmentView;
            _cachedRouteOffset = offset + count;
            _cachedRouteExpectedStartTile = route[_cachedRouteOffset - 1].TileId;
            bool routeExhausted = _cachedRouteOffset >= route.Length;
            pReachedTarget = routeExhausted && _cachedRouteCompletesRequest;
            if (routeExhausted) ClearCachedRoute();
            return true;
        }

        internal void CacheRoute(AWPathStep[] pRoute, int pConsumed)
        {
            CacheRoute(pRoute, pConsumed, pCompletesRequest: true,
                TerrainRevision, ReuseKey.WorldGeneration, InsideBoat);
        }

        internal void CacheRoute(AWPathStep[] pRoute, int pConsumed,
            bool pCompletesRequest, long pTerrainRevision,
            long pWorldGeneration, bool pInsideBoat)
        {
            _cachedRoute = pRoute;
            _cachedRouteOffset = Math.Max(0, Math.Min(pConsumed,
                pRoute?.Length ?? 0));
            _cachedRouteCompletesRequest = pCompletesRequest;
            _cachedRouteTerrainRevision = pTerrainRevision;
            _cachedRouteWorldGeneration = pWorldGeneration;
            _cachedRouteHasRevision = pRoute != null && pRoute.Length > 0;
            _cachedRouteInsideBoat = pInsideBoat;
            _cachedRouteExpectedStartTile = _cachedRouteOffset > 0 &&
                pRoute != null && _cachedRouteOffset <= pRoute.Length
                ? pRoute[_cachedRouteOffset - 1].TileId
                : -1;
            if (_cachedRouteOffset >= (pRoute?.Length ?? 0))
                ClearCachedRoute();
        }

        internal bool TryBeginRecovery(int pTargetId, long pTerrainRevision,
            long pWorldGeneration)
        {
            if (_recoveryAttempted && _recoveryTargetId == pTargetId &&
                _recoveryTerrainRevision == pTerrainRevision &&
                _recoveryWorldGeneration == pWorldGeneration)
                return false;
            _recoveryAttempted = true;
            _recoveryTargetId = pTargetId;
            _recoveryTerrainRevision = pTerrainRevision;
            _recoveryWorldGeneration = pWorldGeneration;
            return true;
        }

        private bool IsCachedRouteCurrent(long pTerrainRevision,
            long pWorldGeneration, bool pInsideBoat)
        {
            if (_cachedRoute == null || !_cachedRouteHasRevision)
                return false;
            return _cachedRouteTerrainRevision == pTerrainRevision &&
                   _cachedRouteWorldGeneration == pWorldGeneration &&
                   _cachedRouteInsideBoat == pInsideBoat;
        }

        private void ClearCachedRoute()
        {
            _cachedRoute = null;
            _cachedRouteOffset = 0;
            _cachedRouteCompletesRequest = false;
            _cachedRouteTerrainRevision = 0L;
            _cachedRouteWorldGeneration = 0L;
            _cachedRouteHasRevision = false;
            _cachedRouteExpectedStartTile = -1;
            _cachedRouteInsideBoat = false;
        }

        private sealed class CachedSegmentView : IReadOnlyList<AWPathStep>
        {
            private AWPathStep[] _buffer = Array.Empty<AWPathStep>();
            private int _count;

            internal AWPathStep[] Buffer => _buffer;

            internal void SetCapacity(int pCount)
            {
                if (_buffer.Length >= pCount) return;
                int capacity = Math.Max(8, _buffer.Length);
                while (capacity < pCount) capacity *= 2;
                Array.Resize(ref _buffer, capacity);
            }

            internal void SetCount(int pCount) => _count = pCount;

            public int Count => _count;

            public AWPathStep this[int pIndex]
            {
                get
                {
                    if (pIndex < 0 || pIndex >= _count)
                        throw new ArgumentOutOfRangeException(nameof(pIndex));
                    return _buffer[pIndex];
                }
            }

            public IEnumerator<AWPathStep> GetEnumerator()
            {
                return ((IEnumerable<AWPathStep>)new ArraySegment<AWPathStep>(
                    _buffer, 0, _count)).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
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

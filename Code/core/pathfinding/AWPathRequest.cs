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
#if !AW3_RULES_TESTS
        private AWPathNavigationGrid _navigationGrid;
#endif
        private AWTransportRouteSnapshot _transportRoute;

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
            AgentKey = new AWPathAgentKey(
                AWPathWorldKey.MainWorld(pWorldGeneration), pActorId);
            Initialize(pStartTileId, pTargetTileId, pOptions, pProfile,
                pGeneration, pCreatedTime, pWorkClass, pTerrainRevision,
                pWorldGeneration, pInsideBoat, pPhysicalTransportAvailable);
        }

        public AWPathRequest(AWPathAgentKey pAgentKey, int pStartTileId,
            int pTargetTileId, AWPathRequestOptions pOptions,
            AWActorTraversalProfile pProfile, AWTraversalGeneration pGeneration,
            double pCreatedTime, AWPathWorkClass pWorkClass = AWPathWorkClass.Ambient,
            long pTerrainRevision = 0L, long pWorldGeneration = 0L,
            bool pInsideBoat = false, bool pPhysicalTransportAvailable = false)
        {
            if (!pAgentKey.IsValid)
                throw new ArgumentException("Path agent identity is invalid.",
                    nameof(pAgentKey));
            AgentKey = pAgentKey;
            Initialize(pStartTileId, pTargetTileId, pOptions, pProfile,
                pGeneration, pCreatedTime, pWorkClass, pTerrainRevision,
                pWorldGeneration, pInsideBoat, pPhysicalTransportAvailable);
        }

#if !AW3_RULES_TESTS
        private AWPathRequest(AWPathAgentKey pAgentKey, int pStartTileId,
            int pTargetTileId, AWPathRequestOptions pOptions,
            AWActorTraversalProfile pProfile, AWTraversalGeneration pGeneration,
            double pCreatedTime, AWPathWorkClass pWorkClass,
            long pTerrainRevision, long pWorldGeneration, bool pInsideBoat,
            bool pPhysicalTransportAvailable, AWPathNavigationGrid pGrid)
            : this(pAgentKey, pStartTileId, pTargetTileId, pOptions, pProfile,
                pGeneration, pCreatedTime, pWorkClass, pTerrainRevision,
                pWorldGeneration, pInsideBoat, pPhysicalTransportAvailable)
        {
            _navigationGrid = pGrid ?? _navigationGrid;
        }
#endif

        private void Initialize(int pStartTileId, int pTargetTileId,
            AWPathRequestOptions pOptions, AWActorTraversalProfile pProfile,
            AWTraversalGeneration pGeneration, double pCreatedTime,
            AWPathWorkClass pWorkClass, long pTerrainRevision,
            long pWorldGeneration, bool pInsideBoat,
            bool pPhysicalTransportAvailable)
        {
            _startTileId = pStartTileId;
            TargetTileId = pTargetTileId;
            Options = pOptions;
            Key = new AWPathRequestKey(pTargetTileId, pOptions.PathOnWater,
                pOptions.WalkOnBlocks, pOptions.WalkOnLava,
                pOptions.LimitPathfindingRegions,
                pOptions.BoundedMilitaryWater,
                pOptions.MaximumConsecutiveWaterTiles);
            ReuseKey = new AWPathReuseKey(AgentKey,
                StartRegion(pStartTileId, pGeneration), Key,
                pTerrainRevision, pWorldGeneration, pInsideBoat);
            Profile = pProfile;
            Generation = pGeneration?.Retain() ?? throw new ArgumentNullException(nameof(pGeneration));
            CreatedTime = pCreatedTime;
            WorkClass = pWorkClass;
            PhysicalTransportAvailable = pPhysicalTransportAvailable;
#if !AW3_RULES_TESTS
            // World identity is part of the path request contract. Never
            // substitute the current main-world grid for another world: a
            // missing sub-world registration must remain a rejected request.
            _navigationGrid = AWPathNavigationGridService.Get(AgentKey.World);
#endif
            Cancellation = new CancellationTokenSource();
            Stream = new AWPathStream();
        }

        public AWPathAgentKey AgentKey { get; }
        public long ActorId => AgentKey.AgentId;
        private int _startTileId;

        public int StartTileId => Volatile.Read(ref _startTileId);
        public int TargetTileId { get; private set; }
        public AWPathRequestOptions Options { get; private set; }
        public AWPathRequestKey Key { get; private set; }
        public AWPathReuseKey ReuseKey { get; private set; }
        public AWActorTraversalProfile Profile { get; private set; }
        public AWTraversalGeneration Generation { get; private set; }
        // Generation.Id identifies a snapshot within one world.  Requests
        // must carry the world lifecycle generation instead, otherwise a
        // freshly rebuilt grid can be rejected as a different world.
        public long WorldGeneration => Generation.WorldGeneration;
        public double CreatedTime { get; private set; }
        public AWPathWorkClass WorkClass { get; private set; }
        public AWPathWorldKind WorldKind => AgentKey.World.Kind;
        public bool HighPriority => WorkClass == AWPathWorkClass.Operational;
        public bool PhysicalTransportAvailable { get; private set; }
        internal AWTransportRouteSnapshot TransportRoute => _transportRoute;
        public long TerrainRevision => ReuseKey.TerrainRevision;
        public bool InsideBoat => ReuseKey.InsideBoat;
        public float ActorCurrentStamina => Profile.Stamina;
        public float ActorMaxStamina => Profile.MaxStamina;
        public float ActorCurrentHealth => Profile.Health;
        public float ActorMaxHealth => Profile.MaxHealth;
        public CancellationTokenSource Cancellation { get; private set; }
        public AWPathStream Stream { get; private set; }

#if !AW3_RULES_TESTS
        // Navigation snapshots are captured once per world lifecycle and
        // refreshed through the main-thread path lifecycle.  Generators use
        // this view instead of scanning live tiles from worker threads.
        internal AWPathNavigationGrid NavigationGrid =>
            _navigationGrid;
#endif

        /// <summary>
        /// Builds a request for an independently registered navigation world.
        /// This is a path-core API only; it does not create any gameplay
        /// entity or world instance.
        /// </summary>
#if !AW3_RULES_TESTS
        internal static AWPathRequest CreateSubWorld(
            AWPathAgentKey pAgentKey,
            int pStartTileId,
            int pTargetTileId,
            AWPathNavigationGrid pNavigationGrid,
            AWActorTraversalProfile pProfile,
            double pCreatedTime = 0d,
            AWPathWorkClass pWorkClass = AWPathWorkClass.Operational)
        {
            if (!pAgentKey.IsValid ||
                pAgentKey.World.Kind != AWPathWorldKind.SubWorld)
                throw new ArgumentException(
                    "SubWorld path agent identity is invalid.",
                    nameof(pAgentKey));
            if (pNavigationGrid == null ||
                pNavigationGrid.WorldKey != pAgentKey.World)
                throw new ArgumentException(
                    "SubWorld navigation grid does not match the agent world.",
                    nameof(pNavigationGrid));

            AWTraversalGeneration generation =
                pNavigationGrid.CreateTraversalGeneration();
            try
            {
                return new AWPathRequest(
                    pAgentKey,
                    pStartTileId,
                    pTargetTileId,
                    AWPathRequestOptions.Default,
                    pProfile,
                    generation,
                    pCreatedTime,
                    pWorkClass,
                    pNavigationGrid.Revision,
                    pAgentKey.World.Generation,
                    pInsideBoat: false,
                    pPhysicalTransportAvailable: false,
                    pNavigationGrid);
            }
            finally
            {
                generation.Dispose();
            }
        }
#endif

        internal void SetTransportRoute(AWTransportRouteSnapshot pRoute)
        {
            _transportRoute = pRoute;
            PhysicalTransportAvailable = pRoute.IsValid ||
                                         PhysicalTransportAvailable;
        }

        internal void AdvanceContinuationState(AWTraversalState pState)
        {
            AWActorTraversalProfile profile = Profile;
            Profile = new AWActorTraversalProfile(
                profile.CanFly, profile.IsBoat, profile.IsWaterCreature,
                profile.ForceLandCreature, profile.ImmuneToFire,
                profile.DamagedByOcean, profile.DiesInLava, profile.Burning,
                profile.StartsInLiquid, profile.StartsInWater,
                pState.Health, profile.MaxHealth, pState.Stamina,
                profile.MaxStamina, profile.MovementSpeed,
                profile.WaterDamage, profile.StaminaRegeneration,
                profile.IsMilitary, profile.HasFastSwimming);
        }

        internal AWPathRequest WithStart(int pStartTileId,
            float pCurrentStamina, float pCurrentHealth)
        {
            AWActorTraversalProfile profile = new AWActorTraversalProfile(
                Profile.CanFly, Profile.IsBoat, Profile.IsWaterCreature,
                Profile.ForceLandCreature, Profile.ImmuneToFire,
                Profile.DamagedByOcean, Profile.DiesInLava, Profile.Burning,
                Profile.StartsInLiquid, Profile.StartsInWater,
                pCurrentHealth, Profile.MaxHealth, pCurrentStamina,
                Profile.MaxStamina, Profile.MovementSpeed,
                Profile.WaterDamage, Profile.StaminaRegeneration,
                Profile.IsMilitary, Profile.HasFastSwimming);
            var request = new AWPathRequest(AgentKey, pStartTileId, TargetTileId,
                Options, profile, Generation, CreatedTime, WorkClass,
                TerrainRevision, ReuseKey.WorldGeneration, InsideBoat,
                PhysicalTransportAvailable);
            request.SetTransportRoute(TransportRoute);
            return request;
        }

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

    public static class AWPathRequestValidationRules
    {
        public static AWPathFailureReason Validate(AWPathRequest pRequest,
            long pCurrentWorldGeneration)
        {
            if (pRequest == null || !pRequest.AgentKey.IsValid)
                return AWPathFailureReason.InvalidActor;
            if (pRequest.Generation == null)
                return AWPathFailureReason.StaleTraversal;

            long generation = pRequest.Generation.WorldGeneration;
            long requestWorld = pRequest.ReuseKey.WorldGeneration;
            long agentWorld = pRequest.AgentKey.World.Generation;
            // Rule-test generators intentionally use a generation-less
            // synthetic snapshot and may emit abstract tile ids. Production
            // snapshots always carry the lifecycle generation and get the
            // same bounds checks as Cultiway's navigation grid.
            if (generation != 0L || pCurrentWorldGeneration != 0L)
            {
                if (pRequest.StartTileId < 0 ||
                    pRequest.StartTileId >= pRequest.Generation.TileCount)
                    return AWPathFailureReason.InvalidStart;
                if (pRequest.TargetTileId < 0 ||
                    pRequest.TargetTileId >= pRequest.Generation.TileCount)
                    return AWPathFailureReason.InvalidTarget;
            }
            bool isSubWorld = pRequest.AgentKey.World.Kind ==
                AWPathWorldKind.SubWorld;
            if ((generation != 0L && requestWorld != 0L &&
                 generation != requestWorld) ||
                (generation != 0L && agentWorld != 0L &&
                 generation != agentWorld) ||
                (requestWorld != 0L && agentWorld != 0L &&
                 requestWorld != agentWorld) ||
                (!isSubWorld && pCurrentWorldGeneration != 0L &&
                 generation != 0L &&
                 generation != pCurrentWorldGeneration) ||
                (!isSubWorld && pCurrentWorldGeneration != 0L &&
                 requestWorld != 0L &&
                 requestWorld != pCurrentWorldGeneration))
                return AWPathFailureReason.StaleTraversal;

            return AWPathFailureReason.None;
        }
    }
}

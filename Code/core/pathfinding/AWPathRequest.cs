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

        public AWPathRequest(long pActorId, int pStartTileId, int pTargetTileId,
            AWPathRequestOptions pOptions, AWActorTraversalProfile pProfile,
            AWTraversalGeneration pGeneration, double pCreatedTime,
            bool pHighPriority)
            : this(pActorId, pStartTileId, pTargetTileId, pOptions, pProfile,
                pGeneration, pCreatedTime, pHighPriority
                    ? AWPathWorkClass.Operational
                    : AWPathWorkClass.Ambient)
        {
        }

        public AWPathRequest(long pActorId, int pStartTileId, int pTargetTileId,
            AWPathRequestOptions pOptions, AWActorTraversalProfile pProfile,
            AWTraversalGeneration pGeneration, double pCreatedTime,
            AWPathWorkClass pWorkClass = AWPathWorkClass.Ambient)
        {
            ActorId = pActorId;
            StartTileId = pStartTileId;
            TargetTileId = pTargetTileId;
            Options = pOptions;
            Key = new AWPathRequestKey(pTargetTileId, pOptions.PathOnWater,
                pOptions.WalkOnBlocks, pOptions.WalkOnLava,
                pOptions.LimitPathfindingRegions,
                pOptions.BoundedMilitaryWater,
                pOptions.MaximumConsecutiveWaterTiles);
            Profile = pProfile;
            Generation = pGeneration?.Retain() ?? throw new ArgumentNullException(nameof(pGeneration));
            CreatedTime = pCreatedTime;
            WorkClass = pWorkClass;
            Cancellation = new CancellationTokenSource();
            Stream = new AWPathStream();
        }

        public long ActorId { get; }
        public int StartTileId { get; }
        public int TargetTileId { get; }
        public AWPathRequestOptions Options { get; }
        public AWPathRequestKey Key { get; }
        public AWActorTraversalProfile Profile { get; }
        public AWTraversalGeneration Generation { get; }
        public int WorldGeneration => Generation.Id;
        public double CreatedTime { get; }
        public AWPathWorkClass WorkClass { get; }
        public bool HighPriority => WorkClass == AWPathWorkClass.Operational;
        public CancellationTokenSource Cancellation { get; }
        public AWPathStream Stream { get; }

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

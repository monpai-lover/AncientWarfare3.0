// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    public readonly struct AWPathRequestOptions : IEquatable<AWPathRequestOptions>
    {
        public AWPathRequestOptions(bool pPathOnWater, bool pWalkOnBlocks, bool pWalkOnLava,
            int pLimitPathfindingRegions)
        {
            PathOnWater = pPathOnWater;
            WalkOnBlocks = pWalkOnBlocks;
            WalkOnLava = pWalkOnLava;
            LimitPathfindingRegions = Math.Max(0, pLimitPathfindingRegions);
        }

        public bool PathOnWater { get; }
        public bool WalkOnBlocks { get; }
        public bool WalkOnLava { get; }
        public int LimitPathfindingRegions { get; }

        public static AWPathRequestOptions Default => new AWPathRequestOptions(false, false, false, 0);

        public bool Equals(AWPathRequestOptions pOther)
        {
            return PathOnWater == pOther.PathOnWater &&
                   WalkOnBlocks == pOther.WalkOnBlocks &&
                   WalkOnLava == pOther.WalkOnLava &&
                   LimitPathfindingRegions == pOther.LimitPathfindingRegions;
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
                return hash;
            }
        }
    }

    public sealed class AWPathRequest : IDisposable
    {
        private int _disposed;

        public AWPathRequest(long pActorId, int pStartTileId, int pTargetTileId,
            AWPathRequestOptions pOptions, AWActorTraversalProfile pProfile,
            AWTraversalGeneration pGeneration, double pCreatedTime)
        {
            ActorId = pActorId;
            StartTileId = pStartTileId;
            TargetTileId = pTargetTileId;
            Options = pOptions;
            Key = new AWPathRequestKey(pTargetTileId, pOptions.PathOnWater,
                pOptions.WalkOnBlocks, pOptions.WalkOnLava,
                pOptions.LimitPathfindingRegions);
            Profile = pProfile;
            Generation = pGeneration?.Retain() ?? throw new ArgumentNullException(nameof(pGeneration));
            CreatedTime = pCreatedTime;
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
        public CancellationTokenSource Cancellation { get; }
        public AWPathStream Stream { get; }

        public bool Matches(int pTargetTileId, AWPathRequestOptions pOptions)
        {
            return Key.Matches(pTargetTileId, pOptions.PathOnWater, pOptions.WalkOnBlocks,
                       pOptions.WalkOnLava, pOptions.LimitPathfindingRegions) &&
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

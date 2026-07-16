// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;

namespace AncientWarfare3.core.pathfinding
{
    public readonly struct AWPathRequestKey : IEquatable<AWPathRequestKey>
    {
        public AWPathRequestKey(int pTarget, bool pWater, bool pBlocks, bool pLava,
            int pRegionLimit)
        {
            TargetTileId = pTarget;
            PathOnWater = pWater;
            WalkOnBlocks = pBlocks;
            WalkOnLava = pLava;
            RegionLimit = Math.Max(0, pRegionLimit);
        }

        public int TargetTileId { get; }
        public bool PathOnWater { get; }
        public bool WalkOnBlocks { get; }
        public bool WalkOnLava { get; }
        public int RegionLimit { get; }

        public bool Matches(int pTarget, bool pWater, bool pBlocks, bool pLava,
            int pRegionLimit)
        {
            return TargetTileId == pTarget && PathOnWater == pWater &&
                   WalkOnBlocks == pBlocks && WalkOnLava == pLava &&
                   RegionLimit == Math.Max(0, pRegionLimit);
        }

        public bool Equals(AWPathRequestKey pOther)
        {
            return Matches(pOther.TargetTileId, pOther.PathOnWater, pOther.WalkOnBlocks,
                pOther.WalkOnLava, pOther.RegionLimit);
        }

        public override bool Equals(object pObject)
        {
            return pObject is AWPathRequestKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = TargetTileId;
                hash = hash * 397 ^ (PathOnWater ? 1 : 0);
                hash = hash * 397 ^ (WalkOnBlocks ? 1 : 0);
                hash = hash * 397 ^ (WalkOnLava ? 1 : 0);
                hash = hash * 397 ^ RegionLimit;
                return hash;
            }
        }
    }

    public enum AWPathSlotAction
    {
        Enqueue,
        ReplacePending,
        StoreAfterRunning
    }

    public sealed class AWLatestPathSlotRules
    {
        private bool _queued;
        private bool _running;

        public int MaximumQueuedNodes { get; private set; }

        public AWPathSlotAction Submit(bool pHasPending, bool pHasRunning)
        {
            _queued |= pHasPending;
            _running |= pHasRunning;
            if (_running) return AWPathSlotAction.StoreAfterRunning;
            if (_queued) return AWPathSlotAction.ReplacePending;
            _queued = true;
            MaximumQueuedNodes = Math.Max(MaximumQueuedNodes, 1);
            return AWPathSlotAction.Enqueue;
        }

        public void WorkerStarted()
        {
            _queued = false;
            _running = true;
        }

        public bool WorkerFinished()
        {
            _running = false;
            _queued = true;
            MaximumQueuedNodes = Math.Max(MaximumQueuedNodes, 1);
            return true;
        }
    }

    public static class AWPathLifecycleRules
    {
        public static int RetryLimit(AWPathFailureReason pReason)
        {
            switch (pReason)
            {
                case AWPathFailureReason.StepBlocked:
                case AWPathFailureReason.UnsafeStep:
                    return 4;
                case AWPathFailureReason.PortalUnavailable:
                case AWPathFailureReason.TransportFailed:
                case AWPathFailureReason.Timeout:
                    return 2;
                case AWPathFailureReason.GeneratorException:
                    return 1;
                default:
                    return 0;
            }
        }

        public static float RetryDelay(int pAttempt)
        {
            if (pAttempt <= 1) return 0.3f;
            return Math.Min(2f, 0.3f * (1 << Math.Min(3, pAttempt - 1)));
        }

        public static bool AcceptWorldGeneration(int pRequestGeneration,
            int pCurrentGeneration)
        {
            return pRequestGeneration == pCurrentGeneration;
        }
    }
}

using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.pathfinding
{
    public enum AWPathOwnerState
    {
        Pending,
        Aw3,
        Suspending,
        Cultiway
    }

    public sealed class PathfindingOwnershipRules
    {
        public const string CultiwayHarmonyOwner = "inmny.cultiway";

        private int _stableTicks;
        private bool _yieldedThisWorld;

        public AWPathOwnerState State { get; private set; } = AWPathOwnerState.Pending;

        public AWPathOwnerState ObserveOwners(IEnumerable<string> pOwners)
        {
            if (_yieldedThisWorld) return State = AWPathOwnerState.Cultiway;
            if (ContainsCultiway(pOwners))
            {
                _yieldedThisWorld = true;
                _stableTicks = 0;
                return State = AWPathOwnerState.Cultiway;
            }
            _stableTicks++;
            if (_stableTicks >= 2) State = AWPathOwnerState.Aw3;
            return State;
        }

        public void OnMatchingAssemblyLoad()
        {
            if (_yieldedThisWorld) return;
            _stableTicks = 0;
            State = State == AWPathOwnerState.Aw3
                ? AWPathOwnerState.Suspending
                : AWPathOwnerState.Pending;
        }

        public void BeginStabilization()
        {
            if (_yieldedThisWorld) return;
            _stableTicks = 0;
            State = State == AWPathOwnerState.Aw3
                ? AWPathOwnerState.Suspending
                : AWPathOwnerState.Pending;
        }

        public void ResetWorld()
        {
            _yieldedThisWorld = false;
            _stableTicks = 0;
            State = AWPathOwnerState.Pending;
        }

        private static bool ContainsCultiway(IEnumerable<string> pOwners)
        {
            if (pOwners == null) return false;
            foreach (string owner in pOwners)
                if (string.Equals(owner, CultiwayHarmonyOwner, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}

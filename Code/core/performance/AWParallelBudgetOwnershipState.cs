using System;

namespace AncientWarfare3.core.performance
{
    internal sealed class AWParallelBudgetOwnershipState
    {
        private object _world;
        private int _nativeParallelism;

        public bool IsOwned => _world != null;

        public bool IsOwnedBy(object pWorld)
        {
            return IsOwned && ReferenceEquals(_world, pWorld);
        }

        public void Acquire(object pWorld, int pNativeParallelism)
        {
            if (pWorld == null)
                throw new ArgumentNullException(nameof(pWorld));
            if (IsOwned)
            {
                if (!ReferenceEquals(_world, pWorld))
                    throw new InvalidOperationException(
                        "parallel budget is already owned by another world");
                return;
            }

            _world = pWorld;
            _nativeParallelism = pNativeParallelism;
        }

        public bool TryGetOwnership(out object pWorld,
            out int pNativeParallelism)
        {
            if (!IsOwned)
            {
                pWorld = null;
                pNativeParallelism = 0;
                return false;
            }

            pWorld = _world;
            pNativeParallelism = _nativeParallelism;
            return true;
        }

        public void Release(object pWorld)
        {
            if (!IsOwned || !ReferenceEquals(_world, pWorld))
                throw new InvalidOperationException(
                    "parallel budget release owner does not match");
            _world = null;
            _nativeParallelism = 0;
        }
    }
}

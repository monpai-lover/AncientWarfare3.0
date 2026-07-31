using System;

namespace AncientWarfare3.core.performance
{
    internal sealed class AWSchedulerResourceOwnership<TWorld>
        where TWorld : class
    {
        private readonly Func<TWorld, int> _readParallelism;
        private readonly Action<TWorld, int> _writeParallelism;
        private readonly AWParallelBudgetOwnershipState _parallelOwnership =
            new AWParallelBudgetOwnershipState();

        public AWSchedulerResourceOwnership(
            Func<TWorld, int> pReadParallelism,
            Action<TWorld, int> pWriteParallelism)
        {
            _readParallelism = pReadParallelism ??
                throw new ArgumentNullException(nameof(pReadParallelism));
            _writeParallelism = pWriteParallelism ??
                throw new ArgumentNullException(nameof(pWriteParallelism));
        }

        public bool IsParallelOwned => _parallelOwnership.IsOwned;

        public void Acquire(TWorld pWorld, int pSchedulerParallelism)
        {
            if (pWorld == null)
                throw new ArgumentNullException(nameof(pWorld));

            if (_parallelOwnership.IsOwned &&
                !_parallelOwnership.IsOwnedBy(pWorld))
                ReleaseParallelBudget();
            if (!_parallelOwnership.IsOwned)
                _parallelOwnership.Acquire(pWorld,
                    _readParallelism(pWorld));
            _writeParallelism(pWorld, pSchedulerParallelism);
        }

        public void Release()
        {
            ReleaseParallelBudget();
        }

        private void ReleaseParallelBudget()
        {
            if (!_parallelOwnership.TryGetOwnership(out object rawWorld,
                    out int nativeParallelism)) return;
            _writeParallelism((TWorld)rawWorld, nativeParallelism);
            _parallelOwnership.Release(rawWorld);
        }
    }
}

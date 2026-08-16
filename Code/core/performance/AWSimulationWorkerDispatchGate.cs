using System;
using System.Threading;

namespace AncientWarfare3.core.performance
{
    internal sealed class AWSimulationWorkerDispatchGate
    {
        private readonly int[] _assignedGenerations;

        internal AWSimulationWorkerDispatchGate(int pWorkerCount)
        {
            if (pWorkerCount < 0)
                throw new ArgumentOutOfRangeException(nameof(pWorkerCount));
            _assignedGenerations = new int[pWorkerCount];
        }

        internal void Assign(int pWorkerIndex, int pGeneration)
        {
            Volatile.Write(ref _assignedGenerations[pWorkerIndex], pGeneration);
        }

        internal int Consume(int pWorkerIndex)
        {
            return Interlocked.Exchange(
                ref _assignedGenerations[pWorkerIndex], 0);
        }
    }
}

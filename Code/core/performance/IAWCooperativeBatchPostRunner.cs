using System.Collections.Generic;
using System.Threading.Tasks;

namespace AncientWarfare3.core.performance
{
    internal interface IAWCooperativeBatchPostRunner<TBatch, TObject>
        where TBatch : Batch<TObject>, new()
    {
        void Start(List<TBatch> pActiveBatches, float pElapsed,
            ParallelOptions pParallelOptions);
        string GetNextPhaseName(string pPhasePrefix);
        bool WaitingForBackgroundWork { get; }
        bool IsBackgroundWorkCompleted { get; }
        bool TryJoinBackgroundWork(double pMaximumMilliseconds);
        void WaitForBackgroundWork();
        bool Step();
        void Abort();
    }
}

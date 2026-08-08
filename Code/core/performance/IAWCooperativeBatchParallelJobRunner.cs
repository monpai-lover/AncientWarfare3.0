using System.Collections.Generic;
using System.Threading.Tasks;

namespace AncientWarfare3.core.performance
{
    internal interface IAWCooperativeBatchParallelJobRunner<TBatch, TObject>
        where TBatch : Batch<TObject>, new()
    {
        bool TrySkipAllBatches(Job<TObject> pJob, int pBatchCount,
            float pElapsed);
        bool TryRunGroup(IReadOnlyList<TBatch> pBatches, int pJobIndex,
            int[] pActiveBatchIndices, int pActiveBatchCount,
            float pElapsed, ParallelOptions pParallelOptions);
        bool TryRun(TBatch pBatch, Job<TObject> pJob, float pElapsed);
    }
}

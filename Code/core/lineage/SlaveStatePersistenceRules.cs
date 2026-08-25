using System;

namespace AncientWarfare3.core.lineage
{
    internal static class SlaveStatePersistenceRules
    {
        // Synchronous SQLite fallback must stay below the old 32-row burst.
        // Remaining snapshots stay in the coalesced queue for the next frame.
        internal const int SynchronousBatchSize = 8;

        internal static int ResolveSynchronousBatchSize(int pPendingCount)
        {
            return Math.Min(SynchronousBatchSize,
                Math.Max(0, pPendingCount));
        }
    }
}

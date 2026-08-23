using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    internal static class AWDeferredPathRequestBatchRules
    {
        internal const int DefaultCapacity = 512;
        internal const int MaximumAmbientFlushPerFrame = 96;

        internal static bool ShouldDeferAmbientFlush(bool pOperational,
            int pAmbientSubmitted)
        {
            return !pOperational &&
                   pAmbientSubmitted >= MaximumAmbientFlushPerFrame;
        }

        internal static bool CanCapture(int pCount, int pCapacity,
            bool pCycleAccepting)
        {
            return pCycleAccepting && pCount >= 0 &&
                   pCapacity > 0 && pCount < pCapacity;
        }

        internal static int ReplaceSlotForActor(
            IDictionary<long, int> pSlots, long pActorId)
        {
            if (pSlots == null || pActorId < 0) return -1;
            return pSlots.TryGetValue(pActorId, out int slot) ? slot : -1;
        }
    }
}

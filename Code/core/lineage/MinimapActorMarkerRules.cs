using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class MinimapActorMarkerRules
    {
        public static bool TryReserve(HashSet<long> pReservedActorIds, long pActorId)
        {
            return pReservedActorIds != null && pActorId >= 0 &&
                   pReservedActorIds.Add(pActorId);
        }
    }
}

using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.naming
{
    internal static class AWLocalizedNameProjectionChangeRules
    {
        internal static bool ShouldInvalidate(string pBefore, string pAfter)
        {
            return !string.Equals(pBefore ?? string.Empty,
                pAfter ?? string.Empty, StringComparison.Ordinal);
        }

        internal static bool TryMarkInvalidated(ISet<long> pInvalidatedIds,
            long pObjectId, string pBefore, string pAfter)
        {
            return pInvalidatedIds != null && pObjectId >= 0L &&
                   ShouldInvalidate(pBefore, pAfter) &&
                   pInvalidatedIds.Add(pObjectId);
        }
    }
}

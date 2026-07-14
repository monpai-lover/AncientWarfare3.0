using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public static class HistoricalSchoolVenueRules
    {
        public static bool TrySelect(long pStableKey, int pCandidateCount,
            ISet<int> pOccupied, out int pIndex)
        {
            pIndex = -1;
            if (pCandidateCount <= 0) return false;
            int start = StableIndex(pStableKey, pCandidateCount);
            for (int offset = 0; offset < pCandidateCount; offset++)
            {
                int index = (start + offset) % pCandidateCount;
                if (pOccupied != null && pOccupied.Contains(index)) continue;
                pIndex = index;
                return true;
            }
            return false;
        }

        private static int StableIndex(long pStableKey, int pCount)
        {
            unchecked
            {
                long mixed = (pStableKey ^ (pStableKey >> 32)) * 1103515245L + 12345L;
                return (int)(Math.Abs(mixed % pCount));
            }
        }
    }
}

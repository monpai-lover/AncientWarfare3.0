using System;

namespace AncientWarfare3.core.court
{
    internal static class CourtCandidateBudgetRules
    {
        internal const int MaximumScanSize = 12;

        internal static int ResolveScanSize(int pCandidateCount)
        {
            return Math.Min(MaximumScanSize, Math.Max(0, pCandidateCount));
        }
    }
}

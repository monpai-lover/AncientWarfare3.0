using System;

namespace AncientWarfare3.core.lineage
{
    public static class KingdomVisualRandomizationRules
    {
        public static bool ShouldRerollNewCivVisuals(bool pHasKingdom, bool pIsCivilized, bool pIsNeutral,
            int pColorCount, int pBackgroundCount, int pIconCount)
        {
            return pHasKingdom && pIsCivilized && !pIsNeutral &&
                   pColorCount > 0 && pBackgroundCount > 0 && pIconCount > 0;
        }

        public static int NormalizeVisualIndex(int pCandidateIndex, int pCurrentIndex, int pCount)
        {
            if (pCount <= 0) return -1;
            long raw = pCandidateIndex;
            int index = (int)((raw % pCount + pCount) % pCount);
            if (pCount > 1 && pCurrentIndex >= 0 && pCurrentIndex < pCount && index == pCurrentIndex)
                index = (index + 1) % pCount;
            return index;
        }
    }
}

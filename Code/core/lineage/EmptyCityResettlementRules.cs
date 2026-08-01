using System;

namespace AncientWarfare3.core.lineage
{
    public static class EmptyCityResettlementRules
    {
        public static bool CanResettle(bool pCityValid, bool pCityRekt,
            bool pNeutral, int pZoneCount, int pLivingPopulation,
            bool pRazeIntent, bool pFrozenOccupation)
        {
            return pCityValid && !pCityRekt && pNeutral && pZoneCount > 0 &&
                   pLivingPopulation <= 0 && !pRazeIntent &&
                   !pFrozenOccupation;
        }

        public static int ResolveScanCount(int pAvailable, int pBudget)
        {
            return Math.Max(0, Math.Min(pAvailable, pBudget));
        }
    }
}

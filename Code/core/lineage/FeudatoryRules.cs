using System;

namespace AncientWarfare3.core.lineage
{
    public static class FeudatoryRules
    {
        public const int MaximumCities = 5;
        public const int MaximumPrincesPerDecision = 8;
        public const int AnnualModulo = 4;

        public static bool IsEligiblePrince(bool pIsMandateDynast, bool pAdult,
            bool pMale, bool pKing, bool pHeir, bool pAlreadyPrince,
            bool pValidRestorationState)
        {
            return pIsMandateDynast && pAdult && pMale && !pKing && !pHeir &&
                   !pAlreadyPrince && pValidRestorationState;
        }

        public static bool CanAssignCity(bool pSameKingdom, bool pAlive,
            bool pCapital, bool pCapitalAdjacent, bool pAssigned,
            bool pConnected, int pSelectedCount)
        {
            return pSameKingdom && pAlive && !pCapital && !pCapitalAdjacent &&
                   !pAssigned && pConnected && pSelectedCount >= 0 &&
                   pSelectedCount < MaximumCities;
        }

        public static bool ShouldRunAnnualWork(int pYear, long pKingdomId,
            int pModulo = AnnualModulo)
        {
            if (pModulo <= 0) return false;
            int slot = (int)(Math.Abs(pKingdomId) % pModulo);
            int yearSlot = ((pYear % pModulo) + pModulo) % pModulo;
            return yearSlot == slot;
        }
    }
}

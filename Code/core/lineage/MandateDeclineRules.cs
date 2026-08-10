using System;

namespace AncientWarfare3.core.lineage
{
    public static class MandateDeclineRules
    {
        public const int ChaosCollapseYears = 8;
        public const int ChaosRecoveryYears = 3;
        public const int MaximumAnnualCityLoss = 12;

        public static int CityTransferDelta(bool pCapital)
        {
            return pCapital ? -8 : -2;
        }

        public static int ClampAnnualCityLoss(int pAccumulatedLoss)
        {
            return Math.Max(-MaximumAnnualCityLoss,
                Math.Min(0, pAccumulatedLoss));
        }

        public static int WarDefeatDelta(bool pHalfLoss, bool pTotalLoss)
        {
            if (pTotalLoss) return -9;
            return pHalfLoss ? -7 : -4;
        }

        public static bool ShouldCollapseChaos(int pUnresolvedYears,
            bool pUnresolved)
        {
            return pUnresolved && pUnresolvedYears >= ChaosCollapseYears;
        }

        public static bool ShouldRecoverChaos(int pMandateValue,
            int pAuthority, float pCoreControl, bool pActiveClaimants,
            bool pActiveZhuluWars, int pStableYears)
        {
            return pMandateValue >= 40 && pAuthority >= 40 &&
                   pCoreControl >= 0.70f && !pActiveClaimants &&
                   !pActiveZhuluWars && pStableYears >= ChaosRecoveryYears;
        }
    }
}

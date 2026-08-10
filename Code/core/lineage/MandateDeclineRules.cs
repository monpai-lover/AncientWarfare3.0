using System;

namespace AncientWarfare3.core.lineage
{
    public enum MandateProtectionResolution
    {
        Collapse,
        StartGrace,
        ContinueGrace
    }

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
            bool pActiveZhuluWars, int pCatalystScore, int pStableYears)
        {
            return IsChaosRecoveryYear(pMandateValue, pAuthority,
                       pCoreControl, pActiveClaimants, pActiveZhuluWars,
                       pCatalystScore) &&
                   pStableYears >= ChaosRecoveryYears;
        }

        public static bool IsChaosRecoveryYear(int pMandateValue,
            int pAuthority, float pCoreControl, bool pActiveClaimants,
            bool pActiveZhuluWars, int pCatalystScore)
        {
            return pMandateValue >= 40 && pAuthority >= 40 &&
                   pCoreControl >= 0.70f && !pActiveClaimants &&
                   !pActiveZhuluWars && pCatalystScore <= 40;
        }

        public static bool IsChaosUnresolved(int pMandateValue,
            float pCoreControl, bool pActiveClaimants, bool pActiveZhuluWars,
            int pCatalystScore)
        {
            return pMandateValue <= 0 || pCoreControl < 0.50f ||
                   pActiveClaimants || pActiveZhuluWars || pCatalystScore >= 90;
        }

        public static MandateProtectionResolution ResolveProtection(
            bool pEligible, bool pUsed, int pCurrentYear, int pGraceUntilYear)
        {
            if (pUsed && pCurrentYear < pGraceUntilYear)
                return MandateProtectionResolution.ContinueGrace;
            if (pEligible && !pUsed)
                return MandateProtectionResolution.StartGrace;
            return MandateProtectionResolution.Collapse;
        }
    }
}

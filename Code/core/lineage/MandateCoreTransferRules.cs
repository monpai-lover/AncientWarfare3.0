using System;

namespace AncientWarfare3.core.lineage
{
    public static class MandateCoreTransferRules
    {
        public static bool ShouldInvalidate(bool pHasCurrentPeriod, bool pIsLegalCore)
        {
            return pHasCurrentPeriod && pIsLegalCore;
        }

        public static bool ShouldApplyMandateLoss(bool pHasCurrentPeriod,
            bool pIsLegalCore, bool pOldOwnerIsMandate, bool pOwnerChanged)
        {
            return pHasCurrentPeriod && pIsLegalCore &&
                   pOldOwnerIsMandate && pOwnerChanged;
        }

        public static int AllowedAnnualLossDelta(int pCurrentAnnualLoss,
            int pRequestedDelta)
        {
            int current = Math.Max(0, Math.Min(12, -pCurrentAnnualLoss));
            int requested = Math.Max(0, -pRequestedDelta);
            return -Math.Min(requested, 12 - current);
        }

        public static bool ShouldTransferCapitalRing(bool pMandateWar,
            bool pAttackersWon, bool pCapitalCaptured,
            bool pCityOwnedByFormerMandate, bool pAlreadyTransferred)
        {
            return pMandateWar && pAttackersWon && pCapitalCaptured &&
                   !pAlreadyTransferred;
        }
    }
}

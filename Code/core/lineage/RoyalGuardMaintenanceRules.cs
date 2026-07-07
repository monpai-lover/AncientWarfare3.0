namespace AncientWarfare3.core.lineage
{
    public static class RoyalGuardMaintenanceRules
    {
        public static bool ShouldCheckFromCity(bool pHasCity, bool pHasKingdom, bool pHasCapital, bool pIsCapital)
        {
            if (!pHasCity || !pHasKingdom) return false;
            if (!pHasCapital) return true;
            return pIsCapital;
        }

        public static bool ShouldRunScheduledCheck(int pNow, int pLastCheck, int pInterval, long pKingdomId)
        {
            if (pInterval <= 0) return true;
            if (pLastCheck >= 0 && pNow < pLastCheck) return true;
            if (pLastCheck >= 0 && pNow - pLastCheck < pInterval) return false;

            int slot = PositiveModulo(pKingdomId, pInterval);
            if (PositiveModulo(pNow, pInterval) == slot) return true;

            return pLastCheck >= 0 && pNow - pLastCheck >= pInterval * 2;
        }

        public static bool ShouldDismissNonXiaKingdom(bool pKingIsXia, bool pHasGuardStateHint)
        {
            if (pKingIsXia) return false;
            return pHasGuardStateHint;
        }

        public static bool ShouldSearchCandidates(int pActiveCount, int pActiveNobleCount,
            int pTargetNobleCount, bool pHasCaptain, int pRefillThreshold)
        {
            if (!pHasCaptain) return true;
            if (pActiveNobleCount < pTargetNobleCount) return true;

            int threshold = pRefillThreshold <= 0 ? 1 : pRefillThreshold;
            return pActiveCount < threshold;
        }

        public static bool ShouldPersistGuardIdentityRefresh(
            bool pWasGuard,
            bool pWasCaptain,
            bool pCaptain,
            bool pKingdomChanged,
            bool pNameChanged,
            bool pMissingTrait,
            bool pArmyChanged,
            bool pProfessionChanged,
            bool pJobChanged)
        {
            if (!pWasGuard) return true;
            if (pWasCaptain != pCaptain) return true;
            return pKingdomChanged || pNameChanged || pMissingTrait;
        }

        public static bool ShouldApplyGuardRuntimeRefresh(
            bool pArmyChanged,
            bool pProfessionChanged,
            bool pJobChanged)
        {
            return pArmyChanged || pProfessionChanged || pJobChanged;
        }

        public static bool ShouldPersistNewGuardDuringFill(bool pFinalRefreshWillRun)
        {
            return !pFinalRefreshWillRun;
        }

        private static int PositiveModulo(long pValue, int pModulo)
        {
            long result = pValue % pModulo;
            if (result < 0) result += pModulo;
            return (int)result;
        }
    }
}

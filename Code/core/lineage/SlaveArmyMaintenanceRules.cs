namespace AncientWarfare3.core.lineage
{
    public static class SlaveArmyMaintenanceRules
    {
        public static bool ShouldRunMaintenance(bool pSlaveryEnabled, bool pSlaveArmyEnabled, bool pOnSchedule)
        {
            return ShouldRunMaintenance(pSlaveryEnabled, pSlaveArmyEnabled, pOnSchedule,
                pContinuationDue: false);
        }

        public static bool ShouldRunMaintenance(bool pSlaveryEnabled, bool pSlaveArmyEnabled,
            bool pOnSchedule, bool pContinuationDue)
        {
            return pSlaveryEnabled && pSlaveArmyEnabled && (pOnSchedule || pContinuationDue);
        }

        public static bool ShouldCheckSlaveLabor(bool pHasCity, bool pHasKingdom,
            bool pSlaveryEnabled, bool pAlreadyRecordedForKingdom, bool pMaintenanceDue)
        {
            return pHasCity && pHasKingdom && pSlaveryEnabled &&
                   !pAlreadyRecordedForKingdom && pMaintenanceDue;
        }

        public static bool ShouldInferSlaveArmyComposition(bool pRoleMarkedSlaveArmy,
            bool pSlaveryEnabled)
        {
            return !pRoleMarkedSlaveArmy && pSlaveryEnabled;
        }

        public static bool HasReachedFormationThreshold(int pCount, int pThreshold)
        {
            return pCount >= System.Math.Max(1, pThreshold);
        }

        public static bool ShouldReuseFrontlineTarget(bool pHasEntry, bool pTargetAlive,
            bool pStillHostile, bool pSameIsland, double pNow, double pExpiresAt)
        {
            return pHasEntry && pTargetAlive && pStillHostile && pSameIsland &&
                   pNow <= pExpiresAt;
        }

        public static bool ShouldIssueFrontlineOrder(bool pAlreadyTargetsActor, bool pIsMoving)
        {
            return !pAlreadyTargetsActor || !pIsMoving;
        }

        public static bool ShouldPromoteCandidate(bool pCompositionAllowsCandidate,
            bool pAlreadyWarrior, int pPromotionsThisPass, int pPromotionLimit)
        {
            if (!pCompositionAllowsCandidate || pAlreadyWarrior) return false;
            int limit = pPromotionLimit <= 0 ? 1 : pPromotionLimit;
            return pPromotionsThisPass < limit;
        }

        public static bool ShouldPreferReadyWarrior(bool pCandidateIsWarrior,
            bool pHavePromotionCandidate)
        {
            return pCandidateIsWarrior;
        }

        public static int NextScanCursor(int pStartCursor, int pScanned, bool pScanComplete)
        {
            if (pScanComplete) return 0;
            return System.Math.Max(0, pStartCursor) + System.Math.Max(0, pScanned);
        }

        public static bool ShouldScheduleContinuation(bool pArmyUnderfilled,
            bool pScanComplete, int pAddedThisPass)
        {
            return pArmyUnderfilled && (!pScanComplete || pAddedThisPass > 0);
        }

        public static bool ShouldRefreshKingdomArmyNames(bool pIsSlaveArmy)
        {
            return pIsSlaveArmy;
        }

        public static bool ShouldSkipStableArmyFill(
            bool pArmyExists,
            int pTotalWarriors,
            int pSlaveWarriors,
            int pNonSlaveWarriors,
            bool pCaptainValid,
            int pCitySlaveCount)
        {
            if (!pArmyExists || !pCaptainValid) return false;
            if (!SlaveArmyFormationRules.IsSlaveArmyComposition(
                    pTotalWarriors,
                    pSlaveWarriors,
                    pNonSlaveWarriors,
                    pCaptainValid))
                return false;
            if (pTotalWarriors >= 25) return true;

            int citySlaves = pCitySlaveCount < 0 ? 0 : pCitySlaveCount;
            return citySlaves > 0 && pSlaveWarriors >= citySlaves;
        }

        public static bool ShouldCountCitySlavesForStableCheck(
            bool pArmyExists,
            int pTotalWarriors,
            int pSlaveWarriors,
            int pNonSlaveWarriors,
            bool pCaptainValid)
        {
            if (!pArmyExists || !pCaptainValid) return true;
            if (!SlaveArmyFormationRules.IsSlaveArmyComposition(
                    pTotalWarriors,
                    pSlaveWarriors,
                    pNonSlaveWarriors,
                    pCaptainValid))
                return true;
            return pTotalWarriors < 25;
        }

        public static bool ShouldDriveFrontline(bool pHasArmy, bool pHasEnemies, bool pOnSchedule)
        {
            return pHasArmy && pHasEnemies && pOnSchedule;
        }

        public static bool ShouldStopFillBatch(int pAddedThisPass, int pBatchLimit)
        {
            int limit = pBatchLimit <= 0 ? 1 : pBatchLimit;
            return pAddedThisPass >= limit;
        }

        public static bool ShouldSkipAfterFailedMaintenance(int pNow, int pLastFailure, int pCooldownYears)
        {
            if (pCooldownYears <= 0) return false;
            if (pLastFailure < 0) return false;
            if (pNow < pLastFailure) return false;
            return pNow - pLastFailure < pCooldownYears;
        }
    }
}

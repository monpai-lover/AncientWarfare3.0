using System;

namespace AncientWarfare3.core.lineage
{
    public static class RoyalGuardMaintenanceRules
    {
        public static bool ShouldDissolveGuards(bool pIsRepublic, bool pIsRebel, bool pKingdomExtinct)
        {
            return pIsRepublic || pIsRebel || pKingdomExtinct;
        }

        public static bool ShouldInspectNormalArmyForGuards(bool pIsGuardArmy,
            bool pHasGuardStateHint)
        {
            return !pIsGuardArmy && pHasGuardStateHint;
        }

        public static bool ShouldPreserveGuards(bool pSuccessionPending, bool pIsRepublic,
            bool pIsRebel, bool pKingdomExtinct)
        {
            return pSuccessionPending && !ShouldDissolveGuards(pIsRepublic, pIsRebel, pKingdomExtinct);
        }

        public static int DismissCountForPass(int pRemainingCount, int pBudget)
        {
            int remaining = Math.Max(0, pRemainingCount);
            int budget = Math.Max(1, pBudget);
            return Math.Min(remaining, budget);
        }

        public static int TrimExcessCountForPass(int pActiveCount, int pDesiredCount,
            int pHardMaximum, int pRemainingBudget)
        {
            if (pRemainingBudget <= 0) return 0;
            int limit = Math.Max(0, Math.Min(pDesiredCount, pHardMaximum));
            int excess = Math.Max(0, pActiveCount - limit);
            return Math.Min(excess, pRemainingBudget);
        }

        public static bool ShouldClearDismissState(bool pDismissComplete)
        {
            return pDismissComplete;
        }

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

        public static bool ShouldScanKingdomForDismiss(
            bool pHasCollectedActiveList,
            int pActiveGuardCount,
            bool pHasGuardStateHint)
        {
            if (pHasCollectedActiveList) return false;
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

        public static bool ShouldUseArmyFastPathForActiveGuards(bool pGuardArmyFound, int pGuardArmyUnitCount)
        {
            return pGuardArmyFound && pGuardArmyUnitCount >= 0;
        }

        public static bool ShouldUseRosterFastPathForActiveGuards(bool pGuardArmyFound, bool pHasRoster)
        {
            return !pGuardArmyFound && pHasRoster;
        }

        public static bool ShouldUseRosterForDismiss(bool pHasRoster)
        {
            return pHasRoster;
        }

        public static int MaxFastPathGuardArmyScan(int pMaxActiveGuards, int pRuntimeRefreshLimit)
        {
            int maxGuards = pMaxActiveGuards <= 0 ? 1 : pMaxActiveGuards;
            int refreshLimit = pRuntimeRefreshLimit <= 0 ? 1 : pRuntimeRefreshLimit;
            return Math.Max(maxGuards + refreshLimit, maxGuards * 2);
        }

        public static bool ShouldStopFastPathGuardArmyScan(int pScanned, int pActiveCount,
            int pMaxActiveGuards, int pMaxScan)
        {
            int maxActive = pMaxActiveGuards <= 0 ? 1 : pMaxActiveGuards;
            int maxScan = pMaxScan <= 0 ? maxActive : pMaxScan;
            return pActiveCount >= maxActive || pScanned >= maxScan;
        }

        public static bool HasGuardDataForKingdom(bool pGuardFlag, long pGuardKingdomId,
            long pActorKingdomId, long pTargetKingdomId)
        {
            if (pTargetKingdomId < 0) return false;
            if (pGuardKingdomId >= 0) return pGuardKingdomId == pTargetKingdomId;
            return pGuardFlag && pActorKingdomId == pTargetKingdomId;
        }

        public static bool ShouldRemoveStaleActorFromGuardArmy(bool pHasGuardDataForKingdom,
            bool pActorArmyIsGuardArmy, int pRemovedCount, int pRemovalLimit)
        {
            if (pHasGuardDataForKingdom || !pActorArmyIsGuardArmy) return false;
            int limit = pRemovalLimit <= 0 ? 1 : pRemovalLimit;
            return pRemovedCount < limit;
        }

        public static bool ShouldUseBoundedDismissScan(bool pGuardArmyFound, bool pHasGuardStateHint)
        {
            return !pGuardArmyFound && pHasGuardStateHint;
        }

        public static bool ShouldStopBoundedDismissScan(int pProcessed, int pLimit)
        {
            int limit = pLimit <= 0 ? 1 : pLimit;
            return pProcessed >= limit;
        }

        public static bool ShouldClearGuardHintAfterFallbackScan(
            bool pScanComplete,
            int pActiveGuardCount,
            bool pFoundGuardArmy)
        {
            return pScanComplete && pActiveGuardCount <= 0 && !pFoundGuardArmy;
        }

        public static bool ShouldKeepExistingCaptain(bool pExistingCaptainValid, bool pExistingCaptainNoble)
        {
            return pExistingCaptainValid && pExistingCaptainNoble;
        }

        public static bool ShouldDismissActiveGuardsForCaptainShortage(
            int pActiveGuardCount,
            int pAvailableNobleCount)
        {
            return false;
        }

        public static bool ShouldDeferGuardMaintenanceForCaptainShortage(
            int pActiveGuardCount,
            int pAvailableNobleCount)
        {
            return pActiveGuardCount > 0 && pAvailableNobleCount <= 0;
        }

        public static bool ShouldRefreshGuardInMaintenancePass(bool pIsCaptain, bool pIsNewlyAppointed,
            int pActorIndex, int pCursor, int pBatchLimit, int pActiveCount)
        {
            if (pIsCaptain || pIsNewlyAppointed) return true;
            if (pActorIndex < 0 || pActiveCount <= 0) return false;

            int activeCount = Math.Max(1, pActiveCount);
            int cursor = PositiveModulo(pCursor, activeCount);
            int limit = Math.Max(1, Math.Min(pBatchLimit <= 0 ? 1 : pBatchLimit, activeCount));
            for (int i = 0; i < limit; i++)
            {
                if ((cursor + i) % activeCount == pActorIndex) return true;
            }
            return false;
        }

        public static int NextRefreshCursor(int pCursor, int pActiveCount, int pBatchLimit)
        {
            if (pActiveCount <= 0) return 0;
            int limit = Math.Max(1, pBatchLimit <= 0 ? 1 : pBatchLimit);
            return PositiveModulo(pCursor + limit, pActiveCount);
        }

        public static bool ShouldWriteGuardRoster(string pCurrentRoster, string pNextRoster)
        {
            return !string.Equals(pCurrentRoster ?? "", pNextRoster ?? "", StringComparison.Ordinal);
        }

        public static bool ShouldRecordExpensiveIdentityRefresh(
            bool pWasGuard,
            bool pWasCaptain,
            bool pCaptain,
            bool pMissingTrait,
            bool pKingdomChanged,
            bool pNameChanged)
        {
            if (!pWasGuard) return true;
            if (pWasCaptain != pCaptain) return true;
            if (pMissingTrait) return true;
            return pKingdomChanged;
        }

        public static bool ShouldFallbackToKingdomScanForActiveGuards(bool pGuardArmyFound, bool pHasGuardStateHint)
        {
            return !pGuardArmyFound && pHasGuardStateHint;
        }

        public static bool ShouldKeepBoundedCandidate(int pCurrentCount, int pLimit,
            float pLowestScore, float pCandidateScore)
        {
            int limit = pLimit <= 0 ? 1 : pLimit;
            if (pCurrentCount < limit) return true;
            return pCandidateScore > pLowestScore;
        }

        public static bool ShouldStopCandidateScan(int pScannedCount, int pMaxScan)
        {
            int maxScan = pMaxScan <= 0 ? 1 : pMaxScan;
            return pScannedCount >= maxScan;
        }

        public static int NextBoundedScanCursor(int pStartCursor, int pScannedCount, bool pScanComplete)
        {
            if (pScanComplete) return 0;
            int start = Math.Max(0, pStartCursor);
            int scanned = Math.Max(0, pScannedCount);
            return start + scanned;
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

        // 每趟维护只重建有限个禁卫头像(clearGraphicsFully 极贵),其余标脏留到后续趟处理,
        // 把编队/改制时的瞬时图形重建尖峰摊平到多趟。
        public static bool ShouldRebuildGuardGraphicsNow(bool pGfxDirty, int pRebuiltThisPass, int pBudget)
        {
            if (!pGfxDirty) return false;
            int budget = pBudget <= 0 ? 1 : pBudget;
            return pRebuiltThisPass < budget;
        }

        public static bool ShouldApplyRuntimeRefreshNow(
            bool pPersistRefresh,
            bool pRuntimeRefresh,
            int pRuntimeRefreshesApplied,
            int pRuntimeRefreshLimit)
        {
            if (pPersistRefresh) return true;
            if (!pRuntimeRefresh) return false;

            int limit = pRuntimeRefreshLimit <= 0 ? 1 : pRuntimeRefreshLimit;
            return pRuntimeRefreshesApplied < limit;
        }

        public static int ClampDesiredGuardCountForBatch(
            int pCurrentActiveCount,
            int pDesiredCount,
            int pRecruitmentLimit)
        {
            if (pDesiredCount <= pCurrentActiveCount) return pDesiredCount;

            int limit = pRecruitmentLimit <= 0 ? 1 : pRecruitmentLimit;
            return Math.Min(pDesiredCount, pCurrentActiveCount + limit);
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

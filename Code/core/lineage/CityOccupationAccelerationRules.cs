using System;

namespace AncientWarfare3.core.lineage
{
    public static class CityOccupationAccelerationRules
    {
        public static bool ShouldAdoptDominantCapturer(bool hasDominantEnemy, bool hasCaptureOwner,
            bool captureOwnerAlive, bool captureOwnerStillEnemyOfCity)
        {
            return hasDominantEnemy &&
                   (!hasCaptureOwner || !captureOwnerAlive || !captureOwnerStillEnemyOfCity);
        }

        public static bool ShouldApplyCapturePointContribution(bool contributorIsCityOwner,
            int capturePoints, bool cityOwnerHasActiveDefenders)
        {
            bool passiveWatchtowerContribution = contributorIsCityOwner && capturePoints >= 10;
            return !passiveWatchtowerContribution || cityOwnerHasActiveDefenders;
        }

        public static float ExtraCapturePoints(bool pIsBeingCapturedByEnemy, bool pHasDefenders,
            bool pHasCityControlGoal, int pWatchTowerCount)
        {
            return ExtraCapturePoints(pIsBeingCapturedByEnemy, true, pHasDefenders,
                pHasCityControlGoal, pWatchTowerCount);
        }

        public static float ExtraCapturePoints(bool pIsBeingCapturedByEnemy, bool pHasActiveCaptureUnits,
            bool pHasDefenders, bool pHasCityControlGoal, int pWatchTowerCount)
        {
            return ExtraCapturePoints(pIsBeingCapturedByEnemy, pHasActiveCaptureUnits,
                pCanAdvanceCurrentCapture: true, pHasDefenders, pHasCityControlGoal, pWatchTowerCount);
        }

        public static float ExtraCapturePoints(bool pIsBeingCapturedByEnemy, bool pHasActiveCaptureUnits,
            bool pCanAdvanceCurrentCapture, bool pHasDefenders, bool pHasCityControlGoal,
            int pWatchTowerCount)
        {
            if (!pIsBeingCapturedByEnemy) return 0f;
            if (!pHasActiveCaptureUnits) return 0f;
            if (!pCanAdvanceCurrentCapture) return 0f;
            if (pHasDefenders) return 0f;

            float bonus = pHasCityControlGoal ? 1.55f : 0.45f;
            bonus -= Math.Max(0, pWatchTowerCount) * 0.35f;
            return Math.Max(0f, bonus);
        }
    }
}

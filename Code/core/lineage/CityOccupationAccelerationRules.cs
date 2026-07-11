using System;

namespace AncientWarfare3.core.lineage
{
    public static class CityOccupationAccelerationRules
    {
        public static float ExtraCapturePoints(bool pIsBeingCapturedByEnemy, bool pHasDefenders,
            bool pHasCityControlGoal, int pWatchTowerCount)
        {
            return ExtraCapturePoints(pIsBeingCapturedByEnemy, true, pHasDefenders,
                pHasCityControlGoal, pWatchTowerCount);
        }

        public static float ExtraCapturePoints(bool pIsBeingCapturedByEnemy, bool pHasActiveCaptureUnits,
            bool pHasDefenders, bool pHasCityControlGoal, int pWatchTowerCount)
        {
            if (!pIsBeingCapturedByEnemy) return 0f;
            if (!pHasActiveCaptureUnits) return 0f;
            if (pHasDefenders) return 0f;

            float bonus = pHasCityControlGoal ? 1.55f : 0.45f;
            bonus -= Math.Max(0, pWatchTowerCount) * 0.35f;
            return Math.Max(0f, bonus);
        }
    }
}

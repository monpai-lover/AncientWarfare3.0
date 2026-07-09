using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    public static class CityOccupationAccelerationRules
    {
        public static float ExtraCapturePoints(bool pIsBeingCapturedByEnemy, bool pHasDefenders,
            bool pHasCityControlGoal, int pWatchTowerCount)
        {
            if (!pIsBeingCapturedByEnemy) return 0f;
            if (pHasDefenders) return 0f;

            float bonus = pHasCityControlGoal ? 1.55f : 0.45f;
            bonus -= Mathf.Max(0, pWatchTowerCount) * 0.35f;
            return Mathf.Max(0f, bonus);
        }
    }
}

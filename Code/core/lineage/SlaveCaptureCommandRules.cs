namespace AncientWarfare3.core.lineage
{
    public static class SlaveCaptureCommandRules
    {
        public static bool CanCommandSlaveCapture(bool pIsSlaveArmyCaptain, bool pIsSlave, bool pSlaveryEnabled)
        {
            return pSlaveryEnabled && pIsSlaveArmyCaptain && !pIsSlave;
        }

        public static bool ShouldScanForCaptureTargets(bool pHasEnemyWar, bool pInEnemyTerritory)
        {
            return pHasEnemyWar && pInEnemyTerritory;
        }

        public static int CityFallSlaveTargetCount(int pEligibleCount, float pRatio, int pMaximum)
        {
            if (pEligibleCount <= 0) return 0;
            int maximum = System.Math.Max(1, pMaximum);
            int target = System.Math.Max(1,
                (int)System.Math.Ceiling(pEligibleCount * System.Math.Max(0f, pRatio)));
            return System.Math.Min(maximum, target);
        }

        public static float WaitAfterNoTarget(float pMin, float pMax)
        {
            return ClampMin(pMin, pMax);
        }

        public static float WaitAfterFailure(float pMin, float pMax)
        {
            return ClampMin(pMin, pMax);
        }

        public static float WaitAfterSuccess(float pMin, float pMax)
        {
            return ClampMin(pMin, pMax);
        }

        private static float ClampMin(float pMin, float pMax)
        {
            if (pMax < pMin) return pMax;
            return pMin;
        }
    }
}

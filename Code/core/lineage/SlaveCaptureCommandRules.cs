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

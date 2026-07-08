namespace AncientWarfare3.core.lineage
{
    public static class SlaveArmyMaintenanceRules
    {
        public static bool ShouldRunMaintenance(bool pSlaveryEnabled, bool pSlaveArmyEnabled, bool pOnSchedule)
        {
            return pSlaveryEnabled && pSlaveArmyEnabled && pOnSchedule;
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

        public static bool ShouldDriveFrontline(bool pHasArmy, bool pHasEnemies, bool pOnSchedule)
        {
            return pHasArmy && pHasEnemies && pOnSchedule;
        }

        public static bool ShouldStopFillBatch(int pAddedThisPass, int pBatchLimit)
        {
            int limit = pBatchLimit <= 0 ? 1 : pBatchLimit;
            return pAddedThisPass >= limit;
        }
    }
}

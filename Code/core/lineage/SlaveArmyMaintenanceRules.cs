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
            bool pCaptainValid)
        {
            if (!pArmyExists || !pCaptainValid) return false;
            return SlaveArmyFormationRules.IsSlaveArmyComposition(
                       pTotalWarriors,
                       pSlaveWarriors,
                       pNonSlaveWarriors,
                       pCaptainValid) &&
                   pTotalWarriors >= 25;
        }

        public static bool ShouldDriveFrontline(bool pHasArmy, bool pHasEnemies, bool pOnSchedule)
        {
            return pHasArmy && pHasEnemies && pOnSchedule;
        }
    }
}

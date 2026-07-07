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
    }
}

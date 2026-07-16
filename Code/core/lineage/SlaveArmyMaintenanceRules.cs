namespace AncientWarfare3.core.lineage
{
    public static class SlaveArmyMaintenanceRules
    {
        public static bool ShouldCheckSlaveLabor(bool pHasCity, bool pHasKingdom,
            bool pSlaveryEnabled, bool pAlreadyRecordedForKingdom, bool pMaintenanceDue)
        {
            return pHasCity && pHasKingdom && pSlaveryEnabled &&
                   !pAlreadyRecordedForKingdom && pMaintenanceDue;
        }
    }
}

namespace AncientWarfare3.core.policy
{
    internal static class AWMapModeCoordinator
    {
        private static readonly string[] Priority =
        {
            HierarchicalVassalMapModeService.POWER_ID,
            ShiLineageMapModeService.POWER_ID,
            FeudatoryMapModeService.POWER_ID,
            SchoolMapModeService.POWER_ID,
            MandateCoreMapModeService.POWER_ID,
            MandateDynastyMapModeService.POWER_ID,
            WarCoreMapModeService.POWER_ID,
            VassalMapModeService.POWER_ID,
            TechMapModeService.POWER_ID
        };

        public static bool IsActive(string pPowerId)
        {
            return ActivePowerId() == pPowerId;
        }

        public static string ActivePowerId()
        {
            for (int i = 0; i < Priority.Length; i++)
                if (IsSelected(Priority[i])) return Priority[i];

            for (int i = 0; i < Priority.Length; i++)
                if (IsOptionActive(Priority[i])) return Priority[i];

            return "";
        }

        public static bool IsSelected(string pPowerId)
        {
            try { return World.world != null && World.world.isSelectedPower(pPowerId); }
            catch { return false; }
        }

        private static bool IsOptionActive(string pPowerId)
        {
            try
            {
                return PlayerConfig.optionBoolEnabled(AWMapModeMetaRules.ResolveOptionId(pPowerId));
            }
            catch { return false; }
        }
    }
}

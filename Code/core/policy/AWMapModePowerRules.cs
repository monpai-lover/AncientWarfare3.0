namespace AncientWarfare3.core.policy
{
    public static class AWMapModePowerRules
    {
        public static int ResolveForcedMapModeForLayerPowerId()
        {
            return 0;
        }

        public static MetaType ResolveForcedMapModeForLayerPower()
        {
            return (MetaType)ResolveForcedMapModeForLayerPowerId();
        }

        public static MetaType ResolveForcedMapModeForPower(string pPowerId)
        {
            return pPowerId == "aw_de_jure_region_create" ||
                   pPowerId == "aw_de_jure_region_assign" ||
                   pPowerId == "aw_de_jure_region_retire"
                ? AWMapModeMetaTypes.HierarchicalVassal
                : ResolveForcedMapModeForLayerPower();
        }

        public static bool ShouldUseGodPowerMultiToggle(int pOptionCount)
        {
            return pOptionCount > 1;
        }
    }
}

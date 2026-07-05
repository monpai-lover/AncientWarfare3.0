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

        public static bool ShouldUseGodPowerMultiToggle(int pOptionCount)
        {
            return pOptionCount > 1;
        }
    }
}

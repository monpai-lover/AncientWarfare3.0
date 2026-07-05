namespace AncientWarfare3.core.policy
{
    public enum WarMapModeLayer
    {
        Core,
        Claim
    }

    public static class WarMapModeOptionRules
    {
        public static WarMapModeLayer ResolveLayer(int pZoneOption)
        {
            return pZoneOption <= 0 ? WarMapModeLayer.Core : WarMapModeLayer.Claim;
        }
    }
}

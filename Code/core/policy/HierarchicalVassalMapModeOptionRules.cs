namespace AncientWarfare3.core.policy
{
    public enum HierarchicalVassalMapModeLayer
    {
        Countries,
        Cities
    }

    public static class HierarchicalVassalMapModeOptionRules
    {
        public static HierarchicalVassalMapModeLayer ResolveLayer(int pZoneOption)
        {
            return pZoneOption <= 0
                ? HierarchicalVassalMapModeLayer.Countries
                : HierarchicalVassalMapModeLayer.Cities;
        }
    }
}

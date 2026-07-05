namespace AncientWarfare3.core.policy
{
    public enum TechMapModeLayer
    {
        CityTech,
        Development
    }

    public static class TechMapModeOptionRules
    {
        public static TechMapModeLayer ResolveLayer(int pZoneOption)
        {
            return pZoneOption <= 0 ? TechMapModeLayer.CityTech : TechMapModeLayer.Development;
        }
    }
}

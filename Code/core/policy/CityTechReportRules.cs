namespace AncientWarfare3.core.policy
{
    public static class CityTechReportRules
    {
        public static bool ShouldLoadNeighborBonus(bool includeNeighborBonus, string currentTechId)
        {
            return includeNeighborBonus && !string.IsNullOrEmpty(currentTechId);
        }
    }
}

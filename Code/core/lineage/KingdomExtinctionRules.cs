namespace AncientWarfare3.core.lineage
{
    public static class KingdomExtinctionRules
    {
        public static bool ShouldDisbandSurvivors(
            bool isCivilization,
            bool cityIndexStable,
            bool hasCities)
        {
            return isCivilization && cityIndexStable && !hasCities;
        }
    }
}

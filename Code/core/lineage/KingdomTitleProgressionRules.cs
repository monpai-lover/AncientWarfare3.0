namespace AncientWarfare3.core.lineage
{
    public static class KingdomTitleProgressionRules
    {
        public const int MinimumBaseCityCapacity = 2;

        public static bool MeetsTerritoryRequirement(int currentTitle,
            int cityCount, int zoneCount)
        {
            return currentTitle switch
            {
                0 => cityCount >= 2 || zoneCount > 300,
                1 => cityCount >= 4 || zoneCount > 800,
                2 => cityCount >= 6 || zoneCount > 1300,
                3 => cityCount >= 10 || zoneCount > 2000,
                _ => false
            };
        }
    }
}

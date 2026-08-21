namespace AncientWarfare3.core.court
{
    internal enum CourtStatisticsScope
    {
        City,
        Region,
        National
    }

    internal static class CourtStatisticsRules
    {
        internal static CourtStatisticsScope ResolveScope(
            bool pIsCentralCourt, bool pIsRegionSeat, bool pHasRegion)
        {
            if (pIsCentralCourt) return CourtStatisticsScope.National;
            if (pIsRegionSeat && pHasRegion) return CourtStatisticsScope.Region;
            return CourtStatisticsScope.City;
        }
    }
}

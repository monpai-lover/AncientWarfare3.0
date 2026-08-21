namespace AncientWarfare3.core.court
{
    internal static class DeJureRegionRetirementRules
    {
        internal static bool CanRetire(bool liveCity, bool activeRegion,
            bool memberCity, bool banditStronghold)
        {
            return liveCity && activeRegion && memberCity &&
                   !banditStronghold;
        }

        internal static bool RequiresCapitalReplacement(bool isCapital,
            bool regionWillBeRetired)
        {
            return isCapital && regionWillBeRetired;
        }

        internal static bool ShouldRepairEmptyRegion(bool activeRegion,
            bool hasLiveMember)
        {
            return activeRegion && !hasLiveMember;
        }
    }
}

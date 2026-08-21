namespace AncientWarfare3.core.court
{
    internal enum DeJureRegionRemovalAction
    {
        None = 0,
        UnassignCity = 1,
        RetireRegion = 2
    }

    internal static class DeJureRegionRetirementRules
    {
        internal static bool CanRetire(bool liveCity, bool activeRegion,
            bool memberCity, bool banditStronghold)
        {
            return liveCity && activeRegion && memberCity &&
                   !banditStronghold;
        }

        internal static DeJureRegionRemovalAction ResolveRemovalAction(
            bool liveCity, bool activeRegion, bool memberCity,
            bool isRegionCapital, bool banditStronghold)
        {
            if (!CanRetire(liveCity, activeRegion, memberCity,
                    banditStronghold))
                return DeJureRegionRemovalAction.None;
            return isRegionCapital
                ? DeJureRegionRemovalAction.RetireRegion
                : DeJureRegionRemovalAction.UnassignCity;
        }

        internal static bool ShouldAutoCreateCapitalSeat(
            bool hasCurrentRegion, bool explicitlyRemoved)
        {
            return !hasCurrentRegion && !explicitlyRemoved;
        }

        internal static bool ShouldRepairEmptyRegion(bool activeRegion,
            bool hasLiveMember)
        {
            return activeRegion && !hasLiveMember;
        }
    }
}

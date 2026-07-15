namespace AncientWarfare3.core.schools
{
    public static class SchoolAffiliationTransferRules
    {
        public static bool AllowsExtinctionRelease(
            bool sourceIsLiveCivilization,
            bool cityIndexStable,
            bool sourceHasCities,
            bool targetMatchesActorWildKingdom)
        {
            return sourceIsLiveCivilization && cityIndexStable && !sourceHasCities &&
                   targetMatchesActorWildKingdom;
        }
    }
}

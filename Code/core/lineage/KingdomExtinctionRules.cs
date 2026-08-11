namespace AncientWarfare3.core.lineage
{
    public static class KingdomExtinctionRules
    {
        public static bool ShouldDeferRemovalVerification(
            bool cityIndexStable)
        {
            return !cityIndexStable;
        }

        public static bool ShouldDeferRemovalVerification(
            bool cityIndexStable, bool actorKingdomIndexStable)
        {
            return !cityIndexStable;
        }

        public static bool ShouldDisbandSurvivors(
            bool isCivilization,
            bool cityIndexStable,
            bool hasCities)
        {
            return isCivilization && cityIndexStable && !hasCities;
        }

        public static bool ShouldForceImmediateRemoval(
            bool isCivilization, bool cityIndexStable, int liveCityCount)
        {
            return isCivilization && cityIndexStable && liveCityCount <= 0;
        }

        public static bool ShouldDemobilizeFallenRealmWarrior(
            bool recordedForFallenRealm, bool stillInFallenRealm,
            bool currentRealmIsCivilized)
        {
            return recordedForFallenRealm &&
                   (stillInFallenRealm || !currentRealmIsCivilized);
        }

        public static bool ShouldTreatAsHavingCities(
            bool cityIndexStable, int liveCityCount)
        {
            return liveCityCount > 0;
        }

        public static bool ShouldQueueVerification(
            bool isCivilization, bool cityIndexStable, int liveCityCount)
        {
            return isCivilization && !cityIndexStable && liveCityCount <= 0;
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    internal static class ActorBirthAffiliationRules
    {
        internal static bool ShouldRepairCityKingdom(
            bool pHasCity,
            bool pCityKingdomValid,
            bool pActorKingdomValid,
            bool pActorKingdomMatchesCity,
            bool pActorCityMatchesTarget = true)
        {
            return pHasCity && pCityKingdomValid &&
                   (!pActorCityMatchesTarget || !pActorKingdomValid ||
                    !pActorKingdomMatchesCity);
        }

        internal static bool ShouldQueueRetry(
            bool pHasCity,
            bool pCityKingdomValid,
            bool pActorKingdomMatchesCity,
            bool pActorCityMatchesTarget = true)
        {
            return pHasCity && pCityKingdomValid &&
                   (!pActorCityMatchesTarget || !pActorKingdomMatchesCity);
        }
    }
}

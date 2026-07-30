namespace AncientWarfare3.core.court
{
    public enum CityGovernorProjectionDecision
    {
        Ignore,
        ApplyNow,
        Defer
    }

    public static class CityGovernorProjectionTimingRules
    {
        public static CityGovernorProjectionDecision Decide(
            bool pNewAssignment, bool pActorValid, bool pCityValid,
            bool pIsCurrentLeader, bool pActorKingdomValid,
            bool pCityKingdomValid, bool pSameKingdom,
            bool pRoyalAsylum)
        {
            if (!pNewAssignment || !pActorValid || !pCityValid ||
                !pIsCurrentLeader || !pCityKingdomValid || pRoyalAsylum)
                return CityGovernorProjectionDecision.Ignore;

            return pActorKingdomValid && pSameKingdom
                ? CityGovernorProjectionDecision.ApplyNow
                : CityGovernorProjectionDecision.Defer;
        }

        public static bool ShouldRetry(int pAttempt)
        {
            return pAttempt < 3;
        }

        public static string CoalescingKey(long pActorId, long pCityId)
        {
            return "city_governor_projection:" + pActorId + ":" + pCityId;
        }
    }
}

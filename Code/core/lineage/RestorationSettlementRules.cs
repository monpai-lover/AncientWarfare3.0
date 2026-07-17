namespace AncientWarfare3.core.lineage
{
    public static class RestorationSettlementRules
    {
        public static bool ShouldMoveClaimantToTargetCityBeforeKingdomCreation(bool pClaimantInTargetCity)
        {
            return !pClaimantInTargetCity;
        }
    }

    public static class RoyalRestorationClaimRules
    {
        public static bool IsEligibleClaimant(bool pHasActor, bool pIsRekt, bool pIsMale, bool pIsMad)
        {
            if (!pHasActor) return false;
            if (pIsRekt) return false;
            if (!pIsMale) return false;
            return !pIsMad;
        }

        public static bool ShouldUseHostedClaim(bool pHasClaim, bool pHasEligibleClaimant, bool pHasTargetCity)
        {
            return pHasClaim && pHasEligibleClaimant && pHasTargetCity;
        }

        public static bool CanLeadRestoration(bool pClaimantEligible,
            bool pAlreadyReigning)
        {
            return pClaimantEligible && !pAlreadyReigning;
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    public static class RestorationSettlementRules
    {
        public static bool ShouldMoveClaimantToTargetCityBeforeKingdomCreation(bool pClaimantInTargetCity)
        {
            return !pClaimantInTargetCity;
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    public static class MandateConquestDirectTransferRules
    {
        public static bool ShouldDirectTransfer(bool pWarActive,
            bool pGoalMatchesCity, bool pCapturerOnAttackerSide,
            bool pCityStillOwnedByDefender)
        {
            return pWarActive && pGoalMatchesCity &&
                   pCapturerOnAttackerSide && pCityStillOwnedByDefender;
        }

        public static bool ShouldBypassOrdinarySettlement(bool pWarActive,
            bool pHasOpenGoal)
        {
            return pWarActive && pHasOpenGoal;
        }
    }
}

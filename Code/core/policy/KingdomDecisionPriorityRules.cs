namespace AncientWarfare3.core.policy
{
    public static class KingdomDecisionPriorityRules
    {
        public static int ScoreDecision(string pDecisionId, bool pCanRoyalExpansion,
            int pCityCount, bool pSlaveryEnabled, int pXiaizationScore, bool pMissingYearName)
        {
            switch (pDecisionId ?? "")
            {
                case "aw_decision_fabricate_core":
                    return 2000;
                case "aw_decision_claim_mandate":
                    return 1200;
                case "aw_decision_title_upgrade":
                    return 1000;
                case "aw_decision_royal_expansion":
                    return pCanRoyalExpansion ? 900 : 0;
                case "aw_decision_change_capital":
                    return pCityCount >= 2 ? 760 : 0;
                case "aw_decision_control_slaves":
                    return pSlaveryEnabled ? 680 : 420;
                case "aw_decision_year_name":
                    return pMissingYearName ? 620 : 220;
                case "aw_decision_appease_xia_cities":
                    return pXiaizationScore;
                default:
                    return 100;
            }
        }
    }
}

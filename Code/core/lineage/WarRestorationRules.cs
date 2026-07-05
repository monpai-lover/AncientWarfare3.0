namespace AncientWarfare3.core.lineage
{
    public static class WarRestorationRules
    {
        public static bool CanExposeRestorationAction(bool pHasHostedClaim,
            bool pBlockedByVassalRelation, bool pAlreadyAtWar, out string pReason)
        {
            if (!pHasHostedClaim)
            {
                pReason = "no_hosted_claim";
                return false;
            }

            if (pBlockedByVassalRelation)
            {
                pReason = "vassal_annex_by_decision";
                return false;
            }

            if (pAlreadyAtWar)
            {
                pReason = "already_at_war";
                return false;
            }

            pReason = "";
            return true;
        }
    }
}

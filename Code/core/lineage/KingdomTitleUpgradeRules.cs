namespace AncientWarfare3.core.lineage
{
    public static class KingdomTitleUpgradeRules
    {
        public static bool CanVassalUpgradeUnderSuzerain(int pSuzerainTitle, int pVassalCurrentTitle,
            out string pReason)
        {
            int nextTitle = pVassalCurrentTitle + 1;
            if (nextTitle >= pSuzerainTitle)
            {
                pReason = "must_remain_below_suzerain";
                return false;
            }

            pReason = "";
            return true;
        }
    }
}

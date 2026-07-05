namespace AncientWarfare3.core.lineage
{
    public static class VassalAnnexDecisionRules
    {
        public static bool CanStart(bool pSuzerainValid, bool pTargetDirectVassal,
            bool pSuzerainAtWar, bool pTargetAtWar, out string pReason)
        {
            if (!pSuzerainValid)
            {
                pReason = "invalid";
                return false;
            }

            if (!pTargetDirectVassal)
            {
                pReason = "not_direct_vassal";
                return false;
            }

            if (pSuzerainAtWar || pTargetAtWar)
            {
                pReason = "at_war";
                return false;
            }

            pReason = "";
            return true;
        }
    }
}

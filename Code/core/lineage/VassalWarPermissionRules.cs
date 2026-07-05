namespace AncientWarfare3.core.lineage
{
    public static class VassalWarPermissionRules
    {
        public static bool CanDeclareWar(bool pAttackerIsVassal, bool pDefenderIsSuzerain,
            bool pSameSuzerain, string pWarType, out string pReason)
        {
            if (!pAttackerIsVassal)
            {
                pReason = "";
                return true;
            }

            if (pDefenderIsSuzerain && pWarType == "independence_war")
            {
                pReason = "";
                return true;
            }

            if (pSameSuzerain)
            {
                pReason = "";
                return true;
            }

            pReason = "vassal_external_war_blocked";
            return false;
        }

        public static bool CanCreateAlliance(bool pActorIsVassal, out string pReason)
        {
            if (!pActorIsVassal)
            {
                pReason = "";
                return true;
            }

            pReason = "vassal_no_alliance";
            return false;
        }
    }
}

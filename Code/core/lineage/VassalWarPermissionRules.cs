namespace AncientWarfare3.core.lineage
{
    public static class VassalWarPermissionRules
    {
        public static bool CanDeclareWar(bool pAttackerIsVassal, bool pDefenderIsSuzerain,
            bool pSameRootSuzerain, bool pBlockInternalWar, string pWarType, out string pReason)
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

            if (pSameRootSuzerain && pBlockInternalWar)
            {
                pReason = "centralization_internal_war_blocked";
                return false;
            }

            if (pSameRootSuzerain)
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

        public static bool CanUseAlliancePlot(bool initiatorIsVassal, bool targetIsVassal)
        {
            return !initiatorIsVassal && !targetIsVassal;
        }
    }
}

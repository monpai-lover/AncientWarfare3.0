namespace AncientWarfare3.core.lineage
{
    public enum VassalAnnexProgressState
    {
        Advance,
        Pause,
        Complete,
        Cancel
    }

    public static class VassalAnnexDecisionRules
    {
        public static bool CanStart(bool pSuzerainValid, bool pTargetDirectVassal,
            bool pSuzerainAtWar, bool pTargetAtWar,
            bool pActiveSpyNetwork, out string pReason)
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

            if (!pActiveSpyNetwork)
            {
                pReason = "spy_network_required";
                return false;
            }

            pReason = "";
            return true;
        }

        public static bool CanComplete(bool pSuzerainValid,
            bool pTargetDirectVassal, out string pReason)
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

            pReason = "";
            return true;
        }

        public static VassalAnnexProgressState ResolveProgressState(
            bool pSuzerainValid, bool pTargetValid,
            bool pTargetDirectVassal, bool pIndependenceSuspended,
            float pProgress, float pCost)
        {
            if (!pSuzerainValid || !pTargetValid)
                return VassalAnnexProgressState.Cancel;
            if (pIndependenceSuspended)
                return VassalAnnexProgressState.Pause;
            if (!pTargetDirectVassal)
                return VassalAnnexProgressState.Cancel;
            if (pProgress + 0.001f >= pCost)
                return VassalAnnexProgressState.Complete;
            return VassalAnnexProgressState.Advance;
        }
    }
}

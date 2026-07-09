namespace AncientWarfare3.core.lineage
{
    public static class VassalWarSupportRules
    {
        public static bool ShouldPullIntoSuzerainWar(bool pSuzerainInWar,
            bool pVassalAlreadyHelping,
            bool pVassalAlreadyInWar,
            bool pVassalOpposesSuzerain)
        {
            if (!pSuzerainInWar) return false;
            if (pVassalAlreadyHelping) return false;
            if (pVassalOpposesSuzerain) return false;
            if (pVassalAlreadyInWar) return false;
            return true;
        }
    }
}

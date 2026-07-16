namespace AncientWarfare3.core.lineage
{
    public enum VassalNameplateFlagAction
    {
        Hide,
        ShowCached,
        Reload
    }

    public static class VassalNameplateFlagStateRules
    {
        public static VassalNameplateFlagAction Resolve(bool pFullPlate,
            bool pKingdomValid, long pKingdomId, long pSuzerainId,
            bool pSuzerainValid, long pShownSuzerainId)
        {
            if (!pFullPlate || !pKingdomValid || !pSuzerainValid ||
                pSuzerainId < 0 || pSuzerainId == pKingdomId)
                return VassalNameplateFlagAction.Hide;

            return pSuzerainId == pShownSuzerainId
                ? VassalNameplateFlagAction.ShowCached
                : VassalNameplateFlagAction.Reload;
        }
    }
}

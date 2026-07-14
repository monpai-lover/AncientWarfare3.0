namespace AncientWarfare3.core.lineage
{
    public enum RoyalLineageSourceKind
    {
        Self,
        Father,
        CurrentRoyal,
        Sibling,
        Create
    }

    public static class RoyalLineageResolutionRules
    {
        public static RoyalLineageSourceKind Resolve(bool pSelfComplete,
            bool pFatherComplete, bool pCurrentRoyalComplete,
            bool pCurrentRoyalRelated, bool pSiblingComplete)
        {
            if (pSelfComplete) return RoyalLineageSourceKind.Self;
            if (pFatherComplete) return RoyalLineageSourceKind.Father;
            if (pCurrentRoyalComplete && pCurrentRoyalRelated)
                return RoyalLineageSourceKind.CurrentRoyal;
            if (pSiblingComplete) return RoyalLineageSourceKind.Sibling;
            return RoyalLineageSourceKind.Create;
        }

        public static bool SharesKnownFather(long pFirstFatherId, long pSecondFatherId)
        {
            return pFirstFatherId >= 0 && pFirstFatherId == pSecondFatherId;
        }
    }
}

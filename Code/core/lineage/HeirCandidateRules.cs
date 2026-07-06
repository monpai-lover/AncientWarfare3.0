namespace AncientWarfare3.core.lineage
{
    public static class HeirCandidateRules
    {
        public static bool IsFallbackEligibleCore(bool isSuitable, bool sameKingdom, bool hasLineage, bool hasShi)
        {
            return isSuitable && sameKingdom && hasLineage && hasShi;
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    public static class SpecialArmyLookupCacheRules
    {
        public static string BuildKey(long pKingdomId, string pRole, long pCityId)
        {
            long scopedCityId = AWArmyRoleRules.MaxArmiesPerKingdom(pRole) == 1 ? -1L : pCityId;
            return pKingdomId.ToString() + "|" + (pRole ?? "") + "|" + scopedCityId.ToString();
        }

        public static bool ShouldUseCachedArmy(
            long pCachedArmyId,
            bool pCachedArmyAlive,
            bool pRoleMatches,
            bool pKingdomMatches,
            bool pAnchorMatches)
        {
            return pCachedArmyId >= 0 &&
                   pCachedArmyAlive &&
                   pRoleMatches &&
                   pKingdomMatches &&
                   pAnchorMatches;
        }
    }
}

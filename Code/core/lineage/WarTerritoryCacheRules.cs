namespace AncientWarfare3.core.lineage
{
    public static class WarTerritoryCacheRules
    {
        public static string BuildOwnedNonCoreKey(long pFocusKingdomId, long pCityId, long pOwnerKingdomId)
        {
            if (pFocusKingdomId < 0 || pCityId < 0 || pOwnerKingdomId < 0) return "";
            return pFocusKingdomId + ":" + pCityId + ":" + pOwnerKingdomId;
        }
    }
}

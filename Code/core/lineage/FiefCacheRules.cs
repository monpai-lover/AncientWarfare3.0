namespace AncientWarfare3.core.lineage
{
    public static class FiefCacheRules
    {
        public const long UnknownGeneralId = long.MinValue;
        public const long NoActiveFiefGeneralId = -1L;

        public static bool IsUnknown(long pCachedGeneralId)
        {
            return pCachedGeneralId == UnknownGeneralId;
        }

        public static bool HasActiveFief(long pCachedGeneralId)
        {
            return pCachedGeneralId >= 0;
        }
    }
}

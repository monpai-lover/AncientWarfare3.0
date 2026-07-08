namespace AncientWarfare3.core.lineage
{
    public static class MetaColorCacheRules
    {
        public static bool ShouldRefreshAfterGeneratedColor(bool pHasMetaObject, int pColorId, int pColorCount)
        {
            return pHasMetaObject && pColorId >= 0 && pColorCount > 0 && pColorId < pColorCount;
        }
    }
}

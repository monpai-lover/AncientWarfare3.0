namespace AncientWarfare3.core.lineage
{
    public static class XiaOldHeadRefreshRules
    {
        public static bool ShouldRefresh(bool wasOldHead, bool shouldUseOldHead)
        {
            return wasOldHead != shouldUseOldHead;
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    public static class ClanBannerFrameRules
    {
        public static bool ShouldCacheDefaultFrame(bool pHasCurrentFrame, bool pCurrentIsXiaFrame, bool pDefaultKnown)
        {
            return pHasCurrentFrame && !pCurrentIsXiaFrame && !pDefaultKnown;
        }

        public static bool ShouldApplyXiaFrame(bool pIsXiaClan, bool pHasXiaFrame)
        {
            return pIsXiaClan && pHasXiaFrame;
        }

        public static bool ShouldRestoreDefaultFrame(bool pIsXiaClan, bool pDefaultKnown)
        {
            return !pIsXiaClan && pDefaultKnown;
        }
    }
}

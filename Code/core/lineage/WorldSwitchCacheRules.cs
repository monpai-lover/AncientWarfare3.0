namespace AncientWarfare3.core.lineage
{
    public static class WorldSwitchCacheRules
    {
        public static bool ShouldClearContextBoundWindow(long pContextId)
        {
            return pContextId >= 0;
        }

        public static bool ShouldRefreshContextFreeWindow(bool pIsCurrentWindow)
        {
            return pIsCurrentWindow;
        }
    }
}

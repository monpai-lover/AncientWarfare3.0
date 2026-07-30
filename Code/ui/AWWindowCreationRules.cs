namespace AncientWarfare3.ui
{
    public static class AWWindowCreationRules
    {
        public static bool ShouldRestoreCurrent(bool isAwWindowId,
            bool hadCurrentWindow, bool currentRegistryCleared,
            bool previousWindowStillActive)
        {
            return isAwWindowId && hadCurrentWindow &&
                   currentRegistryCleared && previousWindowStillActive;
        }
    }
}

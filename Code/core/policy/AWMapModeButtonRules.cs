namespace AncientWarfare3.core.policy
{
    public static class AWMapModeButtonRules
    {
        public static bool ShouldSuppressNmlAutoToggle(bool mapModeSwitch, bool hasCustomToggleAction)
        {
            return mapModeSwitch && hasCustomToggleAction;
        }
    }
}

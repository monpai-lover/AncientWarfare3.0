namespace AncientWarfare3.core.policy
{
    internal static class SaveWorldSafetyRules
    {
        public static bool CanEnterSave(bool worldPresent,
            bool itemsPresent)
        {
            return worldPresent && itemsPresent;
        }
    }
}

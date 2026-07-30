namespace AncientWarfare3.core.lineage
{
    public static class ArmyCreationSafetyRules
    {
        public static bool ShouldSkipSave(bool hasData, bool alive)
        {
            return !hasData || !alive;
        }

        public static bool ShouldCleanupFailedCreation(bool objectAllocated,
            bool initializationSucceeded)
        {
            return objectAllocated && !initializationSucceeded;
        }
    }
}

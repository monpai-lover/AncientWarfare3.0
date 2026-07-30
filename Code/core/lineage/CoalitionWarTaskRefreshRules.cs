namespace AncientWarfare3.core.lineage
{
    public static class CoalitionWarTaskRefreshRules
    {
        public const int MaximumParticipantsPerWorkItem = 16;
        public const int MaximumCitiesPerWorkItem = 32;
        public const int MaximumTargetInvalidationsPerWorkItem = 8;

        public static bool ShouldPublish(bool attackersComplete,
            bool defendersComplete)
        {
            return attackersComplete && defendersComplete;
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    internal static class NameIntegrationMaterializationService
    {
        internal const int DefaultBudget = 8;

        internal static void Request(Kingdom pKingdom)
        {
            // Living/authored names are intentionally preserved. Xia naming
            // applies at birth and actor initialization only.
        }

        internal static void Reset()
        {
            // Kept as a compatibility hook for old save/runtime callers.
        }

        internal static void ProcessAuthorityCycle()
        {
            // No whole-population migration stage remains.
        }
        internal static void ProcessAuthorityCycle(int pBudget)
        {
            // Compatibility overload for older callers; intentionally empty.
        }
    }
}

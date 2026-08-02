using System;

namespace AncientWarfare3.core.policy
{
    internal static class ActorDiagnosticSamplingRules
    {
        public const int MaximumSamplesPerFrame = 64;

        public static bool ShouldCollectDetail(bool diagnosticsEnabled,
            bool benchmarkEnabled, int sampleOrdinal, int perFrameBudget)
        {
            if (!diagnosticsEnabled && !benchmarkEnabled) return false;
            int budget = Math.Max(0, Math.Min(MaximumSamplesPerFrame,
                perFrameBudget));
            return sampleOrdinal >= 0 && sampleOrdinal < budget;
        }
    }
}

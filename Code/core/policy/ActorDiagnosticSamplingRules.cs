using System;

namespace AncientWarfare3.core.policy
{
    public static class ActorDiagnosticSamplingRules
    {
        public const int MaximumDetailSamplesPerFrame = 64;

        public static int ClampBudget(int pBudget)
        {
            return Math.Max(0, Math.Min(MaximumDetailSamplesPerFrame,
                pBudget));
        }

        public static bool ShouldCollect(bool diagnosticsEnabled,
            bool benchmarkEnabled, int used, int budget)
        {
            return (diagnosticsEnabled || benchmarkEnabled) && used >= 0 &&
                   used < ClampBudget(budget);
        }
    }
}

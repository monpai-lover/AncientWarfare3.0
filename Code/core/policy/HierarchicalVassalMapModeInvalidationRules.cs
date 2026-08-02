using System;

namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalMapModeInvalidationRules
    {
        public const double FallbackIntervalSeconds = 2d;
        public const int MaximumFallbackItemsPerFrame = 16;

        public static bool IsFallbackDue(double elapsedSeconds,
            double fallbackIntervalSeconds)
        {
            if (double.IsNaN(elapsedSeconds) ||
                double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0d)
                return false;
            double interval = double.IsNaN(fallbackIntervalSeconds) ||
                               double.IsInfinity(fallbackIntervalSeconds)
                ? FallbackIntervalSeconds
                : Math.Max(0d, fallbackIntervalSeconds);
            return elapsedSeconds >= interval;
        }

        public static int ClampFallbackBudget(int pRequested)
        {
            return Math.Max(0, Math.Min(MaximumFallbackItemsPerFrame,
                pRequested));
        }
    }
}

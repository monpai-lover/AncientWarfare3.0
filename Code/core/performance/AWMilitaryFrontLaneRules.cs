using System;

namespace AncientWarfare3.core.performance
{
    public readonly struct AWMilitaryFrontLaneCursor
    {
        public AWMilitaryFrontLaneCursor(int pIndex, int pRemainingInSweep)
        {
            Index = Math.Max(0, pIndex);
            RemainingInSweep = Math.Max(0, pRemainingInSweep);
        }

        public int Index { get; }
        public int RemainingInSweep { get; }
    }

    public static class AWMilitaryFrontLaneRules
    {
        public const double FixedStepSeconds = 0.02d;
        public const double MaximumDebtSeconds = 0.4d;

        public static double AddDebt(double pCurrentDebt,
            double pRealElapsedSeconds, double pRequestedSpeed,
            bool hasWork = true)
        {
            if (!hasWork) return 0d;
            double admitted = Math.Max(0d, pRealElapsedSeconds) *
                              Math.Max(0d, pRequestedSpeed);
            return Math.Min(MaximumDebtSeconds,
                Math.Max(0d, pCurrentDebt) + admitted);
        }

        public static bool HasStepDue(double pDebtSeconds)
        {
            return pDebtSeconds + 0.0000001d >= FixedStepSeconds;
        }

        public static double ConsumeCompletedSweep(double pDebtSeconds)
        {
            return Math.Max(0d, pDebtSeconds - FixedStepSeconds);
        }

        public static int NormalizeCursor(int pCursor, int pCount)
        {
            if (pCount <= 0) return 0;
            int normalized = pCursor % pCount;
            return normalized < 0 ? normalized + pCount : normalized;
        }

        public static int ResolveMaximumActors(int remainingActors,
            int configuredMaximum)
        {
            return Math.Min(Math.Max(0, remainingActors),
                Math.Max(0, configuredMaximum));
        }

        public static AWMilitaryFrontLaneCursor AdvanceCursor(
            AWMilitaryFrontLaneCursor pCursor, int pProcessedActors)
        {
            int processed = Math.Min(Math.Max(0, pProcessedActors),
                pCursor.RemainingInSweep);
            int remaining = pCursor.RemainingInSweep - processed;
            int index = remaining == 0
                ? 0
                : pCursor.Index + processed;
            return new AWMilitaryFrontLaneCursor(index, remaining);
        }
    }
}

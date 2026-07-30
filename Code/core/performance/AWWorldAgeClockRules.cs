using System;

namespace AncientWarfare3.core.performance
{
    public enum AWWorldAgeDurationUnit
    {
        GameDays,
        GameMonths,
        GameYears,
        RealSeconds,
        RealMinutes,
        RealHours
    }

    public readonly struct AWWorldAgeDuration
    {
        public AWWorldAgeDuration(AWWorldAgeDurationUnit pUnit,
            double pValue)
        {
            Unit = pUnit;
            Value = pValue;
        }

        public AWWorldAgeDurationUnit Unit { get; }
        public double Value { get; }
    }

    public static class AWWorldAgeClockRules
    {
        public const float SampleWindowSeconds = 0.75f;
        public const float SampleSmoothing = 0.35f;
        public const double WorldSecondsPerMonth = 5d;
        public const double WorldSecondsPerYear = 60d;
        public const double GameDaysPerWorldSecond = 6d;

        public static float RequestedSpeed(float pMultiplier, int pTicks)
        {
            return Math.Max(0f, pMultiplier) * Math.Max(1, pTicks);
        }

        public static bool HasCompleteSampleWindow(float pRealSeconds)
        {
            return pRealSeconds >= SampleWindowSeconds;
        }

        public static float SmoothActualSpeed(float pPreviousSpeed,
            float pSampledSpeed, bool pHasPreviousSample)
        {
            if (!pHasPreviousSample) return pSampledSpeed;
            return pPreviousSpeed +
                   (pSampledSpeed - pPreviousSpeed) * SampleSmoothing;
        }

        public static AWWorldAgeDuration GameDurationForOneRealSecond(
            double pActualSpeed)
        {
            double worldSeconds = Math.Max(0d, pActualSpeed);
            double days = worldSeconds * GameDaysPerWorldSecond;
            if (days >= 360d)
                return new AWWorldAgeDuration(
                    AWWorldAgeDurationUnit.GameYears,
                    worldSeconds / WorldSecondsPerYear);
            if (days >= 30d)
                return new AWWorldAgeDuration(
                    AWWorldAgeDurationUnit.GameMonths,
                    worldSeconds / WorldSecondsPerMonth);
            return new AWWorldAgeDuration(AWWorldAgeDurationUnit.GameDays,
                days);
        }

        public static AWWorldAgeDuration RealDurationForOneGameYear(
            double pActualSpeed)
        {
            double seconds = pActualSpeed > 0d
                ? WorldSecondsPerYear / pActualSpeed
                : double.PositiveInfinity;
            if (seconds >= 3600d)
                return new AWWorldAgeDuration(
                    AWWorldAgeDurationUnit.RealHours, seconds / 3600d);
            if (seconds >= 60d)
                return new AWWorldAgeDuration(
                    AWWorldAgeDurationUnit.RealMinutes, seconds / 60d);
            return new AWWorldAgeDuration(
                AWWorldAgeDurationUnit.RealSeconds, seconds);
        }

        public static bool ShouldResetSample(bool pSameMapStats,
            int pPreviousSeedId, int pCurrentSeedId,
            double pPreviousWorldTime, double pCurrentWorldTime,
            float pPreviousRequestedSpeed, float pCurrentRequestedSpeed,
            bool pPreviousPaused, bool pCurrentPaused)
        {
            return !pSameMapStats ||
                   pPreviousSeedId != pCurrentSeedId ||
                   pCurrentWorldTime < pPreviousWorldTime ||
                   Math.Abs(pCurrentRequestedSpeed -
                            pPreviousRequestedSpeed) > 0.001f ||
                   pCurrentPaused != pPreviousPaused;
        }
    }
}

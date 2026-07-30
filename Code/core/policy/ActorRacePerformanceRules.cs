using System;

namespace AncientWarfare3.core.policy
{
    public enum ActorRacePerformanceBucket
    {
        Other = 0,
        Xia = 1
    }

    public enum ActorRacePerformanceMetric
    {
        ActorAi = 0,
        PathSubmit = 1,
        PathSmooth = 2,
        PathStep = 3,
        UpdateAge = 4,
        MainSprite = 5
    }

    public static class ActorRacePerformanceRules
    {
        public const int BucketCount = 2;
        public const int MetricCount = 6;

        public static ActorRacePerformanceBucket Classify(string pAssetId)
        {
            return string.Equals(pAssetId, "Xia", StringComparison.Ordinal)
                ? ActorRacePerformanceBucket.Xia
                : ActorRacePerformanceBucket.Other;
        }

        public static int Index(ActorRacePerformanceBucket pBucket,
            ActorRacePerformanceMetric pMetric)
        {
            return (int)pBucket * MetricCount + (int)pMetric;
        }

        public static double MicrosecondsPerCall(long elapsedTicks,
            long timestampFrequency, int calls)
        {
            if (elapsedTicks <= 0L || timestampFrequency <= 0L || calls <= 0)
                return 0d;
            return elapsedTicks * 1000000d / timestampFrequency / calls;
        }
    }
}

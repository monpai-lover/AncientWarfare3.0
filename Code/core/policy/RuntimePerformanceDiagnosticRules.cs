namespace AncientWarfare3.core.policy
{
    public static class RuntimePerformanceDiagnosticRules
    {
        public const string AutoLoadSlotEnvironmentVariable =
            "AW3_BENCHMARK_LOAD_SLOT";
        public const string AutoLoadPathEnvironmentVariable =
            "AW3_BENCHMARK_LOAD_PATH";
        public const string FamilyTreeActorEnvironmentVariable =
            "AW3_BENCHMARK_FAMILY_TREE_ACTOR";
        public const int SampleIntervalFrames = 120;

        public static bool TryResolveBenchmarkAutoLoadSlot(string value,
            out int slot)
        {
            slot = -1;
            if (!int.TryParse(value, out int parsed) || parsed < 1 || parsed > 50)
                return false;
            slot = parsed;
            return true;
        }

        public static bool TryResolveBenchmarkAutoLoadPath(string value,
            out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(value) ||
                !System.IO.Path.IsPathRooted(value))
                return false;
            try
            {
                string fullPath = System.IO.Path.GetFullPath(value);
                string root = System.IO.Path.GetPathRoot(fullPath) ??
                              string.Empty;
                while (fullPath.Length > root.Length &&
                       IsDirectorySeparator(fullPath[fullPath.Length - 1]))
                    fullPath = fullPath.Substring(0, fullPath.Length - 1);
                path = fullPath;
                return true;
            }
            catch (System.Exception error) when (
                error is System.ArgumentException ||
                error is System.NotSupportedException ||
                error is System.IO.PathTooLongException)
            {
                return false;
            }
        }

        public static bool TryResolveBenchmarkFamilyTreeActor(string value,
            out long actorId)
        {
            actorId = -1L;
            if (!long.TryParse(value, out long parsed) || parsed <= 0L)
                return false;
            actorId = parsed;
            return true;
        }

        private static bool IsDirectorySeparator(char value)
        {
            return value == System.IO.Path.DirectorySeparatorChar ||
                   value == System.IO.Path.AltDirectorySeparatorChar;
        }

        public static bool ResolveBenchmarkForceGenerate(bool requested,
            bool hasBenchmarkSlot)
        {
            return hasBenchmarkSlot ? false : requested;
        }

        public static bool ResolveStockStartupSave(bool requested,
            bool hasBenchmarkTarget)
        {
            return hasBenchmarkTarget ? false : requested;
        }

        public static bool ShouldDispatchBenchmarkAutoLoad(bool configured,
            bool dispatched)
        {
            return configured && !dispatched;
        }

        public static bool HasBenchmarkAutoLoadTimedOut(bool pending,
            long utcNowTicks, long deadlineUtcTicks)
        {
            return pending && deadlineUtcTicks > 0L &&
                   utcNowTicks >= deadlineUtcTicks;
        }

        public static bool ShouldSample(bool enabled, long frame)
        {
            return enabled && frame > 0L &&
                   frame % SampleIntervalFrames == 0L;
        }

        public static bool ShouldEnableDetailedSampling(bool diagnosticsEnabled,
            bool benchmarkEnabled)
        {
            return diagnosticsEnabled || benchmarkEnabled;
        }

        public static bool ShouldEmitTextLog(bool diagnosticsEnabled,
            bool benchmarkEnabled)
        {
            return diagnosticsEnabled || benchmarkEnabled;
        }

        public static bool ShouldStartIntervalBaseline(bool intervalEnabled,
            bool diagnosticsEnabled)
        {
            return diagnosticsEnabled && !intervalEnabled;
        }

        public static long IntervalFrameCount(long intervalStartFrame,
            long currentFrame)
        {
            long start = System.Math.Max(0L, intervalStartFrame);
            return currentFrame <= start ? 0L : currentFrame - start;
        }

        public static double AverageFramesPerSecond(long intervalFrames,
            long intervalTicks, long timestampFrequency)
        {
            if (intervalFrames <= 0L || intervalTicks <= 0L ||
                timestampFrequency <= 0L) return 0d;
            return intervalFrames * (double)timestampFrequency /
                   intervalTicks;
        }

        public static long ExclusiveTicks(long totalTicks, long nestedTicks)
        {
            return System.Math.Max(0L, totalTicks - nestedTicks);
        }

        public static long UnaccountedTicks(long wallTicks, long knownTicks)
        {
            return System.Math.Max(0L, wallTicks - knownTicks);
        }

        public static bool ShouldReplaceSlowest(long currentTicks,
            long candidateTicks)
        {
            return candidateTicks > currentTicks;
        }
    }
}

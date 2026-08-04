using System;
using System.Globalization;

namespace AncientWarfare3.core.lineage
{
    public static class ActorDeathArchiveRules
    {
        public const int MaximumRetryDelayFrames = 256;

        public static string Key(long pWorldGeneration, long pActorId,
            long pDeathRevision, string pStage)
        {
            return "death:" + pWorldGeneration.ToString(
                       CultureInfo.InvariantCulture) + ":" +
                   pActorId.ToString(CultureInfo.InvariantCulture) + ":" +
                   pDeathRevision.ToString(CultureInfo.InvariantCulture) +
                   ":" + (string.IsNullOrWhiteSpace(pStage)
                       ? "unknown"
                       : pStage);
        }

        public static bool AcceptCompletion(long resultWorldGeneration,
            long currentWorldGeneration, long resultDeathRevision,
            long currentDeathRevision)
        {
            return resultWorldGeneration == currentWorldGeneration &&
                   resultDeathRevision == currentDeathRevision;
        }

        public static int RetryDelayFrames(int pAttempt)
        {
            int exponent = Math.Max(0, Math.Min(8, pAttempt - 1));
            return Math.Min(MaximumRetryDelayFrames, 1 << exponent);
        }

        public static bool ShouldUseSynchronousFallback(bool writerReady,
            bool queueAccepted)
        {
            return !writerReady || !queueAccepted;
        }

        public static bool ReadyForSave(int captured, int running,
            int retries, int completions)
        {
            return captured == 0 && running == 0 && retries == 0 &&
                   completions == 0;
        }

        public static int ResolveAuthorityItemLimit(int pPendingCount)
        {
            if (pPendingCount > 512) return 256;
            if (pPendingCount > 128) return 128;
            return 64;
        }

        public static double ResolveAuthorityMilliseconds(int pPendingCount)
        {
            if (pPendingCount > 512) return 4.0;
            if (pPendingCount > 128) return 2.0;
            return 1.0;
        }

        public static int ResolveSaveTimeoutSeconds(int baseSeconds,
            int pendingCount)
        {
            int baseline = Math.Max(1, baseSeconds);
            if (pendingCount <= 0) return baseline;
            int backlogAllowance = (pendingCount + 31) / 32;
            return Math.Max(baseline,
                Math.Min(30, baseline + backlogAllowance));
        }
    }
}

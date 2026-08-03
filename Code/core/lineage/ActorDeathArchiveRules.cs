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
    }
}

namespace AncientWarfare3.core.court
{
    public static class CityBureauRetryRules
    {
        public static bool ShouldRetry(bool pProcessCompleted,
            int pAttempt, int pMaximumAttempts)
        {
            return !pProcessCompleted && pAttempt + 1 < pMaximumAttempts;
        }

        public static bool ShouldSkipSameFrame(int pLastAttemptFrame,
            int pCurrentFrame)
        {
            return pCurrentFrame >= 0 && pLastAttemptFrame == pCurrentFrame;
        }
    }
}

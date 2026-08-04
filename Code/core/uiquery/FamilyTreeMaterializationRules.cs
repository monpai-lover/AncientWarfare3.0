namespace AncientWarfare3.core.uiquery
{
    public static class FamilyTreeMaterializationRules
    {
        public const int ReaderStartupFallbackFrames = 15;
        public const int RequestTimeoutFrames = 60;

        public static bool ShouldQueueAfterInactiveCompletion(
            bool snapshotAccepted, bool windowActive)
        {
            return snapshotAccepted && !windowActive;
        }

        public static bool AcceptCompletedSnapshot(
            bool sameGeneration, bool sameWorldGeneration, bool sameSpec,
            bool sameProjectionRevision)
        {
            return sameGeneration && sameWorldGeneration && sameSpec &&
                   sameProjectionRevision;
        }

        public static bool AcceptRetryWithoutContentRevision(
            bool sameGeneration, bool sameWorldGeneration, bool sameSpec)
        {
            return sameGeneration && sameWorldGeneration && sameSpec;
        }

        public static bool ShouldConsumeDetachedReadAttempt(
            bool asynchronousReadRequired, bool historicalReadReady)
        {
            return !asynchronousReadRequired || historicalReadReady;
        }

        public static bool ShouldUseSynchronousFallback(
            bool asynchronousReadRequired, bool historicalReadReady,
            int waitingFrames, int fallbackAfterFrames)
        {
            if (!asynchronousReadRequired) return true;
            if (historicalReadReady) return false;
            return waitingFrames >= System.Math.Max(1,
                fallbackAfterFrames);
        }

        public static bool ShouldRecoverTimedOutRequest(
            bool requestInFlight, int elapsedFrames, int timeoutFrames)
        {
            return requestInFlight && elapsedFrames >= System.Math.Max(1,
                timeoutFrames);
        }
    }
}

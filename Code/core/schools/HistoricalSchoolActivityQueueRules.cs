namespace AncientWarfare3.core.schools
{
    public static class HistoricalSchoolActivityQueueRules
    {
        public static bool CanEnqueue(int pCurrentCount, int pCapacity,
            bool pDuplicateOperation)
        {
            return !pDuplicateOperation && pCapacity > 0 &&
                   pCurrentCount >= 0 && pCurrentCount < pCapacity;
        }

        public static bool CanAdvance(int pTransitionsThisFrame,
            double pElapsedMilliseconds, double pBudgetMilliseconds)
        {
            return pTransitionsThisFrame == 0 && pBudgetMilliseconds > 0d &&
                   pElapsedMilliseconds >= 0d &&
                   pElapsedMilliseconds < pBudgetMilliseconds;
        }

        public static bool CanActivate(int pActiveCount, int pConcurrentLimit)
        {
            return pActiveCount >= 0 && pConcurrentLimit > 0 &&
                   pActiveCount < pConcurrentLimit;
        }

        public static string ActorYearKey(int pYear, long pActorId)
        {
            return pYear.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   pActorId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static bool ShouldCancelInterrupted(bool pReady, bool pExpectedTask,
            long pAgeFrames, long pGraceFrames)
        {
            return !pReady && !pExpectedTask && pGraceFrames >= 0 &&
                   pAgeFrames > pGraceFrames;
        }

        internal static bool ShouldFlushForSave(bool pReady)
        {
            return pReady;
        }

        internal static bool IsPersistenceResolved(
            HistoricalSchoolTeachingPersistenceOutcome pOutcome)
        {
            return pOutcome != HistoricalSchoolTeachingPersistenceOutcome.Unknown;
        }
    }
}

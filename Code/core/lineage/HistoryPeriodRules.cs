namespace AncientWarfare3.core.lineage
{
    public static class HistoryPeriodRules
    {
        private const double EPSILON = 0.001;

        public static double NormalizeEndTime(double pStartTime, double pCandidateEndTime)
        {
            if (pCandidateEndTime < 0) return -1.0;
            return pCandidateEndTime + EPSILON < pStartTime ? -1.0 : pCandidateEndTime;
        }

        public static bool ShouldKeepPeriod(double pStartTime, double pEndTime, int pEventCount)
        {
            if (pEventCount > 0) return true;
            if (pEndTime < 0) return true;
            return pEndTime + EPSILON >= pStartTime;
        }
    }
}

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

        public static double CloseEndBeforeNextStart(double pStartTime, double pCandidateEndTime,
            double pNextStartTime)
        {
            if (pNextStartTime + EPSILON < pStartTime)
                return NormalizeEndTime(pStartTime, pCandidateEndTime);
            double normalized = NormalizeEndTime(pStartTime, pCandidateEndTime);
            if (normalized < 0 || normalized > pNextStartTime)
                return pNextStartTime;
            return normalized;
        }

        public static bool ShouldKeepPeriod(double pStartTime, double pEndTime, int pEventCount)
        {
            if (pEventCount > 0) return true;
            if (pEndTime < 0) return true;
            return pEndTime + EPSILON >= pStartTime;
        }

        public static bool IsSameCityOwnerSnapshot(long pCurrentOwnerId, string pCurrentOwnerName,
            long pEventOwnerId, string pEventOwnerName)
        {
            if (pCurrentOwnerId >= 0 && pEventOwnerId >= 0)
                return pCurrentOwnerId == pEventOwnerId;

            string currentName = NormalizeOwnerName(pCurrentOwnerName);
            string eventName = NormalizeOwnerName(pEventOwnerName);
            return currentName.Length > 0 && currentName == eventName;
        }

        public static string BuildCityOwnerToken(long pOwnerId, string pOwnerName)
        {
            return pOwnerId >= 0 ? pOwnerId.ToString() : NormalizeOwnerName(pOwnerName);
        }

        private static string NormalizeOwnerName(string pName)
        {
            return string.IsNullOrWhiteSpace(pName) ? "" : pName.Trim();
        }
    }
}

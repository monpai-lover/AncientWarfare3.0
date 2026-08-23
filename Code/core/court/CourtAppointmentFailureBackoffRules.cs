namespace AncientWarfare3.core.court
{
    /// <summary>
    /// Keeps a malformed or temporarily unavailable office from causing a
    /// database appointment retry on every authority pass.
    /// </summary>
    public static class CourtAppointmentFailureBackoffRules
    {
        public const int RetryCooldownYears = 3;

        public static bool ShouldAttempt(int pLastFailureYear,
            int pCurrentYear)
        {
            if (pLastFailureYear < 0) return true;
            return pCurrentYear - pLastFailureYear >= RetryCooldownYears;
        }

        public static bool ShouldStopCurrentReconcile(bool pAttempted,
            bool pCommitted)
        {
            return pAttempted && !pCommitted;
        }
    }
}

namespace AncientWarfare3.core.court
{
    public static class WesternMayorTermRules
    {
        public const int TermYears = 10;

        public static int InitialCycleEndYear(int pCurrentYear)
        {
            return AddYears(pCurrentYear, TermYears);
        }

        public static int AppointmentTermEndYear(int currentYear,
            int sharedCycleEndYear)
        {
            return sharedCycleEndYear < 0
                ? InitialCycleEndYear(currentYear)
                : sharedCycleEndYear;
        }

        public static int AdvanceExpiredCycleEndYear(int currentYear,
            int sharedCycleEndYear)
        {
            if (sharedCycleEndYear < 0)
                return InitialCycleEndYear(currentYear);
            if (sharedCycleEndYear > currentYear)
                return sharedCycleEndYear;
            long elapsed = (long)currentYear - sharedCycleEndYear;
            long cycles = elapsed / TermYears + 1L;
            long result = sharedCycleEndYear + cycles * TermYears;
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        public static int RetryTermEndYear(int pCurrentYear)
        {
            return AddYears(pCurrentYear, 1);
        }

        public static int SharedCycleAfterFailedRotation(
            int pSharedCycleEndYear)
        {
            return pSharedCycleEndYear;
        }

        private static int AddYears(int pYear, int pYears)
        {
            long result = (long)pYear + pYears;
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }
    }
}

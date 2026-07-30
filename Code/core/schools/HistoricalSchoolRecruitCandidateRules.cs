using System;

namespace AncientWarfare3.core.schools
{
    public static class HistoricalSchoolRecruitCandidateRules
    {
        public const int MaxScanPerCityYear = 96;
        public const int MaxCachedPerCityYear = 48;
        private const int AnnualCursorStep = 97;

        public static int ScanStart(long pCityId, int pYear,
            int pResidentCount)
        {
            if (pResidentCount <= 0) return 0;
            unchecked
            {
                long citySeed = pCityId * 6364136223846793005L +
                                1442695040888963407L;
                long cursor = citySeed + (long)Math.Max(0, pYear) *
                              AnnualCursorStep;
                int result = (int)(cursor % pResidentCount);
                return result < 0 ? result + pResidentCount : result;
            }
        }

        public static int ScanCount(int pResidentCount)
        {
            return Math.Min(Math.Max(0, pResidentCount),
                MaxScanPerCityYear);
        }

        public static int ResidentIndex(int pStart, int pOffset,
            int pResidentCount)
        {
            if (pResidentCount <= 0) return 0;
            int start = pStart % pResidentCount;
            if (start < 0) start += pResidentCount;
            int offset = Math.Max(0, pOffset) % pResidentCount;
            return (start + offset) % pResidentCount;
        }
    }
}

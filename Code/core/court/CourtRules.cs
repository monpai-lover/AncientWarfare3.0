namespace AncientWarfare3.core.court
{
    public static class CourtRules
    {
        public const int CentralOfficeCount = 11;
        public const int DefaultRefreshIntervalYears = 5;
        public const int CandidateRefreshIntervalYears = 8;
        public const int StrongEventCooldownYears = 12;

        public static bool IsCourtUnlocked(bool hasPolicySystem, bool hasOfficialCourtTech)
        {
            return hasPolicySystem && hasOfficialCourtTech;
        }

        public static bool UsePrimitiveCourt(bool hasPolicySystem, bool hasOfficialCourtTech)
        {
            return hasPolicySystem && !hasOfficialCourtTech;
        }

        public static int CityOfficeSlots(int population, int zoneCount, bool isCapital)
        {
            int score = population + zoneCount * 4 + (isCapital ? 30 : 0);
            if (score >= 190) return 3;
            if (score >= 85) return 2;
            return 1;
        }

        public static bool ShouldRefreshCourt(int currentYear, int lastRefreshYear, int intervalYears)
        {
            int interval = intervalYears <= 0 ? DefaultRefreshIntervalYears : intervalYears;
            return lastRefreshYear < 0 || currentYear - lastRefreshYear >= interval;
        }

        public static bool ShouldRefreshCandidates(int currentYear, int lastRefreshYear)
        {
            return ShouldRefreshCourt(currentYear, lastRefreshYear, CandidateRefreshIntervalYears);
        }

        public static bool ShouldUseSingleYearRoster(int officeCount)
        {
            return officeCount > 1;
        }

        public static bool CanHoldOffice(bool alive, bool sameKingdom, bool slave, bool madness)
        {
            return alive && sameKingdom && !slave && !madness;
        }
    }
}

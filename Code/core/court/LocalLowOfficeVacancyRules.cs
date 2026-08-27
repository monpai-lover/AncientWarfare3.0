namespace AncientWarfare3.core.court
{
    public enum LocalLowOfficeCandidateTier
    {
        Qualified = 0,
        Clan = 1,
        Ordinary = 2
    }

    public static class LocalLowOfficeVacancyRules
    {
        public static bool IsLowestLocalGrade(int officeGrade)
        {
            return officeGrade == 30;
        }

        public static LocalLowOfficeCandidateTier CandidateTier(
            bool hasFormalQualification, bool hasClanOrShi)
        {
            if (hasFormalQualification)
                return LocalLowOfficeCandidateTier.Qualified;
            return hasClanOrShi
                ? LocalLowOfficeCandidateTier.Clan
                : LocalLowOfficeCandidateTier.Ordinary;
        }

        public static bool CanUseUnqualifiedFallback(bool isCityLayer,
            int officeGrade, bool vacancyPromotion)
        {
            return isCityLayer && officeGrade == 30 && vacancyPromotion;
        }

        public static bool CanUseCountyFallback(bool isCountyLayer,
            int officeGrade, bool vacancyPromotion)
        {
            return isCountyLayer && officeGrade == 30 && vacancyPromotion;
        }

        public static int ResolveEntryRank(int currentRank, int officeGrade)
        {
            if (officeGrade != 30) return currentRank;
            return currentRank <= OfficialCareerRankRules.Unranked
                ? OfficialCareerRankRules.RequiredRankForLocalOfficeGrade(
                    officeGrade)
                : currentRank;
        }
    }
}

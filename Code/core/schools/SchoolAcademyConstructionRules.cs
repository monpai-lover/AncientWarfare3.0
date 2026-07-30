namespace AncientWarfare3.core.schools
{
    public static class SchoolAcademyConstructionRules
    {
        public static bool ShouldStart(
            bool cityValid,
            bool assetAvailable,
            bool academyIdAlreadyPresent,
            bool academyTypeAlreadyPresent,
            bool placementAvailable)
        {
            return cityValid &&
                   assetAvailable &&
                   !academyIdAlreadyPresent &&
                   !academyTypeAlreadyPresent &&
                   placementAvailable;
        }

        public static bool ShouldRequestForLecture(
            bool cityValid,
            bool academyUsable,
            bool academyBuildingPresent)
        {
            return cityValid && !academyUsable && !academyBuildingPresent;
        }

        public static int ZoneStartIndex(int zoneCount, int attempt, int zonesPerAttempt)
        {
            if (zoneCount <= 0 || zonesPerAttempt <= 0) return 0;
            long safeAttempt = attempt < 0 ? 0L : attempt;
            return (int)(safeAttempt * zonesPerAttempt % zoneCount);
        }

        public static bool ShouldAttemptPlacement(int currentYear,
            int lastAttemptYear)
        {
            return currentYear >= 0 && currentYear != lastAttemptYear;
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    public static class WarReparationsRules
    {
        public static bool IsDue(bool active, int currentYear,
            int nextDueYear, int endYear)
        {
            return active && nextDueYear >= 0 &&
                   currentYear >= nextDueYear && currentYear <= endYear;
        }

        public static int NextDueYear(int paidYear, int endYear)
        {
            return paidYear >= endYear ? -1 : paidYear + 1;
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    public static class FamilyTreeChronologyRules
    {
        public static bool HasKnownBirthTime(double pBirthTime)
        {
            return !double.IsNaN(pBirthTime) &&
                   !double.IsInfinity(pBirthTime);
        }

        public static bool HasKnownDeathTime(double pDeathTime)
        {
            return HasKnownBirthTime(pDeathTime) && pDeathTime >= 0d;
        }
    }
}

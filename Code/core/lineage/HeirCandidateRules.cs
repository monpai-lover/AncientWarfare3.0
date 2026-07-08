namespace AncientWarfare3.core.lineage
{
    public static class HeirCandidateRules
    {
        public static bool IsBasicMaleSuccessionEligible(bool isAlive, bool sameAsCurrentKing,
            bool isMale, bool isCurrentKing, bool isAdult, bool hasMadness, bool isSlave)
        {
            return isAlive &&
                   !sameAsCurrentKing &&
                   isMale &&
                   !isCurrentKing &&
                   isAdult &&
                   !hasMadness &&
                   !isSlave;
        }

        public static bool IsUnderageDirectSonEligible(bool isDirectSon, bool isMale, bool isAlive,
            bool isCurrentKing, bool hasAdultDirectSon, bool hasMadness, bool isSlave)
        {
            return isDirectSon &&
                   isMale &&
                   isAlive &&
                   !isCurrentKing &&
                   !hasAdultDirectSon &&
                   !hasMadness &&
                   !isSlave;
        }

        public static bool IsFallbackEligibleCore(bool isSuitable, bool sameKingdom, bool hasLineage, bool hasShi)
        {
            return isSuitable && sameKingdom && hasLineage && hasShi;
        }
    }
}

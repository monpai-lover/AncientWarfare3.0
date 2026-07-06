namespace AncientWarfare3.core.lineage
{
    public static class RoyalSuccessionBirthRules
    {
        public const int KingCapWithDirectSon = 8;
        public const int KingCapWithoutDirectSon = 12;

        public static bool ShouldRefreshHeirForNewChild(bool childIsMale, bool fatherIsCurrentKing)
        {
            return childIsMale && fatherIsCurrentKing;
        }

        public static int KingChildCap(bool hasLivingDirectSon)
        {
            return hasLivingDirectSon ? KingCapWithDirectSon : KingCapWithoutDirectSon;
        }
    }
}

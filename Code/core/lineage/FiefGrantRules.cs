namespace AncientWarfare3.core.lineage
{
    public static class FiefGrantRules
    {
        public const int MinimumMeritForFief = 45;

        public static bool CanGrantToGeneral(bool isGeneral, int merit)
        {
            return isGeneral && merit >= MinimumMeritForFief;
        }
    }
}

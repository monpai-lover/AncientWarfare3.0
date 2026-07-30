namespace AncientWarfare3.core.lineage
{
    public static class FamilyTreeBiographyButtonRules
    {
        public const int Size = 14;

        public static (float X, float Y) AnchoredPosition => (-15f, -23f);

        public static bool ShouldShow(long pActorId)
        {
            return pActorId >= 0L;
        }
    }
}

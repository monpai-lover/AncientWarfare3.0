namespace AncientWarfare3.core.lineage
{
    public static class FormerRulerPosthumousRules
    {
        public static bool ShouldTryPosthumousOnDeath(bool isCurrentKing, bool hasUntitledClosedReign)
        {
            return !isCurrentKing && hasUntitledClosedReign;
        }
    }
}

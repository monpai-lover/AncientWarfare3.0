namespace AncientWarfare3.core.lineage
{
    public static class FormerRulerPosthumousRules
    {
        public static bool ShouldInspectDeathContext(bool hasFormerKingMarker,
            long formerKingdomId, long capturedRulerKingdomId)
        {
            return hasFormerKingMarker || formerKingdomId >= 0L ||
                   capturedRulerKingdomId >= 0L;
        }

        public static bool ShouldTryPosthumousOnDeath(bool isCurrentKing, bool hasUntitledClosedReign)
        {
            return !isCurrentKing && hasUntitledClosedReign;
        }

        public static bool ShouldTryPosthumousOnDeath(bool isCurrentKing, bool hasUntitledClosedReign,
            bool hasCapturedRulerSnapshot)
        {
            return !isCurrentKing && (hasUntitledClosedReign || hasCapturedRulerSnapshot);
        }
    }
}

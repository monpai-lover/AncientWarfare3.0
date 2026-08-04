namespace AncientWarfare3.core.lineage
{
    public static class CapturedMilitaryOwnershipRules
    {
        public static bool ShouldReleaseFormerArmy(bool captureSucceeded,
            bool hasFormerArmy, long formerKingdomId,
            long currentKingdomId)
        {
            return captureSucceeded && hasFormerArmy &&
                   formerKingdomId >= 0L && currentKingdomId >= 0L &&
                   formerKingdomId != currentKingdomId;
        }
    }
}

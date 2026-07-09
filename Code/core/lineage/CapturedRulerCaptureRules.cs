namespace AncientWarfare3.core.lineage
{
    public static class CapturedRulerCaptureRules
    {
        public static bool ShouldPreserveFormerKingContext(bool wasKingBeforeRelocation,
            long formerKingdomId, long captorKingdomId)
        {
            return wasKingBeforeRelocation &&
                   formerKingdomId >= 0 &&
                   captorKingdomId >= 0 &&
                   formerKingdomId != captorKingdomId;
        }

        public static bool ShouldReleaseAsNobleDependent(bool wasKingBeforeRelocation,
            bool wasLeaderBeforeRelocation)
        {
            return wasKingBeforeRelocation || wasLeaderBeforeRelocation;
        }
    }
}

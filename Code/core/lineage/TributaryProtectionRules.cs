namespace AncientWarfare3.core.lineage
{
    internal static class TributaryProtectionRules
    {
        internal static bool IsDirectActivePair(long leftKingdomId,
            long rightKingdomId, long relationVassalId,
            long relationSuzerainId, bool relationActive,
            double relationEndTime, int relationContractTier)
        {
            if (leftKingdomId < 0 || rightKingdomId < 0 ||
                leftKingdomId == rightKingdomId || !relationActive ||
                relationEndTime >= 0d ||
                !VassalContractTierRules.IsLooseTributary(
                    relationContractTier)) return false;
            return relationVassalId == leftKingdomId &&
                   relationSuzerainId == rightKingdomId ||
                   relationVassalId == rightKingdomId &&
                   relationSuzerainId == leftKingdomId;
        }
    }
}

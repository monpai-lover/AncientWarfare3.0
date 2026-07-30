namespace AncientWarfare3.core.lineage
{
    public static class MandateNameProjectionRules
    {
        public static bool ShouldRefresh(bool active, long trackedKingdomId,
            long renamedKingdomId, long activePeriodId, bool validName)
        {
            return active && trackedKingdomId >= 0 &&
                   trackedKingdomId == renamedKingdomId &&
                   activePeriodId >= 0 && validName;
        }
    }
}

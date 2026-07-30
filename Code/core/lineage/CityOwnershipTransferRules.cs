namespace AncientWarfare3.core.lineage
{
    public static class CityOwnershipTransferRules
    {
        public static bool ShouldDisbandLocalArmy(bool hasOldOwner,
            bool ownerChanged, bool captureAlreadyCleaned)
        {
            return hasOldOwner && ownerChanged && !captureAlreadyCleaned;
        }
    }
}

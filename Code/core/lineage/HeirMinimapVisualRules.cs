namespace AncientWarfare3.core.lineage
{
    public static class HeirMinimapVisualRules
    {
        public static long ResolveVisualKingdomId(long pLegalKingdomId, long pCurrentKingdomId)
        {
            return pCurrentKingdomId >= 0 ? pCurrentKingdomId : pLegalKingdomId;
        }

        public static bool ShouldDrawIcon(
            bool markersEnabled,
            bool isAlive,
            bool isInMagnet,
            bool hasCurrentTile,
            bool hasVisibleZone,
            bool isKing,
            bool isCityLeader)
        {
            return markersEnabled &&
                   isAlive &&
                   !isInMagnet &&
                   hasCurrentTile &&
                   hasVisibleZone &&
                   !isKing &&
                   !isCityLeader;
        }
    }
}

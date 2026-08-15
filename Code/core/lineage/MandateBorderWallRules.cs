namespace AncientWarfare3.core.lineage
{
    public static class MandateBorderWallRules
    {
        public const string PreferredWallTopTileId = "wall_order";

        public static bool IsExternalLandBorderNeighbor(
            bool pNeighborHasCity,
            bool pNeighborGround,
            bool pNeighborLiquid,
            bool pNeighborLava,
            bool pNeighborBlock,
            bool pNeighborNeutral,
            bool pSameMandateSystem)
        {
            if (!pNeighborHasCity || pNeighborNeutral || pSameMandateSystem) return false;
            return pNeighborGround && !pNeighborLiquid && !pNeighborLava && !pNeighborBlock;
        }
    }
}

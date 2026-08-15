namespace AncientWarfare3.core.lineage
{
    public static class MandateBorderWallRules
    {
        public const string PreferredWallTopTileId = "wall_order";

        public static bool ShouldFortifyKingdom(
            bool pNeighborHasKingdom,
            bool pNeighborAlive,
            bool pNeighborNeutral,
            bool pSameMandateSystem,
            bool pSameAlliance,
            bool pMandateTributary)
        {
            return pNeighborHasKingdom && pNeighborAlive &&
                !pNeighborNeutral && !pSameMandateSystem &&
                !pSameAlliance && !pMandateTributary;
        }

        public static bool IsExternalLandBorderNeighbor(
            bool pFortificationTarget,
            bool pNeighborHasCity,
            bool pNeighborGround,
            bool pNeighborLiquid,
            bool pNeighborLava,
            bool pNeighborBlock)
        {
            if (!pNeighborHasCity || !pFortificationTarget) return false;
            return pNeighborGround && !pNeighborLiquid &&
                !pNeighborLava && !pNeighborBlock;
        }

        public static bool IsExternalLandBorderNeighbor(
            bool pNeighborHasCity,
            bool pNeighborGround,
            bool pNeighborLiquid,
            bool pNeighborLava,
            bool pNeighborBlock,
            bool pNeighborNeutral,
            bool pSameMandateSystem)
        {
            bool target = ShouldFortifyKingdom(
                pNeighborHasCity, true, pNeighborNeutral,
                pSameMandateSystem, false, false);
            return IsExternalLandBorderNeighbor(target,
                pNeighborHasCity, pNeighborGround, pNeighborLiquid,
                pNeighborLava, pNeighborBlock);
        }
    }
}

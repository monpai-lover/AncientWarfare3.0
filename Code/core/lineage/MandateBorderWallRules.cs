namespace AncientWarfare3.core.lineage
{
    public static class MandateBorderWallRules
    {
        public const string PreferredWallTopTileId = "wall_order";
        private const int GapInterval = 9;

        public static bool ShouldBuildWallAtOrderedIndex(int pOrderedIndex)
        {
            if (pOrderedIndex < 0) return false;
            return (pOrderedIndex + 1) % GapInterval != 0;
        }

        public static int CompareWallTileOrder(int pAx, int pAy, int pBx, int pBy)
        {
            int y = pAy.CompareTo(pBy);
            return y != 0 ? y : pAx.CompareTo(pBx);
        }

        public static bool IsWallBuildTileTerrainValid(
            bool pInsideCity,
            bool pGround,
            bool pLiquid,
            bool pLava,
            bool pBlock,
            bool pWall,
            bool pRoad,
            bool pHasTopTile,
            bool pHasBuilding)
        {
            if (!pInsideCity) return false;
            if (!pGround || pLiquid || pLava || pBlock) return false;
            if (pWall || pRoad || pHasTopTile || pHasBuilding) return false;
            return true;
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
            if (!pNeighborHasCity || pNeighborNeutral || pSameMandateSystem) return false;
            return pNeighborGround && !pNeighborLiquid && !pNeighborLava && !pNeighborBlock;
        }
    }
}

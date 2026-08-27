namespace AncientWarfare3.core.lineage
{
    public static class MandateBorderWallRules
    {
        public const string PreferredWallTopTileId = "wall_order";
        public const int PoorRelationOpinionThreshold = 30;

        public static bool IsPoorRelation(int pOpinion)
        {
            return pOpinion < PoorRelationOpinionThreshold;
        }

        public static bool ShouldUsePoorRelationTargetsForDecision(
            int pUsesBeforeDecision)
        {
            return pUsesBeforeDecision >= 0;
        }

        public static bool ShouldUsePoorRelationTargets(int pCompletedUses)
        {
            return pCompletedUses >= 0;
        }

        public static bool ShouldFortifyKingdom(
            bool pNeighborHasKingdom,
            bool pNeighborAlive,
            bool pNeighborNeutral,
            bool pSameMandateSystem,
            bool pSameAlliance,
            bool pMandateTributary,
            bool pRebelKingdom = false,
            bool pBanditKingdom = false,
            bool pGuiyiKingdom = false)
        {
            return pNeighborHasKingdom && pNeighborAlive &&
                !pNeighborNeutral && !pSameMandateSystem &&
                !pSameAlliance && !pMandateTributary &&
                !pRebelKingdom && !pBanditKingdom && !pGuiyiKingdom;
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

    }
}

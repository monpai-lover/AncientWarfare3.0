namespace AncientWarfare3.core.lineage
{
    public static class LineageBranchRules
    {
        public static bool IsDirectSuccessionFromKnownKing(long newKingParent1Id, long newKingParent2Id,
            long previousKingId, long recordedPreSuccessionKingId)
        {
            return MatchesParent(newKingParent1Id, newKingParent2Id, previousKingId) ||
                   MatchesParent(newKingParent1Id, newKingParent2Id, recordedPreSuccessionKingId);
        }

        private static bool MatchesParent(long parent1Id, long parent2Id, long kingId)
        {
            if (kingId < 0) return false;
            return parent1Id == kingId || parent2Id == kingId;
        }

        public static bool ShouldApplyCollateralRestoration(string successionMode, bool wasHeir,
            bool isCurrentHeir, bool isDirectSuccessionFromPreviousKing)
        {
            if (successionMode != "collateral_restore") return false;
            if (wasHeir || isCurrentHeir || isDirectSuccessionFromPreviousKing) return false;
            return true;
        }

        public static bool ShouldFoundKingBranch(
            bool validKingdom,
            bool isXiaKing,
            bool wasHeir,
            bool isCurrentHeir,
            bool isDirectSuccessionFromPreviousKing,
            bool isCollateralRestoration,
            bool hasLineage,
            bool hasShi,
            bool isHistoricalFigure,
            bool isLineageRootFounder,
            int aliveInCurrentShi,
            int minAliveForNewBranch,
            long currentKingdomId,
            long originKingdomId,
            bool alreadyFoundedForKingdom,
            int cadetGenerationDistance,
            int minCadetDistanceForBranch)
        {
            if (!validKingdom || !isXiaKing) return false;
            if (wasHeir || isCurrentHeir || isDirectSuccessionFromPreviousKing) return false;
            if (isCollateralRestoration) return false;
            if (!hasLineage || !hasShi) return false;
            if (isHistoricalFigure || isLineageRootFounder) return false;
            // 严格:只有关系稀薄的旁支才可建支——须连续 minCadetDistanceForBranch 代都是非嫡系(未任贵族)子孙,
            // 然后本人成王,才建立新分支。距离不足(近支/直系)一律不建支。
            if (cadetGenerationDistance < minCadetDistanceForBranch) return false;
            if (aliveInCurrentShi < minAliveForNewBranch) return false;
            if (originKingdomId == currentKingdomId) return false;
            if (alreadyFoundedForKingdom) return false;
            return true;
        }

        public static bool ShouldFoundCollateralBranch(
            bool newDynastyCreatedFromPreBranchIdentity,
            bool isEmpireOrMandate, bool collateral,
            bool hasTraceableAncestor, bool foreignThrone,
            bool highInfluenceElsewhere, bool directHeir,
            bool previousKingDirectChild,
            bool alreadyFoundedForDestination)
        {
            return newDynastyCreatedFromPreBranchIdentity &&
                   isEmpireOrMandate && collateral &&
                   hasTraceableAncestor && foreignThrone &&
                   !directHeir && !previousKingDirectChild &&
                   !alreadyFoundedForDestination;
        }

        public static bool ShouldFoundCollateralBranch(bool collateral,
            bool hasTraceableAncestor, bool foreignThrone,
            bool highInfluenceElsewhere, bool directHeir,
            bool previousKingDirectChild,
            bool alreadyFoundedForDestination)
        {
            return collateral && hasTraceableAncestor &&
                   (foreignThrone || highInfluenceElsewhere) &&
                   !directHeir && !previousKingDirectChild &&
                   !alreadyFoundedForDestination;
        }
    }
}

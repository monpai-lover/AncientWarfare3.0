namespace AncientWarfare3.core.lineage
{
    public static class RoyalGuardSelectionRules
    {
        public static bool IsEligibleCore(
            bool isXia,
            bool sameKingdom,
            bool isMale,
            bool isBoat,
            bool isRekt,
            bool isAdult,
            bool isKing,
            bool isCityLeader,
            bool isSlave,
            bool isRetiredSoldier,
            bool isCurrentHeir,
            bool hasMadness,
            bool isHistoricalFigure)
        {
            if (!isXia || !sameKingdom) return false;
            if (isBoat || isRekt || !isAdult) return false;
            if (isKing || isCityLeader) return false;
            if (isSlave || isRetiredSoldier || isCurrentHeir) return false;
            if (hasMadness || isHistoricalFigure) return false;
            return true;
        }
    }
}

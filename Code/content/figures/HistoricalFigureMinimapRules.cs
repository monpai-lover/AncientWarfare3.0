namespace AncientWarfare3.content.figures
{
    public static class HistoricalFigureMinimapRules
    {
        public static bool ShouldDrawIcon(
            bool isAlive,
            bool isInMagnet,
            bool hasCurrentTile,
            bool hasVisibleZone,
            bool isKing,
            bool isCityLeader,
            bool hasFigureTrait,
            bool hasFirstTrait)
        {
            return isAlive &&
                   !isInMagnet &&
                   hasCurrentTile &&
                   hasVisibleZone &&
                   !isKing &&
                   !isCityLeader &&
                   (hasFigureTrait || hasFirstTrait);
        }
    }
}

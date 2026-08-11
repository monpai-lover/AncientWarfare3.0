namespace AncientWarfare3.core.lineage
{
    public static class ActorDeathSafetyRules
    {
        public static bool ShouldRunDeathCheck(bool hasData, bool isRekt,
            bool isAlive, bool hasCurrentTile)
        {
            return hasData && !isRekt && isAlive && hasCurrentTile;
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    public static class FormerRulerRecordRules
    {
        public static bool ShouldRecordLostThrone(long previousKingId, long newKingId, bool previousAlive)
        {
            if (!previousAlive) return false;
            if (previousKingId < 0 || newKingId < 0) return false;
            return previousKingId != newKingId;
        }
    }
}

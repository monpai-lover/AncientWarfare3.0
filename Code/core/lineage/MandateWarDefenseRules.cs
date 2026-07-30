namespace AncientWarfare3.core.lineage
{
    public static class MandateWarDefenseRules
    {
        public static bool ShouldActivate(bool mandateActive, long mandateKingdomId,
            long mainAttackerKingdomId, long mainDefenderKingdomId)
        {
            if (!mandateActive || mandateKingdomId < 0) return false;
            return mandateKingdomId == mainAttackerKingdomId ||
                   mandateKingdomId == mainDefenderKingdomId;
        }
    }
}

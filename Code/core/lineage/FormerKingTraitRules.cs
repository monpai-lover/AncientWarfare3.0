namespace AncientWarfare3.core.lineage
{
    public static class FormerKingTraitRules
    {
        public static bool ShouldMarkFormerKing(bool pKingdomDestroyed, bool pWasLastKing, bool pFormerKingAlive)
        {
            return pKingdomDestroyed && pWasLastKing && pFormerKingAlive;
        }

        public static bool ShouldUseMandateDeposedTitle(bool pIsMandateKingdom, string pEndReason,
            bool pFormerKingAlive)
        {
            return pIsMandateKingdom && pFormerKingAlive && pEndReason == "kingdom_fell";
        }

        public static bool ShouldSnapshotLivingRulerTitle(string pEndReason, bool pFormerKingAlive)
        {
            return pFormerKingAlive && (pEndReason == "abdicated" || pEndReason == "replaced");
        }

        public static string BuildMandateDeposedTitle(string pKingdomName)
        {
            string prefix = string.IsNullOrEmpty(pKingdomName) ? "" : pKingdomName.Substring(0, 1);
            return prefix + "\u5E9F\u5E1D";
        }
    }
}

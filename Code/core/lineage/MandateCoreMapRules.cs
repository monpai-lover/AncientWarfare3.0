namespace AncientWarfare3.core.lineage
{
    public static class MandateCoreMapRules
    {
        public static string SelectCoreStatus(bool isLegalCore, bool hasMandate, bool hasOwner,
            bool ownerIsMandate, bool ownerRootSuzerainIsMandate)
        {
            if (!isLegalCore) return "none";
            if (!hasMandate || !hasOwner) return "orphan";
            if (ownerIsMandate) return "controlled";
            if (ownerRootSuzerainIsMandate) return "vassal";
            return "lost";
        }

        public static bool ShouldAddNewKingdomCoreToMandateLegalCore(bool pIsActiveMandateKingdom,
            bool pCoreAlreadyLegal)
        {
            return pIsActiveMandateKingdom && !pCoreAlreadyLegal;
        }
    }
}

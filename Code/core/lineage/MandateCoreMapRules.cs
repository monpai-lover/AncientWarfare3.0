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

        public static string HexForStatus(string pStatus)
        {
            switch (pStatus ?? "")
            {
                case "controlled": return "#226B3A";
                case "vassal": return "#4F8F45";
                case "lost": return "#B3124B";
                case "orphan": return "#8A8A8A";
                default: return "#242424";
            }
        }
    }
}

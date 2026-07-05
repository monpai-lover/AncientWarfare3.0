namespace AncientWarfare3.core.policy
{
    public static class MandateDynastyMapRules
    {
        public const string StatusMandate = "mandate";
        public const string StatusVassal = "vassal";

        public static string ResolveStatus(bool pIsMandateKingdom, bool pRootSuzerainIsMandate)
        {
            if (pIsMandateKingdom) return StatusMandate;
            return pRootSuzerainIsMandate ? StatusVassal : "";
        }

        public static string BuildStatusCacheKey(long pMandateId, long pKingdomId)
        {
            if (pMandateId < 0 || pKingdomId < 0) return "";
            return pMandateId + ":" + pKingdomId;
        }

        public static bool ShouldDrawStatus(string pStatus)
        {
            return pStatus == StatusMandate || pStatus == StatusVassal;
        }

        public static string HexForStatus(string pStatus)
        {
            switch (pStatus ?? "")
            {
                case StatusMandate:
                    return "#D72F8A";
                case StatusVassal:
                    return "#6E4BD8";
                default:
                    return "#242424";
            }
        }
    }
}

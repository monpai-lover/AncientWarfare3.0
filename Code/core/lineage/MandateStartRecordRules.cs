namespace AncientWarfare3.core.lineage
{
    public static class MandateStartRecordRules
    {
        public static bool IsForeignPseudo(string pOriginType, string pClaimantKind)
        {
            return pOriginType == "pseudo_foreign" || pClaimantKind == "foreign_pseudo";
        }

        public static bool IsRebel(string pOriginType, string pClaimantKind)
        {
            return pOriginType == "rebel" || pClaimantKind == "rebel";
        }

        public static string EventType(string pOriginType, string pClaimantKind)
        {
            if (IsForeignPseudo(pOriginType, pClaimantKind)) return "mandate_declared_foreign_pseudo";
            if (IsRebel(pOriginType, pClaimantKind)) return "mandate_declared_rebel";
            return "mandate_declared_orthodox";
        }
    }
}

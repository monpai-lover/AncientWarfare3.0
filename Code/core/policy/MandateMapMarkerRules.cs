namespace AncientWarfare3.core.policy
{
    public static class MandateMapMarkerRules
    {
        public const string IconMandate = "moh_nameplate";
        public const string IconRebel = "ui/Icons/traits/iconrebel";
        public const string IconPseudo = "ui/wars/Mandate_of_Heaven";

        public static string ResolveIcon(bool pKingdomValid, bool pCurrentMandate,
            string pMarkerKind, bool pRebel, string pOrigin, string pClaimant)
        {
            if (!pKingdomValid) return "";

            if (pCurrentMandate)
            {
                if (pMarkerKind == "rebel_claimant") return IconRebel;
                if (pMarkerKind == "pseudo_foreign") return IconPseudo;
                return IconMandate;
            }

            if (pRebel) return IconRebel;
            if (pOrigin == "pseudo_foreign" || pClaimant == "foreign_pseudo")
                return IconPseudo;
            return "";
        }
    }
}

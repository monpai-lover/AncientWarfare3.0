namespace AncientWarfare3.core.lineage
{
    public static class KingdomTitleDisplayRules
    {
        public static string GetNameplateTitleSuffix(int pTitle, bool pIsMandateKingdom)
        {
            return GetNameplateTitleSuffix(pTitle, pIsMandateKingdom, pIsRebelKingdom: false,
                pIsRepublic: false);
        }

        public static string GetNameplateTitleSuffix(int pTitle, bool pIsMandateKingdom, bool pIsRebelKingdom)
        {
            return GetNameplateTitleSuffix(pTitle, pIsMandateKingdom, pIsRebelKingdom, pIsRepublic: false);
        }

        public static string GetNameplateTitleSuffix(int pTitle, bool pIsMandateKingdom, bool pIsRebelKingdom,
            bool pIsRepublic)
        {
            if (pIsRebelKingdom) return "\u4e49\u519b";
            string republic = RepublicGovernmentRules.SuffixForNameplate(pIsRepublic);
            if (!string.IsNullOrEmpty(republic)) return republic;
            if (pIsMandateKingdom && pTitle == (int)KingdomTitle.Emperor) return "\u671d";
            return KingdomTitleService.GetTitleString((KingdomTitle)pTitle);
        }
    }
}

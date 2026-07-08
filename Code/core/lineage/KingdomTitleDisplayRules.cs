namespace AncientWarfare3.core.lineage
{
    public static class KingdomTitleDisplayRules
    {
        public static string GetNameplateTitleSuffix(int pTitle, bool pIsMandateKingdom)
        {
            return GetNameplateTitleSuffix(pTitle, pIsMandateKingdom, pIsRebelKingdom: false);
        }

        public static string GetNameplateTitleSuffix(int pTitle, bool pIsMandateKingdom, bool pIsRebelKingdom)
        {
            if (pIsRebelKingdom) return "\u4e49\u519b";
            if (pIsMandateKingdom && pTitle == (int)KingdomTitle.Emperor) return "\u671d";
            return KingdomTitleService.GetTitleString((KingdomTitle)pTitle);
        }
    }
}

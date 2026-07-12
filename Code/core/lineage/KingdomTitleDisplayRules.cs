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
            return KingdomNameplateSuffixRules.Resolve(pTitle, pIsMandateKingdom, pIsRebelKingdom,
                pIsRepublic);
        }
    }
}

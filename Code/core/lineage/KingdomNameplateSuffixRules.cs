namespace AncientWarfare3.core.lineage
{
    public static class KingdomNameplateSuffixRules
    {
        public static string Resolve(int pTitle, bool pIsMandateKingdom, bool pIsRebelKingdom,
            bool pIsRepublic)
        {
            if (pIsRebelKingdom) return "\u4e49\u519b";
            if (pIsRepublic) return "\u5171\u548c\u56fd";
            if (pIsMandateKingdom && pTitle == 4) return "\u671d";

            switch (pTitle)
            {
                case 0:
                case 1:
                case 2:
                    return "\u56fd";
                case 3:
                    return "\u738b\u56fd";
                case 4:
                    return "\u5e1d\u56fd";
                default:
                    return "";
            }
        }
    }
}

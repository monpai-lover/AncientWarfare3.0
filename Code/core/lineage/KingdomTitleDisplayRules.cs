namespace AncientWarfare3.core.lineage
{
    public static class KingdomTitleDisplayRules
    {
        public static string GetNameplateTitleSuffix(int pTitle, bool pIsMandateKingdom)
        {
            if (pIsMandateKingdom && pTitle == (int)KingdomTitle.Emperor) return "朝";
            return KingdomTitleService.GetTitleString((KingdomTitle)pTitle);
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    public static class HeirTitleRules
    {
        public const string ShiziKey = "aw_heir_shizi";
        public const string TaiziKey = "aw_heir_taizi";

        public static string TitleKey(bool pIsMandateKingdom)
        {
            return pIsMandateKingdom ? TaiziKey : ShiziKey;
        }

        internal static string TitleKey(Kingdom pKingdom)
        {
            return TitleKey(MandateService.IsMandateKingdom(pKingdom));
        }

        public static bool ShouldRewriteOriginalHeirTitle(string pTitle)
        {
            return pTitle == "heir" ||
                   pTitle == "village_statistics_heir" ||
                   pTitle == "aw_heir" ||
                   pTitle == "aw_label_heir";
        }
    }
}

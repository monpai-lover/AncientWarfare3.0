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

        public static string DefaultTitleText(bool pIsMandateKingdom)
        {
            return pIsMandateKingdom ? "\u592a\u5b50" : "\u4e16\u5b50";
        }

        public static string BuildSocialTitle(string pKingdomName, bool pIsMandateKingdom)
        {
            string title = DefaultTitleText(pIsMandateKingdom);
            return string.IsNullOrEmpty(pKingdomName) ? title : pKingdomName + " " + title;
        }

        public static bool IsGenericHeirTitle(string pTitle)
        {
            return !string.IsNullOrEmpty(pTitle) && pTitle.Contains("\u7ee7\u627f\u4eba");
        }

        public static string RoleSnapshot(bool pIsMandateKingdom)
        {
            return pIsMandateKingdom ? "heir_taizi" : "heir_shizi";
        }

        internal static string TitleKey(Kingdom pKingdom)
        {
            return TitleKey(MandateService.IsMandateKingdom(pKingdom));
        }

        internal static string BuildSocialTitle(string pKingdomName, Kingdom pKingdom)
        {
            return BuildSocialTitle(pKingdomName, MandateService.IsMandateKingdom(pKingdom));
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

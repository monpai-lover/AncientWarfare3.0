namespace AncientWarfare3.core.lineage
{
    public static class HeirTitleRules
    {
        public const string ShiziKey = HeirTitleSelectionRules.ShiziKey;
        public const string TaiziKey = HeirTitleSelectionRules.TaiziKey;
        public const string LiuhouKey = HeirTitleSelectionRules.LiuhouKey;
        public const string SijunKey = HeirTitleSelectionRules.SijunKey;

        public static string TitleKey(bool pIsEmpireOrMandate)
        {
            return HeirTitleSelectionRules.TitleKey(pIsEmpireOrMandate, "");
        }

        public static string DefaultTitleText(bool pIsEmpireOrMandate)
        {
            return HeirTitleSelectionRules.DefaultTitleText(
                pIsEmpireOrMandate, "");
        }

        public static string TitleKey(bool pIsEmpireOrMandate,
            string pSuccessionMode)
        {
            return HeirTitleSelectionRules.TitleKey(
                pIsEmpireOrMandate, pSuccessionMode);
        }

        public static string DefaultTitleText(bool pIsEmpireOrMandate,
            string pSuccessionMode)
        {
            return HeirTitleSelectionRules.DefaultTitleText(
                pIsEmpireOrMandate, pSuccessionMode);
        }

        public static string BuildSocialTitle(string pKingdomName,
            bool pIsEmpireOrMandate)
        {
            string title = DefaultTitleText(pIsEmpireOrMandate);
            return string.IsNullOrEmpty(pKingdomName) ? title : pKingdomName + " " + title;
        }

        public static bool IsGenericHeirTitle(string pTitle)
        {
            return !string.IsNullOrEmpty(pTitle) && pTitle.Contains("\u7ee7\u627f\u4eba");
        }

        public static string RoleSnapshot(bool pIsEmpireOrMandate)
        {
            return HeirTitleSelectionRules.RoleSnapshot(pIsEmpireOrMandate);
        }

        internal static bool IsImperialOrMandate(Kingdom pKingdom)
        {
            return KingdomTitleService.IsEmperor(pKingdom) ||
                   MandateService.IsMandateKingdom(pKingdom);
        }

        internal static string TitleKey(Kingdom pKingdom)
        {
            if (RepublicGovernmentService.IsRepublic(pKingdom))
                return GovernmentTitleRules.SuccessorKey(true,
                    IsImperialOrMandate(pKingdom));
            string successionMode = SuccessionMode.NONE;
            if (pKingdom?.data != null)
                pKingdom.data.get(LineageKeys.INHERITANCE_CANDIDATE_MODE,
                    out successionMode, SuccessionMode.NONE);
            return TitleKey(IsImperialOrMandate(pKingdom),
                successionMode);
        }

        internal static string DefaultTitleText(Kingdom pKingdom,
            string pSuccessionMode)
        {
            return DefaultTitleText(IsImperialOrMandate(pKingdom),
                pSuccessionMode);
        }

        internal static string BuildSocialTitle(string pKingdomName, Kingdom pKingdom)
        {
            if (RepublicGovernmentService.IsRepublic(pKingdom))
                return GovernmentTitleRules.BuildSocialTitle(pKingdomName, pIsHead: false, pIsElder: true);
            string successionMode = SuccessionMode.NONE;
            if (pKingdom?.data != null)
                pKingdom.data.get(LineageKeys.INHERITANCE_CANDIDATE_MODE,
                    out successionMode, SuccessionMode.NONE);
            string title = DefaultTitleText(
                IsImperialOrMandate(pKingdom), successionMode);
            return string.IsNullOrEmpty(pKingdomName)
                ? title
                : pKingdomName + " " + title;
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

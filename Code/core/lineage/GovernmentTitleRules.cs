namespace AncientWarfare3.core.lineage
{
    public static class GovernmentTitleRules
    {
        public const string RepublicHeadKey = "aw_republic_head";
        public const string RepublicElderKey = "aw_republic_elder";

        public static string RulerKey(bool pIsRepublic)
        {
            return pIsRepublic ? RepublicHeadKey : "aw_label_king";
        }

        public static string SuccessorKey(bool pIsRepublic, bool pIsMandate)
        {
            return pIsRepublic ? RepublicElderKey : HeirTitleRules.TitleKey(pIsMandate);
        }

        public static string RoleSnapshot(bool pIsRepublic, bool pIsRuler,
            bool pIsSuccessor, bool pIsMandate)
        {
            if (pIsRepublic && pIsRuler) return "republic_head";
            if (pIsRepublic && pIsSuccessor) return "republic_elder";
            if (pIsRuler) return "king";
            if (pIsSuccessor) return HeirTitleRules.RoleSnapshot(pIsMandate);
            return "";
        }

        public static string BuildSocialTitle(string pKingdomName, bool pIsHead, bool pIsElder)
        {
            string title = pIsHead ? "\u5143\u9996" : pIsElder ? "\u5143\u8001" : "";
            return string.IsNullOrEmpty(pKingdomName) ? title : pKingdomName + " " + title;
        }

        public static bool IsRepublicSocialTitle(string pTitle)
        {
            return !string.IsNullOrEmpty(pTitle) &&
                   (pTitle.EndsWith(" \u5143\u9996") || pTitle.EndsWith(" \u5143\u8001") ||
                    pTitle == "\u5143\u9996" || pTitle == "\u5143\u8001");
        }
    }
}

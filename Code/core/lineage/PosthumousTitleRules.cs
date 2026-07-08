namespace AncientWarfare3.core.lineage
{
    public static class PosthumousTitleRules
    {
        private const string Taizu = "\u592a\u7956";
        private const string EmperorSuffix = "\u5e1d";
        private const string EmperorFullSuffix = "\u7687\u5e1d";

        public static bool ShouldUseTaizuForOrdinaryFirstEmperor(bool pIsMandateKingdom,
            bool pIsEmperor, bool pHasPriorEmperorTitle)
        {
            return !pIsMandateKingdom && pIsEmperor && !pHasPriorEmperorTitle;
        }

        public static string BuildFullTitle(string pKingdomPrefix, string pTitleChar, string pSuffix,
            bool pUseOrdinaryFirstEmperorTaizu)
        {
            string prefix = pKingdomPrefix ?? "";
            string titleChar = pTitleChar ?? "";
            string suffix = pSuffix ?? "";
            if (pUseOrdinaryFirstEmperorTaizu && suffix == EmperorSuffix)
                return prefix + Taizu + titleChar + suffix;
            return prefix + titleChar + suffix;
        }

        public static bool IsCompactOrdinaryEmperorTitle(string pTitle)
        {
            if (string.IsNullOrEmpty(pTitle)) return false;
            if (!pTitle.EndsWith(EmperorSuffix)) return false;
            if (pTitle.Contains(EmperorFullSuffix)) return false;
            if (pTitle.Contains(" ")) return false;
            return pTitle.Length >= 3;
        }

        public static string RepairFirstOrdinaryEmperorDisplayTitle(string pTitle,
            bool pHasPriorOrdinaryEmperorTitle)
        {
            if (pHasPriorOrdinaryEmperorTitle) return pTitle ?? "";
            if (!IsCompactOrdinaryEmperorTitle(pTitle)) return pTitle ?? "";
            if (pTitle.Contains(Taizu)) return pTitle;

            string body = pTitle.Substring(0, pTitle.Length - EmperorSuffix.Length);
            if (body.Length < 2) return pTitle;
            string prefix = body.Substring(0, 1);
            string rest = body.Substring(1);
            if (rest.Contains("\u7956") || rest.Contains("\u5b97")) return pTitle;
            return prefix + Taizu + rest + EmperorSuffix;
        }
    }
}

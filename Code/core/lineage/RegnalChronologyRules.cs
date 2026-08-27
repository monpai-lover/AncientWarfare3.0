namespace AncientWarfare3.core.lineage
{
    public enum RegnalChronologyProfile
    {
        None = 0,
        Xia = 1,
        Western = 2
    }

    public static class RegnalChronologyRules
    {
        public static RegnalChronologyProfile ResolveProfile(bool valid,
            bool civilized, bool biologicalXia, bool monkey,
            bool enteredXia)
        {
            if (!valid || !civilized) return RegnalChronologyProfile.None;
            return biologicalXia || monkey || enteredXia
                ? RegnalChronologyProfile.Xia
                : RegnalChronologyProfile.Western;
        }

        public static string Format(string pStateName, string pRank,
            string pGivenName, int pReignYear, bool isHereditaryMonarchy,
            bool isRepublic)
        {
            if (!isHereditaryMonarchy || isRepublic || pReignYear < 1)
                return "";
            string state = (pStateName ?? "").Trim();
            string rank = (pRank ?? "").Trim();
            string given = (pGivenName ?? "").Trim();
            if (state.Length == 0 || rank.Length == 0 || given.Length == 0)
                return "";
            return state + rank + given + EraNameRules.FormatYear(pReignYear);
        }

        public static string Format(RegnalChronologyProfile pProfile,
            string pStateName, int pWesternRank, string pXiaRank,
            string pRulerName, int pReignYear,
            bool isHereditaryMonarchy, bool isRepublic)
        {
            if (pProfile == RegnalChronologyProfile.Xia)
                return Format(pStateName, pXiaRank, pRulerName, pReignYear,
                    isHereditaryMonarchy, isRepublic);
            if (pProfile != RegnalChronologyProfile.Western ||
                !isHereditaryMonarchy || isRepublic || pReignYear < 1)
                return "";

            string state = (pStateName ?? "").Trim();
            string ruler = (pRulerName ?? "").Trim();
            string title = WesternTitleSuffix(pWesternRank);
            if (state.Length == 0 || ruler.Length == 0 || title.Length == 0)
                return "";
            return state + " " + ruler + title +
                   EraNameRules.FormatYear(pReignYear);
        }

        public static string WesternTitleSuffix(int pRank)
        {
            switch (pRank)
            {
                case 0:
                    return "伯爵";
                case 1:
                    return "侯爵";
                case 2:
                    return "公爵";
                case 3:
                    return "国王";
                case 4:
                    return "皇帝";
                default:
                    return "";
            }
        }

        public static string SelectDisplay(string pFormalEra,
            string pLocalRegnalChronology)
        {
            string formal = (pFormalEra ?? "").Trim();
            return formal.Length > 0
                ? formal
                : (pLocalRegnalChronology ?? "").Trim();
        }

        public static bool ShouldUseEraName(bool isXiaProfile,
            bool isBandit, bool isRebel)
        {
            return isXiaProfile && !isBandit && !isRebel;
        }
    }
}

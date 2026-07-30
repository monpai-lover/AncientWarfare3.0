namespace AncientWarfare3.core.lineage
{
    public static class RegnalChronologyRules
    {
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

        public static string SelectDisplay(string pFormalEra,
            string pLocalRegnalChronology)
        {
            string formal = (pFormalEra ?? "").Trim();
            return formal.Length > 0
                ? formal
                : (pLocalRegnalChronology ?? "").Trim();
        }
    }
}

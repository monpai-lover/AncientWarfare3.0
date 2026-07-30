namespace AncientWarfare3.core.lineage
{
    public static class GanzhiChronologyRules
    {
        private static readonly char[] HeavenlyStems =
            "甲乙丙丁戊己庚辛壬癸".ToCharArray();
        private static readonly char[] EarthlyBranches =
            "子丑寅卯辰巳午未申酉戌亥".ToCharArray();

        public static string GetYearName(int pWorldYear)
        {
            int cycle = FloorMod((long)pWorldYear - 1L, 60);
            return string.Concat(HeavenlyStems[cycle % 10],
                EarthlyBranches[cycle % 12]);
        }

        public static string FormatPrefix(string pEra, string pWorldDate,
            int pWorldYear)
        {
            string ganzhi = GetYearName(pWorldYear);
            string world = pWorldDate ?? "";
            return string.IsNullOrEmpty(pEra)
                ? ganzhi + "（" + world + "）"
                : pEra + "·" + ganzhi + "（" + world + "）";
        }

        public static string RemoveWorldLabel(string pText)
        {
            return string.IsNullOrEmpty(pText)
                ? pText ?? ""
                : pText.Replace("（世界", "（")
                    .Replace("(世界", "(");
        }

        public static bool IsCanonicalPrefix(string pText)
        {
            if (string.IsNullOrEmpty(pText)) return false;
            if (pText.IndexOf('(') >= 0) return true;

            int open = pText.LastIndexOf('\uFF08');
            int close = pText.LastIndexOf('\uFF09');
            if (open < 0 || close <= open) return false;
            int year = pText.IndexOf('\u5E74', open + 1);
            int month = year < 0
                ? -1
                : pText.IndexOf('\u6708', year + 1);
            int day = month < 0
                ? -1
                : pText.IndexOf('\u65E5', month + 1);
            return year > open && month > year && day > month &&
                   day < close;
        }

        private static int FloorMod(long pValue, int pModulus)
        {
            long result = pValue % pModulus;
            return (int)(result < 0 ? result + pModulus : result);
        }
    }
}

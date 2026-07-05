namespace AncientWarfare3.core.lineage
{
    public static class ChronicleFormatRules
    {
        public static string FormatDateParts(int pYear, int pMonth, int pDay)
        {
            return pYear + "\u5e74" + pMonth + "\u6708" + pDay + "\u65e5";
        }
    }
}

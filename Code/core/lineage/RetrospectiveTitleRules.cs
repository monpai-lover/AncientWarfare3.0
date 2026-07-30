namespace AncientWarfare3.core.lineage
{
    public static class RetrospectiveTitleRules
    {
        public const int MaximumAncestorGenerations = 2;

        public static string BuildImperialAppellation(string pTempleName,
            string pInheritedPosthumousName)
        {
            string temple = (pTempleName ?? "").Trim();
            if (temple.Length == 0) return "";
            return temple + (pInheritedPosthumousName ?? "").Trim() +
                   "\u7687\u5E1D";
        }
    }
}

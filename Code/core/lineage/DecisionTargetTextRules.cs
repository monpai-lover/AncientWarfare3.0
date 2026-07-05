namespace AncientWarfare3.core.lineage
{
    public static class DecisionTargetTextRules
    {
        public static string TargetLine(string pTargetName)
        {
            return string.IsNullOrEmpty(pTargetName) ? "" : "\u76EE\u6807\uFF1A" + pTargetName;
        }
    }
}

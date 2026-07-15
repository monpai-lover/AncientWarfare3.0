namespace AncientWarfare3.core.lineage
{
    public static class FormerHeirTitleRules
    {
        public static bool ShouldSnapshot(bool kingdomDestroyed, bool registeredHeir, bool heirAlive)
        {
            return kingdomDestroyed && registeredHeir && heirAlive;
        }

        public static string BuildFormerTitle(string activeTitle)
        {
            if (string.IsNullOrWhiteSpace(activeTitle)) return "";
            string normalized = activeTitle.Trim();
            return normalized.StartsWith("前") ? normalized : "前" + normalized;
        }
    }
}

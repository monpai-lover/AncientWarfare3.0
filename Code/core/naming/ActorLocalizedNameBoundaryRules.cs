namespace AncientWarfare3.core.naming
{
    public static class ActorLocalizedNameBoundaryRules
    {
        public static string ResolveTemplateFamily(string chineseFamilyName,
            string lineageFamilyName)
        {
            string chinese = (chineseFamilyName ?? string.Empty).Trim();
            if (chinese.Length > 0) return chinese;
            return (lineageFamilyName ?? string.Empty).Trim();
        }
    }
}

using System;

namespace AncientWarfare3.core.naming
{
    public static class AWNamingLanguageRules
    {
        public static bool IsChinesePresentation(string pLanguage)
        {
            return string.Equals(pLanguage, "ch",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(pLanguage, "cz",
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldGenerateChineseIdentity(string pLanguage,
            bool hasChineseIdentity)
        {
            return !hasChineseIdentity && IsChinesePresentation(pLanguage);
        }
    }
}

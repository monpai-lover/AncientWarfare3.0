namespace AncientWarfare3.core.naming
{
    public static class AWLocalizedNameProjectionRules
    {
        public static string Select(string pLanguage, string pNativeName,
            string pChineseName)
        {
            string nativeName = (pNativeName ?? string.Empty).Trim();
            string chineseName = (pChineseName ?? string.Empty).Trim();
            if (AWNamingLanguageRules.IsChinesePresentation(pLanguage))
                return chineseName.Length > 0 ? chineseName : nativeName;
            return nativeName.Length > 0 ? nativeName : chineseName;
        }
    }
}

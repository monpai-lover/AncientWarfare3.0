namespace AncientWarfare3.core.naming
{
    public static class AWLocalizedMottoCreationRules
    {
        public static bool ShouldUseFallback(string language,
            string observedMotto, string nativeMotto, string chineseMotto)
        {
            return string.IsNullOrWhiteSpace(observedMotto) &&
                   string.IsNullOrWhiteSpace(nativeMotto) &&
                   string.IsNullOrWhiteSpace(chineseMotto);
        }

        public static bool ShouldProjectOnCreation(string observedMotto,
            string chineseMotto, string nativeMotto)
        {
            return string.IsNullOrWhiteSpace(observedMotto) &&
                   string.IsNullOrWhiteSpace(chineseMotto) &&
                   string.IsNullOrWhiteSpace(nativeMotto);
        }

        public static string ResolveFallback(string language)
        {
            return AWNamingLanguageRules.IsChinesePresentation(language)
                ? "共守家园"
                : "United in purpose";
        }
    }
}

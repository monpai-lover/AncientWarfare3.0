using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.court
{
    /// <summary>
    /// Resolves the display name of the lowest administrative unit.  Chinese
    /// presentation uses the persisted historical-name JSON value when one is
    /// available; every other presentation follows the city's current name.
    /// </summary>
    public static class CountyNameRules
    {
        public static string ResolveForPresentation(string pLanguage,
            string pHistoricalChineseName, string pCityName)
        {
            string historical = (pHistoricalChineseName ?? string.Empty).Trim();
            string city = (pCityName ?? string.Empty).Trim();
            if (AWNamingLanguageRules.IsChinesePresentation(pLanguage) &&
                historical.Length > 0)
                return historical;
            return city;
        }
    }
}

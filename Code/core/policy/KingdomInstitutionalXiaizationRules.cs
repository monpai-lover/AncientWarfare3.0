namespace AncientWarfare3.core.policy
{
    /// <summary>
    /// Separates culture-level personal naming from kingdom-level authorities.
    /// </summary>
    public static class KingdomInstitutionalXiaizationRules
    {
        public const int InstitutionalXiaizationLevel = 5;

        public static bool ShouldUseXiaPersonalNaming(
            bool integrated, bool fullyIntegrated)
        {
            return integrated || fullyIntegrated;
        }

        public static bool ShouldUseXiaInstitutions(int level)
        {
            return level >= InstitutionalXiaizationLevel;
        }

        public static bool ShouldUseIntegratedSurname(bool kingdomIntegrated)
        {
            return kingdomIntegrated;
        }
    }
}

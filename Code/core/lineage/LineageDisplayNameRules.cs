namespace AncientWarfare3.core.lineage
{
    public static class LineageDisplayNameRules
    {
        public static string ProjectStored(string storedDisplayName,
            string givenName, string familyName, string clanName,
            bool isNoble, bool isMale, bool isNameIntegrated)
        {
            string stored = storedDisplayName ?? "";
            string given = givenName ?? "";
            if (string.IsNullOrEmpty(given)) return stored;

            if (!string.IsNullOrEmpty(stored) && stored != given)
                return stored;

            return Build(given, familyName, clanName, isNoble, isMale,
                isNameIntegrated);
        }

        public static string Build(string givenName, string familyName,
            string clanName, bool isNoble, bool isMale,
            bool isNameIntegrated)
        {
            string given = givenName ?? "";
            string family = familyName ?? "";
            string clan = clanName ?? "";

            if (isNameIntegrated)
            {
                string integratedPrefix = !string.IsNullOrEmpty(clan)
                    ? clan
                    : family;
                return !string.IsNullOrEmpty(integratedPrefix)
                    ? integratedPrefix + given
                    : given;
            }

            if (!isNoble)
                return !string.IsNullOrEmpty(clan) ? clan + given : given;

            if (isMale)
            {
                string prefix = !string.IsNullOrEmpty(clan) ? clan : family;
                return !string.IsNullOrEmpty(prefix) ? prefix + given : given;
            }

            if (!string.IsNullOrEmpty(family)) return given + family;
            return !string.IsNullOrEmpty(clan) ? clan + given : given;
        }
    }
}

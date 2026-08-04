using System;

namespace AncientWarfare3.core.lineage
{
    public static class LineageGivenNameNormalizationRules
    {
        public static string Normalize(string givenName, string familyName,
            string clanName, bool isNoble, bool isMale,
            bool isNameIntegrated)
        {
            string given = (givenName ?? string.Empty).Trim();
            string family = (familyName ?? string.Empty).Trim();
            string clan = (clanName ?? string.Empty).Trim();

            string token;
            bool suffix;
            if (isNameIntegrated)
            {
                token = !string.IsNullOrEmpty(clan) ? clan : family;
                suffix = false;
            }
            else if (!isNoble)
            {
                token = clan;
                suffix = false;
            }
            else if (isMale)
            {
                token = !string.IsNullOrEmpty(clan) ? clan : family;
                suffix = false;
            }
            else if (!string.IsNullOrEmpty(family))
            {
                token = family;
                suffix = true;
            }
            else
            {
                token = clan;
                suffix = false;
            }

            if (string.IsNullOrEmpty(token) || given.Length <= token.Length)
                return given;

            if (suffix && given.EndsWith(token,
                    StringComparison.Ordinal))
            {
                string remainder = given.Substring(0,
                    given.Length - token.Length).Trim();
                return string.IsNullOrEmpty(remainder) ? given : remainder;
            }

            if (!suffix && given.StartsWith(token,
                    StringComparison.Ordinal))
            {
                string remainder = given.Substring(token.Length).Trim();
                return string.IsNullOrEmpty(remainder) ? given : remainder;
            }

            return given;
        }
    }
}

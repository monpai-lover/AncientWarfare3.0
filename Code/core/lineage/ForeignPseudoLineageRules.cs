using System;

namespace AncientWarfare3.core.lineage
{
    public readonly struct ForeignPseudoNameParts
    {
        public readonly string GivenName;
        public readonly string FamilyName;
        public readonly string ClanName;

        public ForeignPseudoNameParts(string pGivenName, string pFamilyName, string pClanName)
        {
            GivenName = pGivenName ?? "";
            FamilyName = pFamilyName ?? "";
            ClanName = pClanName ?? "";
        }
    }

    public static class ForeignPseudoLineageRules
    {
        private static readonly char[] NameDelimiters = { ' ', '\t', '\u00b7', '\u2022', '-', '_', '/', '\\' };
        private static readonly string[] ClanSuffixes = { "家族", "氏族", "部落", "家", "族", "氏" };

        public static ForeignPseudoNameParts ResolveNameParts(string pDisplayName, string pVisibleClanName,
            string pExistingGivenName, string pExistingFamilyName, string pChineseFamilyName,
            string pExistingClanName, string pKingdomName)
        {
            string displayName = Clean(pDisplayName);
            string existingGiven = Clean(pExistingGivenName);
            string existingFamily = Clean(pExistingFamilyName);
            string chineseFamily = Clean(pChineseFamilyName);
            string existingClan = Clean(pExistingClanName);
            string parsedFamily = ExtractDelimitedFamily(displayName);
            string visibleClan = NormalizeClanName(pVisibleClanName);
            string kingdomFallback = FirstUsefulCharacter(pKingdomName);

            string familyName = FirstNonEmpty(existingFamily, existingClan, chineseFamily,
                parsedFamily, visibleClan, kingdomFallback);
            string clanName = FirstNonEmpty(existingClan, existingFamily, chineseFamily,
                parsedFamily, visibleClan, kingdomFallback);

            string givenName = existingGiven;
            if (string.IsNullOrEmpty(givenName))
            {
                givenName = RemoveFamily(displayName, familyName);
                if (string.IsNullOrEmpty(givenName)) givenName = ExtractGivenName(displayName);
                if (string.IsNullOrEmpty(givenName)) givenName = displayName;
            }

            return new ForeignPseudoNameParts(givenName, familyName, clanName);
        }

        public static string NormalizeClanName(string pClanName)
        {
            string clanName = Clean(pClanName);
            int possessive = clanName.LastIndexOf('的');
            if (possessive >= 0 && possessive + 1 < clanName.Length)
                clanName = clanName.Substring(possessive + 1).Trim();

            bool stripped;
            do
            {
                stripped = false;
                foreach (string suffix in ClanSuffixes)
                {
                    if (!clanName.EndsWith(suffix, StringComparison.Ordinal) || clanName.Length <= suffix.Length)
                        continue;
                    clanName = clanName.Substring(0, clanName.Length - suffix.Length).Trim();
                    stripped = true;
                    break;
                }
            } while (stripped);

            return clanName;
        }

        public static string ExtractClanName(string pDisplayName, string pFallback)
        {
            string raw = Clean(pDisplayName);
            return FirstNonEmpty(ExtractDelimitedFamily(raw), NormalizeClanName(pFallback),
                NormalizeClanName(raw));
        }

        public static string ExtractGivenName(string pDisplayName)
        {
            string raw = (pDisplayName ?? "").Trim();
            int index = LastDelimiterIndex(raw);
            string given = index > 0 ? raw.Substring(0, index).Trim() : raw;
            return string.IsNullOrEmpty(given) ? raw : given;
        }

        public static bool ShouldUseAwLineageSystem(bool pIsXiaActor, bool pKingdomIsForeignPseudoDynasty,
            bool pKingdomIsXia, bool pHasLineage)
        {
            return pHasLineage && (pIsXiaActor || pKingdomIsForeignPseudoDynasty || pKingdomIsXia);
        }

        public static bool ShouldIntegrateOfficial(bool pIsKing, bool pIsCityLeader, bool pIsArmyLeader)
        {
            return pIsKing || pIsCityLeader || pIsArmyLeader;
        }

        public static bool ShouldUseLineageBirth(bool isXia, bool isCivilizedSpecies,
            bool parentHasLineage)
        {
            return isXia || (isCivilizedSpecies && parentHasLineage);
        }

        public static bool ShouldRenameInstitutionalClan(bool leaderIsXia,
            bool kingdomUsesXiaizedInstitutions, bool hasClan, bool hasBranch, bool hasPlace)
        {
            return (leaderIsXia || kingdomUsesXiaizedInstitutions) && hasClan && hasBranch && hasPlace;
        }

        private static int LastDelimiterIndex(string pRaw)
        {
            if (string.IsNullOrEmpty(pRaw)) return -1;
            return pRaw.LastIndexOfAny(NameDelimiters);
        }

        private static string ExtractDelimitedFamily(string pDisplayName)
        {
            int index = LastDelimiterIndex(pDisplayName);
            return index >= 0 && index + 1 < pDisplayName.Length
                ? pDisplayName.Substring(index + 1).Trim()
                : "";
        }

        private static string RemoveFamily(string pDisplayName, string pFamilyName)
        {
            if (string.IsNullOrEmpty(pDisplayName) || string.IsNullOrEmpty(pFamilyName)) return pDisplayName;
            if (pDisplayName.StartsWith(pFamilyName, StringComparison.Ordinal))
                return pDisplayName.Substring(pFamilyName.Length).Trim(NameDelimiters);
            if (pDisplayName.EndsWith(pFamilyName, StringComparison.Ordinal))
                return pDisplayName.Substring(0, pDisplayName.Length - pFamilyName.Length).Trim(NameDelimiters);
            return pDisplayName;
        }

        private static string FirstUsefulCharacter(string pValue)
        {
            foreach (char value in Clean(pValue))
            {
                if (char.IsWhiteSpace(value) || Array.IndexOf(NameDelimiters, value) >= 0 || value == '国')
                    continue;
                return value.ToString();
            }
            return "";
        }

        private static string FirstNonEmpty(params string[] pValues)
        {
            foreach (string value in pValues)
            {
                string clean = Clean(value);
                if (!string.IsNullOrEmpty(clean)) return clean;
            }
            return "";
        }

        private static string Clean(string pValue)
        {
            return (pValue ?? "").Trim();
        }
    }
}

using AncientWarfare3.core.naming;

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

            // Structured identity is authoritative.  The stored display value
            // can be an old async projection (or a vanilla name) and must not
            // hide a newly admitted Shi/family branch in the family tree.
            bool hasStructuredIdentity = isNoble || isNameIntegrated ||
                                         !string.IsNullOrEmpty(familyName) ||
                                         !string.IsNullOrEmpty(clanName);
            if (hasStructuredIdentity)
            {
                string familyStem = !string.IsNullOrEmpty(familyName)
                    ? familyName
                    : clanName;
                if (isNoble && familyStem.IndexOf('·') >= 0)
                    return AWWesternFamilyNameRules.BuildActor(
                        given, familyStem, noble: true);

                string authoritative = Build(given, familyName, clanName,
                    isNoble, isMale, isNameIntegrated);
                if (!string.IsNullOrEmpty(authoritative))
                    return authoritative;
            }

            if (!string.IsNullOrEmpty(stored) && stored != given)
                return stored;

            return Build(given, familyName, clanName, isNoble, isMale,
                isNameIntegrated);
        }

        public static string ProjectArchive(string storedDisplayName,
            string givenName, string familyName, string clanName,
            string status, bool isMale, bool isNameIntegrated,
            string namingProfile, string westernNamingTradition,
            string originCityName, string displayStem)
        {
            NamingProfileId profile =
                AWCultureNamingTraditionRules.ParseProfile(namingProfile);
            bool noble = string.Equals(status, LineageStatus.NOBLE,
                System.StringComparison.Ordinal);
            if (profile == NamingProfileId.Western ||
                profile == NamingProfileId.OrcNomadic)
            {
                string rawStem = !string.IsNullOrWhiteSpace(displayStem)
                    ? displayStem
                    : (!string.IsNullOrWhiteSpace(familyName)
                        ? familyName
                        : clanName);
                FamilyBranchIdentityProjection identity =
                    WesternFamilyIdentityRules.ProjectBranch(profile,
                        westernNamingTradition, -1L, originCityName,
                        rawStem);
                string projected = WesternFamilyIdentityRules.BuildActor(
                    identity, givenName, noble);
                if (!string.IsNullOrWhiteSpace(projected)) return projected;
            }

            return ProjectStored(storedDisplayName, givenName, familyName,
                clanName, noble, isMale, isNameIntegrated);
        }

        public static string Build(string givenName, string familyName,
            string clanName, bool isNoble, bool isMale,
            bool isNameIntegrated)
        {
            string given = givenName ?? "";
            string family = familyName ?? "";
            string clan = clanName ?? "";
            given = LineageGivenNameNormalizationRules.Normalize(given,
                family, clan, isNoble, isMale, isNameIntegrated);

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

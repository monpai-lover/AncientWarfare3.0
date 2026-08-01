using System;
using System.Text;
using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    public readonly struct FamilyBranchIdentityProjection
    {
        public readonly NamingProfileId Profile;
        public readonly string PersistedNamingProfile;
        public readonly string PersistedWesternNamingTradition;
        public readonly long ParentShiId;
        public readonly string OriginCityChineseName;
        public readonly string DisplayStem;

        public FamilyBranchIdentityProjection(NamingProfileId pProfile,
            string pPersistedNamingProfile,
            string pPersistedWesternNamingTradition, long pParentShiId,
            string pOriginCityChineseName, string pDisplayStem)
        {
            Profile = pProfile;
            PersistedNamingProfile = pPersistedNamingProfile ?? string.Empty;
            PersistedWesternNamingTradition =
                pPersistedWesternNamingTradition ?? string.Empty;
            ParentShiId = pParentShiId;
            OriginCityChineseName = pOriginCityChineseName ?? string.Empty;
            DisplayStem = pDisplayStem ?? string.Empty;
        }
    }

    public static class WesternFamilyIdentityRules
    {
        public static FamilyBranchIdentityProjection ProjectBranch(
            NamingProfileId pProfile, string pPersistedWesternTradition,
            long parentShiId, string originCityChineseName,
            string rawDisplayStem)
        {
            string profile = AWCultureNamingTraditionRules.SerializeProfile(
                pProfile);
            string origin = NormalizeWhitespace(originCityChineseName);
            string persistedTradition = string.Empty;
            string displayStem;

            if (pProfile == NamingProfileId.Western &&
                TryParseWesternTradition(pPersistedWesternTradition,
                    out WesternNamingTradition tradition))
            {
                persistedTradition =
                    AWCultureNamingTraditionRules.SerializeTradition(
                        tradition);
                displayStem = AWWesternFamilyNameRules.BuildFamilyStem(
                    tradition, origin);
                if (string.IsNullOrWhiteSpace(displayStem))
                    displayStem = NormalizeWhitespace(rawDisplayStem);
            }
            else if (pProfile == NamingProfileId.OrcNomadic)
            {
                displayStem = NormalizeWhitespace(rawDisplayStem);
            }
            else
            {
                displayStem = rawDisplayStem ?? string.Empty;
            }

            return new FamilyBranchIdentityProjection(pProfile, profile,
                persistedTradition, parentShiId, origin, displayStem);
        }

        public static string BuildActor(
            FamilyBranchIdentityProjection pIdentity, string pGivenName,
            bool noble, string xiaFamilyName = "",
            string xiaClanName = "", bool xiaMale = true,
            bool xiaNameIntegrated = false)
        {
            if (pIdentity.Profile == NamingProfileId.Western)
                return AWWesternFamilyNameRules.BuildActor(pGivenName,
                    pIdentity.DisplayStem, noble);
            if (pIdentity.Profile == NamingProfileId.OrcNomadic)
                return JoinNameComponents(pGivenName,
                    pIdentity.DisplayStem);

            return LineageDisplayNameRules.Build(pGivenName, xiaFamilyName,
                xiaClanName, noble, xiaMale, xiaNameIntegrated);
        }

        public static string BuildHeading(
            FamilyBranchIdentityProjection pIdentity)
        {
            if (pIdentity.Profile == NamingProfileId.Western)
                return string.IsNullOrEmpty(pIdentity.DisplayStem)
                    ? string.Empty
                    : pIdentity.DisplayStem + "家族";
            if (pIdentity.Profile == NamingProfileId.OrcNomadic)
                return AWOrcNomadicNamingRules.BuildClanTitle(
                    pIdentity.DisplayStem);
            return pIdentity.DisplayStem;
        }

        private static bool TryParseWesternTradition(string pValue,
            out WesternNamingTradition pTradition)
        {
            string value = (pValue ?? string.Empty).Trim();
            if (string.Equals(value, "von", StringComparison.OrdinalIgnoreCase))
            {
                pTradition = WesternNamingTradition.Von;
                return true;
            }
            if (string.Equals(value, "de", StringComparison.OrdinalIgnoreCase))
            {
                pTradition = WesternNamingTradition.De;
                return true;
            }
            if (string.Equals(value, "van", StringComparison.OrdinalIgnoreCase))
            {
                pTradition = WesternNamingTradition.Van;
                return true;
            }
            if (string.Equals(value, "di", StringComparison.OrdinalIgnoreCase))
            {
                pTradition = WesternNamingTradition.Di;
                return true;
            }

            pTradition = default;
            return false;
        }

        private static string JoinNameComponents(string pGivenName,
            string pDisplayStem)
        {
            string given = NormalizeWhitespace(pGivenName);
            string stem = NormalizeWhitespace(pDisplayStem);
            if (stem.Length == 0) return given;
            return given.Length == 0 ? stem : given + " " + stem;
        }

        private static string NormalizeWhitespace(string pValue)
        {
            string value = (pValue ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;

            var builder = new StringBuilder(value.Length);
            bool pendingSpace = false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (char.IsWhiteSpace(current))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }
                builder.Append(current);
            }
            return builder.ToString();
        }
    }
}

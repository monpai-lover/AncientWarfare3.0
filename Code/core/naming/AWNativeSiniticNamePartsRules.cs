using System;

namespace AncientWarfare3.core.naming
{
    public readonly struct NativeSiniticNameParts
    {
        public NativeSiniticNameParts(bool pValid, string pFamilyName,
            string pGivenName)
        {
            Valid = pValid;
            FamilyName = pFamilyName ?? string.Empty;
            GivenName = pGivenName ?? string.Empty;
        }

        public bool Valid { get; }
        public string FamilyName { get; }
        public string GivenName { get; }
    }

    public static class AWNativeSiniticNamePartsRules
    {
        public static NativeSiniticNameParts Resolve(string pGeneratedName,
            string pGeneratedFamily, string pTaggedGivenName)
        {
            string generated = (pGeneratedName ?? string.Empty).Trim();
            string family = (pGeneratedFamily ?? string.Empty).Trim();
            if (generated.Length == 0 || family.Length == 0 ||
                !generated.StartsWith(family, StringComparison.Ordinal))
                return Invalid;

            string given = generated.Substring(family.Length).Trim();
            if (given.Length == 0) return Invalid;
            return new NativeSiniticNameParts(true, family, given);
        }

        private static NativeSiniticNameParts Invalid =>
            new NativeSiniticNameParts(false, string.Empty, string.Empty);
    }
}

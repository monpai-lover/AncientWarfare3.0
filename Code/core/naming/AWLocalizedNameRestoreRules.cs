using System;

namespace AncientWarfare3.core.naming
{
    internal static class AWLocalizedNameRestoreRules
    {
        internal static AWLocalizedNameIdentitySnapshot Merge(
            AWLocalizedNameIdentitySnapshot pSaved,
            AWLocalizedNameIdentitySnapshot pDatabase,
            int pCurrentSchemaVersion)
        {
            if (pSaved == null) return pDatabase;
            if (pDatabase == null) return pSaved;
            if (pSaved.SchemaVersion < pCurrentSchemaVersion)
                return pDatabase;

            return new AWLocalizedNameIdentitySnapshot(
                PreferSaved(pSaved.NativeName, pDatabase.NativeName),
                PreferSaved(pSaved.ChineseName, pDatabase.ChineseName),
                PreferSaved(pSaved.GivenName, pDatabase.GivenName),
                PreferSaved(pSaved.FamilyComponent,
                    pDatabase.FamilyComponent),
                PreferSaved(pSaved.GeneratorId, pDatabase.GeneratorId),
                pSaved.CultureId >= 0L
                    ? pSaved.CultureId
                    : pDatabase.CultureId,
                Math.Max(pSaved.SchemaVersion, pDatabase.SchemaVersion));
        }

        internal static bool Same(AWLocalizedNameIdentitySnapshot pLeft,
            AWLocalizedNameIdentitySnapshot pRight)
        {
            if (ReferenceEquals(pLeft, pRight)) return true;
            if (pLeft == null || pRight == null) return false;
            return string.Equals(pLeft.NativeName, pRight.NativeName,
                       StringComparison.Ordinal) &&
                   string.Equals(pLeft.ChineseName, pRight.ChineseName,
                       StringComparison.Ordinal) &&
                   string.Equals(pLeft.GivenName, pRight.GivenName,
                       StringComparison.Ordinal) &&
                   string.Equals(pLeft.FamilyComponent,
                       pRight.FamilyComponent, StringComparison.Ordinal) &&
                   string.Equals(pLeft.GeneratorId, pRight.GeneratorId,
                       StringComparison.Ordinal) &&
                   pLeft.CultureId == pRight.CultureId &&
                   pLeft.SchemaVersion == pRight.SchemaVersion;
        }

        private static string PreferSaved(string pSaved, string pDatabase)
        {
            return string.IsNullOrWhiteSpace(pSaved)
                ? pDatabase ?? string.Empty
                : pSaved.Trim();
        }
    }
}

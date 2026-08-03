using System;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class FamilyTreeArchivePresentationRules
    {
        internal static bool ShouldUseUnavailablePortrait(string pResolution)
        {
            return string.Equals(pResolution,
                LineageFamilyArchiveMigration.UnresolvedLegacy,
                StringComparison.Ordinal);
        }

        internal static string ResolveSexLabel(int pSex, string pMale,
            string pFemale)
        {
            if (pSex == 0) return pMale ?? string.Empty;
            if (pSex == 1) return pFemale ?? string.Empty;
            return string.Empty;
        }
    }
}

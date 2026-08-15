using System;
using System.Collections.Generic;
using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelOutlawNameService
    {
        private static bool _missingLibraryWarningLogged;

        internal static bool EnsureRoot(Kingdom pKingdom, Actor pFounder,
            int pYear, out string pRoot)
        {
            pRoot = "";
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pFounder?.data == null || pFounder.isRekt()) return false;

            IReadOnlyList<string> roots =
                AWWordLibraryManager.Instance.GetWords(
                    PeasantRebelOutlawNameRules.LibraryId);
            if (roots.Count == 0)
            {
                WarnMissingLibrary();
                return false;
            }

            pKingdom.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
                out string stored, "");
            long seed = unchecked(pKingdom.getID() ^
                (pFounder.getID() << 1) ^ ((long)pYear << 32));
            pRoot = PeasantRebelOutlawNameRules.ResolveRoot(
                stored, roots, seed);
            if (pRoot.Length == 0)
            {
                WarnMissingLibrary();
                return false;
            }
            if (!string.Equals(stored, pRoot, StringComparison.Ordinal))
                pKingdom.data.set(LineageKeys.MANDATE_REBEL_NAME_ROOT,
                    pRoot);
            return true;
        }

        internal static bool HasValidRoot(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
                out string stored, "");
            return PeasantRebelOutlawNameRules.IsValidLibraryRoot(stored,
                AWWordLibraryManager.Instance.GetWords(
                    PeasantRebelOutlawNameRules.LibraryId));
        }

        private static void WarnMissingLibrary()
        {
            if (_missingLibraryWarningLogged) return;
            _missingLibraryWarningLogged = true;
            ModClass.LogWarning(
                "Peasant rebel outlaw root library is empty or invalid.");
        }
    }
}

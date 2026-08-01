using System;

namespace AncientWarfare3.core.naming
{
    public static class AWNamingCollisionRules
    {
        public const string ExternalChineseNameUid =
            "\u4e00\u7c73_\u4e2d\u6587\u540d";

        private const string IntegratedNamingPatchNamespace =
            "AncientWarfare3.patch.naming";

        public static bool IsRecognizedModConflict(string pUid,
            string pStateName)
        {
            if (!string.Equals(pUid, ExternalChineseNameUid,
                    StringComparison.Ordinal))
                return false;

            return string.Equals(pStateName, "LOADED",
                       StringComparison.Ordinal) ||
                   string.Equals(pStateName, "FAILED",
                       StringComparison.Ordinal);
        }

        public static bool ShouldDisableIntegratedNamingPatches(
            bool loadedModsConflict, bool registryScanSucceeded,
            bool registryConflictDetected)
        {
            return loadedModsConflict ||
                   registryScanSucceeded && registryConflictDetected;
        }

        public static bool ShouldSkipHarmonyPatch(string pPatchNamespace,
            bool disableIntegratedNamingPatches)
        {
            if (!disableIntegratedNamingPatches ||
                string.IsNullOrEmpty(pPatchNamespace))
                return false;

            return string.Equals(pPatchNamespace,
                       IntegratedNamingPatchNamespace,
                       StringComparison.Ordinal) ||
                   pPatchNamespace.StartsWith(
                       IntegratedNamingPatchNamespace + ".",
                       StringComparison.Ordinal);
        }
    }
}

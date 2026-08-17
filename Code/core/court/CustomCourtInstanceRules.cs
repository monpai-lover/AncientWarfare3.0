using System;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtInstanceRules
    {
        public static string ResolveName(string builtinName,
            string templateName, string localOverride)
        {
            if (!string.IsNullOrWhiteSpace(localOverride))
                return localOverride;
            if (!string.IsNullOrWhiteSpace(templateName))
                return templateName;
            return builtinName ?? string.Empty;
        }

        public static bool CanUseSavedSnapshot(bool hasSnapshot,
            bool snapshotIsStale)
        {
            return hasSnapshot && !snapshotIsStale;
        }

        public static bool IsValidKingdomId(string kingdomId)
        {
            return !string.IsNullOrWhiteSpace(kingdomId) &&
                kingdomId.Length <= 128;
        }
    }
}

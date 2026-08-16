using System;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// Pure decisions used before vanilla Kingdom founder-species lookup.
    /// </summary>
    public static class KingdomFounderSpeciesSafetyRules
    {
        public static bool IsUsableAssetId(string pAssetId)
        {
            return !string.IsNullOrWhiteSpace(pAssetId);
        }

        public static string SelectFirstUsableAssetId(
            params string[] pCandidates)
        {
            if (pCandidates == null) return null;
            foreach (string candidate in pCandidates)
                if (IsUsableAssetId(candidate)) return candidate;
            return null;
        }

        public static bool ShouldBypassVanillaLookup(string pAssetId)
        {
            return !IsUsableAssetId(pAssetId);
        }
    }
}

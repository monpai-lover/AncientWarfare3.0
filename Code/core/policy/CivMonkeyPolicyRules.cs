using System;

namespace AncientWarfare3.core.policy
{
    internal static class CivMonkeyPolicyRules
    {
        private const string CivilizedMonkeyAssetId = "civ_monkey";

        public static bool IsNativePolicySpecies(string pOriginalActorAssetId,
            string pKingdomAssetId, string pResolvedActorAssetId)
        {
            return IsNativeXiaCultureSpecies(pOriginalActorAssetId) ||
                   IsNativeXiaCultureSpecies(pKingdomAssetId) ||
                   IsNativeXiaCultureSpecies(pResolvedActorAssetId);
        }

        public static bool IsNativeXiaCultureSpecies(string pAssetId)
        {
            return string.Equals(pAssetId, CivilizedMonkeyAssetId,
                StringComparison.Ordinal);
        }
    }
}

using System;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.presentation
{
    public static class XiaActorTextureRules
    {
        public const int NoOfficialTier = 0;
        public const int LowOfficialTier = 1;
        public const int MiddleOfficialTier = 2;
        public const int HighOfficialTier = 3;

        public static int ResolveOfficialTier(int pRank)
        {
            int rank = OfficialCareerRankRules.ClampRank(pRank);
            if (rank == OfficialCareerRankRules.Unranked)
                return NoOfficialTier;
            if (rank <= 6) return LowOfficialTier;
            if (rank <= 12) return MiddleOfficialTier;
            return HighOfficialTier;
        }

        public static string ResolveOfficialBodyDirectory(int pRank)
        {
            int tier = ResolveOfficialTier(pRank);
            return tier == NoOfficialTier ? null : "leader_" + tier;
        }

        public static string ResolveOfficialHeadPath(int pRank)
        {
            int tier = ResolveOfficialTier(pRank);
            return tier == NoOfficialTier
                ? null
                : "heads_leader/head_" + (tier - 1);
        }

        public static int StableVariantIndex(long pActorId, int pCount)
        {
            if (pCount <= 0) return 0;
            unchecked
            {
                ulong value = (ulong)pActorId;
                value ^= value >> 33;
                value *= 0xff51afd7ed558ccdUL;
                value ^= value >> 33;
                return (int)(value % (ulong)pCount);
            }
        }

        public static string[] ExpandSkins(string[] pSkins, int pCount)
        {
            if (pSkins == null || pSkins.Length == 0 || pCount <= 0)
                return Array.Empty<string>();
            var result = new string[pCount];
            for (int i = 0; i < result.Length; i++)
                result[i] = pSkins[i % pSkins.Length];
            return result;
        }
    }
}

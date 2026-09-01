using System;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.presentation
{
    public static class XiaActorTextureRules
    {
        public const string KingHeadPath = "heads_special/head_king/head_0";

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
            return BodyDirectoryForTier(tier);
        }

        public static string ResolveOfficialBodyDirectory(int pRank,
            int pOfficeGrade)
        {
            int tier = ResolveOfficialTier(pRank, pOfficeGrade);
            return BodyDirectoryForTier(tier);
        }

        public static int ResolveOfficialTier(int pRank, int pOfficeGrade)
        {
            int byRank = ResolveOfficialTier(pRank);
            if (byRank != NoOfficialTier) return byRank;
            if (pOfficeGrade == 10) return HighOfficialTier;
            if (pOfficeGrade == 20) return MiddleOfficialTier;
            if (pOfficeGrade == 30) return LowOfficialTier;
            return NoOfficialTier;
        }

        private static string BodyDirectoryForTier(int pTier)
        {
            if (pTier == LowOfficialTier) return "leader_3";
            if (pTier == MiddleOfficialTier) return "leader_2";
            if (pTier == HighOfficialTier) return "leader_1";
            return null;
        }

        public static string ResolveOfficialHeadPath(int pRank,
            int pOfficeGrade)
        {
            int tier = ResolveOfficialTier(pRank, pOfficeGrade);
            return tier == NoOfficialTier
                ? null
                : "heads_leader/head_" + (tier - 1);
        }

        public static string ResolveOfficialHeadPath(int pRank)
        {
            int tier = ResolveOfficialTier(pRank);
            return tier == NoOfficialTier
                ? null
                : "heads_leader/head_" + (tier - 1);
        }

        public static string ResolveKingHeadPath()
        {
            return KingHeadPath;
        }

        public static string ResolveWarriorHeadPath(long pActorId)
        {
            return "heads_warrior/head_" + StableVariantIndex(pActorId, 2);
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

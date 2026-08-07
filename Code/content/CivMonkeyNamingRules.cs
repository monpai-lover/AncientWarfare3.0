using System;
using System.Collections.Generic;

namespace AncientWarfare3.content
{
    internal readonly struct CivMonkeyLineageIdentity
    {
        internal readonly string FamilyName;
        internal readonly string ChineseFamilyName;
        internal readonly string ClanName;

        internal CivMonkeyLineageIdentity(string pShi)
        {
            FamilyName = pShi ?? "";
            ChineseFamilyName = pShi ?? "";
            ClanName = pShi ?? "";
        }
    }

    internal static class CivMonkeyNamingRules
    {
        internal const string ActorAssetId = "civ_monkey";
        internal const string NameSetId = "aw_civ_monkey_easter_egg_set";
        internal const string ActorGeneratorId = "civ_monkey_name";
        internal const string CityGeneratorId = "civ_monkey_city";
        internal const string ClanGeneratorId = "civ_monkey_clan";
        internal const string KingdomGeneratorId = "civ_monkey_kingdom";

        // Editable runtime copies live in name_generators/lib. These arrays are the
        // deterministic fallback when ChineseName is absent or its library cannot load.
        internal static readonly string[] Surnames =
        {
            "蒙", "猴", "侯"
        };

        internal static readonly string[] GivenNames =
        {
            "monpai", "蒙派", "Beruk", "豚尾猴", "食蟹猴", "莫娜", "猴妹",
            "猴姐", "猴弟", "猴宝", "臭宝", "歹毒白毛", "短尾猴"
        };

        internal static readonly string[] CityNames =
        {
            "花果城", "水帘城", "灵台邑", "方寸城", "通臂寨", "长臂关",
            "猕岭", "猿乡"
        };

        internal static readonly string[] KingdomNames =
        {
            "猴国", "大猴国", "蒙派帝国", "猴汉帝国", "猴汉国"
        };

        public static bool IsCivilizedMonkey(string pActorAssetId)
        {
            return string.Equals(pActorAssetId, ActorAssetId,
                StringComparison.Ordinal);
        }

        public static string ResolveSurname(string pInheritedSurname, long pSeed)
        {
            return ResolveSurname(pInheritedSurname, pSeed, Surnames);
        }

        internal static string ResolveLineageSurname(bool pHasExistingShi,
            string pClanName, string pChineseFamilyName, string pFamilyName)
        {
            if (pHasExistingShi)
            {
                string clan = Normalize(pClanName);
                if (!string.IsNullOrEmpty(clan)) return clan;
            }

            string chineseFamily = Normalize(pChineseFamilyName);
            if (!string.IsNullOrEmpty(chineseFamily)) return chineseFamily;
            return Normalize(pFamilyName);
        }

        internal static string ResolveSurname(string pInheritedSurname, long pSeed,
            IReadOnlyList<string> pRuntimeSurnames)
        {
            string inherited = Normalize(pInheritedSurname);
            if (!string.IsNullOrEmpty(inherited)) return inherited;
            return PickFrom(pRuntimeSurnames, Surnames, pSeed, 0x534E);
        }

        internal static CivMonkeyLineageIdentity ResolveLineageIdentity(
            string pInheritedOrExistingShi, long pSeed,
            IReadOnlyList<string> pRuntimeSurnames)
        {
            return new CivMonkeyLineageIdentity(ResolveSurname(
                pInheritedOrExistingShi, pSeed, pRuntimeSurnames));
        }

        public static string BuildActorName(string pInheritedSurname, long pSeed,
            int pMetaType)
        {
            return ResolveSurname(pInheritedSurname, pSeed) +
                   PickGivenName(pSeed, pMetaType);
        }

        internal static string BuildActorName(string pInheritedSurname, long pSeed,
            int pMetaType, IReadOnlyList<string> pRuntimeSurnames,
            IReadOnlyList<string> pRuntimeGivenNames, out string pResolvedSurname,
            out string pGivenName)
        {
            pResolvedSurname = ResolveSurname(pInheritedSurname, pSeed,
                pRuntimeSurnames);
            pGivenName = PickFrom(pRuntimeGivenNames, GivenNames, pSeed,
                pMetaType ^ 0x474E);
            return pResolvedSurname + pGivenName;
        }

        public static string PickGivenName(long pSeed, int pMetaType)
        {
            return PickFrom(GivenNames, GivenNames, pSeed, pMetaType ^ 0x474E);
        }

        public static string NormalizeGivenName(string pFamilyName,
            string pCandidateName)
        {
            string family = Normalize(pFamilyName);
            string candidate = Normalize(pCandidateName);
            if (family.Length > 0 && candidate.StartsWith(family,
                    StringComparison.Ordinal) && candidate.Length > family.Length)
                return candidate.Substring(family.Length).Trim();
            return candidate;
        }

        public static string PickCity(long pSeed, int pMetaType)
        {
            return PickFrom(CityNames, CityNames, pSeed, pMetaType ^ 0x4349);
        }

        internal static string PickCity(long pSeed, int pMetaType,
            IReadOnlyList<string> pRuntimeNames)
        {
            return PickFrom(pRuntimeNames, CityNames, pSeed, pMetaType ^ 0x4349);
        }

        // Kept for non-person meta types that are outside this naming slice.
        public static string Pick(long pSeed, int pMetaType)
        {
            return PickGivenName(pSeed, pMetaType);
        }

        public static string PickKingdom(long pSeed, int pMetaType)
        {
            return PickFrom(KingdomNames, KingdomNames, pSeed, pMetaType ^ 0x4B47);
        }

        internal static string PickKingdom(long pSeed, int pMetaType,
            IReadOnlyList<string> pRuntimeNames)
        {
            return PickFrom(pRuntimeNames, KingdomNames, pSeed, pMetaType ^ 0x4B47);
        }

        private static string PickFrom(IReadOnlyList<string> pPool,
            IReadOnlyList<string> pFallbackPool, long pSeed, int pSalt)
        {
            IReadOnlyList<string> pool = HasUsableEntry(pPool) ? pPool : pFallbackPool;
            ulong mixed = unchecked((ulong)pSeed) ^
                          (unchecked((ulong)(uint)pSalt) * 0x9E3779B97F4A7C15UL);
            mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
            mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
            mixed ^= mixed >> 31;

            int start = (int)(mixed % (uint)pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                string candidate = Normalize(pool[(start + i) % pool.Count]);
                if (!string.IsNullOrEmpty(candidate)) return candidate;
            }

            return "蒙派";
        }

        private static bool HasUsableEntry(IReadOnlyList<string> pPool)
        {
            if (pPool == null || pPool.Count == 0) return false;
            for (int i = 0; i < pPool.Count; i++)
                if (!string.IsNullOrEmpty(Normalize(pPool[i])))
                    return true;
            return false;
        }

        private static string Normalize(string pValue)
        {
            string value = pValue?.Trim() ?? "";
            if (string.Equals(value, "name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "NO_NAME", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "#NO_NAME#", StringComparison.OrdinalIgnoreCase))
                return "";
            return value;
        }
    }
}

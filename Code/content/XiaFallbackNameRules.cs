namespace AncientWarfare3.content
{
    public static class XiaFallbackNameRules
    {
        private static readonly string[] KingdomNames =
        {
            "夏", "商", "周", "秦", "汉", "魏", "晋", "楚", "齐", "鲁", "燕", "赵",
            "宋", "郑", "卫", "陈", "蔡", "吴", "越", "唐", "虞"
        };

        private static readonly string[] LanguageNames =
        {
            "夏语", "雅言", "华言", "诸夏雅言", "九州雅言", "河洛雅言", "王畿雅言", "邦国雅言"
        };

        private static readonly string[] ReligionNames =
        {
            "社稷礼", "宗庙礼", "礼乐祖祀", "华夏大礼", "诸夏礼",
            "天命礼", "王畿祖祀", "河洛礼", "先王祀典", "九州王礼"
        };

        private static readonly string[] CultureNames =
        {
            "诸夏文化", "华夏文化", "中原礼制", "河洛雅风", "九州礼乐",
            "青铜礼制", "王畿雅风", "邦国礼制"
        };

        private static readonly string[] SubspeciesNames =
        {
            "华夏人", "诸夏人", "河洛夏人", "中原夏人", "九州夏人",
            "王畿夏人", "礼乐夏人", "玄鸟夏人", "青铜夏人", "邦国夏人"
        };

        private static readonly string[] SubspeciesPrefixes =
        {
            "河洛", "中原", "九州", "王畿", "礼乐", "玄鸟", "青铜", "邦国", "洛邑", "镐京"
        };

        private static readonly string[] AllianceRoots =
        {
            "\u8bf8\u590f", "\u534e\u590f", "\u4e5d\u5dde", "\u6cb3\u6d1b",
            "\u738b\u757f", "\u793c\u4e50", "\u5c71\u6cb3", "\u6d77\u5185"
        };

        private static readonly string[] AllianceSuffixes =
        {
            "\u76df", "\u4f1a\u76df", "\u540c\u76df", "\u76df\u8a93"
        };

        public static string FirstUsefulMetaName(params string[] pCandidates)
        {
            if (pCandidates == null) return "";
            foreach (string candidate in pCandidates)
            {
                if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(candidate))
                    return candidate;
            }
            return "";
        }

        public static string FirstUsefulSubspeciesName(params string[] pCandidates)
        {
            if (pCandidates == null) return "";
            foreach (string candidate in pCandidates)
            {
                if (!XiaNameRepairRules.IsInvalidXiaSubspeciesName(candidate))
                    return candidate;
            }
            return "";
        }

        public static string LocalKingdomName(long pSeed)
        {
            return Pick(KingdomNames, pSeed);
        }

        public static string LocalLanguageName(long pSeed)
        {
            return Pick(LanguageNames, pSeed);
        }

        public static string LocalReligionName(long pSeed)
        {
            return Pick(ReligionNames, pSeed);
        }

        public static string LocalCultureName(long pSeed)
        {
            return Pick(CultureNames, pSeed);
        }

        public static string LocalSubspeciesName(long pSeed)
        {
            var random = CreateRandom(pSeed);
            if (random.Next(100) < 45)
                return SubspeciesNames[random.Next(SubspeciesNames.Length)];
            return SubspeciesPrefixes[random.Next(SubspeciesPrefixes.Length)] + "夏人";
        }

        public static string LocalAllianceName(long pSeed)
        {
            var random = CreateRandom(pSeed);
            return AllianceRoots[random.Next(AllianceRoots.Length)] +
                   AllianceSuffixes[random.Next(AllianceSuffixes.Length)];
        }

        private static string Pick(string[] pNames, long pSeed)
        {
            if (pNames == null || pNames.Length == 0) return "";
            return pNames[CreateRandom(pSeed).Next(pNames.Length)];
        }

        private static System.Random CreateRandom(long pSeed)
        {
            return new System.Random(unchecked((int)(pSeed * 1103515245L + 12345L)));
        }
    }
}

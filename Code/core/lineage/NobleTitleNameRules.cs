using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class NobleTitleNameRules
    {
        private static readonly string[] AncientPlaces =
        {
            "河间", "中山", "琅琊", "汝南", "淮南", "广陵", "东平", "北海",
            "常山", "长沙", "临淄", "彭城", "南阳", "陈留", "弘农", "江夏",
            "会稽", "吴兴", "南康", "庐陵", "安定", "扶风", "天水", "太原",
            "上党", "雁门", "渔阳", "辽东", "河内", "颍川", "魏郡", "赵郡",
            "清河", "博陵", "平原", "乐安", "济北", "济南", "东莱", "高密",
            "广平", "武安", "宜都", "新安", "始安", "建安", "临川", "豫章",
            "丹阳", "宣城", "武陵", "零陵", "桂阳", "巴东", "汉中", "武都",
            "金城", "武威", "酒泉", "敦煌", "朔方", "五原", "云中", "定襄"
        };

        private static readonly string[] PrincessNames =
        {
            "平阳", "安乐", "太平", "长乐", "昌乐", "升平", "咸宜", "永泰",
            "文成", "金城", "玉真", "万安", "宁国", "临海", "南康", "新城",
            "高阳", "清河", "襄城", "汝南", "常山", "安康", "兰陵", "城阳",
            "馆陶", "阳信", "隆虑", "平原", "南宫", "鄂邑", "敬武", "卫长",
            "石邑", "诸邑", "沁水", "义阳", "淮阳", "涅阳", "舞阴", "濮阳",
            "博陵", "乐平", "东乡", "平昌", "昌邑", "富平", "武安", "舞阳",
            "获嘉", "成安", "封丘", "灵寿", "颍阴", "巴陵", "普安", "东阳",
            "和政", "永嘉", "义宁", "临晋", "太和", "信成", "西平", "晋阳",
            "霍国", "郜国", "蔡国", "虢国", "宿国", "萧国", "鄎国", "道国",
            "代国", "凉国", "毕国", "纪国", "郑国", "衡阳", "宣城", "安兴",
            "昌隆", "宜城", "定安", "宜芳", "宜都", "永寿", "永和", "永福",
            "永徽", "永穆", "寿安", "寿春", "义昌", "同安", "安定", "金仙",
            "临安", "福清", "南昌", "含山", "汝阳", "宝庆", "怀庆", "大名",
            "福成", "庆阳", "永安", "咸宁", "常宁", "嘉善", "清城", "真宁",
            "德清", "延庆", "瑞安", "南平", "宜兴", "丹阳", "长宁", "永宁",
            "富阳", "嘉兴", "仁和", "宁安", "鲁元", "修成", "阴安", "阳石"
        };

        public static IReadOnlyList<string> HistoricalPrincessNames =>
            PrincessNames;

        public static string SelectUnused(string pActualFiefName,
            string pExistingTitleName, int pRank, NobleTitleStyle pStyle,
            long pSeed, ISet<string> pUsedNames,
            IReadOnlyList<string> pAncientStateNames)
        {
            ISet<string> used = pUsedNames ??
                                new HashSet<string>(StringComparer.Ordinal);
            string existing = Normalize(pExistingTitleName);
            if (Available(existing, used)) return existing;

            string actual = Normalize(pActualFiefName);
            if (Available(actual, used)) return actual;

            if (IsPrincessStyle(pStyle))
            {
                string princess = PickUnused(PrincessNames, pSeed, used);
                if (!string.IsNullOrEmpty(princess)) return princess;
                return PickUnused(AncientPlaces, pSeed, used);
            }

            bool stateRank = NobleRankRules.ClampRank(pRank) >=
                             NobleRankRules.RankStateDuke;
            string selected = stateRank
                ? PickUnused(pAncientStateNames, pSeed, used)
                : PickUnused(AncientPlaces, pSeed, used);
            if (!string.IsNullOrEmpty(selected)) return selected;
            return stateRank
                ? PickUnused(AncientPlaces, pSeed, used)
                : PickUnused(pAncientStateNames, pSeed, used);
        }

        public static string ComposeDisplayTitle(string pTitleName,
            string pRankOrStyle)
        {
            string name = Normalize(pTitleName);
            string rank = pRankOrStyle?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) return rank;
            return string.IsNullOrEmpty(rank) ? name : name + rank;
        }

        public static bool ShouldKeepSuccessorTitle(int pCurrentRank,
            int pInheritedRank)
        {
            return NobleRankRules.ClampRank(pCurrentRank) >
                   NobleRankRules.ClampRank(pInheritedRank);
        }

        private static string PickUnused(IReadOnlyList<string> pPool,
            long pSeed, ISet<string> pUsedNames)
        {
            int count = pPool?.Count ?? 0;
            if (count == 0) return "";
            int start = (int)(unchecked((ulong)pSeed) % (ulong)count);
            for (int offset = 0; offset < count; offset++)
            {
                string candidate = Normalize(pPool[(start + offset) % count]);
                if (Available(candidate, pUsedNames)) return candidate;
            }
            return "";
        }

        private static bool Available(string pName, ISet<string> pUsedNames)
        {
            return !string.IsNullOrEmpty(pName) &&
                   !pUsedNames.Contains(pName);
        }

        private static bool IsPrincessStyle(NobleTitleStyle pStyle)
        {
            return pStyle is NobleTitleStyle.Princess or
                NobleTitleStyle.SeniorPrincess or
                NobleTitleStyle.GrandPrincess;
        }

        private static string Normalize(string pName)
        {
            string value = pName?.Trim() ?? "";
            if (value.EndsWith("藩", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1).Trim();
            return value;
        }
    }
}

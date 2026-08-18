using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class RulerHouseholdRankRules
    {
        public static readonly string[] ImperialSeatCodes =
        {
            "empress",
            "consort_de",
            "consort_li",
            "consort_zhuang",
            "consort_xian",
            "consort_hui",
            "consort_an",
            "consort_he",
            "consort_xi",
            "consort_kang"
        };

        public static string SeatCode(int pSlot)
        {
            return pSlot >= 0 && pSlot < ImperialSeatCodes.Length
                ? ImperialSeatCodes[pSlot]
                : "";
        }

        public static bool IsFixedImperialRank(string pRankCode)
        {
            return Array.IndexOf(ImperialSeatCodes,
                pRankCode ?? "") >= 0;
        }

        public static string TitleKey(string pRankCode)
        {
            return IsFixedImperialRank(pRankCode)
                ? "aw_household_rank_" + pRankCode
                : "";
        }

        public static bool KeepsSeatAfterAge(int pAge)
        {
            return true;
        }

        public static int ConsortScore(int attributeScore,
            int lineagePriority, bool noble)
        {
            int attributes = Math.Max(0, Math.Min(10000, attributeScore));
            int lineage = Math.Max(0, Math.Min(3, lineagePriority));
            return attributes * 100 + (3 - lineage) * 2 +
                   (noble ? 1 : 0);
        }

        public static string NextEmptySeat(ISet<string> pUsed,
            bool pPrincipal)
        {
            if (pUsed == null) return "";
            if (pPrincipal)
            {
                string empress = ImperialSeatCodes[0];
                return pUsed.Contains(empress) ? "" : empress;
            }
            int first = 1;
            for (int i = first; i < ImperialSeatCodes.Length; i++)
            {
                if (!pUsed.Contains(ImperialSeatCodes[i]))
                    return ImperialSeatCodes[i];
            }
            return "";
        }
    }
}

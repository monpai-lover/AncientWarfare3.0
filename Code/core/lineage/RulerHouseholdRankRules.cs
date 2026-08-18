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

        public static bool KeepsSeatAfterAge(int pAge)
        {
            return true;
        }

        public static string NextEmptySeat(ISet<string> pUsed,
            bool pPrincipal)
        {
            if (pUsed == null) return "";
            int first = pPrincipal ? 0 : 1;
            for (int i = first; i < ImperialSeatCodes.Length; i++)
            {
                if (!pUsed.Contains(ImperialSeatCodes[i]))
                    return ImperialSeatCodes[i];
            }
            return "";
        }
    }
}
